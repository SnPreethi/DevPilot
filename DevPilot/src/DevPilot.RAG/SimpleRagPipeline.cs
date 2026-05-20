using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DevPilot.RAG;

public sealed class SimpleRagPipeline : IRagPipeline
{
    private readonly ISemanticSearchService _semanticSearchService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ILLMService _llmService;
    private readonly RagSettings _ragSettings;
    private readonly LLMSettings _llmSettings;
    private readonly PromptingSettings _promptingSettings;
    private readonly ILogger<SimpleRagPipeline> _logger;

    public SimpleRagPipeline(
        ISemanticSearchService semanticSearchService,
        IPromptBuilder promptBuilder,
        ILLMService llmService,
        IOptions<RagSettings> ragSettings,
        IOptions<LLMSettings> llmSettings,
        IOptions<PromptingSettings> promptingSettings,
        ILogger<SimpleRagPipeline> logger)
    {
        _semanticSearchService = semanticSearchService;
        _promptBuilder = promptBuilder;
        _llmService = llmService;
        _ragSettings = ragSettings.Value;
        _llmSettings = llmSettings.Value;
        _promptingSettings = promptingSettings.Value;
        _logger = logger;
    }

    public async Task<AssistantResponse> AskAsync(
        RagRequest request,
        CancellationToken cancellationToken = default)
    {
        var retrievalCount = request.MaxContextChunks > 0
            ? request.MaxContextChunks
            : _ragSettings.RetrievalCount;

        _logger.LogInformation("Retrieving semantic context for grounded answer.");
        var retrievalStopwatch = Stopwatch.StartNew();
        var searchResult = await _semanticSearchService.SearchAsync(
            new SearchRequest(request.Question, retrievalCount * 3, request.RepositoryId), // Retrieve more candidates for reranking
            cancellationToken).ConfigureAwait(false);
        retrievalStopwatch.Stop();

        var rawChunks = searchResult.Matches.Select(m => m.Chunk).ToList();
        var context = RagOptimizer.Optimize(
            request.Question,
            rawChunks,
            _ragSettings.MaxContextChunks,
            _promptingSettings.MaxPromptCharacters,
            _promptingSettings.MaxChunkCharacters);

        _logger.LogInformation(
            "Retrieved {ContextCount} context chunks in {ElapsedMilliseconds} ms.",
            context.Count,
            retrievalStopwatch.ElapsedMilliseconds);

        _logger.LogInformation("Building grounded prompt.");
        var prompt = await _promptBuilder.BuildAsync(request.Question, context, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Running local inference.");
        var inference = await _llmService.GenerateAsync(
            new InferenceRequest(
                prompt.Text,
                _llmSettings.ModelId,
                _llmSettings.MaxOutputTokens,
                _llmSettings.Temperature,
                _llmSettings.TopP,
                context),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Inference completed in {ElapsedMilliseconds} ms using model {ModelId}.",
            inference.Duration.TotalMilliseconds,
            inference.ModelId);

        return new AssistantResponse(
            inference.Answer,
            context,
            inference.Duration,
            inference.PromptTokenCount,
            inference.OutputTokenCount,
            context.Count);
    }
}
