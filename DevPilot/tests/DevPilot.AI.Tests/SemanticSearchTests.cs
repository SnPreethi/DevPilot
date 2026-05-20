using DevPilot.AI;
using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.AI.Tests;

public sealed class SemanticSearchTests
{
    [Fact]
    public async Task SemanticSearch_ReturnsRankedMatchesByCosineSimilarity()
    {
        using var workspace = TemporaryWorkspace.Create();
        var provider = BuildProvider(Path.Combine(workspace.RootPath, "devpilot.db"));
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        var repository = new RepositoryDocument("repo-1", "Repo", workspace.RootPath, DateTimeOffset.UtcNow);
        var file = new FileMetadata("file-1", repository.RepositoryId, repository.RepositoryName, "AuthService.cs", "AuthService.cs", ".cs", "csharp", 100, "hash", DateTimeOffset.UtcNow);
        var jwtChunk = new CodeChunk("chunk-jwt", repository.RepositoryId, file.Id, file.RelativePath, "ValidateJwt", "method", 10, 20, "jwt token validation bearer authentication", "csharp");
        var storageChunk = new CodeChunk("chunk-storage", repository.RepositoryId, file.Id, file.RelativePath, "SaveFile", "method", 30, 40, "write sqlite repository metadata chunk persistence", "csharp");

        await provider.GetRequiredService<IRepositoryStore>().SaveAsync(repository);
        await provider.GetRequiredService<IFileMetadataStore>().SaveAsync(file);
        await provider.GetRequiredService<IChunkStore>().SaveManyAsync([jwtChunk, storageChunk]);
        await provider.GetRequiredService<IEmbeddingPipelineService>().EmbedChunksAsync([jwtChunk, storageChunk]);

        var result = await provider.GetRequiredService<ISemanticSearchService>().SearchAsync(
            new SearchRequest("jwt token validation", 1));

        Assert.Single(result.Matches);
        Assert.Equal("chunk-jwt", result.Matches[0].Chunk.ChunkId);
        Assert.True(result.Matches[0].Chunk.RelevanceScore > 0);
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        services.AddSingleton(Options.Create(new EmbeddingSettings
        {
            Dimensions = 64,
            BatchSize = 4,
            AllowDeterministicFallback = true
        }));
        services.AddSingleton(Options.Create(new EmbeddingVersioningSettings()));
        services.AddSingleton(Options.Create(new TokenEstimationSettings()));
        services.AddSingleton(Options.Create(new VectorSearchSettings { DefaultTopK = 3 }));
        services.AddSingleton<ITokenEstimator, ApproximateTokenEstimator>();
        services.AddDevPilotStorage();
        services.AddDevPilotAi();

        return services.BuildServiceProvider();
    }
}
