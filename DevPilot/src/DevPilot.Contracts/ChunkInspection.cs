namespace DevPilot.Contracts;

public sealed record ChunkInspectionItem(
    string ChunkId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    int CharacterCount,
    int TokenEstimate,
    string ChunkHash);

public interface IChunkInspectionService
{
    Task<IReadOnlyList<ChunkInspectionItem>> InspectAsync(
        string? fileFilter = null,
        CancellationToken cancellationToken = default);
}
