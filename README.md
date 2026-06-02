# Intropy Business Incident Service

A service for ingesting, deduplicating, and distributing business incident events, with a .NET client library for producing incidents from your applications.

This repository contains:

- **`client/`** — `Intropy.BusinessIncidentService.Client` NuGet package for triggering and resolving incidents via Dapr service invocation.
- **`service/`** — The ASP.NET Core service that receives CloudEvents, persists incidents to PostgreSQL, and republishes them.
- **`charts/`** — Helm chart for deploying the service.
- **`migrations/`** — Database migrations applied at startup.
- **`local/`** — Docker Compose stack for running the service locally.
- **`api/openapi.yaml`** — OpenAPI specification for the service.

## Client usage

Install:

```bash
dotnet add package Intropy.BusinessIncidentService.Client
```

Register with DI:

```csharp
services.AddBusinessIncidentServiceClient(options =>
{
    options.AppId = "business-incident-service"; // default
});
```

Or bind from configuration:

```json
{
  "BusinessIncidentService": {
    "AppId": "custom-app-id-business-incident-service"
  }
}
```

```csharp
services.AddBusinessIncidentServiceClient(Configuration);
```

Trigger and resolve incidents:

```csharp
public class MyService(BusinessIncidentServiceClient client)
{
    public Task TriggerIncident() =>
        client.Trigger(
            source: new Uri("urn:myservice"),
            subject: "order-processing",
            id: "unique-event-id",
            data: new BusinessIncidentData { Description = "Payment failed" },
            batchId: null);

    public Task ResolveIncident() =>
        client.Resolve(
            source: new Uri("urn:myservice"),
            subject: "order-processing",
            id: "unique-event-id",
            batchId: null);
}
```

### API surface

**Events**

- `Trigger(source, subject, id, data, batchId)` — Trigger a new business incident.
- `Resolve(source, subject, id, batchId)` — Auto-resolve an incident.

**Queries**

- `GetById(Guid id)` — Get a single incident (`null` if not found).
- `List(IncidentFilter? filter)` — List with optional filtering and pagination.
- `GetEvents(Guid id)` — Event history for an incident.
- `ResolveManual(Guid id)` — Manually resolve an incident (`false` if not found).

### Requirements

- .NET 10.0 or later
- Dapr runtime with sidecar configured
- Access to the Business Incident Service via Dapr service invocation

## Running the service locally

```bash
docker compose -f local/compose.yaml up --build
```

This starts the service, Dapr sidecar, RabbitMQ, PostgreSQL, migrations, and an `otel-lgtm` stack on `localhost:3000`.

The default Postgres connection string in `service/src/appsettings.json` uses `Password=password` for local development only. Override `ConnectionStrings__Postgres` in production deployments.

## License

[MIT](LICENSE)
