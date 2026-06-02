namespace Intropy.BusinessIncidentService.Models;

public sealed record RetriedMetadata(DateTimeOffset TriggeredAt);
public sealed record RecurredMetadata(Guid PreviousId);
