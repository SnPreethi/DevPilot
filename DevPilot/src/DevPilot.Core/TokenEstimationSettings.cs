namespace DevPilot.Core;

public sealed class TokenEstimationSettings
{
    public int CharactersPerToken { get; set; } = 4;
    public int MinimumTokens { get; set; } = 1;
}
