using DevPilot.Contracts;
using DevPilot.AI;
using DevPilot.Core;
using DevPilot.Indexer;
using DevPilot.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Indexer.Tests;

public sealed class RepositoryIndexingServiceTests
{
    [Fact]
    public async Task IndexAsync_PersistsRepositoryFilesAndChunks()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var database = TemporaryWorkspace.Create();
        workspace.WriteFile("src\\Example.cs", "public sealed class Example { public void Run() { } }");
        workspace.WriteFile("README.md", "# Title\nBody");
        workspace.WriteFile("bin\\Ignored.cs", "public class Ignored { }");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new StorageSettings
        {
            DatabasePath = Path.Combine(database.RootPath, "devpilot.db"),
            Pooling = false
        }));
        services.AddSingleton(Options.Create(new IndexingSettings()));
        services.AddSingleton(Options.Create(new EmbeddingSettings
        {
            GenerateDuringIndexing = true,
            AllowDeterministicFallback = true
        }));
        services.AddSingleton(Options.Create(new EmbeddingVersioningSettings()));
        services.AddSingleton(Options.Create(new IncrementalIndexingSettings()));
        services.AddSingleton(Options.Create(new PerformanceSettings()));
        services.AddSingleton(Options.Create(new TokenEstimationSettings()));
        services.AddSingleton(Options.Create(new VectorSearchSettings()));
        services.AddSingleton<ITokenEstimator, ApproximateTokenEstimator>();
        services.AddDevPilotStorage();
        services.AddDevPilotAi();
        services.AddDevPilotIndexer();

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        var service = provider.GetRequiredService<IRepositoryIndexingService>();
        var result = await service.IndexAsync(workspace.RootPath);

        var repositories = await provider.GetRequiredService<IRepositoryStore>().ListAsync();
        var files = await provider.GetRequiredService<IFileMetadataStore>().ListByRepositoryAsync(result.RepositoryId);
        var chunks = await provider.GetRequiredService<IChunkStore>().ListByFileAsync(files[0].Id);
        var embedded = new List<EmbeddingVector>();
        await foreach (var embedding in provider.GetRequiredService<IEmbeddingStore>().ListByModelAsync("all-MiniLM-L6-v2"))
        {
            embedded.Add(embedding);
        }

        Assert.Single(repositories);
        Assert.Equal(2, result.FilesScanned);
        Assert.True(result.FilesIgnored >= 1);
        Assert.True(result.ChunksCreated >= 2);
        Assert.Equal(2, files.Count);
        Assert.NotEmpty(chunks);
        Assert.NotEmpty(embedded);
    }

    [Fact]
    public async Task IndexAsync_SkipsUnchangedFilesAndRemovesDeletedFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        using var database = TemporaryWorkspace.Create();
        workspace.WriteFile("src\\Example.cs", "public sealed class Example { public void Run() { } }");
        workspace.WriteFile("src\\DeleteMe.cs", "public sealed class DeleteMe { }");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new StorageSettings
        {
            DatabasePath = Path.Combine(database.RootPath, "devpilot.db"),
            Pooling = false
        }));
        services.AddSingleton(Options.Create(new IndexingSettings()));
        services.AddSingleton(Options.Create(new EmbeddingSettings
        {
            GenerateDuringIndexing = true,
            AllowDeterministicFallback = true
        }));
        services.AddSingleton(Options.Create(new EmbeddingVersioningSettings()));
        services.AddSingleton(Options.Create(new IncrementalIndexingSettings()));
        services.AddSingleton(Options.Create(new PerformanceSettings()));
        services.AddSingleton(Options.Create(new TokenEstimationSettings()));
        services.AddSingleton(Options.Create(new VectorSearchSettings()));
        services.AddSingleton<ITokenEstimator, ApproximateTokenEstimator>();
        services.AddDevPilotStorage();
        services.AddDevPilotAi();
        services.AddDevPilotIndexer();

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        var service = provider.GetRequiredService<IRepositoryIndexingService>();
        var first = await service.IndexAsync(workspace.RootPath);
        var second = await service.IndexAsync(workspace.RootPath);

        File.Delete(Path.Combine(workspace.RootPath, "src", "DeleteMe.cs"));
        var third = await service.IndexAsync(workspace.RootPath);
        var files = await provider.GetRequiredService<IFileMetadataStore>().ListByRepositoryAsync(first.RepositoryId);

        Assert.Equal(first.FilesScanned, second.FilesSkipped);
        Assert.True(third.FilesDeleted >= 1);
        Assert.DoesNotContain(files, file => file.RelativePath.EndsWith("DeleteMe.cs", StringComparison.OrdinalIgnoreCase));
    }
}
