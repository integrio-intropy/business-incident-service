using System.Net;
using System.Text.Json;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.Http;
using CloudNative.CloudEvents.SystemTextJson;
using Dapr;
using Dapr.Client;
using Intropy.Contracts.BusinessIncidentService;
using Microsoft.Extensions.Options;
using CloudEvent = CloudNative.CloudEvents.CloudEvent;

namespace Intropy.BusinessIncidentService.Client;

/// <summary>
/// Client for interacting with the Intropy Business Incident Service via Dapr service invocation.
/// Use it to trigger and resolve incidents (as CloudEvents) and to query incident state and history.
/// </summary>
/// <param name="daprClient">The Dapr client used for service invocation.</param>
/// <param name="options">Client configuration, including the target Dapr app id.</param>
public class BusinessIncidentServiceClient(
    DaprClient daprClient,
    IOptions<BusinessIncidentServiceClientOptions> options)
{
    private readonly DaprClient _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
    private readonly string _appId = options.Value.AppId ?? throw new ArgumentNullException(nameof(options));

    private static readonly JsonEventFormatter JsonEventFormatter = new();

    /// <summary>
    /// Triggers a new business incident by sending a <c>Triggered</c> CloudEvent to the service.
    /// </summary>
    /// <param name="source">URI identifying the origin system (used as the CloudEvent <c>source</c>).</param>
    /// <param name="subject">Logical subject the incident relates to (e.g. an operation or resource name).</param>
    /// <param name="id">Unique CloudEvent id. Repeated calls with the same id are deduplicated by the service.</param>
    /// <param name="data">Incident payload, including the human-readable description.</param>
    /// <param name="batchId">Optional batch id used to group related incidents.</param>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task Trigger(Uri source, string subject, string id, BusinessIncidentData data, string? batchId)
    {
        var cloudEvent = new CloudEvent
        {
            Source = source,
            Id = id,
            Subject = subject,
            Type = EventTypes.Triggered,
            DataContentType = "application/json",
            Data = data,
            Time = DateTimeOffset.UtcNow,
            ["batchid"] = batchId
        };

        await SendEvent(cloudEvent);
    }

    /// <summary>
    /// Retrieves a single incident by its id.
    /// </summary>
    /// <param name="id">The incident id.</param>
    /// <returns>The incident, or <c>null</c> if no incident with that id exists.</returns>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task<IncidentResponse?> GetById(Guid id)
    {
        return await Invoke<IncidentResponse?>(HttpMethod.Get, $"incidents/{id}", allowNotFound: true);
    }

    /// <summary>
    /// Lists incidents matching the supplied filter, with optional pagination.
    /// </summary>
    /// <param name="filter">Optional filter and pagination parameters. When <c>null</c>, all incidents are returned using the service's default page size.</param>
    /// <returns>A paged list of incidents.</returns>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task<IncidentListResponse> List(IncidentFilter? filter = null)
    {
        var query = BuildQuery(filter);
        var path = string.IsNullOrEmpty(query) ? "incidents" : $"incidents?{query}";
        return (await Invoke<IncidentListResponse>(HttpMethod.Get, path))!;
    }

    /// <summary>
    /// Retrieves the event history for an incident.
    /// </summary>
    /// <param name="id">The incident id.</param>
    /// <returns>The events recorded for the incident, in chronological order.</returns>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task<List<IncidentEventResponse>> GetEvents(Guid id)
    {
        return (await Invoke<List<IncidentEventResponse>>(HttpMethod.Get, $"incidents/{id}/events"))!;
    }

    /// <summary>
    /// Manually marks an incident as resolved.
    /// </summary>
    /// <param name="id">The incident id.</param>
    /// <returns><c>true</c> if the incident was resolved; <c>false</c> if no incident with that id exists.</returns>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task<bool> ResolveManual(Guid id)
    {
        var response = await InvokeRaw(HttpMethod.Post, $"incidents/{id}/resolve");
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    /// <summary>
    /// Auto-resolves an incident by sending a <c>Resolved</c> CloudEvent to the service.
    /// </summary>
    /// <param name="source">URI identifying the origin system (used as the CloudEvent <c>source</c>).</param>
    /// <param name="subject">Logical subject the incident relates to. Must match the value used when triggering.</param>
    /// <param name="id">Unique CloudEvent id. Repeated calls with the same id are deduplicated by the service.</param>
    /// <param name="batchId">Optional batch id used to group related incidents.</param>
    /// <exception cref="BusinessIncidentServiceException">The service returned an error response or could not be reached.</exception>
    public async Task Resolve(Uri source, string subject, string id, string? batchId)
    {
        var cloudEvent = new CloudEvent
        {
            Source = source,
            Id = id,
            Subject = subject,
            Type = EventTypes.Resolved,
            DataContentType = "application/json",
            Data = new { },
            Time = DateTimeOffset.UtcNow,
            ["batchid"] = batchId
        };

        await SendEvent(cloudEvent);
    }

    private async Task<T?> Invoke<T>(HttpMethod method, string path, bool allowNotFound = false)
    {
        var response = await InvokeRaw(method, path);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return default;

        return await JsonSerializer.DeserializeAsync<T>(
            await response.Content.ReadAsStreamAsync());
    }

    private async Task<HttpResponseMessage> InvokeRaw(HttpMethod method, string path)
    {
        using var client = _daprClient.CreateInvokableHttpClient(_appId);
        var request = new HttpRequestMessage(method, path);

        try
        {
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var problemDetails = await response.Content.ReadAsStringAsync();
                throw new BusinessIncidentServiceException(
                    "Failed to call the business incident service",
                    (int)response.StatusCode,
                    problemDetails,
                    null);
            }

            return response;
        }
        catch (InvocationException ie)
        {
            throw new BusinessIncidentServiceException(
                "Failed to call the business incident service",
                500, ie.Message, ie);
        }
        catch (DaprException de)
        {
            throw new BusinessIncidentServiceException("Failed to communicate with dapr", de);
        }
        catch (Exception ex)
        {
            throw new BusinessIncidentServiceException("Unknown exception", ex);
        }
    }

    private static string BuildQuery(IncidentFilter? filter)
    {
        if (filter is null) return "";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(filter.Source)) parts.Add($"source={Uri.EscapeDataString(filter.Source)}");
        if (!string.IsNullOrEmpty(filter.Status)) parts.Add($"status={Uri.EscapeDataString(filter.Status)}");
        if (filter.Page.HasValue) parts.Add($"page={filter.Page.Value}");
        if (filter.PageSize.HasValue) parts.Add($"page_size={filter.PageSize.Value}");
        return string.Join("&", parts);
    }

    private async Task SendEvent(CloudEvent cloudEvent)
    {
        using var client = _daprClient.CreateInvokableHttpClient(_appId);
        var content = cloudEvent.ToHttpContent(ContentMode.Structured, JsonEventFormatter);

        var request = new HttpRequestMessage(HttpMethod.Post, "events");
        request.Content = content;

        try
        {
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var problemDetails = await response.Content.ReadAsStringAsync();
                throw new BusinessIncidentServiceException(
                    "Failed to call the business incident service",
                    (int)response.StatusCode,
                    problemDetails,
                    null);
            }
        }
        catch (InvocationException ie)
        {
            throw new BusinessIncidentServiceException(
                "Failed to call the business incident service",
                500,
                ie.Message,
                ie);
        }
        catch (DaprException de)
        {
            throw new BusinessIncidentServiceException("Failed to communicate with dapr", de);
        }
        catch (Exception ex)
        {
            throw new BusinessIncidentServiceException("Unknown exception", ex);
        }
    }
}