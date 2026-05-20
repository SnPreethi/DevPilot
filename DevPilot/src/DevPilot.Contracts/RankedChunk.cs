namespace DevPilot.Contracts;

public sealed record RankedChunk(
    string ChunkId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    string ContentPreview,
    double RelevanceScore);
