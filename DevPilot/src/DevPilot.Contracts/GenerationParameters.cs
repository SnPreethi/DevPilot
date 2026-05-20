using System;

namespace DevPilot.Contracts;

public sealed class GenerationParameters
{
    public float Temperature { get; init; } = 0.7f;

    public int TopK { get; init; } = 40;

    public float TopP { get; init; } = 0.9f;

    public float RepetitionPenalty { get; init; } = 1.1f;

    public float FrequencyPenalty { get; init; } = 0.0f;

    public float PresencePenalty { get; init; } = 0.0f;

    public int MaxTokens { get; init; } = 512;

    public string[] StopSequences { get; init; } = Array.Empty<string>();
}
