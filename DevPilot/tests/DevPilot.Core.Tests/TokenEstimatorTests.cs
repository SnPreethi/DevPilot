using DevPilot.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class TokenEstimatorTests
{
    [Fact]
    public void Estimate_UsesConfiguredCharacterApproximation()
    {
        var estimator = new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings
        {
            CharactersPerToken = 4,
            MinimumTokens = 1
        }));

        Assert.Equal(3, estimator.Estimate("abcdefghijkl"));
        Assert.Equal(0, estimator.Estimate(""));
    }
}
