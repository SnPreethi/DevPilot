namespace DevPilot.Contracts;

public interface IVectorStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveEmbeddingsAsync(
        IReadOnlyCollection<EmbeddingVector> embeddings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RankedChunk>> SearchAsync(
        EmbeddingResult queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}
