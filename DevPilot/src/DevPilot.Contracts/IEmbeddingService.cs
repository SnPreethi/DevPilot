namespace DevPilot.Contracts;

public interface IEmbeddingService
{
    Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
        EmbeddingBatchRequest request,
        CancellationToken cancellationToken = default);
}
