namespace DevPilot.Contracts;

public interface IChunkStore
{
    Task SaveAsync(
        CodeChunk chunk,
        CancellationToken cancellationToken = default);

    Task SaveManyAsync(
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<CodeChunk?> GetAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeChunk>> ListByFileAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeChunk>> ListByRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task ReplaceFileChunksAsync(
        string fileId,
        IReadOnlyCollection<CodeChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<int> DeleteMissingByFileAsync(
        string fileId,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default);
}
