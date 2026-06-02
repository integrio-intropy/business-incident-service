using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Dapr.Client;
using Intropy.BusinessIncidentService.Configuration;
using Intropy.BusinessIncidentService.Database;
using Intropy.BusinessIncidentService.Domain;
using Intropy.Contracts.BusinessIncidentService;
using Microsoft.Extensions.Options;

namespace Intropy.BusinessIncidentService.Endpoints;

public static class OutboxEndpoints
{
    private const int OutboxBatchSize = 100;
    private static readonly TimeSpan ClaimTimeOut = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PurgeRetention = TimeSpan.FromDays(7);
    private static readonly CloudEventFormatter CloudEventFormatter = new JsonEventFormatter();

    private static readonly Dictionary<string, string> EventTypeMap = new()
    {
        [IncidentEventTypes.Triggered] = EventTypes.NotificationTriggered,
        [IncidentEventTypes.Retried] = EventTypes.NotificationRetried,
        [IncidentEventTypes.ResolvedAuto] = EventTypes.NotificationResolved,
        [IncidentEventTypes.ResolvedManual] = EventTypes.NotificationResolved,
        [IncidentEventTypes.Recurred] = EventTypes.NotificationRecurred
    };

    public static IEndpointRouteBuilder MapOutboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/outbox/process",
                async (IncidentRepository repository,
                    DaprClient dapr,
                    IOptions<DaprComponentsConfig> daprConfig,
                    ILogger<Program> logger) =>
                {
                    var config = daprConfig.Value;
                    var batch = await repository.ClaimOutboxBatch(OutboxBatchSize, ClaimTimeOut);

                    foreach (var (id, incidentId, eventType) in batch)
                    {
                        var cloudEvent = CreateEvent(id, incidentId, eventType);
                        var bytes = CloudEventFormatter.EncodeStructuredModeMessage(cloudEvent, out _);

                        try
                        {
                            await dapr.PublishByteEventAsync(
                                config.NotificationPubSubName,
                                config.NotificationTopicName,
                                bytes,
                                "application/cloudevents+json");

                            await repository.MarkOutboxEventPublished(id);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "Failed to publish outbox event {outbox.id} for incident {business_incident.id}, " +
                                "claim will expire for retry",
                                id, incidentId);

                            await repository.ReleaseOutboxClaim(id);
                        }
                    }

                    return Results.Ok();
                })
            .ExcludeFromDescription()
            .RequireHost($"*:{Ports.Internal}");

        endpoints.MapPost("/outbox/purge",
                async (IncidentRepository repository, ILogger<Program> logger) =>
                {
                    var deleted = await repository.PurgePublishedOutboxEvents(PurgeRetention);
                    logger.LogInformation("Purged {purge_count} published outbox events", deleted);
                    return Results.Ok(new { deleted });
                })
            .ExcludeFromDescription()
            .RequireHost($"*:{Ports.Internal}");

        return endpoints;
    }

    private static CloudEvent CreateEvent(Guid id, Guid incidentId, string eventType)
    {
        return new CloudEvent
        {
            Source = new Uri("urn:intropy:businessincidentservice"),
            Type = EventTypeMap.GetValueOrDefault(eventType, eventType),
            Id = id.ToString(),
            Subject = incidentId.ToString(),
            Time = DateTimeOffset.UtcNow
        };
    }
}