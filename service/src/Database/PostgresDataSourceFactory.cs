using Npgsql;

namespace Intropy.BusinessIncidentService.Database;

public class PostgresDataSourceFactory(IConfiguration configuration) : INpgsqlDataSourceFactory
{
    public NpgsqlDataSource GetDataSource()
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException("Postgres connection string not configured");

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        return builder.Build();
    }
}