namespace DevPilot.Contracts;

public sealed record SearchMatch(
    int Rank,
    RankedChunk Chunk);
