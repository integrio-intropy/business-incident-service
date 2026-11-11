using System.Text.Json;
using Intropy.BusinessIncidentService.Configuration;
using Intropy.BusinessIncidentService.Database;
using Intropy.BusinessIncidentService.Models;
using Intropy.Contracts.BusinessIncidentService;
using Microsoft.AspNetCore.Mvc;

namespace Intropy.BusinessIncidentService.Endpoints;

public static class IncidentEndpoints
{
    // Answer on the public port (Kubernetes Service, local tooling) and on the internal port,
    // which is the Dapr app port, so the query API can be reached through Dapr service invocation.
    private static readonly string[] QueryHosts = [$"*:{Ports.Public}", $"*:{Ports.Internal}"];

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/incidents/{id:guid}",
                async ([FromRoute] Guid id, [FromServices] IncidentRepository repository,
                    CancellationToken ct) =>
                {
                    var incident = await repository.GetById(id, ct);
                    if (incident == null)
                        return Results.Problem(statusCode: 404);

                    return Results.Ok(ToResponse(incident));
                })
            .WithName("getIncident")
            .WithDisplayName("Get Incident")
            .Produces<IncidentResponse>()
            .RequireHost(QueryHosts);

        endpoints.MapGet("/incidents",
                async ([AsParameters] IncidentFilter filter,
                    [FromServices] IncidentRepository repository,
                    CancellationToken ct) =>
                {
                    var page = filter.Page is null or < 1 ? 1 : filter.Page.Value;
                    var pageSize = filter.PageSize is null or < 1 ? 20 : filter.PageSize.Value > 100 ? 100 : filter.PageSize.Value;

                    var (items, total) = await repository.List(
                        filter.Source, filter.Status,
                        page, pageSize, ct);

                    return Results.Ok(new IncidentListResponse
                    {
                        Items = items.Select(ToResponse).ToList(),
                        Total = total
                    });
                })
            .WithName("listIncidents")
            .WithDisplayName("List Incidents")
            .Produces<IncidentListResponse>()
            .RequireHost(QueryHosts);

        endpoints.MapGet("/incidents/{id:guid}/events",
                async ([FromRoute] Guid id, [FromServices] IncidentRepository repository,
                    CancellationToken ct) =>
                {
                    var events = await repository.GetEvents(id, ct);
                    return Results.Ok(events.Select(e => new IncidentEventResponse
                    {
                        Id = e.Id,
                        IncidentId = e.IncidentId,
                        EventType = e.EventType,
                        Metadata = e.Metadata,
                        OccurredAt = e.OccurredAt
                    }));
                })
            .WithName("getIncidentEvents")
            .WithDisplayName("Get Incident Events")
            .Produces<IncidentEventResponse>()
            .RequireHost(QueryHosts);

        endpoints.MapPost("/incidents/{id:guid}/resolve",
                async ([FromRoute] Guid id, [FromServices] IncidentRepository repository,
                    CancellationToken ct) =>
                {
                    var resolved = await repository.ResolveManual(id, ct);
                    return resolved ? Results.Ok() : Results.Problem(statusCode: 404);
                })
            .WithName("resolveIncident")
            .WithDisplayName("Resolve Incident")
            .Produces(200)
            .Produces<ProblemDetails>()
            .RequireHost(QueryHosts);

        return endpoints;
    }

    private static IncidentResponse ToResponse(BusinessIncident i)
    {
        Dictionary<string, string>? context = null;
        if (i.Context != null)
            context = JsonSerializer.Deserialize<Dictionary<string, string>>(i.Context);

        return new IncidentResponse
        {
            Id = i.Id,
            PreviousId = i.PreviousId,
            Source = i.Source,
            CeId = i.CeId,
            Subject = i.Subject,
            Description = i.Description,
            Status = i.Status,
            BatchId = i.BatchId,
            Context = context,
            RetryCount = i.RetryCount,
            TriggeredAt = i.TriggeredAt,
            LastRetriedAt = i.LastRetriedAt,
            ResolvedAt = i.ResolvedAt,
            ResolvedBy = i.ResolvedBy
        };
    }
}
