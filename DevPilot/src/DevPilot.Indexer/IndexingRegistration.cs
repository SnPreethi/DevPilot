using Microsoft.Extensions.DependencyInjection;
using DevPilot.Contracts;

namespace DevPilot.Indexer;

public static class IndexingRegistration
{
    public static IServiceCollection AddDevPilotIndexer(this IServiceCollection services)
    {
        services.AddSingleton<FileLanguageDetector>();
        services.AddSingleton<RepositoryScanner>();
        services.AddSingleton<IRepositoryScanner>(sp => sp.GetRequiredService<RepositoryScanner>());
        services.AddSingleton<FileMetadataExtractor>();
        services.AddSingleton<CodeChunker>();
        services.AddSingleton<ICodeChunker>(sp => sp.GetRequiredService<CodeChunker>());
        services.AddSingleton<IRepositoryIndexingService, RepositoryIndexingService>();
        services.AddSingleton<IChunkInspectionService, ChunkInspectionService>();

        return services;
    }
}
