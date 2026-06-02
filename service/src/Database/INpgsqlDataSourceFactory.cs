using Npgsql;

namespace Intropy.BusinessIncidentService.Database;

public interface INpgsqlDataSourceFactory
{
    NpgsqlDataSource GetDataSource();
}