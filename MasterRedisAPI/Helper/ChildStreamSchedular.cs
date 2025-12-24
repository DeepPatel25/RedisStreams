using MasterRedisAPI.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace MasterRedisAPI.Helper;

/// <summary>
/// Background scheduler responsible for pulling Redis Stream messages
/// from the master API and processing them on child nodes.
/// </summary>
/// <remarks>
/// This service runs only on CHILD nodes.
/// It periodically pulls messages from the master Redis stream via HTTP APIs,
/// applies business logic locally, and acknowledges messages after successful processing.
/// </remarks>
public sealed class ChildStreamScheduler(
    MasterApiOptions options,
    IOptions<RedisNodeOptions> redisOptions,
    ILogger<ChildStreamScheduler> logger
) : BackgroundService
{
    #region Private Fields

    /// <summary>
    /// Master API endpoint configuration.
    /// </summary>
    private readonly MasterApiOptions _options = options;

    /// <summary>
    /// Logger instance for scheduler diagnostics.
    /// </summary>
    private readonly ILogger<ChildStreamScheduler> _logger = logger;

    #endregion

    #region Background Execution

    /// <summary>
    /// Executes the background polling loop for child stream synchronization.
    /// </summary>
    /// <param name="stoppingToken">
    /// Cancellation token triggered when the application is shutting down.
    /// </param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not run scheduler on Master node
        if (redisOptions.Value.Role == RedisEnumRole.Master)
            return;

        _logger.LogInformation(
            "Child Stream Scheduler started | Consumer={Consumer} | Group={Group}",
            redisOptions.Value.ConsumerName,
            redisOptions.Value.ConsumerGroup
        );

        // Ensure consumer group exists on master
        await InvokeAsync(
            $"{_options.CreateGroupEndpoint}?groupName={redisOptions.Value.ConsumerGroup}",
            Method.Post
        );

        // Poll loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PullAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Child Stream Scheduler execution failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }

    #endregion

    #region Stream Processing

    /// <summary>
    /// Pulls messages from the master Redis stream and processes them.
    /// </summary>
    /// <remarks>
    /// Pending messages are prioritized by the master API.
    /// New messages are pulled only if no pending messages exist.
    /// Messages are acknowledged only after successful processing.
    /// </remarks>
    /// <param name="token">Cancellation token.</param>
    private async Task PullAndProcessAsync(CancellationToken token)
    {
        Response? response = await InvokeAsync(
            $"{_options.PullEndpoint}"
                + $"?consumerGroup={redisOptions.Value.ConsumerGroup}"
                + $"&consumerName={redisOptions.Value.ConsumerName}"
                + $"&batchSize={_options.BatchSize}",
            Method.Get
        );

        if (response?.IsError != false || response.DataModel == null)
            return;

        List<StreamMessageDto<StreamDataDto>> messages =
            JsonConvert.DeserializeObject<List<StreamMessageDto<StreamDataDto>>>(
                response.DataModel.ToString()!
            ) ?? [];

        if (messages.Count == 0)
            return;

        foreach (StreamMessageDto<StreamDataDto> msg in messages)
        {
            // 🔹 Apply business logic (cache locally)
            await CacheManager.Cache.SetStringAsync(
                msg.Data.SessionId,
                msg.Data.JsonValue,
                TimeSpan.FromMinutes(5)
            );

            // 🔹 Acknowledge message after successful processing
            await InvokeAsync(
                $"{_options.AckEndpoint}"
                    + $"?consumerGroup={redisOptions.Value.ConsumerGroup}"
                    + $"&messageId={msg.MessageId}",
                Method.Post
            );

            _logger.LogInformation(
                "Stream message processed and acknowledged | MessageId={MessageId}",
                msg.MessageId
            );
        }
    }

    #endregion

    #region HTTP Invocation Helper

    /// <summary>
    /// Invokes a master API endpoint asynchronously.
    /// </summary>
    /// <param name="endpoint">Relative API endpoint URL.</param>
    /// <param name="method">HTTP method to use.</param>
    /// <returns>
    /// Deserialized <see cref="Response"/> object or error response.
    /// </returns>
    private async Task<Response?> InvokeAsync(string endpoint, Method method)
    {
        Response response = new();

        RestClient restClient = new("http://localhost:5000");
        RestRequest restRequest = new(endpoint, method);
        restRequest.AddHeader("Accept", "application/json");

        RestResponse restResponse = await restClient.ExecuteAsync(restRequest);

        if (restResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            response =
                JsonConvert.DeserializeObject<Response>(restResponse.Content ?? "{}")
                ?? new Response();

            // Normalize DataModel if returned as JObject
            if (!response.IsError && response.DataModel is JObject)
            {
                response.DataModel = JsonConvert.DeserializeObject<object>(
                    response.DataModel.ToString() ?? "{}"
                );
            }
        }
        else
        {
            response.IsError = true;
            response.Message = restResponse.Content;
        }

        return response;
    }

    #endregion
}
