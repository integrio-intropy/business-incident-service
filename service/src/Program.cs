using Dapper;
using Intropy.BusinessIncidentService.Configuration;
using Intropy.BusinessIncidentService.Database;
using Intropy.BusinessIncidentService.Endpoints;
using Intropy.BusinessIncidentService.Health;
using Intropy.BusinessIncidentService.Telemetry;
using Intropy.Telemetry.AspNetCore;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(Ports.Public);
    kestrel.ListenAnyIP(Ports.Internal);
});

builder.Services.AddOpenTelemetry(conf =>
{
    conf.ServiceNamespace = "intropy";
    conf.ServiceName = ActivitySourceProvider.ActivitySourceName;
    conf.Environment = environment;
});

builder.Services.AddOpenApi();
builder.Services.AddSingleton<ShutdownCheck>();
builder.Services.AddHealthChecks()
    .AddCheck<BaseCheck>("base", tags: [HealthCheckTypes.Liveness, HealthCheckTypes.Readiness])
    .AddCheck<ShutdownCheck>("shutdown", tags: [HealthCheckTypes.Readiness]);

builder.Services.AddDaprClient();

if (builder.Configuration.GetValue<bool>("Database:UseAzureAuth"))
    builder.Services.AddSingleton<INpgsqlDataSourceFactory, AzureDataSourceFactory>();
else
    builder.Services.AddSingleton<INpgsqlDataSourceFactory, PostgresDataSourceFactory>();

builder.Services.AddSingleton(sp => sp.GetRequiredService<INpgsqlDataSourceFactory>().GetDataSource());
builder.Services.AddSingleton<IncidentRepository>();

builder.Services.AddOptions<DaprComponentsConfig>()
    .Bind(builder.Configuration.GetSection("DaprComponentsConfig"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().RequireHost($"*:{Ports.Public}");
}

app.MapHealthEndpoints();
app.MapEventEndpoints();
app.MapIncidentEndpoints();
app.MapOutboxEndpoints();

app.Run();