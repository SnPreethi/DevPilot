using DevPilot.Contracts;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Storage;

public sealed class SQLiteChunkStore : IChunkStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteChunkStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
    {
        await SaveManyAsync([chunk], cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveManyAsync(
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var chunk in chunks)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Chunks (Id, RepositoryId, FileId, SymbolName, ChunkType, StartLine, EndLine, Content, Language, ChunkHash, TokenEstimate, SymbolKind, Namespace, ParentSymbol, ReferencedSymbols, ImportedNamespaces, FileDependencies, DefinitionLocation)
                VALUES (@Id, @RepositoryId, @FileId, @SymbolName, @ChunkType, @StartLine, @EndLine, @Content, @Language, @ChunkHash, @TokenEstimate, @SymbolKind, @Namespace, @ParentSymbol, @ReferencedSymbols, @ImportedNamespaces, @FileDependencies, @DefinitionLocation)
                ON CONFLICT(Id) DO UPDATE SET
                    RepositoryId = excluded.RepositoryId,
                    FileId = excluded.FileId,
                    SymbolName = excluded.SymbolName,
                    ChunkType = excluded.ChunkType,
                    StartLine = excluded.StartLine,
                    EndLine = excluded.EndLine,
                    Content = excluded.Content,
                    Language = excluded.Language,
                    ChunkHash = excluded.ChunkHash,
                    TokenEstimate = excluded.TokenEstimate,
                    SymbolKind = excluded.SymbolKind,
                    Namespace = excluded.Namespace,
                    ParentSymbol = excluded.ParentSymbol,
                    ReferencedSymbols = excluded.ReferencedSymbols,
                    ImportedNamespaces = excluded.ImportedNamespaces,
                    FileDependencies = excluded.FileDependencies,
                    DefinitionLocation = excluded.DefinitionLocation;
                """;
            AddChunkParameters(command, chunk);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CodeChunk?> GetAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.RepositoryId, c.FileId, f.RelativePath, c.SymbolName, c.ChunkType,
                   c.StartLine, c.EndLine, c.Content, c.Language, c.ChunkHash, c.TokenEstimate,
                   c.SymbolKind, c.Namespace, c.ParentSymbol, c.ReferencedSymbols, c.ImportedNamespaces, c.FileDependencies, c.DefinitionLocation
            FROM Chunks c
            INNER JOIN Files f ON f.Id = c.FileId
            WHERE c.Id = @Id;
            """;
        command.AddParameter("@Id", chunkId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadChunk(reader)
            : null;
    }

    public async Task<IReadOnlyList<CodeChunk>> ListByFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.RepositoryId, c.FileId, f.RelativePath, c.SymbolName, c.ChunkType,
                   c.StartLine, c.EndLine, c.Content, c.Language, c.ChunkHash, c.TokenEstimate,
                   c.SymbolKind, c.Namespace, c.ParentSymbol, c.ReferencedSymbols, c.ImportedNamespaces, c.FileDependencies, c.DefinitionLocation
            FROM Chunks c
            INNER JOIN Files f ON f.Id = c.FileId
            WHERE c.FileId = @FileId
            ORDER BY c.StartLine;
            """;
        command.AddParameter("@FileId", fileId);

        var chunks = new List<CodeChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public async Task<IReadOnlyList<CodeChunk>> ListByRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.RepositoryId, c.FileId, f.RelativePath, c.SymbolName, c.ChunkType,
                   c.StartLine, c.EndLine, c.Content, c.Language, c.ChunkHash, c.TokenEstimate,
                   c.SymbolKind, c.Namespace, c.ParentSymbol, c.ReferencedSymbols, c.ImportedNamespaces, c.FileDependencies, c.DefinitionLocation
            FROM Chunks c
            INNER JOIN Files f ON f.Id = c.FileId
            WHERE c.RepositoryId = @RepositoryId
            ORDER BY f.RelativePath, c.StartLine;
            """;
        command.AddParameter("@RepositoryId", repositoryId);

        var chunks = new List<CodeChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public async Task ReplaceFileChunksAsync(
        string fileId,
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM Chunks WHERE FileId = @FileId;";
        deleteCommand.AddParameter("@FileId", fileId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var chunk in chunks)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Chunks (Id, RepositoryId, FileId, SymbolName, ChunkType, StartLine, EndLine, Content, Language, ChunkHash, TokenEstimate, SymbolKind, Namespace, ParentSymbol, ReferencedSymbols, ImportedNamespaces, FileDependencies, DefinitionLocation)
                VALUES (@Id, @RepositoryId, @FileId, @SymbolName, @ChunkType, @StartLine, @EndLine, @Content, @Language, @ChunkHash, @TokenEstimate, @SymbolKind, @Namespace, @ParentSymbol, @ReferencedSymbols, @ImportedNamespaces, @FileDependencies, @DefinitionLocation);
                """;
            AddChunkParameters(command, chunk);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteMissingByFileAsync(
        string fileId,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var existing = await ListByFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        var current = chunkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = existing.Where(chunk => !current.Contains(chunk.ChunkId)).ToList();
        if (stale.Count == 0)
        {
            return 0;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = 0;
        foreach (var chunk in stale)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Chunks WHERE Id = @Id;";
            command.AddParameter("@Id", chunk.ChunkId);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private static void AddChunkParameters(DbCommand command, CodeChunk chunk)
    {
        command.AddParameter("@Id", chunk.ChunkId);
        command.AddParameter("@RepositoryId", chunk.RepositoryId);
        command.AddParameter("@FileId", chunk.FileId);
        command.AddParameter("@SymbolName", chunk.SymbolName);
        command.AddParameter("@ChunkType", chunk.ChunkType);
        command.AddParameter("@StartLine", chunk.StartLine);
        command.AddParameter("@EndLine", chunk.EndLine);
        command.AddParameter("@Content", chunk.Content);
        command.AddParameter("@Language", chunk.Language);
        command.AddParameter("@ChunkHash", chunk.ChunkHash);
        command.AddParameter("@TokenEstimate", chunk.TokenEstimate);
        command.AddParameter("@SymbolKind", (object?)chunk.SymbolKind ?? DBNull.Value);
        command.AddParameter("@Namespace", (object?)chunk.Namespace ?? DBNull.Value);
        command.AddParameter("@ParentSymbol", (object?)chunk.ParentSymbol ?? DBNull.Value);
        command.AddParameter("@ReferencedSymbols", chunk.ReferencedSymbols != null ? JsonSerializer.Serialize(chunk.ReferencedSymbols) : DBNull.Value);
        command.AddParameter("@ImportedNamespaces", chunk.ImportedNamespaces != null ? JsonSerializer.Serialize(chunk.ImportedNamespaces) : DBNull.Value);
        command.AddParameter("@FileDependencies", chunk.FileDependencies != null ? JsonSerializer.Serialize(chunk.FileDependencies) : DBNull.Value);
        command.AddParameter("@DefinitionLocation", (object?)chunk.DefinitionLocation ?? DBNull.Value);
    }

    private static CodeChunk ReadChunk(DbDataReader reader)
    {
        var refSymsJson = reader.IsDBNull(15) ? null : reader.GetString(15);
        var impNamesJson = reader.IsDBNull(16) ? null : reader.GetString(16);
        var fileDepsJson = reader.IsDBNull(17) ? null : reader.GetString(17);

        var refSyms = refSymsJson != null ? JsonSerializer.Deserialize<List<string>>(refSymsJson) : null;
        var impNames = impNamesJson != null ? JsonSerializer.Deserialize<List<string>>(impNamesJson) : null;
        var fileDeps = fileDepsJson != null ? JsonSerializer.Deserialize<List<string>>(fileDepsJson) : null;

        return new CodeChunk(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            refSyms,
            impNames,
            fileDeps,
            reader.IsDBNull(18) ? null : reader.GetString(18));
    }
}
