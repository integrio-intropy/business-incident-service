using System.Net;
using System.Text;
using Dapr.Client;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Intropy.BusinessIncidentService.Client.Test;

public class BusinessIncidentServiceClientTests
{
    private static readonly Guid IncidentId = Guid.Parse("6f1c2b9e-3d4a-4c1b-9a8e-2f7d5e6c1a0b");

    // The service's wire format: Intropy.Contracts names every property explicitly in snake_case.
    private const string IncidentJson =
        """
        {
          "id": "6f1c2b9e-3d4a-4c1b-9a8e-2f7d5e6c1a0b",
          "previous_id": null,
          "source": "urn:example:orders",
          "ce_id": "evt-1",
          "subject": "order-processing",
          "description": "Payment failed",
          "status": "open",
          "batch_id": "batch-7",
          "context": { "orderId": "42" },
          "retry_count": 2,
          "triggered_at": "2026-11-10T08:00:00+00:00",
          "last_retried_at": "2026-11-10T09:00:00+00:00",
          "resolved_at": null,
          "resolved_by": null
        }
        """;

    private static readonly string NotFoundProblem =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404}""";

    [Fact]
    public async Task GetById_binds_service_response()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, IncidentJson);

        var incident = await client.GetById(IncidentId);

        Assert.NotNull(incident);
        Assert.Equal(IncidentId, incident.Id);
        Assert.Equal("urn:example:orders", incident.Source);
        Assert.Equal("evt-1", incident.CeId);
        Assert.Equal("order-processing", incident.Subject);
        Assert.Equal("Payment failed", incident.Description);
        Assert.Equal("open", incident.Status);
        Assert.Equal("batch-7", incident.BatchId);
        Assert.Equal("42", incident.Context!["orderId"]);
        Assert.Equal(2, incident.RetryCount);
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T08:00:00+00:00"), incident.TriggeredAt);
        Assert.Equal($"/incidents/{IncidentId}", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetById_returns_null_when_incident_does_not_exist()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, NotFoundProblem, "application/problem+json");

        var incident = await client.GetById(IncidentId);

        Assert.Null(incident);
    }

    [Fact]
    public async Task GetById_throws_with_status_code_on_server_error()
    {
        var (client, _) = CreateClient(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<BusinessIncidentServiceException>(() => client.GetById(IncidentId));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("boom", ex.Details);
    }

    [Fact]
    public async Task List_binds_service_response()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, $$"""{ "items": [{{IncidentJson}}], "total": 1 }""");

        var page = await client.List();

        Assert.Equal(1, page.Total);
        var incident = Assert.Single(page.Items);
        Assert.Equal(IncidentId, incident.Id);
        Assert.Equal("Payment failed", incident.Description);
    }

    [Fact]
    public async Task GetEvents_binds_service_response()
    {
        var eventId = Guid.NewGuid();
        var (client, _) = CreateClient(HttpStatusCode.OK,
            $$"""
              [{
                "id": "{{eventId}}",
                "incident_id": "{{IncidentId}}",
                "event_type": "triggered",
                "occurred_at": "2026-11-10T08:00:00+00:00"
              }]
              """);

        var events = await client.GetEvents(IncidentId);

        var e = Assert.Single(events);
        Assert.Equal(eventId, e.Id);
        Assert.Equal(IncidentId, e.IncidentId);
        Assert.Equal("triggered", e.EventType);
    }

    [Fact]
    public async Task List_throws_on_not_found()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, NotFoundProblem, "application/problem+json");

        var ex = await Assert.ThrowsAsync<BusinessIncidentServiceException>(() => client.List());

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ResolveManual_returns_false_when_incident_does_not_exist()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, NotFoundProblem, "application/problem+json");

        Assert.False(await client.ResolveManual(IncidentId));
    }

    [Fact]
    public async Task ResolveManual_returns_true_when_resolved()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "");

        Assert.True(await client.ResolveManual(IncidentId));
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/incidents/{IncidentId}/resolve", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Trigger_throws_with_status_code_when_event_is_rejected()
    {
        var (client, handler) = CreateClient(HttpStatusCode.BadRequest, "Unsupported event type");

        var ex = await Assert.ThrowsAsync<BusinessIncidentServiceException>(() => client.Trigger(
            new Uri("urn:example:orders"), "order-processing", "evt-1",
            new() { Description = "Payment failed" }, batchId: null));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("/events", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    private static (BusinessIncidentServiceClient client, StubHandler handler) CreateClient(
        HttpStatusCode status, string body, string contentType = "application/json")
    {
        var handler = new StubHandler(status, body, contentType);
        var options = Options.Create(new BusinessIncidentServiceClientOptions());

        var dapr = Substitute.For<DaprClient>();
        dapr.CreateInvokableHttpClient(options.Value.AppId)
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri($"http://{options.Value.AppId}/")
            });

        return (new BusinessIncidentServiceClient(dapr, options), handler);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, string contentType) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
        }
    }
}
