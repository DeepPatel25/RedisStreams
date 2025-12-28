using System.Text.Json;
using System.Text.Json.Serialization;
using MasterRedisAPI.Models;
using StackExchange.Redis;

namespace MasterRedisAPI.Helper
{
    /// <summary>
    /// Centralized Redis cache manager responsible for
    /// Redis Streams and basic key-value operations.
    /// </summary>
    /// <remarks>
    /// Implements a thread-safe Singleton pattern and wraps
    /// StackExchange.Redis operations for reuse across the application.
    /// </remarks>
    public sealed class CacheManager
    {
        #region Private Fields

        /// <summary>
        /// Redis connection multiplexer.
        /// </summary>
        private readonly IConnectionMultiplexer _redis;

        /// <summary>
        /// Redis database instance.
        /// </summary>
        private readonly IDatabase _db;

        /// <summary>
        /// Default Redis stream key used for master synchronization.
        /// </summary>
        private const string DefaultStreamKey = "master:sync:stream";

        /// <summary>
        /// Redis connection options configured during application startup.
        /// </summary>
        private static RedisConnectionOptions? _options;

        /// <summary>
        /// JSON serialization options for Redis value storage.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        #endregion

        #region Singleton Implementation

        /// <summary>
        /// Lazy-loaded singleton instance of <see cref="CacheManager"/>.
        /// </summary>
        private static readonly Lazy<CacheManager> _lazyInstance = new(() =>
            new CacheManager(_options ?? throw new Exception("Redis options not configured."))
        );

        /// <summary>
        /// Gets the singleton CacheManager instance.
        /// </summary>
        public static CacheManager Cache => _lazyInstance.Value;

        #endregion

        #region Configuration / Constructor

        /// <summary>
        /// Configures Redis connection options.
        /// Must be called once during application startup.
        /// </summary>
        /// <param name="options">Redis connection settings.</param>
        public static void Configure(RedisConnectionOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// Initializes Redis connection and database.
        /// </summary>
        /// <param name="options">Redis connection options.</param>
        private CacheManager(RedisConnectionOptions options)
        {
            ConfigurationOptions config = new()
            {
                AbortOnConnectFail = false,
                DefaultDatabase = options.Database,
            };

            config.EndPoints.Add(options.Host, options.Port);

            if (!string.IsNullOrWhiteSpace(options.User))
                config.User = options.User;

            if (!string.IsNullOrWhiteSpace(options.Password))
                config.Password = options.Password;

            _redis = ConnectionMultiplexer.Connect(config);
            _db = _redis.GetDatabase();
        }

        #endregion

        #region Redis Stream Operations

        /// <summary>
        /// Adds an entry to a Redis stream with automatic trimming.
        /// </summary>
        /// <param name="entries">Key-value entries to store.</param>
        /// <param name="streamKey">Optional stream key.</param>
        /// <returns>Generated Redis stream message ID.</returns>
        public async Task<string?> AddToStreamAsync(
            NameValueEntry[] entries,
            string? streamKey = null
        )
        {
            RedisValue messageId = await _db.StreamAddAsync(
                streamKey ?? DefaultStreamKey,
                entries,
                maxLength: 100000,
                useApproximateMaxLength: false
            );

            return messageId;
        }

        /// <summary>
        /// Creates a Redis consumer group if it does not already exist.
        /// </summary>
        /// <param name="groupName">Consumer group name.</param>
        /// <param name="streamKey">Optional stream key.</param>
        public async Task CreateConsumerGroupAsync(string groupName, string? streamKey = null)
        {
            try
            {
                await _db.StreamCreateConsumerGroupAsync(
                    streamKey ?? DefaultStreamKey,
                    groupName,
                    StreamPosition.Beginning,
                    createStream: true
                );
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Consumer group already exists – ignore
            }
        }

        /// <summary>
        /// Reads new messages from a Redis stream consumer group.
        /// </summary>
        /// <param name="groupName">Consumer group name.</param>
        /// <param name="consumerName">Consumer name.</param>
        /// <param name="count">Maximum number of messages.</param>
        /// <param name="streamKey">Optional stream key.</param>
        /// <returns>Array of stream entries.</returns>
        public async Task<StreamEntry[]> ReadStreamAsync(
            string groupName,
            string consumerName,
            int count = 100,
            string? streamKey = null
        )
        {
            return await _db.StreamReadGroupAsync(
                streamKey ?? DefaultStreamKey,
                groupName,
                consumerName,
                ">",
                count
            );
        }

        /// <summary>
        /// Acknowledges successfully processed stream messages.
        /// </summary>
        /// <param name="groupName">Consumer group name.</param>
        /// <param name="messageIds">Message IDs to acknowledge.</param>
        /// <param name="streamKey">Optional stream key.</param>
        public async Task AckMessagesAsync(
            string groupName,
            IEnumerable<string> messageIds,
            string? streamKey = null
        )
        {
            RedisValue[] redisIds = messageIds.Select(id => (RedisValue)id).ToArray();
            await _db.StreamAcknowledgeAsync(streamKey ?? DefaultStreamKey, groupName, redisIds);
        }

        /// <summary>
        /// Retrieves pending (unacknowledged) messages for a consumer.
        /// </summary>
        public async Task<StreamPendingMessageInfo[]> GetPendingMessagesAsync(
            string groupName,
            string consumerName,
            int count = 10,
            string? streamKey = null
        )
        {
            return await _db.StreamPendingMessagesAsync(
                streamKey ?? DefaultStreamKey,
                groupName,
                count,
                consumerName
            );
        }

        /// <summary>
        /// Retrieves pending messages along with their stored values.
        /// Expired messages are automatically deleted and acknowledged.
        /// </summary>
        /// <param name="groupName">Consumer group name.</param>
        /// <param name="consumerName">Consumer name.</param>
        /// <param name="count">Maximum number of messages.</param>
        /// <param name="streamKey">Optional stream key.</param>
        /// <returns>
        /// List of pending messages containing message ID,
        /// metadata, and key-value pairs.
        /// </returns>
        public async Task<
            List<(
                string MessageId,
                StreamPendingMessageInfo Meta,
                Dictionary<RedisValue, string> Values
            )>
        > GetPendingWithValuesAsync(
            string groupName,
            string consumerName,
            int count = 10,
            string? streamKey = null
        )
        {
            streamKey ??= DefaultStreamKey;

            StreamPendingMessageInfo[] pendingMeta = await _db.StreamPendingMessagesAsync(
                streamKey,
                groupName,
                count,
                consumerName
            );

            List<(string, StreamPendingMessageInfo, Dictionary<RedisValue, string>)> result = [];

            if (pendingMeta.Length == 0)
                return result;

            RedisValue[] pendingIds = pendingMeta.Select(p => (RedisValue)p.MessageId).ToArray();

            StreamEntry[] claimedMessages = await _db.StreamClaimAsync(
                streamKey,
                groupName,
                consumerName,
                minIdleTimeInMs: 0,
                messageIds: pendingIds
            );

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (StreamEntry msg in claimedMessages)
            {
                Dictionary<RedisValue, string> values = msg.Values.ToDictionary(
                    x => x.Name,
                    x => x.Value.ToString()
                );

                // 🔴 Expiry validation
                if (
                    values.TryGetValue("ExpiryTimeUtc", out string? expiryRaw)
                    && long.TryParse(expiryRaw, out long expiryUnix)
                    && expiryUnix <= nowUnix
                )
                {
                    await _db.StreamDeleteAsync(streamKey, [msg.Id]);
                    await _db.StreamAcknowledgeAsync(streamKey, groupName, msg.Id);
                    continue;
                }

                StreamPendingMessageInfo meta = pendingMeta.First(p => p.MessageId == msg.Id);

                result.Add((msg.Id.ToString(), meta, values));
            }

            return result;
        }

        /// <summary>
        /// Claims pending messages from other consumers (failover recovery).
        /// </summary>
        public async Task<StreamEntry[]> ClaimPendingAsync(
            string groupName,
            string consumerName,
            long minIdleTimeMs,
            IEnumerable<string> messageIds,
            string? streamKey = null
        )
        {
            RedisValue[] redisIds = messageIds.Select(id => (RedisValue)id).ToArray();

            return await _db.StreamClaimAsync(
                streamKey ?? DefaultStreamKey,
                groupName,
                consumerName,
                minIdleTimeMs,
                redisIds
            );
        }

        /// <summary>
        /// Deletes messages from a Redis stream by message ID.
        /// </summary>
        public async Task<long> DeleteMessagesAsync(
            IEnumerable<string> messageIds,
            string? streamKey = null
        )
        {
            RedisValue[] redisIds = messageIds.Select(id => (RedisValue)id).ToArray();
            return await _db.StreamDeleteAsync(streamKey ?? DefaultStreamKey, redisIds);
        }

        /// <summary>
        /// Trims the stream to the specified maximum length.
        /// </summary>
        public async Task<long> TrimStreamAsync(long maxLength = 100000, string? streamKey = null)
        {
            return await _db.StreamTrimAsync(
                streamKey ?? DefaultStreamKey,
                maxLength,
                useApproximateMaxLength: true
            );
        }

        /// <summary>
        /// Retrieves metadata information about the Redis stream.
        /// </summary>
        public async Task<StreamInfo> GetStreamInfoAsync(string? streamKey = null)
        {
            return await _db.StreamInfoAsync(streamKey ?? DefaultStreamKey);
        }

        /// <summary>
        /// Reads stream entries using XRANGE (admin / cleanup / replay use cases).
        /// </summary>
        public async Task<StreamEntry[]> StreamRangeAsync(
            string? streamKey = null,
            string? startId = "0-0",
            string endId = "+",
            int count = 100
        )
        {
            return await _db.StreamRangeAsync(streamKey ?? DefaultStreamKey, startId, endId, count);
        }

        #endregion

        #region General Redis Key-Value Operations

        /// <summary>
        /// Retrieves a string value from Redis by key.
        /// </summary>
        public async Task<string?> GetStringAsync(string key)
        {
            return await _db.StringGetAsync(key);
        }

        /// <summary>
        /// Stores a string value in Redis with expiration.
        /// </summary>
        public async Task SetStringAsync(string key, string value, TimeSpan expiry)
        {
            await _db.StringSetAsync(key, value, expiry);
        }

        /// <summary>
        /// Stores an object in Redis by serializing it to JSON.
        /// </summary>
        /// <typeparam name="T">Generic type of the object to store.</typeparam>
        /// <param name="key">Redis key.</param>
        /// <param name="value">Object to store.</param>
        /// <param name="expiry">Expiration time span.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            return await _db.StringSetAsync(key, bytes, expiry);
        }

        /// <summary>
        /// Retrieves and deserializes an object from Redis by key.
        /// </summary>
        /// <typeparam name="T">Generic type of the object to retrieve.</typeparam>
        /// <param name="key">Redis key.</param>
        /// <returns></returns>
        public async Task<T?> GetAsync<T>(string key)
        {
            RedisValue value = await _db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>((byte[])value!, _jsonOptions);
        }

        /// <summary>
        /// Removes a key from Redis.
        /// </summary>
        /// <param name="key">Redis key.</param>
        /// <returns></returns>
        public async Task<bool> RemoveKeyAsync(string key)
        {
            return await _db.KeyDeleteAsync(key);
        }

        /// <summary>
        /// Updates the expiration time of a Redis key.
        /// </summary>
        /// <param name="key">Redis key.</param>
        /// <param name="expiry">Expiration time span.</param>
        /// <returns></returns>
        public async Task<bool> UpdateExpiryAsync(string key, TimeSpan expiry)
        {
            return await _db.KeyExpireAsync(key, expiry);
        }

        /// <summary>
        /// Adds a value to a Redis hash by serializing it to JSON.
        /// </summary>
        /// <typeparam name="T">Generic type of the object to store.</typeparam>
        /// <param name="hashKey">Redis hash key.</param>
        /// <param name="key">Hash field key.</param>
        /// <param name="value">Object to store.</param>
        /// <returns></returns>
        public async Task<bool> AddHashKeyAsync<T>(string hashKey, string key, T value)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            return await _db.HashSetAsync(hashKey, key, bytes);
        }

        /// <summary>
        /// Removes a field from a Redis hash.
        /// </summary>
        /// <param name="hashKey">Redis hash key.</param>
        /// <param name="key">Hash field key.</param>
        /// <returns></returns>
        public async Task<bool> RemoveHashKeyAsync(string hashKey, string key)
        {
            return await _db.HashDeleteAsync(hashKey, key);
        }

        /// <summary>
        /// Retrieves all entries from a Redis stream.
        /// </summary>
        /// <param name="streamKey">Redis stream key.</param>
        /// <returns></returns>
        public async Task<StreamEntry[]> GetStreamEntriesAsync(
            string streamKey,
            string lastId,
            int count = 1000
        )
        {
            return await _db.StreamRangeAsync(streamKey, count: count, minId: lastId);
        }

        #endregion
    }
}
