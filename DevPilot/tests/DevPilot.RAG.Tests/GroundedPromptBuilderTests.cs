using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.RAG;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class GroundedPromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_IncludesGroundingRulesQuestionAndFileReferences()
    {
        var builder = new GroundedPromptBuilder(
            Options.Create(new PromptingSettings()),
            new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings())));
        var context = new RetrievedContext(
            "chunk-1",
            "Services/AuthService.cs",
            "RefreshToken",
            "method",
            12,
            40,
            "public string RefreshToken() => token;",
            0.91);

        var prompt = await builder.BuildAsync("Explain token refresh", [context]);

        Assert.Contains("Use ONLY the provided context", prompt.Text);
        Assert.Contains("Services/AuthService.cs", prompt.Text);
        Assert.Contains("Explain token refresh", prompt.Text);
        Assert.True(prompt.EstimatedTokenCount > 0);
    }
}
