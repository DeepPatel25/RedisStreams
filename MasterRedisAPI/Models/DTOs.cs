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
