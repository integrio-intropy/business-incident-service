using System.Text.Json;
using Dapper;
using Intropy.BusinessIncidentService.Domain;
using Intropy.BusinessIncidentService.Models;
using Npgsql;

namespace Intropy.BusinessIncidentService.Database;

public class IncidentRepository(NpgsqlDataSource dataSource)
{
    public async Task<(Guid id, bool wasInserted)> UpsertTriggered(TriggeredCommand cmd, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var previousId = await FindPreviousClosedIncident(conn, tx, cmd.Source, cmd.CeId, ct);
        var contextJson = cmd.Context != null ? JsonSerializer.Serialize(cmd.Context) : null;

        var result = await conn.QuerySingleAsync<(Guid id, bool was_inserted, int retry_count)>(
            new CommandDefinition(
                $"""
                INSERT INTO business_incident
                    (previous_id, source, ce_id, subject, description,
                     status, batch_id, context, retry_count, triggered_at)
                VALUES
                    (@PreviousId, @Source, @CeId, @Subject, @Description,
                     @OpenStatus::incident_status, @BatchId, @Context::jsonb, 0, @TriggeredAt)
                ON CONFLICT (source, ce_id) WHERE status = '{IncidentStatus.Open}' DO UPDATE SET
                    retry_count     = business_incident.retry_count + 1,
                    last_retried_at = EXCLUDED.triggered_at
                RETURNING id, (xmax = 0) AS was_inserted, retry_count
                """,
                new
                {
                    PreviousId = previousId,
                    cmd.Source, cmd.CeId, cmd.Subject, cmd.Description, cmd.BatchId,
                    Context = contextJson,
                    TriggeredAt = cmd.Time,
                    OpenStatus = IncidentStatus.Open
                },
                transaction: tx, cancellationToken: ct));

        if (result.was_inserted)
            await WriteInsertEvents(conn, tx, result.id, previousId, cmd, ct);
        else
            await WriteRetryEvents(conn, tx, result.id, cmd, ct);

        await tx.CommitAsync(ct);
        return (result.id, result.was_inserted);
    }

    public async Task<bool> ResolveAuto(ResolvedCommand cmd, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var incidentId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                """
                UPDATE business_incident
                SET status = @ResolvedStatus::incident_status, resolved_at = @Time, resolved_by = @ResolvedByAuto
                WHERE source = @Source AND ce_id = @CeId AND status = @OpenStatus::incident_status
                RETURNING id
                """,
                new
                {
                    cmd.Source, cmd.CeId, cmd.Time,
                    ResolvedStatus = IncidentStatus.Resolved,
                    OpenStatus = IncidentStatus.Open,
                    ResolvedByAuto = ResolvedBy.Auto
                },
                transaction: tx, cancellationToken: ct));

        if (incidentId == null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        await WriteResolveEvents(conn, tx, incidentId.Value, IncidentEventTypes.ResolvedAuto, null, cmd.Time, ct);

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ResolveManual(Guid id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE business_incident
                SET status = @ResolvedStatus::incident_status, resolved_at = @Now, resolved_by = @ResolvedByManual
                WHERE id = @Id AND status = @OpenStatus::incident_status
                """,
                new
                {
                    Id = id, Now = now,
                    ResolvedStatus = IncidentStatus.Resolved,
                    OpenStatus = IncidentStatus.Open,
                    ResolvedByManual = ResolvedBy.Manual
                },
                transaction: tx, cancellationToken: ct));

        if (affected == 0)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        await WriteResolveEvents(conn, tx, id, IncidentEventTypes.ResolvedManual, null, now, ct);

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<BusinessIncident?> GetById(Guid id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BusinessIncident>(
            new CommandDefinition(
                "SELECT * FROM business_incident WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));
    }

    public async Task<(List<BusinessIncident> items, int total)> List(
        string? source, string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var (where, parameters) = BuildWhereClause(source, status);
        var whereClause = where.Length > 0 ? $"WHERE {where}" : "";

        var offset = (page - 1) * pageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        await using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition(
                $"""
                 SELECT COUNT(*) FROM business_incident {whereClause};
                 SELECT * FROM business_incident {whereClause} ORDER BY triggered_at DESC OFFSET @Offset LIMIT @PageSize
                 """,
                parameters, cancellationToken: ct));

        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<BusinessIncident>()).ToList();

        return (items, total);
    }

    public async Task<List<IncidentEvent>> GetEvents(Guid incidentId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<IncidentEvent>(
            new CommandDefinition(
                "SELECT * FROM incident_event WHERE incident_id = @IncidentId ORDER BY occurred_at",
                new { IncidentId = incidentId }, cancellationToken: ct))).ToList();
    }

    public async Task<List<(Guid id, Guid incidentId, string eventType)>> ClaimOutboxBatch(
        int batchSize, TimeSpan claimTimeout, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<(Guid id, Guid incident_id, string event_type)>(
            new CommandDefinition(
                """
                UPDATE outbox_event
                SET claimed_at = now(),
                    attempt_count = attempt_count + 1
                WHERE id IN (
                    SELECT id FROM outbox_event
                    WHERE published_at IS NULL
                      AND attempt_count < 10
                      AND (claimed_at IS NULL OR claimed_at < now() - @ClaimTimeout::interval)
                    ORDER BY created_at
                    LIMIT @BatchSize
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING id, incident_id, event_type
                """,
                new { BatchSize = batchSize, ClaimTimeout = claimTimeout },
                cancellationToken: ct))).ToList();
    }

    public async Task MarkOutboxEventPublished(Guid id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE outbox_event SET published_at = now() WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));
    }

    public async Task ReleaseOutboxClaim(Guid id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE outbox_event SET claimed_at = NULL WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));
    }

    public async Task<int> PurgePublishedOutboxEvents(TimeSpan retention, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM outbox_event WHERE published_at IS NOT NULL AND published_at < now() - @Retention::interval",
                new { Retention = retention }, cancellationToken: ct));
    }

    // --- Private helpers ---

    private static async Task<Guid?> FindPreviousClosedIncident(
        NpgsqlConnection conn, NpgsqlTransaction tx, string source, string ceId, CancellationToken ct)
    {
        return await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                """
                SELECT id FROM business_incident
                WHERE source = @Source AND ce_id = @CeId AND status = @ResolvedStatus::incident_status
                ORDER BY resolved_at DESC
                LIMIT 1
                """,
                new { Source = source, CeId = ceId, ResolvedStatus = IncidentStatus.Resolved },
                transaction: tx, cancellationToken: ct));
    }

    private static async Task WriteInsertEvents(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, Guid? previousId, TriggeredCommand cmd, CancellationToken ct)
    {
        await InsertIncidentEvent(conn, tx, id, IncidentEventTypes.Triggered, null, cmd.Time, ct);

        if (previousId.HasValue)
        {
            var metadata = JsonSerializer.Serialize(new RecurredMetadata(previousId.Value));
            await InsertIncidentEvent(conn, tx, id, IncidentEventTypes.Recurred, metadata, cmd.Time, ct);
        }

        await InsertOutboxEvent(conn, tx, id, IncidentEventTypes.Triggered, ct);
    }

    private static async Task WriteRetryEvents(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, TriggeredCommand cmd, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(new RetriedMetadata(cmd.Time));
        await InsertIncidentEvent(conn, tx, id, IncidentEventTypes.Retried, metadata, cmd.Time, ct);
        await InsertOutboxEvent(conn, tx, id, IncidentEventTypes.Retried, ct);
    }

    private static async Task WriteResolveEvents(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, string eventType, string? metadata, DateTimeOffset time, CancellationToken ct)
    {
        await InsertIncidentEvent(conn, tx, id, eventType, metadata, time, ct);
        await InsertOutboxEvent(conn, tx, id, eventType, ct);
    }

    private static async Task InsertIncidentEvent(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid incidentId, string eventType, string? metadata, DateTimeOffset occurredAt, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO incident_event (incident_id, event_type, metadata, occurred_at)
                VALUES (@IncidentId, @EventType, @Metadata::jsonb, @OccurredAt)
                """,
                new { IncidentId = incidentId, EventType = eventType, Metadata = metadata, OccurredAt = occurredAt },
                transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertOutboxEvent(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid incidentId, string eventType, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO outbox_event (incident_id, event_type)
                VALUES (@IncidentId, @EventType)
                """,
                new { IncidentId = incidentId, EventType = eventType },
                transaction: tx, cancellationToken: ct));
    }

    private static (string where, DynamicParameters parameters) BuildWhereClause(
        string? source, string? status)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(source)) { conditions.Add("source = @Source"); parameters.Add("Source", source); }
        if (!string.IsNullOrEmpty(status)) { conditions.Add("status = @Status::incident_status"); parameters.Add("Status", status); }

        return (string.Join(" AND ", conditions), parameters);
    }
}
