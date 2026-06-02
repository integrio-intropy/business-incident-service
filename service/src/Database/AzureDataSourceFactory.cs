using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace Intropy.BusinessIncidentService.Database;

public class AzureDataSourceFactory(IConfiguration configuration) : INpgsqlDataSourceFactory
{
    public NpgsqlDataSource GetDataSource()
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException("Postgres connection string not configured");

        var credential = new DefaultAzureCredential();
        var builder = new NpgsqlDataSourceBuilder(connectionString);

        builder.UsePeriodicPasswordProvider(async (_, ct) =>
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]),
                ct);
            return token.Token;
        }, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));

        return builder.Build();
    }
}