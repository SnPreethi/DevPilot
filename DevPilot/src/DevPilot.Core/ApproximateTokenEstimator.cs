using DevPilot.Contracts;
using Microsoft.Extensions.Options;

namespace DevPilot.Core;

public sealed class ApproximateTokenEstimator : ITokenEstimator
{
    private readonly TokenEstimationSettings _settings;

    public ApproximateTokenEstimator(IOptions<TokenEstimationSettings> settings)
    {
        _settings = settings.Value;
    }

    public int Estimate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var charactersPerToken = Math.Max(1, _settings.CharactersPerToken);
        var estimated = (int)Math.Ceiling(text.Length / (double)charactersPerToken);
        return Math.Max(_settings.MinimumTokens, estimated);
    }
}
