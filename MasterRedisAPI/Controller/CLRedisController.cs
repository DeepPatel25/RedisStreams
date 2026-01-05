using MasterRedisAPI.Helper;
using MasterRedisAPI.Models;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace MasterRedisAPI.Controller
{
    /// <summary>
    /// Provides APIs for managing Redis Streams used for
    /// master-to-child synchronization operations.
    /// </summary>
    /// <remarks>
    /// This controller supports stream publishing, consumer group management,
    /// message pulling, acknowledgment, deletion, and cleanup of expired entries.
    /// </remarks>
    [ApiController, Route("api/[controller]")]
    public class CLRedisController : ControllerBase
    {
        #region Private Fields

        /// <summary>
        /// Standard API response wrapper.
        /// </summary>
        private readonly Response response = new();

        #endregion

        #region Constants

        /// <summary>
        /// Redis stream key used for master sync operations.
        /// </summary>
        private const string StreamKey = "master:sync:stream";

        #endregion

        #region Public APIs

        /// <summary>
        /// Adds a session entry to Redis cache and publishes it to the Redis stream.
        /// </summary>
        /// <param name="sessionId">Unique session or token identifier.</param>
        /// <param name="validMinutes">Session validity duration in minutes.</param>
        /// <param name="jsonValue">Serialized session data stored in Redis.</param>
        /// <returns>
        /// Returns the generated Redis stream message ID.
        /// </returns>
        /// <response code="200">Session successfully added to stream.</response>
        [HttpPost]
        public async Task<IActionResult> AddAsync(
            string sessionId,
            int validMinutes,
            string jsonValue
        )
        {
            // Calculate absolute expiry timestamp (Unix seconds)
            long expiryTimeUtc = DateTimeOffset.UtcNow.AddMinutes(validMinutes).ToUnixTimeSeconds();

            StreamDataModel data = new()
            {
                SessionId = sessionId,
                ExpiryTimeUtc = expiryTimeUtc,
                JsonValue = jsonValue,
            };

            // Convert model to Redis Stream entries
            NameValueEntry[] entries =
            [
                new("SessionId", data.SessionId),
                new("ExpiryTimeUtc", data.ExpiryTimeUtc.ToString()),
                new("Value", data.JsonValue),
            ];

            // Store value in Redis cache with TTL
            await CacheManager.Cache.SetStringAsync(
                sessionId,
                jsonValue,
                TimeSpan.FromMinutes(validMinutes)
            );

            // Publish message to Redis Stream
            string? messageId = await CacheManager.Cache.AddToStreamAsync(entries, StreamKey);

            if (string.IsNullOrEmpty(messageId))
            {
                response.IsError = true;
                response.Message = "Failed to add message to stream.";
                return Ok(response);
            }

            response.DataModel = messageId;
            return Ok(response);
        }

        /// <summary>
        /// Creates a Redis consumer group for the stream if it does not already exist.
        /// </summary>
        /// <param name="groupName">Name of the consumer group.</param>
        /// <returns>Operation result.</returns>
        [HttpPost("CreateGroup")]
        public async Task<IActionResult> CreateGroupIfNotExistsAsync(string groupName)
        {
            try
            {
                await CacheManager.Cache.CreateConsumerGroupAsync(groupName, StreamKey);
                response.Message = "Consumer group created successfully.";
            }
            catch (RedisServerException ex)
            {
                response.IsError = true;
                response.Message = ex.Message;
            }

            return Ok(response);
        }

        /// <summary>
        /// Pulls messages from Redis Stream for a consumer.
        /// </summary>
        /// <remarks>
        /// The method first retrieves pending (unacknowledged) messages.
        /// If none exist, it fetches new messages from the stream.
        /// </remarks>
        /// <param name="consumerGroup">Consumer group name.</param>
        /// <param name="consumerName">Consumer name.</param>
        /// <param name="batchSize">Number of messages to fetch.</param>
        /// <returns>List of stream messages.</returns>
        [HttpGet("pull")]
        public async Task<IActionResult> PullAsync(
            [FromQuery] string consumerGroup,
            [FromQuery] string consumerName,
            [FromQuery] int batchSize = 1
        )
        {
            // 1️⃣ Fetch pending messages first
            var pending = await CacheManager.Cache.GetPendingWithValuesAsync(
                consumerGroup,
                consumerName,
                batchSize,
                StreamKey
            );

            if (pending.Count > 0)
            {
                response.DataModel = pending
                    .Select(x => new StreamMessageDto<StreamDataDto>
                    {
                        MessageId = x.MessageId,
                        Data = new StreamDataDto
                        {
                            SessionId = x.Values["SessionId"],
                            ExpiryTimeUtc = long.Parse(x.Values["ExpiryTimeUtc"]),
                            JsonValue = x.Values["Value"],
                        },
                    })
                    .ToList();

                response.Message = "Pending messages returned.";
                return Ok(response);
            }

            // 2️⃣ Fetch new messages
            StreamEntry[] messages = await CacheManager.Cache.ReadStreamAsync(
                consumerGroup,
                consumerName,
                count: batchSize,
                StreamKey
            );

            response.DataModel = messages
                .Select(m =>
                {
                    var values = m.Values.ToDictionary(
                        x => x.Name.ToString(),
                        x => x.Value.ToString()
                    );

                    return new StreamMessageDto<StreamDataDto>
                    {
                        MessageId = m.Id,
                        Data = new StreamDataDto
                        {
                            SessionId = values["SessionId"],
                            ExpiryTimeUtc = long.Parse(values["ExpiryTimeUtc"]),
                            JsonValue = values["Value"],
                        },
                    };
                })
                .ToList();

            response.Message = "New messages returned.";
            return Ok(response);
        }

        /// <summary>
        /// Acknowledges a successfully processed Redis stream message.
        /// </summary>
        /// <param name="messageId">Stream message ID.</param>
        /// <param name="consumerGroup">Consumer group name.</param>
        /// <returns>ACK operation result.</returns>
        [HttpPost("Acknowledge")]
        public async Task<IActionResult> AckMessagesAsync(string messageId, string consumerGroup)
        {
            await CacheManager.Cache.AckMessagesAsync(consumerGroup, [messageId]);
            response.Message = "Message acknowledged successfully.";
            return Ok(response);
        }

        /// <summary>
        /// Deletes a specific message from the Redis stream.
        /// </summary>
        /// <param name="messageId">Redis stream message ID.</param>
        /// <returns>Deletion result.</returns>
        [HttpDelete("DeleteStreamMessage")]
        public async Task<IActionResult> DeleteStreamAsync(string messageId)
        {
            await CacheManager.Cache.DeleteMessagesAsync([messageId], StreamKey);
            response.Message = "Stream message deleted.";
            return Ok(response);
        }

        /// <summary>
        /// Removes expired stream entries based on their Unix expiry timestamp.
        /// </summary>
        /// <param name="batchSize">Number of entries scanned per iteration.</param>
        /// <returns>Cleanup operation result.</returns>
        [HttpPost("cleanup-expired")]
        public async Task<IActionResult> CleanupExpiredAsync([FromQuery] int batchSize = 200)
        {
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string lastId = "0-0";

            while (true)
            {
                StreamEntry[] entries = await CacheManager.Cache.StreamRangeAsync(
                    StreamKey,
                    lastId,
                    "+",
                    count: batchSize
                );

                if (entries.Length == 0)
                    break;

                foreach (StreamEntry entry in entries)
                {
                    lastId = GetNextStreamId(entry.Id);

                    var expiryValue = entry
                        .Values.FirstOrDefault(v => v.Name == "ExpiryTimeUtc")
                        .Value;

                    if (
                        !expiryValue.HasValue
                        || !long.TryParse(expiryValue.ToString(), out long expiryUnix)
                        || expiryUnix > nowUnix
                    )
                        continue;

                    // 🔴 Expired entry → delete
                    _ = await CacheManager.Cache.DeleteMessagesAsync([entry.Id], StreamKey);
                }
            }

            response.Message = "Expired stream entries cleaned successfully.";
            return Ok(response);
        }

        /// <summary>
        /// Adds a new Redis stream entry (for testing purposes).
        /// </summary>
        /// <param name="redisStreamAddDTO">Redis stream add DTO.</param>
        /// <returns></returns>
        /// <response code="200">Stream entry added successfully.</response>
        [HttpPost("AddStreamEntry")]
        public async Task<IActionResult> Post(RedisStreamAddDTO redisStreamAddDTO)
        {
            try
            {
                long expiryTimeUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

                switch (redisStreamAddDTO.RedisOperation)
                {
                    // 🔴 Add new cache entry and stream message
                    case EnmRedisOperation.Add:
                        await CacheManager.Cache.SetAsync(
                            redisStreamAddDTO.SessionId,
                            redisStreamAddDTO.JSONValue,
                            TimeSpan.FromMinutes(redisStreamAddDTO.SessionTime)
                        );

                        await AddSessionIdValueToStreamAsync(redisStreamAddDTO);
                        break;

                    // 🔴 Remove cache entry by key
                    case EnmRedisOperation.Remove:
                        await CacheManager.Cache.RemoveKeyAsync(redisStreamAddDTO.SessionId);
                        await RemoveSessionIdValueFromStreamAsync(redisStreamAddDTO.SessionId);

                        break;

                    // 🔴 Update TTL of existing cache entry
                    case EnmRedisOperation.UpdateTTL:
                        await CacheManager.Cache.UpdateExpiryAsync(
                            redisStreamAddDTO.HashSessionId,
                            TimeSpan.FromMinutes(redisStreamAddDTO.SessionTime)
                        );

                        await AddSessionIdValueToStreamAsync(redisStreamAddDTO);
                        break;

                    // 🔴 Add or update specific field in hash
                    case EnmRedisOperation.HashAdd:
                        await CacheManager.Cache.AddHashKeyAsync(
                            redisStreamAddDTO.HashSessionId,
                            redisStreamAddDTO.SessionId,
                            redisStreamAddDTO.JSONValue
                        );

                        await AddSessionIdValueToStreamAsync(redisStreamAddDTO);
                        break;

                    // 🔴 Remove specific field from hash
                    case EnmRedisOperation.HashRemove:
                        await CacheManager.Cache.RemoveHashKeyAsync(
                            redisStreamAddDTO.HashSessionId,
                            redisStreamAddDTO.SessionId
                        );

                        await RemoveSessionIdValueFromStreamAsync(redisStreamAddDTO.SessionId);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            "Specify which operation to perform."
                        );
                }

                response.Message = "Operation completed successfully.";
            }
            catch (Exception ex)
            {
                response.IsError = true;
                response.Message = ex.Message;
                return Ok(response);
            }

            return Ok(response);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Adds a session ID and its associated value to the Redis stream.
        /// </summary>
        /// <param name="redisStreamAddDTO">Redis stream add DTO.</param>
        /// <returns></returns>
        private static async Task AddSessionIdValueToStreamAsync(
            RedisStreamAddDTO redisStreamAddDTO
        )
        {
            NameValueEntry[] entries =
            [
                new("SessionId", redisStreamAddDTO.SessionId),
                new(
                    "ExpiryTimeUtc",
                    DateTimeOffset
                        .UtcNow.AddMinutes(redisStreamAddDTO.SessionTime)
                        .ToUnixTimeSeconds()
                ),
                new("Value", redisStreamAddDTO.JSONValue),
            ];

            _ = await CacheManager.Cache.AddToStreamAsync(entries, StreamKey);
        }

        /// <summary>
        /// Removes all stream entries associated with a specific session ID.
        /// </summary>
        /// <param name="sessionId">Session ID to remove from stream.</param>
        /// <returns></returns>
        private static async Task RemoveSessionIdValueFromStreamAsync(string sessionId)
        {
            string lastId = "0-0";
            while (true)
            {
                StreamEntry[] entries = await CacheManager.Cache.GetStreamEntriesAsync(
                    StreamKey,
                    lastId,
                    count: 100
                );

                if (entries.Length == 0)
                    break;

                foreach (StreamEntry entry in entries)
                {
                    lastId = entry.Id;

                    RedisValue sessionIdValue = entry
                        .Values.FirstOrDefault(v => v.Name == "SessionId")
                        .Value;

                    if (!sessionIdValue.HasValue || sessionIdValue.ToString() != sessionId)
                        continue;

                    // 🔴 Matching entry → delete
                    _ = await CacheManager.Cache.DeleteMessagesAsync([entry.Id], StreamKey);
                }
            }
        }

        /// <summary>
        /// Generates the next Redis stream ID based on the provided ID.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private static string GetNextStreamId(string id)
        {
            var parts = id.Split('-');
            return $"{parts[0]}-{long.Parse(parts[1]) + 1}";
        }

        #endregion
    }
}
