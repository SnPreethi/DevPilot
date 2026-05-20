namespace DevPilot.Core;

public sealed class PromptingSettings
{
    public int MaxPromptCharacters { get; init; } = 12_000;

    public int MaxChunkCharacters { get; init; } = 2_000;
}
