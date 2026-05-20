using DevPilot.Contracts;
using DevPilot.Storage;
using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public sealed class DiagnosticsApplicationService : IDiagnosticsApplicationService
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IRetrievalDiagnosticsService _retrievalDiagnosticsService;
    private readonly IPromptDiagnosticsService _promptDiagnosticsService;
    private readonly IChunkInspectionService _chunkInspectionService;
    private readonly IModelValidationService _modelValidationService;

    public DiagnosticsApplicationService(
        DatabaseInitializer databaseInitializer,
        IRetrievalDiagnosticsService retrievalDiagnosticsService,
        IPromptDiagnosticsService promptDiagnosticsService,
        IChunkInspectionService chunkInspectionService,
        IModelValidationService modelValidationService)
    {
        _databaseInitializer = databaseInitializer;
        _retrievalDiagnosticsService = retrievalDiagnosticsService;
        _promptDiagnosticsService = promptDiagnosticsService;
        _chunkInspectionService = chunkInspectionService;
        _modelValidationService = modelValidationService;
    }

    public async Task<DiagnosticsSummary> InspectAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var retrieval = await _retrievalDiagnosticsService.InspectAsync(
            new SearchRequest(query, maxResults),
            cancellationToken).ConfigureAwait(false);
        var prompt = await _promptDiagnosticsService.InspectAsync(
            new RagRequest(query, null, maxResults),
            cancellationToken).ConfigureAwait(false);
        var chunks = await _chunkInspectionService.InspectAsync(null, cancellationToken).ConfigureAwait(false);
        var runtime = await _modelValidationService.ValidateAsync(query, cancellationToken).ConfigureAwait(false);

        return new DiagnosticsSummary(
            retrieval.Matches.Select(match => new SearchResultItem(
                match.Rank,
                match.ChunkId,
                match.FilePath,
                match.SymbolName,
                match.ChunkType,
                match.StartLine,
                match.EndLine,
                match.Similarity,
                match.Preview)).ToList(),
            prompt.Prompt.Text,
            prompt.EstimatedPromptTokens,
            retrieval.RetrievalDuration,
            chunks.Select(chunk => new ChunkSummaryItem(
                chunk.ChunkId,
                chunk.FilePath,
                chunk.SymbolName,
                chunk.StartLine,
                chunk.EndLine,
                chunk.TokenEstimate,
                chunk.ChunkHash)).ToList(),
            ToRuntimeView(runtime));
    }

    private static RuntimeDiagnosticsView ToRuntimeView(RuntimeValidationReport report)
    {
        var providers = string.Join(", ", report.Runtime.Providers.Select(provider =>
            $"{provider.Provider}: {(provider.IsAvailable ? "available" : "unavailable")}{(provider.IsSelected ? " selected" : string.Empty)}"));

        return new RuntimeDiagnosticsView(
            report.Runtime.SelectedProvider.ToString(),
            providers,
            $"CPU {report.Runtime.Hardware.CpuDescription}, RAM {ToMegabytes(report.Runtime.Hardware.TotalMemoryBytes)} MB, OS {report.Runtime.Hardware.OperatingSystem}",
            ToModelStatus(report.EmbeddingModel),
            ToModelStatus(report.LlmModel),
            report.Tokenizer.IsCompatible
                ? $"Compatible, active tokens {report.Tokenizer.ActiveTokens}/{report.Tokenizer.RequestedMaxTokens}, truncated {report.Tokenizer.WasTruncated}"
                : string.Join("; ", report.Tokenizer.Issues.Select(issue => issue.Message)),
            $"Process {ToMegabytes(report.Runtime.Hardware.CurrentProcessMemoryBytes)} MB, profile delta {ToMegabytes(report.Profile.MemoryAfterBytes - report.Profile.MemoryBeforeBytes)} MB",
            $"Embedding {report.Profile.EmbeddingDuration.TotalMilliseconds:0} ms, retrieval {report.Profile.RetrievalDuration.TotalMilliseconds:0} ms, prompt {report.Profile.PromptBuildDuration.TotalMilliseconds:0} ms, inference {report.Profile.InferenceDuration.TotalMilliseconds:0} ms");
    }

    private static string ToModelStatus(ModelValidationResult result)
    {
        if (!result.Exists)
        {
            return $"{result.ModelName}: missing";
        }

        if (!result.Loaded)
        {
            return $"{result.ModelName}: load failed";
        }

        return $"{result.ModelName}: loaded in {result.LoadDuration.TotalMilliseconds:0} ms via {result.ExecutionProvider}";
    }

    private static long ToMegabytes(long bytes) => bytes / 1024 / 1024;
}
