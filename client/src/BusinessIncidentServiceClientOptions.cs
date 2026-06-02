namespace Intropy.BusinessIncidentService.Client;

/// <summary>
/// Configuration options for the Business Incident Service client.
/// </summary>
public class BusinessIncidentServiceClientOptions
{
    /// <summary>
    /// Gets or sets the Dapr app ID of the target business incident service.
    /// Default value is "business-incident-service".
    /// </summary>
    public string AppId { get; set; } = "business-incident-service";
}