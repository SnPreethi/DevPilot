using DevPilot.Contracts;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Storage;

public sealed class SQLiteSymbolStore : ISymbolStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteSymbolStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveManyAsync(
        IReadOnlyCollection<SymbolIndexEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries == null || entries.Count == 0) return;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Symbols (Id, RepositoryId, FileId, FilePath, Name, Kind, Namespace, ParentSymbol, ReferencedSymbols, ImportedNamespaces, FileDependencies, DefinitionLocation, StartLine, EndLine)
                VALUES (@Id, @RepositoryId, @FileId, @FilePath, @Name, @Kind, @Namespace, @ParentSymbol, @ReferencedSymbols, @ImportedNamespaces, @FileDependencies, @DefinitionLocation, @StartLine, @EndLine)
                ON CONFLICT(Id) DO UPDATE SET
                    RepositoryId = excluded.RepositoryId,
                    FileId = excluded.FileId,
                    FilePath = excluded.FilePath,
                    Name = excluded.Name,
                    Kind = excluded.Kind,
                    Namespace = excluded.Namespace,
                    ParentSymbol = excluded.ParentSymbol,
                    ReferencedSymbols = excluded.ReferencedSymbols,
                    ImportedNamespaces = excluded.ImportedNamespaces,
                    FileDependencies = excluded.FileDependencies,
                    DefinitionLocation = excluded.DefinitionLocation,
                    StartLine = excluded.StartLine,
                    EndLine = excluded.EndLine;
                """;

            AddParameters(command, entry);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SymbolIndexEntry>> ListByRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RepositoryId, FileId, FilePath, Name, Kind, Namespace, ParentSymbol, ReferencedSymbols, ImportedNamespaces, FileDependencies, DefinitionLocation, StartLine, EndLine
            FROM Symbols
            WHERE RepositoryId = @RepositoryId
            ORDER BY FilePath, StartLine;
            """;
        command.AddParameter("@RepositoryId", repositoryId);

        var list = new List<SymbolIndexEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadEntry(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<SymbolIndexEntry>> ListByFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RepositoryId, FileId, FilePath, Name, Kind, Namespace, ParentSymbol, ReferencedSymbols, ImportedNamespaces, FileDependencies, DefinitionLocation, StartLine, EndLine
            FROM Symbols
            WHERE FileId = @FileId
            ORDER BY StartLine;
            """;
        command.AddParameter("@FileId", fileId);

        var list = new List<SymbolIndexEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadEntry(reader));
        }

        return list;
    }

    public async Task<int> DeleteByFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Symbols WHERE FileId = @FileId;";
        command.AddParameter("@FileId", fileId);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameters(DbCommand command, SymbolIndexEntry entry)
    {
        command.AddParameter("@Id", entry.SymbolId);
        command.AddParameter("@RepositoryId", entry.RepositoryId);
        command.AddParameter("@FileId", entry.FileId);
        command.AddParameter("@FilePath", entry.FilePath);
        command.AddParameter("@Name", entry.Name);
        command.AddParameter("@Kind", entry.Kind);
        command.AddParameter("@Namespace", (object?)entry.Namespace ?? DBNull.Value);
        command.AddParameter("@ParentSymbol", (object?)entry.ParentSymbol ?? DBNull.Value);
        command.AddParameter("@ReferencedSymbols", JsonSerializer.Serialize(entry.ReferencedSymbols));
        command.AddParameter("@ImportedNamespaces", JsonSerializer.Serialize(entry.ImportedNamespaces));
        command.AddParameter("@FileDependencies", JsonSerializer.Serialize(entry.FileDependencies));
        command.AddParameter("@DefinitionLocation", entry.DefinitionLocation);
        command.AddParameter("@StartLine", entry.StartLine);
        command.AddParameter("@EndLine", entry.EndLine);
    }

    private static SymbolIndexEntry ReadEntry(DbDataReader reader)
    {
        var refSymsJson = reader.GetString(8);
        var impNamesJson = reader.GetString(9);
        var fileDepsJson = reader.GetString(10);

        var refSyms = JsonSerializer.Deserialize<List<string>>(refSymsJson) ?? new List<string>();
        var impNames = JsonSerializer.Deserialize<List<string>>(impNamesJson) ?? new List<string>();
        var fileDeps = JsonSerializer.Deserialize<List<string>>(fileDepsJson) ?? new List<string>();

        return new SymbolIndexEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            refSyms,
            impNames,
            fileDeps,
            reader.GetString(11),
            reader.GetInt32(12),
            reader.GetInt32(13)
        );
    }
}
