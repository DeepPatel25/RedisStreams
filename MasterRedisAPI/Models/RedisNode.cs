namespace MasterRedisAPI.Models;

/// <summary>
/// Defines the operational role of a Redis node within the system.
/// </summary>
/// <remarks>
/// The role determines whether the node acts as a master (publisher)
/// or a child (consumer) in the Redis stream synchronization architecture.
/// </remarks>
public enum RedisEnumRole
{
    /// <summary>
    /// Master node responsible for publishing messages
    /// to the Redis stream.
    /// </summary>
    Master,

    /// <summary>
    /// Child node responsible for consuming and processing
    /// messages from the Redis stream.
    /// </summary>
    Child,
}
