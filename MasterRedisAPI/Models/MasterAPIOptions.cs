namespace MasterRedisAPI.Models;

/// <summary>
/// Configuration options for interacting with the Master Redis API.
/// </summary>
/// <remarks>
/// This class is used with the .NET Options pattern to configure
/// endpoints, batching behavior, and polling intervals for
/// master-to-child Redis stream synchronization.
/// </remarks>
public class MasterApiOptions
{
    /// <summary>
    /// Base URL of the Master API.
    /// </summary>
    /// <example>http://localhost:5000</example>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// API endpoint used to pull Redis stream messages.
    /// </summary>
    /// <example>/api/CLRedis/pull</example>
    public string PullEndpoint { get; set; } = "/api/CLRedis/pull";

    /// <summary>
    /// API endpoint used to acknowledge processed stream messages.
    /// </summary>
    /// <example>/api/CLRedis/Acknowledge</example>
    public string AckEndpoint { get; set; } = "/api/CLRedis/Acknowledge";

    /// <summary>
    /// API endpoint used to create a Redis consumer group.
    /// </summary>
    /// <example>/api/CLRedis/CreateGroup</example>
    public string CreateGroupEndpoint { get; set; } = "/api/CLRedis/CreateGroup";

    /// <summary>
    /// Number of stream messages fetched per polling cycle.
    /// </summary>
    /// <remarks>
    /// Higher values improve throughput but may increase processing latency.
    /// </remarks>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Interval in seconds between polling attempts.
    /// </summary>
    /// <remarks>
    /// Lower values provide faster synchronization at the cost of higher load.
    /// </remarks>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Name of the Redis consumer group used by child nodes.
    /// </summary>
    public string ConsumerGroup { get; set; } = null!;

    /// <summary>
    /// Name of the Redis consumer within the consumer group.
    /// </summary>
    public string ConsumerName { get; set; } = null!;
}
