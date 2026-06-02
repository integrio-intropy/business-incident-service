using System.Diagnostics;
using System.Text.Json;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Dapr.Client;
using Intropy.BusinessIncidentService.Configuration;
using Intropy.BusinessIncidentService.Database;
using Intropy.BusinessIncidentService.Filters;
using Intropy.BusinessIncidentService.Models;
using Intropy.BusinessIncidentService.Utils;
using Intropy.Contracts.BusinessIncidentService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Intropy.BusinessIncidentService.Endpoints;

public static class EventEndpoints
{
    private static readonly CloudEventAttribute BatchIdAttribute =
        CloudEventAttribute.CreateExtension("batchid", CloudEventAttributeType.String);

    private static readonly JsonEventFormatter Formatter = new();

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/events",
                async (CloudEvent cloudEvent,
                    [FromServices] DaprClient dapr,
                    [FromServices] IOptions<DaprComponentsConfig> daprConfig) =>
                {
                    var activity = Activity.Current;

                    if (cloudEvent.Type != EventTypes.Triggered && cloudEvent.Type != EventTypes.Resolved)
                        return Results.BadRequest($"Unsupported event type: {cloudEvent.Type}");

                    activity?.SetTag("cloudevent.type", cloudEvent.Type);
                    activity?.SetTag("cloudevent.source", cloudEvent.Source?.ToString());
                    activity?.SetTag("cloudevent.id", cloudEvent.Id);

                    var config = daprConfig.Value;

                    var bytes = Formatter.EncodeStructuredModeMessage(cloudEvent, out _);
                    await dapr.PublishByteEventAsync(config.InternalPubSubName, config.InternalTopicName, bytes,
                        "application/cloudevents+json");

                    return Results.Accepted();
                })
            .WithName("receiveEvent")
            .WithDisplayName("Receive CloudEvent")
            .Produces(202)
            .Produces<ProblemDetails>(400)
            .ExcludeFromDescription()
            .RequireHost($"*:{Ports.Internal}");

        endpoints.MapPost("/events/subscribe/triggered",
            async (CloudEvent cloudEvent,
                IncidentRepository repository,
                ILogger<Program> logger) =>
            {
                var rawSource = cloudEvent.Source?.ToString()
                               ?? throw new InvalidOperationException("CloudEvent source is required");

                var source = SourceParser.Parse(rawSource);

                if (cloudEvent.Data == null)
                    return Results.Problem(detail: "data payload is required for .triggered events", statusCode: 400);

                var data = ((JsonElement)cloudEvent.Data).Deserialize<BusinessIncidentData>()
                           ?? throw new InvalidOperationException("data deserialized to null");

                if (string.IsNullOrWhiteSpace(data.Description))
                    return Results.Problem(detail: "data.description is required", statusCode: 400);

                var command = new TriggeredCommand(
                    Source: source,
                    CeId: cloudEvent.Id!,
                    Subject: cloudEvent.Subject!,
                    Time: cloudEvent.Time!.Value,
                    BatchId: cloudEvent[BatchIdAttribute] as string,
                    Description: data.Description,
                    Context: data.Context);

                var (id, wasInserted) = await repository.UpsertTriggered(command);

                logger.LogInformation(
                    wasInserted
                        ? "Created incident {id} for {source}:{ceId}"
                        : "Retried incident {id} for {source}:{ceId}",
                    id, source, cloudEvent.Id);

                return Results.Ok();
            })
            .ExcludeFromDescription()
            .AddEndpointFilter<ExceptionFilter>()
            .RequireHost($"*:{Ports.Internal}");

        endpoints.MapPost("/events/subscribe/resolved",
            async (CloudEvent cloudEvent,
                IncidentRepository repository,
                ILogger<Program> logger) =>
            {
                var rawSource = cloudEvent.Source?.ToString()
                               ?? throw new InvalidOperationException("CloudEvent source is required");

                var command = new ResolvedCommand(
                    Source: SourceParser.Parse(rawSource),
                    CeId: cloudEvent.Id!,
                    Time: cloudEvent.Time!.Value);

                var resolved = await repository.ResolveAuto(command);

                if (!resolved)
                    logger.LogInformation(
                        "No open incident found for {source}:{ceId} — already resolved or never existed",
                        command.Source, cloudEvent.Id);

                return Results.Ok();
            })
            .ExcludeFromDescription()
            .AddEndpointFilter<ExceptionFilter>()
            .RequireHost($"*:{Ports.Internal}");

        return endpoints;
    }
}