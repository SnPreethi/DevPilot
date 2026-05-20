namespace DevPilot.Core;

public sealed class TokenizerSettings
{
    public int EmbeddingMaxTokens { get; init; } = 256;

    public int LlmMaxTokens { get; init; } = 4096;

    public bool EnableTruncation { get; init; } = true;
}
