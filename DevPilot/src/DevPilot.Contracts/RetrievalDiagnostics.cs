namespace DevPilot.Contracts;

public sealed record RetrievalDiagnostics(
    string Query,
    int QueryEmbeddingDimensions,
    TimeSpan QueryEmbeddingDuration,
    TimeSpan RetrievalDuration,
    IReadOnlyList<RetrievalDiagnosticMatch> Matches);

public sealed record RetrievalDiagnosticMatch(
    int Rank,
    string ChunkId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    double Similarity,
    int TokenEstimate,
    string Preview);

public interface IRetrievalDiagnosticsService
{
    Task<RetrievalDiagnostics> InspectAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);
}
