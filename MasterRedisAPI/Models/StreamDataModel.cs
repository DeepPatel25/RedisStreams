namespace MasterRedisAPI.Models;

/// <summary>
/// Represents a Redis stream entry with absolute expiry timestamp.
/// </summary>
/// <remarks>
/// Used for caching session/token data in Redis streams.
/// Contains the session ID, expiry time (UTC Unix timestamp),
/// and the serialized JSON value.
/// </remarks>
public class StreamDataModel
{
    /// <summary>
    /// Unique session or token identifier.
    /// </summary>
    public string SessionId { get; set; } = null!;

    /// <summary>
    /// Absolute expiry time as a Unix timestamp in seconds (UTC).
    /// </summary>
    /// <remarks>
    /// Used for cleaning up expired messages in the stream.
    /// </remarks>
    public long ExpiryTimeUtc { get; set; }

    /// <summary>
    /// Serialized JSON value associated with the session.
    /// </summary>
    public string JsonValue { get; set; } = null!;
}
