namespace MasterRedisAPI.Models;

/// <summary>
/// Configuration options for a Redis node (Master or Child).
/// </summary>
/// <remarks>
/// This class is used with the .NET Options pattern to configure
/// the role, consumer group, stream key, and consumer identity
/// for Redis stream synchronization.
/// </remarks>
public class RedisNodeOptions
{
    /// <summary>
    /// Operational role of the Redis node.
    /// Determines whether the node acts as a Master or Child.
    /// </summary>
    public RedisEnumRole Role { get; set; }

    /// <summary>
    /// Name of the consumer for Redis Streams.
    /// Defaults to the machine name.
    /// </summary>
    public string ConsumerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Redis stream key used for synchronization.
    /// </summary>
    /// <example>master:sync:stream</example>
    public string StreamKey { get; set; } = "master:sync:stream";

    /// <summary>
    /// Consumer group name used for Redis stream consumption.
    /// </summary>
    /// <example>child-group</example>
    public string ConsumerGroup { get; set; } = "child-group";
}
