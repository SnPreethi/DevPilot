using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPilot.Storage;

public sealed class SQLiteVectorStore : IVectorStore
{
    private readonly IEmbeddingStore _embeddingStore;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly VectorSearchSettings _settings;
    private readonly ILogger<SQLiteVectorStore> _logger;

    public SQLiteVectorStore(
        IEmbeddingStore embeddingStore,
        ISqliteConnectionFactory connectionFactory,
        IOptions<VectorSearchSettings> settings,
        ILogger<SQLiteVectorStore> logger)
    {
        _embeddingStore = embeddingStore;
        _connectionFactory = connectionFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SQLite vector store initialized with metric {Metric}.", _settings.SimilarityMetric);
        return Task.CompletedTask;
    }

    public Task SaveEmbeddingsAsync(
        IReadOnlyCollection<EmbeddingVector> embeddings,
        CancellationToken cancellationToken = default)
    {
        return _embeddingStore.SaveManyAsync(embeddings, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return [];
    }

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        EmbeddingResult queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var scored = new List<(string ChunkId, double Score)>();
        await foreach (var embedding in _embeddingStore.ListByModelAsync(queryEmbedding.ModelId, cancellationToken).ConfigureAwait(false))
        {
            if (embedding.Dimensions != queryEmbedding.Dimension)
            {
                continue;
            }

            scored.Add((embedding.ChunkId, CosineSimilarity(queryEmbedding.Vector, embedding.Vector)));
        }

        var topChunkIds = scored
            .OrderByDescending(item => item.Score)
            .Take(Math.Max(1, topK))
            .ToList();

        var results = new List<RankedChunk>();
        foreach (var item in topChunkIds)
        {
            var chunk = await GetChunkAsync(item.ChunkId, cancellationToken).ConfigureAwait(false);
            if (chunk is null)
            {
                continue;
            }

            results.Add(new RankedChunk(
                chunk.ChunkId,
                chunk.FilePath,
                chunk.SymbolName,
                chunk.ChunkType,
                chunk.StartLine,
                chunk.EndLine,
                CreatePreview(chunk.Content),
                item.Score));
        }

        return results;
    }

    private async Task<CodeChunk?> GetChunkAsync(string chunkId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.RepositoryId, c.FileId, f.RelativePath, c.SymbolName, c.ChunkType,
                   c.StartLine, c.EndLine, c.Content, c.Language
            FROM Chunks c
            INNER JOIN Files f ON f.Id = c.FileId
            WHERE c.Id = @Id;
            """;
        command.AddParameter("@Id", chunkId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

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
            reader.GetString(9));
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string CreatePreview(string content)
    {
        var normalized = string.Join(" ", content.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }
}
