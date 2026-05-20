using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.RAG;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class SimpleRagPipelineTests
{
    [Fact]
    public async Task AskAsync_RetrievesBuildsPromptAndRunsInference()
    {
        var semanticSearch = new FakeSemanticSearchService();
        var promptBuilder = new GroundedPromptBuilder(
            Options.Create(new PromptingSettings()),
            new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings())));
        var llm = new FakeLlmService();
        var pipeline = new SimpleRagPipeline(
            semanticSearch,
            promptBuilder,
            llm,
            Options.Create(new RagSettings { RetrievalCount = 2, MaxContextChunks = 2 }),
            Options.Create(new LLMSettings()),
            Options.Create(new PromptingSettings()),
            NullLogger<SimpleRagPipeline>.Instance);

        var response = await pipeline.AskAsync(new RagRequest("How does auth work?", null, 2));

        Assert.Contains("grounded answer", response.Answer);
        Assert.Single(response.ReferencedContext);
        Assert.True(semanticSearch.WasCalled);
        Assert.True(llm.WasCalled);
    }

    private sealed class FakeSemanticSearchService : ISemanticSearchService
    {
        public bool WasCalled { get; private set; }

        public Task<SemanticSearchResult> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            var ranked = new RankedChunk(
                "chunk-1",
                "AuthService.cs",
                "Validate",
                "method",
                1,
                20,
                "jwt validation flow",
                0.9);

            return Task.FromResult(new SemanticSearchResult(request.Query, [new SearchMatch(1, ranked)]));
        }
    }

    private sealed class FakeLlmService : ILLMService
    {
        public bool WasCalled { get; private set; }

        public Task<InferenceResult> GenerateAsync(
            InferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new InferenceResult("grounded answer", "fake", TimeSpan.FromMilliseconds(1), 10, 2, false));
        }

        public async IAsyncEnumerable<string> StreamAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return "grounded";
        }
    }
}
