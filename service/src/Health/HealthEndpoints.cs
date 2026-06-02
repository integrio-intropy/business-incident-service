using Intropy.BusinessIncidentService.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Intropy.BusinessIncidentService.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/healthz/live", new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(HealthCheckTypes.Liveness)
        }).RequireHost($"*:{Ports.Internal}");

        endpoints.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(HealthCheckTypes.Readiness)
        }).RequireHost($"*:{Ports.Internal}");

        return endpoints;
    }
}