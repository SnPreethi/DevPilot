using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Storage.Tests;

public sealed class SQLitePersistenceTests
{
    [Fact]
    public async Task Stores_PersistRepositoryFileMetadataAndChunks()
    {
        using var workspace = TemporaryWorkspace.Create();
        var databasePath = Path.Combine(workspace.RootPath, "devpilot.db");
        var factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        var initializer = new DatabaseInitializer(
            factory,
            Options.Create(new VectorSearchSettings()),
            NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();

        var repositoryStore = new SQLiteRepositoryStore(factory);
        var fileStore = new SQLiteFileMetadataStore(factory);
        var chunkStore = new SQLiteChunkStore(factory);

        var repository = new RepositoryDocument("repo-1", "Repo", workspace.RootPath, DateTimeOffset.UtcNow);
        var file = new FileMetadata(
            "file-1",
            repository.RepositoryId,
            repository.RepositoryName,
            Path.Combine(workspace.RootPath, "Program.cs"),
            "Program.cs",
            ".cs",
            "csharp",
            42,
            "abc123",
            DateTimeOffset.UtcNow);
        var chunk = new CodeChunk("chunk-1", repository.RepositoryId, file.Id, file.RelativePath, "Program", "class", 1, 10, "class Program { }", "csharp");

        await repositoryStore.SaveAsync(repository);
        await fileStore.SaveAsync(file);
        await chunkStore.SaveAsync(chunk);

        var savedRepository = await repositoryStore.GetAsync(repository.RepositoryId);
        var savedFile = await fileStore.GetAsync(file.Id);
        var savedChunk = await chunkStore.GetAsync(chunk.ChunkId);

        Assert.Equal(repository.RepositoryName, savedRepository?.RepositoryName);
        Assert.Equal(file.RelativePath, savedFile?.RelativePath);
        Assert.Equal(chunk.SymbolName, savedChunk?.SymbolName);
    }

    [Fact]
    public async Task EmbeddingStore_DetectsStaleEmbeddingVersions()
    {
        using var workspace = TemporaryWorkspace.Create();
        var databasePath = Path.Combine(workspace.RootPath, "devpilot.db");
        var factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        var initializer = new DatabaseInitializer(
            factory,
            Options.Create(new VectorSearchSettings()),
            NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();

        var repositoryStore = new SQLiteRepositoryStore(factory);
        var fileStore = new SQLiteFileMetadataStore(factory);
        var chunkStore = new SQLiteChunkStore(factory);
        var embeddingStore = new SQLiteEmbeddingStore(factory);

        var repository = new RepositoryDocument("repo-1", "Repo", workspace.RootPath, DateTimeOffset.UtcNow);
        var file = new FileMetadata("file-1", repository.RepositoryId, repository.RepositoryName, Path.Combine(workspace.RootPath, "Program.cs"), "Program.cs", ".cs", "csharp", 10, "hash", DateTimeOffset.UtcNow);
        var chunk = new CodeChunk("chunk-1", repository.RepositoryId, file.Id, file.RelativePath, "Program", "class", 1, 1, "class Program { }", "csharp", "chunk-hash", 4);
        var embedding = new EmbeddingVector("embedding-1", chunk.ChunkId, "model", [1f, 0f], 2, DateTimeOffset.UtcNow, "v1", 1, "chunk-hash", DateTimeOffset.UtcNow);

        await repositoryStore.SaveAsync(repository);
        await fileStore.SaveAsync(file);
        await chunkStore.SaveAsync(chunk);
        await embeddingStore.SaveManyAsync([embedding]);

        Assert.True(await embeddingStore.IsCurrentAsync(chunk.ChunkId, "model", "v1", 1, "chunk-hash"));
        Assert.False(await embeddingStore.IsCurrentAsync(chunk.ChunkId, "model", "v2", 1, "chunk-hash"));
        Assert.False(await embeddingStore.IsCurrentAsync(chunk.ChunkId, "model", "v1", 1, "other-hash"));
    }

    [Fact]
    public async Task DatabaseInitializer_EnablesWalJournalMode()
    {
        using var workspace = TemporaryWorkspace.Create();
        var databasePath = Path.Combine(workspace.RootPath, "devpilot.db");
        var factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        var initializer = new DatabaseInitializer(
            factory,
            Options.Create(new VectorSearchSettings()),
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        await using var connection = await factory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = await command.ExecuteScalarAsync();

        Assert.Equal("wal", mode?.ToString()?.ToLowerInvariant());
    }

    [Fact]
    public async Task WorkflowStateStore_PersistsAndReloadsWorkflowGraph()
    {
        using var workspace = TemporaryWorkspace.Create();
        var databasePath = Path.Combine(workspace.RootPath, "devpilot.db");
        var factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        var initializer = new DatabaseInitializer(
            factory,
            Options.Create(new VectorSearchSettings()),
            NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();

        var now = DateTime.UtcNow;
        var store = new SQLiteWorkflowStateStore(factory);
        var instance = new WorkflowInstance(
            "workflow-1",
            "plan-1",
            EngineeringWorkflowKind.FeatureImplementation,
            WorkflowInstanceStatus.Active,
            EngineeringWorkflowRiskLevel.Medium,
            "Implement persistent workflows",
            "Summary",
            "repo-1",
            workspace.RootPath,
            now,
            now,
            now);
        var tasks = new[]
        {
            new WorkflowTask("task-1", instance.WorkflowId, "Inspect", EngineeringWorkflowStepKind.RepositoryInspection, WorkflowTaskStatus.Completed, 1, "Inspect", Array.Empty<string>(), Array.Empty<string>(), false, null, now, now, CompletedUtc: now),
            new WorkflowTask("task-2", instance.WorkflowId, "Plan", EngineeringWorkflowStepKind.PlanDrafting, WorkflowTaskStatus.Ready, 2, "Plan", new[] { "input" }, new[] { "output" }, true, "Approval", now, now)
        };
        var dependencies = new[]
        {
            new WorkflowDependency(instance.WorkflowId, "task-2", "task-1", "FinishToStart")
        };
        var history = new[]
        {
            new WorkflowExecutionEvent("event-1", instance.WorkflowId, WorkflowExecutionEventType.Created, now, "Created")
        };

        await store.SaveAsync(new WorkflowState(instance, tasks, dependencies, history));

        var loaded = await store.GetAsync(instance.WorkflowId);

        Assert.NotNull(loaded);
        Assert.Equal(instance.Objective, loaded.Instance.Objective);
        Assert.Equal(2, loaded.Tasks.Count);
        Assert.Equal("input", loaded.Tasks.Single(t => t.TaskId == "task-2").Inputs.Single());
        Assert.Single(loaded.Dependencies);
        Assert.Single(loaded.ExecutionHistory);
    }

    [Fact]
    public async Task ExecutionPipelineStore_PersistsArtifactsAndTimeline()
    {
        using var workspace = TemporaryWorkspace.Create();
        var databasePath = Path.Combine(workspace.RootPath, "devpilot.db");
        var factory = new SqliteConnectionFactory(Options.Create(new StorageSettings { DatabasePath = databasePath, Pooling = false }));
        var initializer = new DatabaseInitializer(
            factory,
            Options.Create(new VectorSearchSettings()),
            NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();

        var now = DateTime.UtcNow;
        var pipeline = new ExecutionPipeline("pipe-1", "workflow-1", "task-1", ExecutionPipelineStatus.Running, "Validate", "repo-1", workspace.RootPath, true, false, now, now, now);
        var stage = new ExecutionStage("stage-1", pipeline.PipelineId, ExecutionStageKind.ValidateWorkflow, ExecutionStageStatus.Running, 1, "Validate", "Validate workflow", false, now, now, now);
        var checkpoint = new ExecutionCheckpoint("checkpoint-1", pipeline.PipelineId, stage.StageId, ExecutionCheckpointKind.SafetyValidation, false, now, now, "Safety");
        var artifact = new ExecutionArtifact("artifact-1", pipeline.PipelineId, ExecutionArtifactKind.ValidationReport, "report.json", "{}", now, stage.StageId);
        var validation = new ExecutionValidationResult("validation-1", pipeline.PipelineId, true, new[] { "ok" }, Array.Empty<NormalizedDiagnostic>(), now, stage.StageId);
        var snapshot = new ExecutionRollbackSnapshot("snapshot-1", pipeline.PipelineId, workspace.RootPath, new[] { "Program.cs" }, now);
        var timeline = new ExecutionTimelineEvent("event-1", pipeline.PipelineId, ExecutionTimelineEventType.Started, now, "Started", stage.StageId);
        var store = new SQLiteExecutionPipelineStore(factory);

        await store.SaveAsync(new ExecutionPipelineState(
            pipeline,
            new[] { stage },
            new[] { checkpoint },
            new[] { artifact },
            Array.Empty<ExecutionFailure>(),
            new[] { validation },
            new[] { snapshot },
            new[] { timeline }));

        var loaded = await store.GetAsync(pipeline.PipelineId);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Stages);
        Assert.Single(loaded.Checkpoints);
        Assert.Single(loaded.Artifacts);
        Assert.Single(loaded.Validations);
        Assert.Single(loaded.RollbackSnapshots);
        Assert.Single(loaded.Timeline);
        Assert.Equal("ok", loaded.Validations[0].Messages[0]);
    }
}
