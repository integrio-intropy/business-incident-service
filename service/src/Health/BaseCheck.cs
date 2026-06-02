using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Intropy.BusinessIncidentService.Health;

public class BaseCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("OK"));
    }
}