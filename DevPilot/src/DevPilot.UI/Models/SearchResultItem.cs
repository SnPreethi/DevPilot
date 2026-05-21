namespace DevPilot.UI.Models;

public sealed record SearchResultItem(
    int Rank,
    string ChunkId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    double Similarity,
    string Preview);
