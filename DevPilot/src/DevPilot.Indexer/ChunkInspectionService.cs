using DevPilot.Contracts;

namespace DevPilot.Indexer;

public sealed class ChunkInspectionService : IChunkInspectionService
{
    private readonly IRepositoryStore _repositoryStore;
    private readonly IChunkStore _chunkStore;
    private readonly ITokenEstimator _tokenEstimator;

    public ChunkInspectionService(
        IRepositoryStore repositoryStore,
        IChunkStore chunkStore,
        ITokenEstimator tokenEstimator)
    {
        _repositoryStore = repositoryStore;
        _chunkStore = chunkStore;
        _tokenEstimator = tokenEstimator;
    }

    public async Task<IReadOnlyList<ChunkInspectionItem>> InspectAsync(
        string? fileFilter = null,
        CancellationToken cancellationToken = default)
    {
        var repositories = await _repositoryStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ChunkInspectionItem>();

        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunks = await _chunkStore.ListByRepositoryAsync(repository.RepositoryId, cancellationToken).ConfigureAwait(false);
            foreach (var chunk in chunks)
            {
                if (!string.IsNullOrWhiteSpace(fileFilter) &&
                    !chunk.FilePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new ChunkInspectionItem(
                    chunk.ChunkId,
                    chunk.FilePath,
                    chunk.SymbolName,
                    chunk.ChunkType,
                    chunk.StartLine,
                    chunk.EndLine,
                    chunk.Content.Length,
                    chunk.TokenEstimate > 0 ? chunk.TokenEstimate : _tokenEstimator.Estimate(chunk.Content),
                    chunk.ChunkHash));
            }
        }

        return results
            .OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StartLine)
            .ToList();
    }
}
