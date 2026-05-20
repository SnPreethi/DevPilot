using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.RAG;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class PromptDiagnosticsServiceTests
{
    [Fact]
    public async Task InspectAsync_RendersPromptAndReportsRetrievedChunks()
    {
        var promptBuilder = new GroundedPromptBuilder(
            Options.Create(new PromptingSettings()),
            new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings())));
        var service = new PromptDiagnosticsService(
            new FakeRetrievalDiagnosticsService(),
            promptBuilder,
            Options.Create(new RagSettings { RetrievalCount = 1, MaxContextChunks = 1 }));

        var result = await service.InspectAsync(new RagRequest("How?", null, 1));

        Assert.Equal(1, result.RetrievedChunkCount);
        Assert.Contains("AuthService.cs", result.Prompt.Text);
        Assert.True(result.EstimatedPromptTokens > 0);
    }

    private sealed class FakeRetrievalDiagnosticsService : IRetrievalDiagnosticsService
    {
        public Task<RetrievalDiagnostics> InspectAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var match = new RetrievalDiagnosticMatch(
                1,
                "chunk-1",
                "AuthService.cs",
                "Validate",
                "method",
                1,
                12,
                0.91,
                8,
                "jwt validation flow");

            return Task.FromResult(new RetrievalDiagnostics(
                request.Query,
                3,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(2),
                [match]));
        }
    }
}
