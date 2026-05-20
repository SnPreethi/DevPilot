using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPilot.Storage;

public sealed class DatabaseInitializer
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly VectorSearchSettings _vectorSettings;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        ISqliteConnectionFactory connectionFactory,
        IOptions<VectorSearchSettings> vectorSettings,
        ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _vectorSettings = vectorSettings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SQLite database foundation.");

        await using var connection =
            await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Repositories (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                RootPath TEXT NOT NULL,
                IndexedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Files (
                Id TEXT PRIMARY KEY,
                RepositoryId TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                Extension TEXT NOT NULL,
                Language TEXT NOT NULL,
                SHA256Hash TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Files_RepositoryId
                ON Files (RepositoryId);

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Files_RepositoryId_RelativePath
                ON Files (RepositoryId, RelativePath);

            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                RepositoryId TEXT NOT NULL,
                FileId TEXT NOT NULL,
                SymbolName TEXT NULL,
                ChunkType TEXT NOT NULL,
                StartLine INTEGER NOT NULL,
                EndLine INTEGER NOT NULL,
                Content TEXT NOT NULL,
                Language TEXT NOT NULL,
                ChunkHash TEXT NOT NULL DEFAULT '',
                TokenEstimate INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id) ON DELETE CASCADE,
                FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Chunks_RepositoryId
                ON Chunks (RepositoryId);

            CREATE INDEX IF NOT EXISTS IX_Chunks_FileId
                ON Chunks (FileId);

            CREATE TABLE IF NOT EXISTS Embeddings (
                Id TEXT PRIMARY KEY,
                ChunkId TEXT NOT NULL,
                ModelName TEXT NOT NULL,
                VectorData BLOB NOT NULL,
                Dimensions INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                EmbeddingModelVersion TEXT NOT NULL DEFAULT '1',
                EmbeddingSchemaVersion INTEGER NOT NULL DEFAULT 1,
                ChunkHash TEXT NOT NULL DEFAULT '',
                IndexedAtUtc TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (ChunkId) REFERENCES Chunks(Id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Embeddings_ChunkId_ModelName
                ON Embeddings (ChunkId, ModelName);

            CREATE TABLE IF NOT EXISTS Symbols (
                Id TEXT PRIMARY KEY,
                RepositoryId TEXT NOT NULL,
                FileId TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                Name TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Namespace TEXT NULL,
                ParentSymbol TEXT NULL,
                ReferencedSymbols TEXT NULL,
                ImportedNamespaces TEXT NULL,
                FileDependencies TEXT NULL,
                DefinitionLocation TEXT NOT NULL,
                StartLine INTEGER NOT NULL,
                EndLine INTEGER NOT NULL,
                FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id) ON DELETE CASCADE,
                FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Symbols_RepositoryId ON Symbols (RepositoryId);
            CREATE INDEX IF NOT EXISTS IX_Symbols_FileId ON Symbols (FileId);
            CREATE INDEX IF NOT EXISTS IX_Symbols_Name ON Symbols (Name);

            CREATE TABLE IF NOT EXISTS WorkspaceEvents (
                RepositoryId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                FilePath TEXT NULL,
                SymbolName TEXT NULL,
                Description TEXT NOT NULL,
                Outcome TEXT NOT NULL,
                Payload TEXT NULL,
                FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_WorkspaceEvents_RepositoryId ON WorkspaceEvents (RepositoryId);

            CREATE TABLE IF NOT EXISTS WorkflowInstances (
                Id TEXT PRIMARY KEY,
                PlanId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Status TEXT NOT NULL,
                RiskLevel TEXT NOT NULL,
                Objective TEXT NOT NULL,
                Summary TEXT NOT NULL,
                RepositoryId TEXT NULL,
                RepositoryPath TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                ActiveTaskId TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_WorkflowInstances_RepositoryId
                ON WorkflowInstances (RepositoryId);

            CREATE INDEX IF NOT EXISTS IX_WorkflowInstances_Status
                ON WorkflowInstances (Status);

            CREATE TABLE IF NOT EXISTS WorkflowTasks (
                Id TEXT NOT NULL,
                WorkflowId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Status TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                Description TEXT NOT NULL,
                InputsJson TEXT NOT NULL,
                OutputsJson TEXT NOT NULL,
                RequiresApproval INTEGER NOT NULL,
                ApprovalReason TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                FailureReason TEXT NULL,
                Metadata TEXT NULL,
                PRIMARY KEY (Id, WorkflowId),
                FOREIGN KEY (WorkflowId) REFERENCES WorkflowInstances(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_WorkflowTasks_WorkflowId
                ON WorkflowTasks (WorkflowId);

            CREATE INDEX IF NOT EXISTS IX_WorkflowTasks_Status
                ON WorkflowTasks (Status);

            CREATE TABLE IF NOT EXISTS WorkflowDependencies (
                WorkflowId TEXT NOT NULL,
                TaskId TEXT NOT NULL,
                DependsOnTaskId TEXT NOT NULL,
                DependencyType TEXT NOT NULL,
                Metadata TEXT NULL,
                PRIMARY KEY (WorkflowId, TaskId, DependsOnTaskId),
                FOREIGN KEY (WorkflowId) REFERENCES WorkflowInstances(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_WorkflowDependencies_WorkflowId
                ON WorkflowDependencies (WorkflowId);

            CREATE TABLE IF NOT EXISTS WorkflowExecutionHistory (
                Id TEXT PRIMARY KEY,
                WorkflowId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                Description TEXT NOT NULL,
                TaskId TEXT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (WorkflowId) REFERENCES WorkflowInstances(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_WorkflowExecutionHistory_WorkflowId
                ON WorkflowExecutionHistory (WorkflowId, TimestampUtc);

            CREATE TABLE IF NOT EXISTS ExecutionPipelines (
                Id TEXT PRIMARY KEY,
                WorkflowId TEXT NOT NULL,
                WorkflowTaskId TEXT NULL,
                Status TEXT NOT NULL,
                Objective TEXT NOT NULL,
                RepositoryId TEXT NULL,
                RepositoryPath TEXT NULL,
                DryRun INTEGER NOT NULL,
                ValidationOnly INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                ActiveStageId TEXT NULL,
                FailureReason TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionPipelines_WorkflowId
                ON ExecutionPipelines (WorkflowId);

            CREATE INDEX IF NOT EXISTS IX_ExecutionPipelines_Status
                ON ExecutionPipelines (Status);

            CREATE TABLE IF NOT EXISTS ExecutionStages (
                Id TEXT NOT NULL,
                PipelineId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Status TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                RequiresApproval INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                FailureReason TEXT NULL,
                Metadata TEXT NULL,
                PRIMARY KEY (Id, PipelineId),
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionStages_PipelineId
                ON ExecutionStages (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionCheckpoints (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                StageId TEXT NULL,
                Kind TEXT NOT NULL,
                IsSatisfied INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                Description TEXT NOT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionCheckpoints_PipelineId
                ON ExecutionCheckpoints (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionArtifacts (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Name TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                StageId TEXT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionArtifacts_PipelineId
                ON ExecutionArtifacts (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionFailures (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                StageId TEXT NULL,
                Message TEXT NOT NULL,
                RawOutput TEXT NULL,
                ParsedEventJson TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionFailures_PipelineId
                ON ExecutionFailures (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionValidations (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                IsValid INTEGER NOT NULL,
                MessagesJson TEXT NOT NULL,
                DiagnosticsJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                StageId TEXT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionValidations_PipelineId
                ON ExecutionValidations (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionRollbackSnapshots (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                RepositoryPath TEXT NOT NULL,
                TargetPathsJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionRollbackSnapshots_PipelineId
                ON ExecutionRollbackSnapshots (PipelineId);

            CREATE TABLE IF NOT EXISTS ExecutionTimeline (
                Id TEXT PRIMARY KEY,
                PipelineId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                Description TEXT NOT NULL,
                StageId TEXT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (PipelineId) REFERENCES ExecutionPipelines(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ExecutionTimeline_PipelineId
                ON ExecutionTimeline (PipelineId, TimestampUtc);

            CREATE TABLE IF NOT EXISTS GraphNodes (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                Label TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                Metadata TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_GraphNodes_Kind ON GraphNodes (Kind);
            CREATE INDEX IF NOT EXISTS IX_GraphNodes_EntityId ON GraphNodes (EntityId);

            CREATE TABLE IF NOT EXISTS GraphEdges (
                Id TEXT PRIMARY KEY,
                SourceNodeId TEXT NOT NULL,
                TargetNodeId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                Metadata TEXT NULL,
                FOREIGN KEY (SourceNodeId) REFERENCES GraphNodes(Id) ON DELETE CASCADE,
                FOREIGN KEY (TargetNodeId) REFERENCES GraphNodes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_GraphEdges_SourceNodeId ON GraphEdges (SourceNodeId);
            CREATE INDEX IF NOT EXISTS IX_GraphEdges_TargetNodeId ON GraphEdges (TargetNodeId);
            CREATE INDEX IF NOT EXISTS IX_GraphEdges_Kind ON GraphEdges (Kind);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "ChunkHash", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "TokenEstimate", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "SymbolKind", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "Namespace", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "ParentSymbol", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "ReferencedSymbols", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "ImportedNamespaces", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "FileDependencies", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Chunks", "DefinitionLocation", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Embeddings", "EmbeddingModelVersion", "TEXT NOT NULL DEFAULT '1'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Embeddings", "EmbeddingSchemaVersion", "INTEGER NOT NULL DEFAULT 1", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Embeddings", "ChunkHash", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync("Embeddings", "IndexedAtUtc", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
        await using var versionIndexCommand = connection.CreateCommand();
        versionIndexCommand.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_Embeddings_ModelVersion
                ON Embeddings (ModelName, EmbeddingModelVersion, EmbeddingSchemaVersion);
            """;
        await versionIndexCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await TryInitializeSqliteVssAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("SQLite database foundation is ready.");
    }

    private async Task EnsureColumnAsync(
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var readCommand = connection.CreateCommand();
        readCommand.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SQLite schema updated: added {TableName}.{ColumnName}.", tableName, columnName);
    }

    private async Task TryInitializeSqliteVssAsync(CancellationToken cancellationToken)
    {
        if (!_vectorSettings.UseSqliteVss)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_vectorSettings.SqliteVssExtensionPath))
        {
            _logger.LogWarning("sqlite-vss is enabled but no extension path is configured.");
            return;
        }

        try
        {
            await using var connection =
                await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT load_extension('{_vectorSettings.SqliteVssExtensionPath.Replace("'", "''", StringComparison.Ordinal)}');";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("sqlite-vss extension loaded from {ExtensionPath}.", _vectorSettings.SqliteVssExtensionPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sqlite-vss extension could not be loaded. Falling back to local cosine search.");
        }
    }
}
