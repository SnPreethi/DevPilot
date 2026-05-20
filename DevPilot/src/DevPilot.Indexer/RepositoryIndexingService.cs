using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DevPilot.Indexer;

public sealed class RepositoryIndexingService : IRepositoryIndexingService
{
    private readonly IRepositoryScanner _scanner;
    private readonly RepositoryScanner _scannerMetrics;
    private readonly FileMetadataExtractor _metadataExtractor;
    private readonly CodeChunker _chunker;
    private readonly IRepositoryStore _repositoryStore;
    private readonly IFileMetadataStore _fileMetadataStore;
    private readonly IChunkStore _chunkStore;
    private readonly ISymbolStore _symbolStore;
    private readonly IEmbeddingPipelineService _embeddingPipeline;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly IncrementalIndexingSettings _incrementalSettings;
    private readonly PerformanceSettings _performanceSettings;
    private readonly ILogger<RepositoryIndexingService> _logger;

    public RepositoryIndexingService(
        IRepositoryScanner scanner,
        RepositoryScanner scannerMetrics,
        FileMetadataExtractor metadataExtractor,
        CodeChunker chunker,
        IRepositoryStore repositoryStore,
        IFileMetadataStore fileMetadataStore,
        IChunkStore chunkStore,
        ISymbolStore symbolStore,
        IEmbeddingPipelineService embeddingPipeline,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<IncrementalIndexingSettings> incrementalSettings,
        IOptions<PerformanceSettings> performanceSettings,
        ILogger<RepositoryIndexingService> logger)
    {
        _scanner = scanner;
        _scannerMetrics = scannerMetrics;
        _metadataExtractor = metadataExtractor;
        _chunker = chunker;
        _repositoryStore = repositoryStore;
        _fileMetadataStore = fileMetadataStore;
        _chunkStore = chunkStore;
        _symbolStore = symbolStore;
        _embeddingPipeline = embeddingPipeline;
        _embeddingSettings = embeddingSettings.Value;
        _incrementalSettings = incrementalSettings.Value;
        _performanceSettings = performanceSettings.Value;
        _logger = logger;
    }

    public async Task<IndexingResult> IndexAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        var rootPath = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Repository path does not exist: {rootPath}");
        }

        var repositoryName = new DirectoryInfo(rootPath).Name;
        var repositoryId = DeterministicId(rootPath.ToUpperInvariant());
        var repository = new RepositoryDocument(repositoryId, repositoryName, rootPath, DateTimeOffset.UtcNow);
        var scanDescriptor = new RepositoryDescriptor(repositoryId, repositoryName, rootPath, repository.IndexedAtUtc);

        _logger.LogInformation("Starting repository indexing for {RepositoryPath}.", rootPath);
        _logger.LogInformation("Repository detected: {RepositoryName}", repositoryName);

        await _repositoryStore.SaveAsync(repository, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Repository metadata persisted. Scanning files...");

        var stopwatch = Stopwatch.StartNew();
        var existingFiles = (await _fileMetadataStore.ListByRepositoryAsync(repository.RepositoryId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesScanned = 0;
        var filesSkipped = 0;
        var filesDeleted = 0;
        var chunksCreated = 0;
        var embeddingsCreated = 0;
        var lastProgressLog = DateTimeOffset.UtcNow;
        var progressLock = new object();
        var dbWriteSemaphore = new SemaphoreSlim(1, 1);

        // Gather all scanned files first
        var filesToProcess = new List<RepositoryFile>();
        await foreach (var file in _scanner.ScanAsync(scanDescriptor, cancellationToken).ConfigureAwait(false))
        {
            filesToProcess.Add(file);
        }

        var maxConcurrency = Math.Max(1, Environment.ProcessorCount);
        
        await Parallel.ForEachAsync(filesToProcess, new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        }, async (file, ct) =>
        {
            try
            {
                var metadata = await _metadataExtractor.ExtractAsync(repository, file, ct).ConfigureAwait(false);
                
                lock (scannedPaths)
                {
                    scannedPaths.Add(metadata.RelativePath);
                }

                if (_incrementalSettings.Enabled &&
                    _incrementalSettings.SkipUnchangedFiles &&
                    existingFiles.TryGetValue(metadata.RelativePath, out var existingFile) &&
                    string.Equals(existingFile.SHA256Hash, metadata.SHA256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    if (_embeddingSettings.GenerateDuringIndexing)
                    {
                        var existingChunks = await _chunkStore.ListByFileAsync(existingFile.Id, ct).ConfigureAwait(false);
                        var embedCount = await _embeddingPipeline.EmbedChunksAsync(existingChunks, ct).ConfigureAwait(false);
                        Interlocked.Add(ref embeddingsCreated, embedCount);
                    }

                    Interlocked.Increment(ref filesSkipped);
                    Interlocked.Increment(ref filesScanned);
                    return;
                }

                var content = await File.ReadAllTextAsync(file.FullPath, ct).ConfigureAwait(false);
                var codeFile = new CodeFile(metadata, content);

                var chunks = _chunker.Chunk(
                    repository.RepositoryId,
                    codeFile.Metadata.Id,
                    codeFile.Metadata.RelativePath,
                    codeFile.Content,
                    codeFile.Metadata.Language);

                // Save symbol mappings
                var symbolsToSave = chunks
                    .Where(c => !string.IsNullOrEmpty(c.SymbolName) && c.SymbolKind != null)
                    .Select(c => new SymbolIndexEntry(
                        SymbolId: c.ChunkId,
                        RepositoryId: repository.RepositoryId,
                        FileId: codeFile.Metadata.Id,
                        FilePath: codeFile.Metadata.RelativePath,
                        Name: c.SymbolName!,
                        Kind: c.SymbolKind!,
                        Namespace: c.Namespace,
                        ParentSymbol: c.ParentSymbol,
                        ReferencedSymbols: c.ReferencedSymbols ?? Array.Empty<string>(),
                        ImportedNamespaces: c.ImportedNamespaces ?? Array.Empty<string>(),
                        FileDependencies: c.FileDependencies ?? Array.Empty<string>(),
                        DefinitionLocation: c.DefinitionLocation ?? $"{codeFile.Metadata.RelativePath}:{c.StartLine}:{c.EndLine}",
                        StartLine: c.StartLine,
                        EndLine: c.EndLine
                    ))
                    .ToList();

                var currentEmbeddings = 0;

                // SQLite database writes are serialized using a SemaphoreSlim to keep transactions robust
                await dbWriteSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _fileMetadataStore.SaveAsync(codeFile.Metadata, ct).ConfigureAwait(false);
                    await _chunkStore.SaveManyAsync(chunks, ct).ConfigureAwait(false);
                    await _chunkStore.DeleteMissingByFileAsync(codeFile.Metadata.Id, chunks.Select(chunk => chunk.ChunkId).ToList(), ct).ConfigureAwait(false);

                    await _symbolStore.DeleteByFileAsync(codeFile.Metadata.Id, ct).ConfigureAwait(false);
                    if (symbolsToSave.Count > 0)
                    {
                        await _symbolStore.SaveManyAsync(symbolsToSave, ct).ConfigureAwait(false);
                    }

                    if (_embeddingSettings.GenerateDuringIndexing)
                    {
                        currentEmbeddings = await _embeddingPipeline.EmbedChunksAsync(chunks, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    dbWriteSemaphore.Release();
                }

                Interlocked.Add(ref embeddingsCreated, currentEmbeddings);
                Interlocked.Increment(ref filesScanned);
                Interlocked.Add(ref chunksCreated, chunks.Count);

                var scannedSnapshot = Volatile.Read(ref filesScanned);
                if (scannedSnapshot % 25 == 0 || DateTimeOffset.UtcNow - lastProgressLog > TimeSpan.FromSeconds(10))
                {
                    _logger.LogInformation(
                        "Indexing progress: {FilesScanned} files scanned, {FilesSkipped} files skipped, {FilesIgnored} files ignored, {ChunksCreated} chunks created. Current file: {RelativePath}",
                        scannedSnapshot,
                        Volatile.Read(ref filesSkipped),
                        _scannerMetrics.LastIgnoredFileCount,
                        Volatile.Read(ref chunksCreated),
                        file.RelativePath);
                    
                    lock (progressLock)
                    {
                        lastProgressLog = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Indexing was cancelled for {RepositoryPath}.", rootPath);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Skipping file {FilePath} during indexing.", file.FullPath);
            }
        });

        if (_incrementalSettings.Enabled && _incrementalSettings.RemoveDeletedFiles)
        {
            filesDeleted = await _fileMetadataStore.DeleteMissingAsync(repository.RepositoryId, scannedPaths.ToList(), cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();

        var result = new IndexingResult(
            repository.RepositoryId,
            repository.RepositoryName,
            filesScanned,
            _scannerMetrics.LastIgnoredFileCount,
            chunksCreated,
            filesSkipped,
            filesDeleted,
            embeddingsCreated,
            stopwatch.Elapsed);

        _logger.LogInformation("Files scanned: {FilesScanned}", result.FilesScanned);
        _logger.LogInformation("Files skipped unchanged: {FilesSkipped}", result.FilesSkipped);
        _logger.LogInformation("Files deleted: {FilesDeleted}", result.FilesDeleted);
        _logger.LogInformation("Files ignored: {FilesIgnored}", result.FilesIgnored);
        _logger.LogInformation("Chunks created: {ChunksCreated}", result.ChunksCreated);
        if (_embeddingSettings.GenerateDuringIndexing)
        {
            _logger.LogInformation("Embeddings created: {EmbeddingsCreated}", embeddingsCreated);
        }
        _logger.LogInformation("Metadata persisted successfully.");
        if (_performanceSettings.LogSummaries)
        {
            _logger.LogInformation("Indexing duration: {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
        }
        _logger.LogInformation("Indexing completed for {RepositoryName}.", repositoryName);

        return result;
    }

    private static string DeterministicId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
