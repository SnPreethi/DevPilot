using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DevPilot.RAG;

public sealed class PromptDiagnosticsService : IPromptDiagnosticsService
{
    private readonly IRetrievalDiagnosticsService _retrievalDiagnostics;
    private readonly IPromptBuilder _promptBuilder;
    private readonly RagSettings _ragSettings;

    public PromptDiagnosticsService(
        IRetrievalDiagnosticsService retrievalDiagnostics,
        IPromptBuilder promptBuilder,
        IOptions<RagSettings> ragSettings)
    {
        _retrievalDiagnostics = retrievalDiagnostics;
        _promptBuilder = promptBuilder;
        _ragSettings = ragSettings.Value;
    }

    public async Task<PromptDiagnostics> InspectAsync(
        RagRequest request,
        CancellationToken cancellationToken = default)
    {
        var retrievalCount = request.MaxContextChunks > 0
            ? request.MaxContextChunks
            : _ragSettings.RetrievalCount;

        var retrieval = await _retrievalDiagnostics.InspectAsync(
            new SearchRequest(request.Question, retrievalCount, request.RepositoryId),
            cancellationToken).ConfigureAwait(false);

        var context = retrieval.Matches
            .Take(_ragSettings.MaxContextChunks)
            .Select(match => new RetrievedContext(
                match.ChunkId,
                match.FilePath,
                match.SymbolName,
                match.ChunkType,
                match.StartLine,
                match.EndLine,
                match.Preview,
                match.Similarity))
            .ToList();

        var promptStopwatch = Stopwatch.StartNew();
        var prompt = await _promptBuilder.BuildAsync(request.Question, context, cancellationToken).ConfigureAwait(false);
        promptStopwatch.Stop();

        return new PromptDiagnostics(
            request.Question,
            context.Count,
            prompt.EstimatedTokenCount,
            retrieval.RetrievalDuration,
            promptStopwatch.Elapsed,
            prompt,
            retrieval.Matches);
    }
}
