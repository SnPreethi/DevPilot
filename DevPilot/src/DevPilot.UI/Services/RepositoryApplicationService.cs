using DevPilot.Contracts;
using DevPilot.Storage;
using DevPilot.UI.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.Services;

public sealed class RepositoryApplicationService : IRepositoryApplicationService
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IRepositoryIndexingService _indexingService;
    private readonly IRepositoryStore _repositoryStore;
    private readonly IFileMetadataStore _fileMetadataStore;
    private readonly IChunkStore _chunkStore;
    private readonly ILogger<RepositoryApplicationService> _logger;

    public RepositoryApplicationService(
        DatabaseInitializer databaseInitializer,
        IRepositoryIndexingService indexingService,
        IRepositoryStore repositoryStore,
        IFileMetadataStore fileMetadataStore,
        IChunkStore chunkStore,
        ILogger<RepositoryApplicationService> logger)
    {
        _databaseInitializer = databaseInitializer;
        _indexingService = indexingService;
        _repositoryStore = repositoryStore;
        _fileMetadataStore = fileMetadataStore;
        _chunkStore = chunkStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RepositoryItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var repositories = await _repositoryStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<RepositoryItem>();

        foreach (var repository in repositories)
        {
            items.Add(await MapAsync(repository, cancellationToken).ConfigureAwait(false));
        }

        return items;
    }

    public async Task<RepositoryItem> IndexAsync(
        string repositoryPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Indexing repository...");
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var result = await _indexingService.IndexAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "UI indexing completed for {RepositoryName}: {FilesScanned} scanned, {FilesSkipped} skipped, {ChunksCreated} chunks.",
            result.RepositoryName,
            result.FilesScanned,
            result.FilesSkipped,
            result.ChunksCreated);

        var repository = await _repositoryStore.GetAsync(result.RepositoryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Indexed repository was not persisted.");
        progress?.Report("Indexing complete.");
        return await MapAsync(repository, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _repositoryStore.DeleteAsync(repositoryId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RepositoryItem> MapAsync(
        RepositoryDocument repository,
        CancellationToken cancellationToken)
    {
        var files = await _fileMetadataStore.ListByRepositoryAsync(repository.RepositoryId, cancellationToken).ConfigureAwait(false);
        var chunks = await _chunkStore.ListByRepositoryAsync(repository.RepositoryId, cancellationToken).ConfigureAwait(false);
        return new RepositoryItem(
            repository.RepositoryId,
            repository.RepositoryName,
            repository.RootPath,
            repository.IndexedAtUtc,
            files.Count,
            chunks.Count);
    }
}
