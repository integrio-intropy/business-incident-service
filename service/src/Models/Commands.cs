namespace Intropy.BusinessIncidentService.Models;

public sealed record TriggeredCommand(
    string Source,
    string CeId,
    string Subject,
    DateTimeOffset Time,
    string? BatchId,
    string Description,
    Dictionary<string, string>? Context);

public sealed record ResolvedCommand(
    string Source,
    string CeId,
    DateTimeOffset Time);
