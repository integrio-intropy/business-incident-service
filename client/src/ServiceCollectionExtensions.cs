using Intropy.Contracts.BusinessIncidentService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Intropy.BusinessIncidentService.Client;

/// <summary>
/// Dependency injection extensions for registering the Business Incident Service client.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BusinessIncidentServiceClient"/> as a singleton and binds
    /// <see cref="BusinessIncidentServiceClientOptions"/> from the supplied delegate.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configureOptions">Optional delegate for customizing the client options.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddBusinessIncidentServiceClient(
        this IServiceCollection services,
        Action<BusinessIncidentServiceClientOptions>? configureOptions = null)
    {
        services.Configure<BusinessIncidentServiceClientOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        services.AddSingleton<IBusinessIncidentServiceClient, BusinessIncidentServiceClient>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="BusinessIncidentServiceClient"/> as a singleton and binds
    /// <see cref="BusinessIncidentServiceClientOptions"/> from the <c>BusinessIncidentService</c>
    /// configuration section.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration root to bind options from.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddBusinessIncidentServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BusinessIncidentServiceClientOptions>(
            configuration.GetSection("BusinessIncidentService"));
        return services.AddBusinessIncidentServiceClient();
    }
}
