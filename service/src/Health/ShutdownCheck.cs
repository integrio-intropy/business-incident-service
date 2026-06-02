using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Intropy.BusinessIncidentService.Health;

public class ShutdownCheck : IHealthCheck
{
    private volatile bool _isShuttingDown;

    public ShutdownCheck(IHostApplicationLifetime applicationLifetime)
    {
        applicationLifetime.ApplicationStopping.Register(() =>
        {
            _isShuttingDown = true;
        });
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        if (_isShuttingDown)
            return Task.FromResult(HealthCheckResult.Unhealthy("Application is shutting down"));

        return Task.FromResult(HealthCheckResult.Healthy("Application is running"));
    }
}