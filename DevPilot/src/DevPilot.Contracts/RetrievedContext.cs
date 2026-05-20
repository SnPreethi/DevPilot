namespace DevPilot.Contracts;

public sealed record RetrievedContext(
    string ChunkId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    string Content,
    double RelevanceScore);
