using DevPilot.AI;
using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.AI.Tests;

public sealed class RetrievalDiagnosticsTests
{
    [Fact]
    public async Task InspectAsync_ReturnsSimilarityAndTimingMetadata()
    {
        var service = new RetrievalDiagnosticsService(
            new FakeEmbeddingService(),
            new FakeVectorStore(),
            new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings())),
            Options.Create(new VectorSearchSettings { DefaultTopK = 1 }),
            NullLogger<RetrievalDiagnosticsService>.Instance);

        var result = await service.InspectAsync(new SearchRequest("jwt validation", 1));

        Assert.Equal(3, result.QueryEmbeddingDimensions);
        Assert.Single(result.Matches);
        Assert.Equal(0.92, result.Matches[0].Similarity);
        Assert.True(result.Matches[0].TokenEstimate > 0);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingResult> GenerateEmbeddingAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EmbeddingResult("fake", [1f, 0f, 0f], 3));
        }

        public Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
            EmbeddingBatchRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<EmbeddingResult> results = request.Inputs
                .Select(_ => new EmbeddingResult("fake", [1f, 0f, 0f], 3))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveEmbeddingsAsync(
            IReadOnlyCollection<EmbeddingVector> embeddings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        public Task<IReadOnlyList<RankedChunk>> SearchAsync(
            EmbeddingResult queryEmbedding,
            int topK,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RankedChunk> chunks =
            [
                new RankedChunk("chunk-1", "AuthService.cs", "Validate", "method", 1, 12, "jwt validation flow", 0.92)
            ];
            return Task.FromResult(chunks);
        }
    }
}
