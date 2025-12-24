namespace MasterRedisAPI.Models;

/// <summary>
/// Represents configuration settings required to connect to a Redis server.
/// </summary>
/// <remarks>
/// This class is used with the .NET Options pattern to configure
/// Redis connectivity for cache and stream operations.
/// </remarks>
public sealed class RedisConnectionOptions
{
    /// <summary>
    /// Redis server host name or IP address.
    /// </summary>
    /// <example>localhost</example>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Redis server port number.
    /// </summary>
    /// <example>6379</example>
    public int Port { get; set; } = 6379;

    /// <summary>
    /// Optional Redis ACL username.
    /// </summary>
    /// <remarks>
    /// Used when Redis ACL authentication is enabled.
    /// </remarks>
    public string? User { get; set; }

    /// <summary>
    /// Optional Redis authentication password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Redis logical database index.
    /// </summary>
    /// <remarks>
    /// Default is database 0. Redis supports multiple logical databases per instance.
    /// </remarks>
    public int Database { get; set; } = 0;
}
