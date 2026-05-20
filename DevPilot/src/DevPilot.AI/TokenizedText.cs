namespace DevPilot.AI;

public sealed record TokenizedText(
    long[] InputIds,
    long[] AttentionMask,
    long[] TokenTypeIds,
    bool WasTruncated = false);
