# Intropy Business Incident Service Client

A .NET client library for the Intropy Business Incident Service that uses Dapr service invocation.

## Installation

```bash
dotnet add package Intropy.BusinessIncidentService.Client
```
## Usage

### Configuration with Dependency Injection

```csharp
services.AddBusinessIncidentServiceClient(options =>
{
    options.AppId = "business-incident-service"; // default
});
```

Or configure using appsettings.json:

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

### Using the Client

```csharp
public class MyService(BusinessIncidentServiceClient client)
{
    public async Task TriggerIncident()
    {
        await client.Trigger(
            source: new Uri("urn:myservice"),
            subject: "order-processing",
            id: "unique-event-id",
            data: new BusinessIncidentData { Description = "Payment failed" },
            batchId: null);
    }

    public async Task ResolveIncident()
    {
        await client.Resolve(
            source: new Uri("urn:myservice"),
            subject: "order-processing",
            id: "unique-event-id",
            batchId: null);
    }
}
```

## API Reference

### Event Methods

- `Trigger(Uri source, string subject, string id, BusinessIncidentData data, string? batchId)` - Trigger a new business incident via CloudEvent
- `Resolve(Uri source, string subject, string id, string? batchId)` - Auto-resolve an incident via CloudEvent

### Query Methods

- `GetById(Guid id)` - Get a single incident by ID (returns `null` if not found)
- `List(IncidentFilter? filter)` - List incidents with optional filtering and pagination
- `GetEvents(Guid id)` - Get the event history for an incident
- `ResolveManual(Guid id)` - Manually resolve an incident (returns `false` if not found)

## Requirements

- .NET 10.0 or later
- Dapr runtime with sidecar configured
- Access to the Business Incident Service via Dapr service invocation

## License

MIT
