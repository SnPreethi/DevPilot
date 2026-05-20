using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPilot.AI;

public sealed class SemanticSearchService : ISemanticSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly VectorSearchSettings _settings;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IOptions<VectorSearchSettings> settings,
        ILogger<SemanticSearchService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SemanticSearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var topK = request.MaxResults > 0 ? request.MaxResults : _settings.DefaultTopK;
        _logger.LogInformation("Generating query embedding.");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            new EmbeddingRequest(request.Query),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Performing semantic retrieval with top K {TopK}.", topK);
        var rankedChunks = await _vectorStore.SearchAsync(queryEmbedding, topK, cancellationToken).ConfigureAwait(false);
        var matches = rankedChunks
            .Select((chunk, index) => new SearchMatch(index + 1, chunk))
            .ToList();

        return new SemanticSearchResult(request.Query, matches);
    }
}
