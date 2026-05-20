namespace DevPilot.Contracts;

public interface IEmbeddingStore
{
    Task SaveManyAsync(
        IReadOnlyCollection<EmbeddingVector> embeddings,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string chunkId,
        string modelName,
        CancellationToken cancellationToken = default);

    Task<bool> IsCurrentAsync(
        string chunkId,
        string modelName,
        string embeddingModelVersion,
        int embeddingSchemaVersion,
        string chunkHash,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<EmbeddingVector> ListByModelAsync(
        string modelName,
        CancellationToken cancellationToken = default);
}
