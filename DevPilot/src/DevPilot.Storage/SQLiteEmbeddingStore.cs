using DevPilot.Contracts;
using System.Runtime.CompilerServices;

namespace DevPilot.Storage;

public sealed class SQLiteEmbeddingStore : IEmbeddingStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteEmbeddingStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveManyAsync(
        IReadOnlyCollection<EmbeddingVector> embeddings,
        CancellationToken cancellationToken = default)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var embedding in embeddings)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Embeddings (Id, ChunkId, ModelName, VectorData, Dimensions, CreatedUtc, EmbeddingModelVersion, EmbeddingSchemaVersion, ChunkHash, IndexedAtUtc)
                VALUES (@Id, @ChunkId, @ModelName, @VectorData, @Dimensions, @CreatedUtc, @EmbeddingModelVersion, @EmbeddingSchemaVersion, @ChunkHash, @IndexedAtUtc)
                ON CONFLICT(ChunkId, ModelName) DO UPDATE SET
                    VectorData = excluded.VectorData,
                    Dimensions = excluded.Dimensions,
                    CreatedUtc = excluded.CreatedUtc,
                    EmbeddingModelVersion = excluded.EmbeddingModelVersion,
                    EmbeddingSchemaVersion = excluded.EmbeddingSchemaVersion,
                    ChunkHash = excluded.ChunkHash,
                    IndexedAtUtc = excluded.IndexedAtUtc;
                """;
            command.AddParameter("@Id", embedding.Id);
            command.AddParameter("@ChunkId", embedding.ChunkId);
            command.AddParameter("@ModelName", embedding.ModelName);
            command.AddParameter("@VectorData", VectorSerializer.Serialize(embedding.Vector));
            command.AddParameter("@Dimensions", embedding.Dimensions);
            command.AddParameter("@CreatedUtc", embedding.CreatedUtc.UtcDateTime.ToString("O"));
            command.AddParameter("@EmbeddingModelVersion", embedding.EmbeddingModelVersion);
            command.AddParameter("@EmbeddingSchemaVersion", embedding.EmbeddingSchemaVersion);
            command.AddParameter("@ChunkHash", embedding.ChunkHash);
            command.AddParameter("@IndexedAtUtc", (embedding.IndexedAtUtc ?? embedding.CreatedUtc).UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(
        string chunkId,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM Embeddings
            WHERE ChunkId = @ChunkId AND ModelName = @ModelName
            LIMIT 1;
            """;
        command.AddParameter("@ChunkId", chunkId);
        command.AddParameter("@ModelName", modelName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<bool> IsCurrentAsync(
        string chunkId,
        string modelName,
        string embeddingModelVersion,
        int embeddingSchemaVersion,
        string chunkHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM Embeddings
            WHERE ChunkId = @ChunkId
              AND ModelName = @ModelName
              AND EmbeddingModelVersion = @EmbeddingModelVersion
              AND EmbeddingSchemaVersion = @EmbeddingSchemaVersion
              AND ChunkHash = @ChunkHash
            LIMIT 1;
            """;
        command.AddParameter("@ChunkId", chunkId);
        command.AddParameter("@ModelName", modelName);
        command.AddParameter("@EmbeddingModelVersion", embeddingModelVersion);
        command.AddParameter("@EmbeddingSchemaVersion", embeddingSchemaVersion);
        command.AddParameter("@ChunkHash", chunkHash);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async IAsyncEnumerable<EmbeddingVector> ListByModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChunkId, ModelName, VectorData, Dimensions, CreatedUtc,
                   EmbeddingModelVersion, EmbeddingSchemaVersion, ChunkHash, IndexedAtUtc
            FROM Embeddings
            WHERE ModelName = @ModelName;
            """;
        command.AddParameter("@ModelName", modelName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new EmbeddingVector(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                VectorSerializer.Deserialize((byte[])reader["VectorData"]),
                reader.GetInt32(4),
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? "1" : reader.GetString(6),
                reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.IsDBNull(9) || string.IsNullOrWhiteSpace(reader.GetString(9))
                    ? DateTimeOffset.Parse(reader.GetString(5))
                    : DateTimeOffset.Parse(reader.GetString(9)));
        }
    }
}
