namespace Intropy.BusinessIncidentService.Client;

/// <summary>
/// Represents an error returned by, or while communicating with, the Business Incident Service.
/// </summary>
public class BusinessIncidentServiceException : Exception
{
    /// <summary>
    /// HTTP status code returned by the service, or <c>null</c> when the failure occurred
    /// before a response was received (e.g. transport or Dapr errors).
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Response body (typically a problem-details payload) returned by the service, when available.
    /// </summary>
    public string? Details { get; }

    /// <summary>
    /// Initializes a new instance for failures that occurred before a response was received.
    /// </summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="innerException">The underlying exception.</param>
    public BusinessIncidentServiceException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance for failures returned by the service.
    /// </summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="statusCode">HTTP status code returned by the service.</param>
    /// <param name="details">Response body returned by the service, when available.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public BusinessIncidentServiceException(string message, int statusCode, string? details, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Details = details;
    }
}
