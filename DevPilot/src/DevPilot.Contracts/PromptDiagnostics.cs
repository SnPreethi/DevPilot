namespace DevPilot.Contracts;

public sealed record PromptDiagnostics(
    string Question,
    int RetrievedChunkCount,
    int EstimatedPromptTokens,
    TimeSpan RetrievalDuration,
    TimeSpan PromptBuildDuration,
    GroundedPrompt Prompt,
    IReadOnlyList<RetrievalDiagnosticMatch> RetrievedChunks);

public interface IPromptDiagnosticsService
{
    Task<PromptDiagnostics> InspectAsync(
        RagRequest request,
        CancellationToken cancellationToken = default);
}
