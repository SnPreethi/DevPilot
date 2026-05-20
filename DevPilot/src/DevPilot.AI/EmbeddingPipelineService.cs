using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace DevPilot.AI;

public sealed class EmbeddingPipelineService : IEmbeddingPipelineService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IVectorStore _vectorStore;
    private readonly EmbeddingSettings _settings;
    private readonly EmbeddingVersioningSettings _versioningSettings;
    private readonly ILogger<EmbeddingPipelineService> _logger;

    public EmbeddingPipelineService(
        IEmbeddingService embeddingService,
        IEmbeddingStore embeddingStore,
        IVectorStore vectorStore,
        IOptions<EmbeddingSettings> settings,
        IOptions<EmbeddingVersioningSettings> versioningSettings,
        ILogger<EmbeddingPipelineService> logger)
    {
        _embeddingService = embeddingService;
        _embeddingStore = embeddingStore;
        _vectorStore = vectorStore;
        _settings = settings.Value;
        _versioningSettings = versioningSettings.Value;
        _logger = logger;
    }

    public async Task<int> EmbedChunksAsync(
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return 0;
        }

        var pending = new List<CodeChunk>();
        foreach (var chunk in chunks)
        {
            var isCurrent = _versioningSettings.ReembedStaleEmbeddings
                ? await _embeddingStore.IsCurrentAsync(
                    chunk.ChunkId,
                    _settings.ModelName,
                    _versioningSettings.EmbeddingModelVersion,
                    _versioningSettings.EmbeddingSchemaVersion,
                    chunk.ChunkHash,
                    cancellationToken).ConfigureAwait(false)
                : await _embeddingStore.ExistsAsync(chunk.ChunkId, _settings.ModelName, cancellationToken).ConfigureAwait(false);

            if (!isCurrent)
            {
                pending.Add(chunk);
            }
        }

        var generated = 0;
        foreach (var batch in pending.Chunk(Math.Max(1, _settings.BatchSize)))
        {
            var results = await _embeddingService.GenerateEmbeddingsAsync(
                new EmbeddingBatchRequest(batch.Select(chunk => chunk.Content).ToList(), _settings.ModelName),
                cancellationToken).ConfigureAwait(false);

            var embeddings = batch.Zip(results, (chunk, result) => new EmbeddingVector(
                DeterministicId($"{chunk.ChunkId}:{result.ModelId}"),
                chunk.ChunkId,
                result.ModelId,
                result.Vector,
                result.Dimension,
                DateTimeOffset.UtcNow,
                _versioningSettings.EmbeddingModelVersion,
                _versioningSettings.EmbeddingSchemaVersion,
                chunk.ChunkHash,
                DateTimeOffset.UtcNow)).ToList();

            await _vectorStore.SaveEmbeddingsAsync(embeddings, cancellationToken).ConfigureAwait(false);
            generated += embeddings.Count;
        }

        _logger.LogDebug("Generated and persisted {EmbeddingCount} chunk embeddings.", generated);

        return generated;
    }

    private static string DeterministicId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
