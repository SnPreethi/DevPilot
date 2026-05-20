using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DevPilot.AI;

public sealed class RetrievalDiagnosticsService : IRetrievalDiagnosticsService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly VectorSearchSettings _settings;
    private readonly ILogger<RetrievalDiagnosticsService> _logger;

    public RetrievalDiagnosticsService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ITokenEstimator tokenEstimator,
        IOptions<VectorSearchSettings> settings,
        ILogger<RetrievalDiagnosticsService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _tokenEstimator = tokenEstimator;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RetrievalDiagnostics> InspectAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var topK = request.MaxResults > 0 ? request.MaxResults : _settings.DefaultTopK;

        var embeddingStopwatch = Stopwatch.StartNew();
        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            new EmbeddingRequest(request.Query),
            cancellationToken).ConfigureAwait(false);
        embeddingStopwatch.Stop();

        var retrievalStopwatch = Stopwatch.StartNew();
        var rankedChunks = await _vectorStore.SearchAsync(embedding, topK, cancellationToken).ConfigureAwait(false);
        retrievalStopwatch.Stop();

        var matches = rankedChunks.Select((chunk, index) => new RetrievalDiagnosticMatch(
            index + 1,
            chunk.ChunkId,
            chunk.FilePath,
            chunk.SymbolName,
            chunk.ChunkType,
            chunk.StartLine,
            chunk.EndLine,
            chunk.RelevanceScore,
            _tokenEstimator.Estimate(chunk.ContentPreview),
            chunk.ContentPreview)).ToList();

        _logger.LogInformation(
            "Retrieval diagnostics completed: {MatchCount} matches, embedding {EmbeddingMs} ms, retrieval {RetrievalMs} ms.",
            matches.Count,
            embeddingStopwatch.ElapsedMilliseconds,
            retrievalStopwatch.ElapsedMilliseconds);

        return new RetrievalDiagnostics(
            request.Query,
            embedding.Dimension,
            embeddingStopwatch.Elapsed,
            retrievalStopwatch.Elapsed,
            matches);
    }
}
