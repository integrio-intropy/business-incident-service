using System.Net;
using Intropy.BusinessIncidentService.Configuration;
using Intropy.BusinessIncidentService.Database;
using Intropy.BusinessIncidentService.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Intropy.BusinessIncidentService.Test.Endpoints;

/// <summary>
/// Verifies which port each endpoint answers on. Dapr invokes the app on <see cref="Ports.Internal"/>,
/// so anything reachable through Dapr service invocation must accept that port.
/// </summary>
public class EndpointPortTests : IAsyncLifetime
{
    private const string IncidentId = "6f1c2b9e-3d4a-4c1b-9a8e-2f7d5e6c1a0b";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Registered so minimal API parameter inference treats them as services. They are never
        // resolved: the middleware below stops the pipeline once routing has picked an endpoint.
        builder.Services.AddSingleton<IncidentRepository>();
        builder.Services.AddDaprClient();

        _app = builder.Build();
        _app.UseRouting();
        _app.Use((HttpContext context, RequestDelegate _) =>
        {
            context.Response.StatusCode = context.GetEndpoint() is null
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        _app.MapEventEndpoints();
        _app.MapIncidentEndpoints();
        _app.MapOutboxEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    public static TheoryData<string, string> QueryEndpoints => new()
    {
        { "GET", "/incidents" },
        { "GET", $"/incidents/{IncidentId}" },
        { "GET", $"/incidents/{IncidentId}/events" },
        { "POST", $"/incidents/{IncidentId}/resolve" }
    };

    public static TheoryData<string, string> InternalEndpoints => new()
    {
        { "POST", "/events" },
        { "POST", "/events/subscribe/triggered" },
        { "POST", "/events/subscribe/resolved" },
        { "POST", "/outbox/process" },
        { "POST", "/outbox/purge" }
    };

    [Theory]
    [MemberData(nameof(QueryEndpoints))]
    public async Task Query_endpoints_answer_on_the_dapr_app_port(string method, string path)
    {
        var response = await Send(method, path, Ports.Internal);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(QueryEndpoints))]
    public async Task Query_endpoints_answer_on_the_public_port(string method, string path)
    {
        var response = await Send(method, path, Ports.Public);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InternalEndpoints))]
    public async Task Internal_endpoints_answer_on_the_internal_port(string method, string path)
    {
        var response = await Send(method, path, Ports.Internal);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InternalEndpoints))]
    public async Task Internal_endpoints_are_not_exposed_on_the_public_port(string method, string path)
    {
        var response = await Send(method, path, Ports.Public);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task<HttpResponseMessage> Send(string method, string path, int port)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = $"localhost:{port}";
        return _client.SendAsync(request);
    }
}
