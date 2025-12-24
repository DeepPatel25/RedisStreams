namespace MasterRedisAPI.Models;

/// <summary>
/// Standard API response wrapper used for Redis stream operations.
/// </summary>
/// <remarks>
/// Provides a consistent structure for API responses, including
/// error status, optional data payload, and informational messages.
/// </remarks>
public class Response
{
    /// <summary>
    /// Flag indicating whether the request resulted in an error.
    /// </summary>
    /// <remarks>
    /// True if an error occurred; otherwise, false.
    /// </remarks>
    public bool IsError { get; set; } = false;

    /// <summary>
    /// Optional data payload returned by the API.
    /// </summary>
    /// <remarks>
    /// Can hold any object, including stream messages, status info, or custom models.
    /// </remarks>
    public object? DataModel { get; set; }

    /// <summary>
    /// Informational message describing the result of the API request.
    /// </summary>
    /// <remarks>
    /// Typically used for error messages or confirmation messages.
    /// </remarks>
    public string Message { get; set; } = string.Empty;
}
