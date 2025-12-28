namespace MasterRedisAPI.Models;

/// <summary>
/// Represents a Redis stream message wrapper.
/// </summary>
/// <typeparam name="T">
/// Type of the payload carried by the stream message.
/// </typeparam>
public class StreamMessageDto<T>
{
    /// <summary>
    /// Unique Redis stream message identifier.
    /// </summary>
    public string MessageId { get; set; } = default!;

    /// <summary>
    /// Message payload containing stream data.
    /// </summary>
    public T Data { get; set; } = default!;
}

/// <summary>
/// Represents the data stored within a Redis stream message.
/// </summary>
/// <remarks>
/// This DTO is used for master-to-child synchronization and
/// contains session-related information along with expiry metadata.
/// </remarks>
public class StreamDataDto
{
    /// <summary>
    /// Unique session or token identifier.
    /// </summary>
    public string SessionId { get; set; } = default!;

    /// <summary>
    /// Absolute expiry time represented as Unix timestamp (UTC seconds).
    /// </summary>
    public long ExpiryTimeUtc { get; set; }

    /// <summary>
    /// Serialized JSON value associated with the session.
    /// </summary>
    public string JsonValue { get; set; } = default!;
}

/// <summary>
/// Add Value in Redis Stream DTO
/// </summary>
public class RedisStreamAddDTO
{
    /// <summary>
    /// Session Id Value
    /// </summary>
    public string SessionId { get; set; } = default!;

    /// <summary>
    /// Hash Session Id Value
    /// </summary>
    public string HashSessionId { get; set; } = default!;

    /// <summary>
    /// Session Time Value
    /// </summary>
    public int SessionTime { get; set; }

    /// <summary>
    /// JSON Value
    /// </summary>
    public string JSONValue { get; set; } = default!;

    /// <summary>
    /// Redis Operation Type
    /// </summary>
    public EnmRedisOperation RedisOperation { get; set; }
}

/// <summary>
/// Enumeration of Redis operations.
/// </summary>
public enum EnmRedisOperation
{
    /// <summary>
    /// Add operation.
    /// </summary>
    Add = 1,

    /// <summary>
    /// Remove operation.
    /// </summary>
    Remove = 2,

    /// <summary>
    /// Update TTL operation.
    /// </summary>
    UpdateTTL = 3,

    /// <summary>
    /// Hash Add operation.
    /// </summary>
    HashAdd = 4,

    /// <summary>
    /// Hash Remove operation.
    /// </summary>
    HashRemove = 5,
}
