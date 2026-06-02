using System.Diagnostics;

namespace Intropy.BusinessIncidentService.Telemetry;

public static class ActivitySourceProvider
{
    public const string ActivitySourceName = "Intropy.BusniessIncidentService";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}