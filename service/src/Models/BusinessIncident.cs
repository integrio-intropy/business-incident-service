namespace Intropy.BusinessIncidentService.Models;

public sealed record BusinessIncident
{
    public Guid Id { get; init; }
    public Guid? PreviousId { get; init; }
    public required string Source { get; init; }
    public required string CeId { get; init; }
    public required string Subject { get; init; }
    public required string Description { get; init; }
    public required string Status { get; init; }
    public string? BatchId { get; init; }
    public string? Context { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset TriggeredAt { get; init; }
    public DateTimeOffset? LastRetriedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? ResolvedBy { get; init; }
}
