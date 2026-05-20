using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DevPilot.AI;

public sealed class InferenceProfiler : IInferenceProfiler
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ILLMService _llmService;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly LLMSettings _llmSettings;

    public InferenceProfiler(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IPromptBuilder promptBuilder,
        ILLMService llmService,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<LLMSettings> llmSettings)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _promptBuilder = promptBuilder;
        _llmService = llmService;
        _embeddingSettings = embeddingSettings.Value;
        _llmSettings = llmSettings.Value;
    }

    public async Task<InferenceProfile> ProfileAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var memoryBefore = Environment.WorkingSet;

        var stopwatch = Stopwatch.StartNew();
        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            new EmbeddingRequest(prompt, _embeddingSettings.ModelName),
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var embeddingDuration = stopwatch.Elapsed;

        stopwatch.Restart();
        var ranked = await _vectorStore.SearchAsync(embedding, topK: 3, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var retrievalDuration = stopwatch.Elapsed;

        var context = ranked.Select(chunk => new RetrievedContext(
            chunk.ChunkId,
            chunk.FilePath,
            chunk.SymbolName,
            chunk.ChunkType,
            chunk.StartLine,
            chunk.EndLine,
            chunk.ContentPreview,
            chunk.RelevanceScore)).ToList();

        stopwatch.Restart();
        var groundedPrompt = await _promptBuilder.BuildAsync(prompt, context, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var promptDuration = stopwatch.Elapsed;

        /*stopwatch.Restart();
        var inference = await _llmService.GenerateAsync(
            new InferenceRequest(
                groundedPrompt.Text,
                _llmSettings.ModelId,
                Math.Min(64, _llmSettings.MaxOutputTokens),
                _llmSettings.Temperature,
                _llmSettings.TopP,
                context),
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();*/

        stopwatch.Restart();
        var inferenceRequest =
            new InferenceRequest(
                groundedPrompt.Text,
                _llmSettings.ModelId,
                Math.Min(64, _llmSettings.MaxOutputTokens),
                _llmSettings.Temperature,
                _llmSettings.TopP,
                context);
        var streamedText = string.Empty;
        var streamedTokens = 0;
        
        /*
        await foreach (
            var chunk in _llmService.StreamAsync(
                inferenceRequest,
                cancellationToken).ConfigureAwait(false))
        {
            streamedText += chunk;
            streamedTokens++;
        }
        */

        await foreach (
            var chunk in _llmService.StreamAsync(
                inferenceRequest,
                cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine($"[PROFILE] Stream chunk: '{chunk}'");
            if (string.IsNullOrEmpty(chunk))
            {
                Console.WriteLine("[PROFILE] Empty chunk skipped.");
                continue;
            }

            streamedText += chunk;
            streamedTokens++;
            Console.WriteLine($"[PROFILE] Streamed tokens: {streamedTokens}");
        }

        stopwatch.Stop();
        var inference =
            new InferenceResult(
                streamedText,
                _llmSettings.ModelId,
                stopwatch.Elapsed,
                0,
                streamedTokens,
                UsedFallback: false);

        if (streamedText.Length > 500)
        {
            streamedText = streamedText[..500];
        }
        
        return new InferenceProfile(
            embeddingDuration,
            retrievalDuration,
            promptDuration,
            stopwatch.Elapsed,
            TimeSpan.Zero,
            memoryBefore,
            Environment.WorkingSet,
            UsedEmbeddingFallback: embedding.Dimension == _embeddingSettings.Dimensions,
            UsedLlmFallback: false,
            StreamingTokens: streamedTokens,
            StreamingPartial: streamedText);
    }
}
