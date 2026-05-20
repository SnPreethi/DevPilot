namespace DevPilot.Contracts;

public interface IEmbeddingPipelineService
{
    Task<int> EmbedChunksAsync(
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default);
}
