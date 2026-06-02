namespace Intropy.BusinessIncidentService.Domain;

public static class IncidentEventTypes
{
    public const string Triggered = "triggered";
    public const string Retried = "retried";
    public const string ResolvedAuto = "resolved_auto";
    public const string ResolvedManual = "resolved_manual";
    public const string Recurred = "recurred";
}
