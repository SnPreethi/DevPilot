using DevPilot.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DevPilot.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddDevPilotStorage(this IServiceCollection services)
    {
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IRepositoryStore, SQLiteRepositoryStore>();
        services.AddSingleton<IFileMetadataStore, SQLiteFileMetadataStore>();
        services.AddSingleton<IChunkStore, SQLiteChunkStore>();
        services.AddSingleton<IEmbeddingStore, SQLiteEmbeddingStore>();
        services.AddSingleton<IVectorStore, SQLiteVectorStore>();
        services.AddSingleton<ISymbolStore, SQLiteSymbolStore>();
        services.AddSingleton<IWorkspaceMemoryStore, SQLiteWorkspaceMemoryStore>();
        services.AddSingleton<IWorkflowStateStore, SQLiteWorkflowStateStore>();
        services.AddSingleton<IExecutionPipelineStore, SQLiteExecutionPipelineStore>();
        services.AddSingleton<IGraphStore, SQLiteGraphStore>();

        return services;
    }
}
