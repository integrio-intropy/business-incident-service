namespace Intropy.BusinessIncidentService.Models;

public sealed record IncidentEvent
{
    public Guid Id { get; init; }
    public Guid IncidentId { get; init; }
    public required string EventType { get; init; }
    public string? Metadata { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
