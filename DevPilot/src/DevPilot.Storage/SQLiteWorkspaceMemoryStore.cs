using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;

namespace DevPilot.Storage;

public sealed class SQLiteWorkspaceMemoryStore : IWorkspaceMemoryStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteWorkspaceMemoryStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveEventAsync(WorkspaceEvent ev, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO WorkspaceEvents (RepositoryId, EventType, TimestampUtc, FilePath, SymbolName, Description, Outcome, Payload)
            VALUES (@RepositoryId, @EventType, @TimestampUtc, @FilePath, @SymbolName, @Description, @Outcome, @Payload);
            """;
        command.AddParameter("@RepositoryId", ev.RepositoryId);
        command.AddParameter("@EventType", ev.EventType);
        command.AddParameter("@TimestampUtc", ev.TimestampUtc.ToString("O"));
        command.AddParameter("@FilePath", ev.FilePath ?? (object)DBNull.Value);
        command.AddParameter("@SymbolName", ev.SymbolName ?? (object)DBNull.Value);
        command.AddParameter("@Description", ev.Description);
        command.AddParameter("@Outcome", ev.Outcome);
        command.AddParameter("@Payload", ev.Payload ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkspaceEvent>> ListEventsAsync(string repositoryId, int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RepositoryId, EventType, TimestampUtc, FilePath, SymbolName, Description, Outcome, Payload
            FROM WorkspaceEvents
            WHERE RepositoryId = @RepositoryId
            ORDER BY TimestampUtc DESC
            LIMIT @Limit;
            """;
        command.AddParameter("@RepositoryId", repositoryId);
        command.AddParameter("@Limit", limit);

        var events = new List<WorkspaceEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new WorkspaceEvent(
                RepositoryId: reader.GetString(0),
                EventType: reader.GetString(1),
                TimestampUtc: DateTime.Parse(reader.GetString(2)),
                FilePath: reader.IsDBNull(3) ? null : reader.GetString(3),
                SymbolName: reader.IsDBNull(4) ? null : reader.GetString(4),
                Description: reader.GetString(5),
                Outcome: reader.GetString(6),
                Payload: reader.IsDBNull(7) ? null : reader.GetString(7)
            ));
        }

        return events;
    }
}
