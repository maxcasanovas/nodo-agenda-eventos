using System.Text.Json;
using Npgsql;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

public sealed record IntegrationEventLogEntry(
    string Module, Guid Id, string Type, JsonElement Content, DateTime OccurredOnUtc, DateTime? ProcessedOnUtc,
    int Attempts, string? Error);

/// <summary>
/// Global record of published integration events for the demo panel. It is a read-only union of the modules' outbox
/// tables: they are never purged, already hold type, content and delivery state, and a separate log table would
/// duplicate every write and need a transaction spanning schemas.
/// </summary>
public sealed class IntegrationEventLog(string connectionString, IReadOnlyCollection<string> moduleSchemas)
{
    public async Task<IReadOnlyCollection<IntegrationEventLogEntry>> GetRecentAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        var union = string.Join(" UNION ALL ", moduleSchemas.Select(schema =>
            $"SELECT '{schema}' AS module, id, type, content::text AS content, occurred_on_utc, processed_on_utc, attempts, error " +
            $"FROM \"{schema}\".outbox_messages"));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT * FROM ({union}) log ORDER BY occurred_on_utc DESC, id LIMIT @limit", connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));

        var entries = new List<IntegrationEventLogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(reader.GetString(3));
            entries.Add(new IntegrationEventLogEntry(
                reader.GetString(0), reader.GetGuid(1), reader.GetString(2), document.RootElement.Clone(),
                DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                reader.IsDBNull(5) ? null : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return entries;
    }
}
