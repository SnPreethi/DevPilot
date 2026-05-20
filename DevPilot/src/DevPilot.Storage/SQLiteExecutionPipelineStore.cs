using System.Data.Common;
using System.Text.Json;
using DevPilot.Contracts;

namespace DevPilot.Storage;

public sealed class SQLiteExecutionPipelineStore : IExecutionPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteExecutionPipelineStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(ExecutionPipelineState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertPipelineAsync(connection, transaction, state.Pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in state.Stages)
            await UpsertStageAsync(connection, transaction, stage, cancellationToken).ConfigureAwait(false);
        foreach (var checkpoint in state.Checkpoints)
            await UpsertCheckpointAsync(connection, transaction, checkpoint, cancellationToken).ConfigureAwait(false);
        foreach (var artifact in state.Artifacts)
            await InsertArtifactAsync(connection, transaction, artifact, cancellationToken).ConfigureAwait(false);
        foreach (var failure in state.Failures)
            await InsertFailureAsync(connection, transaction, failure, cancellationToken).ConfigureAwait(false);
        foreach (var validation in state.Validations)
            await InsertValidationAsync(connection, transaction, validation, cancellationToken).ConfigureAwait(false);
        foreach (var snapshot in state.RollbackSnapshots)
            await InsertRollbackSnapshotAsync(connection, transaction, snapshot, cancellationToken).ConfigureAwait(false);
        foreach (var ev in state.Timeline)
            await InsertTimelineEventAsync(connection, transaction, ev, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExecutionPipelineState?> GetAsync(string pipelineId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var pipeline = await ReadPipelineAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false);
        if (pipeline == null)
        {
            return null;
        }

        return new ExecutionPipelineState(
            pipeline,
            await ReadStagesAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadCheckpointsAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadArtifactsAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadFailuresAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadValidationsAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadRollbackSnapshotsAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false),
            await ReadTimelineAsync(connection, pipelineId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ExecutionPipeline>> ListAsync(string? workflowId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(workflowId)
            ? """
              SELECT Id, WorkflowId, WorkflowTaskId, Status, Objective, RepositoryId, RepositoryPath, DryRun, ValidationOnly,
                     CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveStageId, FailureReason
              FROM ExecutionPipelines
              ORDER BY UpdatedUtc DESC;
              """
            : """
              SELECT Id, WorkflowId, WorkflowTaskId, Status, Objective, RepositoryId, RepositoryPath, DryRun, ValidationOnly,
                     CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveStageId, FailureReason
              FROM ExecutionPipelines
              WHERE WorkflowId = @WorkflowId
              ORDER BY UpdatedUtc DESC;
              """;
        if (!string.IsNullOrWhiteSpace(workflowId))
        {
            command.AddParameter("@WorkflowId", workflowId);
        }

        var pipelines = new List<ExecutionPipeline>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            pipelines.Add(ReadPipeline(reader));
        }

        return pipelines;
    }

    public async Task UpdatePipelineAsync(ExecutionPipeline pipeline, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => UpsertPipelineAsync(connection, transaction, pipeline, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateStageAsync(ExecutionStage stage, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => UpsertStageAsync(connection, transaction, stage, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => UpsertCheckpointAsync(connection, transaction, checkpoint, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddArtifactAsync(ExecutionArtifact artifact, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => InsertArtifactAsync(connection, transaction, artifact, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => InsertFailureAsync(connection, transaction, failure, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddValidationAsync(ExecutionValidationResult validation, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => InsertValidationAsync(connection, transaction, validation, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRollbackSnapshotAsync(ExecutionRollbackSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => InsertRollbackSnapshotAsync(connection, transaction, snapshot, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task AddTimelineEventAsync(ExecutionTimelineEvent ev, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync((connection, transaction) => InsertTimelineEventAsync(connection, transaction, ev, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteInTransactionAsync(
        Func<DbConnection, DbTransaction, Task> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await action(connection, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertPipelineAsync(DbConnection connection, DbTransaction transaction, ExecutionPipeline pipeline, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ExecutionPipelines
                (Id, WorkflowId, WorkflowTaskId, Status, Objective, RepositoryId, RepositoryPath, DryRun, ValidationOnly,
                 CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveStageId, FailureReason)
            VALUES
                (@Id, @WorkflowId, @WorkflowTaskId, @Status, @Objective, @RepositoryId, @RepositoryPath, @DryRun, @ValidationOnly,
                 @CreatedUtc, @UpdatedUtc, @StartedUtc, @CompletedUtc, @ActiveStageId, @FailureReason)
            ON CONFLICT(Id) DO UPDATE SET
                WorkflowId = excluded.WorkflowId,
                WorkflowTaskId = excluded.WorkflowTaskId,
                Status = excluded.Status,
                Objective = excluded.Objective,
                RepositoryId = excluded.RepositoryId,
                RepositoryPath = excluded.RepositoryPath,
                DryRun = excluded.DryRun,
                ValidationOnly = excluded.ValidationOnly,
                UpdatedUtc = excluded.UpdatedUtc,
                StartedUtc = excluded.StartedUtc,
                CompletedUtc = excluded.CompletedUtc,
                ActiveStageId = excluded.ActiveStageId,
                FailureReason = excluded.FailureReason;
            """;
        command.AddParameter("@Id", pipeline.PipelineId);
        command.AddParameter("@WorkflowId", pipeline.WorkflowId);
        command.AddParameter("@WorkflowTaskId", pipeline.WorkflowTaskId);
        command.AddParameter("@Status", pipeline.Status.ToString());
        command.AddParameter("@Objective", pipeline.Objective);
        command.AddParameter("@RepositoryId", pipeline.RepositoryId);
        command.AddParameter("@RepositoryPath", pipeline.RepositoryPath);
        command.AddParameter("@DryRun", pipeline.DryRun ? 1 : 0);
        command.AddParameter("@ValidationOnly", pipeline.ValidationOnly ? 1 : 0);
        command.AddParameter("@CreatedUtc", pipeline.CreatedUtc.ToString("O"));
        command.AddParameter("@UpdatedUtc", pipeline.UpdatedUtc.ToString("O"));
        command.AddParameter("@StartedUtc", pipeline.StartedUtc?.ToString("O"));
        command.AddParameter("@CompletedUtc", pipeline.CompletedUtc?.ToString("O"));
        command.AddParameter("@ActiveStageId", pipeline.ActiveStageId);
        command.AddParameter("@FailureReason", pipeline.FailureReason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertStageAsync(DbConnection connection, DbTransaction transaction, ExecutionStage stage, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ExecutionStages
                (Id, PipelineId, Kind, Status, Sequence, Title, Description, RequiresApproval, CreatedUtc, UpdatedUtc,
                 StartedUtc, CompletedUtc, FailureReason, Metadata)
            VALUES
                (@Id, @PipelineId, @Kind, @Status, @Sequence, @Title, @Description, @RequiresApproval, @CreatedUtc, @UpdatedUtc,
                 @StartedUtc, @CompletedUtc, @FailureReason, @Metadata)
            ON CONFLICT(Id, PipelineId) DO UPDATE SET
                Kind = excluded.Kind,
                Status = excluded.Status,
                Sequence = excluded.Sequence,
                Title = excluded.Title,
                Description = excluded.Description,
                RequiresApproval = excluded.RequiresApproval,
                UpdatedUtc = excluded.UpdatedUtc,
                StartedUtc = excluded.StartedUtc,
                CompletedUtc = excluded.CompletedUtc,
                FailureReason = excluded.FailureReason,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@Id", stage.StageId);
        command.AddParameter("@PipelineId", stage.PipelineId);
        command.AddParameter("@Kind", stage.Kind.ToString());
        command.AddParameter("@Status", stage.Status.ToString());
        command.AddParameter("@Sequence", stage.Sequence);
        command.AddParameter("@Title", stage.Title);
        command.AddParameter("@Description", stage.Description);
        command.AddParameter("@RequiresApproval", stage.RequiresApproval ? 1 : 0);
        command.AddParameter("@CreatedUtc", stage.CreatedUtc.ToString("O"));
        command.AddParameter("@UpdatedUtc", stage.UpdatedUtc.ToString("O"));
        command.AddParameter("@StartedUtc", stage.StartedUtc?.ToString("O"));
        command.AddParameter("@CompletedUtc", stage.CompletedUtc?.ToString("O"));
        command.AddParameter("@FailureReason", stage.FailureReason);
        command.AddParameter("@Metadata", stage.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCheckpointAsync(DbConnection connection, DbTransaction transaction, ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ExecutionCheckpoints
                (Id, PipelineId, StageId, Kind, IsSatisfied, CreatedUtc, UpdatedUtc, Description, Metadata)
            VALUES
                (@Id, @PipelineId, @StageId, @Kind, @IsSatisfied, @CreatedUtc, @UpdatedUtc, @Description, @Metadata)
            ON CONFLICT(Id) DO UPDATE SET
                StageId = excluded.StageId,
                Kind = excluded.Kind,
                IsSatisfied = excluded.IsSatisfied,
                UpdatedUtc = excluded.UpdatedUtc,
                Description = excluded.Description,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@Id", checkpoint.CheckpointId);
        command.AddParameter("@PipelineId", checkpoint.PipelineId);
        command.AddParameter("@StageId", checkpoint.StageId);
        command.AddParameter("@Kind", checkpoint.Kind.ToString());
        command.AddParameter("@IsSatisfied", checkpoint.IsSatisfied ? 1 : 0);
        command.AddParameter("@CreatedUtc", checkpoint.CreatedUtc.ToString("O"));
        command.AddParameter("@UpdatedUtc", checkpoint.UpdatedUtc.ToString("O"));
        command.AddParameter("@Description", checkpoint.Description);
        command.AddParameter("@Metadata", checkpoint.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertArtifactAsync(DbConnection connection, DbTransaction transaction, ExecutionArtifact artifact, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO ExecutionArtifacts
                (Id, PipelineId, Kind, Name, Content, CreatedUtc, StageId, Metadata)
            VALUES
                (@Id, @PipelineId, @Kind, @Name, @Content, @CreatedUtc, @StageId, @Metadata);
            """;
        command.AddParameter("@Id", artifact.ArtifactId);
        command.AddParameter("@PipelineId", artifact.PipelineId);
        command.AddParameter("@Kind", artifact.Kind.ToString());
        command.AddParameter("@Name", artifact.Name);
        command.AddParameter("@Content", artifact.Content);
        command.AddParameter("@CreatedUtc", artifact.CreatedUtc.ToString("O"));
        command.AddParameter("@StageId", artifact.StageId);
        command.AddParameter("@Metadata", artifact.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertFailureAsync(DbConnection connection, DbTransaction transaction, ExecutionFailure failure, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO ExecutionFailures
                (Id, PipelineId, StageId, Message, RawOutput, ParsedEventJson, CreatedUtc, Metadata)
            VALUES
                (@Id, @PipelineId, @StageId, @Message, @RawOutput, @ParsedEventJson, @CreatedUtc, @Metadata);
            """;
        command.AddParameter("@Id", failure.FailureId);
        command.AddParameter("@PipelineId", failure.PipelineId);
        command.AddParameter("@StageId", failure.StageId);
        command.AddParameter("@Message", failure.Message);
        command.AddParameter("@RawOutput", failure.RawOutput);
        command.AddParameter("@ParsedEventJson", failure.ParsedEvent == null ? null : JsonSerializer.Serialize(failure.ParsedEvent, JsonOptions));
        command.AddParameter("@CreatedUtc", failure.CreatedUtc.ToString("O"));
        command.AddParameter("@Metadata", failure.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertValidationAsync(DbConnection connection, DbTransaction transaction, ExecutionValidationResult validation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO ExecutionValidations
                (Id, PipelineId, IsValid, MessagesJson, DiagnosticsJson, CreatedUtc, StageId, Metadata)
            VALUES
                (@Id, @PipelineId, @IsValid, @MessagesJson, @DiagnosticsJson, @CreatedUtc, @StageId, @Metadata);
            """;
        command.AddParameter("@Id", validation.ValidationId);
        command.AddParameter("@PipelineId", validation.PipelineId);
        command.AddParameter("@IsValid", validation.IsValid ? 1 : 0);
        command.AddParameter("@MessagesJson", JsonSerializer.Serialize(validation.Messages, JsonOptions));
        command.AddParameter("@DiagnosticsJson", JsonSerializer.Serialize(validation.Diagnostics, JsonOptions));
        command.AddParameter("@CreatedUtc", validation.CreatedUtc.ToString("O"));
        command.AddParameter("@StageId", validation.StageId);
        command.AddParameter("@Metadata", validation.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRollbackSnapshotAsync(DbConnection connection, DbTransaction transaction, ExecutionRollbackSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO ExecutionRollbackSnapshots
                (Id, PipelineId, RepositoryPath, TargetPathsJson, CreatedUtc, Metadata)
            VALUES
                (@Id, @PipelineId, @RepositoryPath, @TargetPathsJson, @CreatedUtc, @Metadata);
            """;
        command.AddParameter("@Id", snapshot.SnapshotId);
        command.AddParameter("@PipelineId", snapshot.PipelineId);
        command.AddParameter("@RepositoryPath", snapshot.RepositoryPath);
        command.AddParameter("@TargetPathsJson", JsonSerializer.Serialize(snapshot.TargetPaths, JsonOptions));
        command.AddParameter("@CreatedUtc", snapshot.CreatedUtc.ToString("O"));
        command.AddParameter("@Metadata", snapshot.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTimelineEventAsync(DbConnection connection, DbTransaction transaction, ExecutionTimelineEvent ev, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO ExecutionTimeline
                (Id, PipelineId, EventType, TimestampUtc, Description, StageId, Metadata)
            VALUES
                (@Id, @PipelineId, @EventType, @TimestampUtc, @Description, @StageId, @Metadata);
            """;
        command.AddParameter("@Id", ev.EventId);
        command.AddParameter("@PipelineId", ev.PipelineId);
        command.AddParameter("@EventType", ev.EventType.ToString());
        command.AddParameter("@TimestampUtc", ev.TimestampUtc.ToString("O"));
        command.AddParameter("@Description", ev.Description);
        command.AddParameter("@StageId", ev.StageId);
        command.AddParameter("@Metadata", ev.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecutionPipeline?> ReadPipelineAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkflowId, WorkflowTaskId, Status, Objective, RepositoryId, RepositoryPath, DryRun, ValidationOnly,
                   CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveStageId, FailureReason
            FROM ExecutionPipelines
            WHERE Id = @Id;
            """;
        command.AddParameter("@Id", pipelineId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadPipeline(reader) : null;
    }

    private static ExecutionPipeline ReadPipeline(DbDataReader reader)
    {
        return new ExecutionPipeline(
            PipelineId: reader.GetString(0),
            WorkflowId: reader.GetString(1),
            WorkflowTaskId: reader.IsDBNull(2) ? null : reader.GetString(2),
            Status: Enum.Parse<ExecutionPipelineStatus>(reader.GetString(3)),
            Objective: reader.GetString(4),
            RepositoryId: reader.IsDBNull(5) ? null : reader.GetString(5),
            RepositoryPath: reader.IsDBNull(6) ? null : reader.GetString(6),
            DryRun: reader.GetInt32(7) == 1,
            ValidationOnly: reader.GetInt32(8) == 1,
            CreatedUtc: DateTime.Parse(reader.GetString(9)).ToUniversalTime(),
            UpdatedUtc: DateTime.Parse(reader.GetString(10)).ToUniversalTime(),
            StartedUtc: reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)).ToUniversalTime(),
            CompletedUtc: reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12)).ToUniversalTime(),
            ActiveStageId: reader.IsDBNull(13) ? null : reader.GetString(13),
            FailureReason: reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static async Task<IReadOnlyList<ExecutionStage>> ReadStagesAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PipelineId, Kind, Status, Sequence, Title, Description, RequiresApproval, CreatedUtc, UpdatedUtc,
                   StartedUtc, CompletedUtc, FailureReason, Metadata
            FROM ExecutionStages
            WHERE PipelineId = @PipelineId
            ORDER BY Sequence;
            """;
        command.AddParameter("@PipelineId", pipelineId);
        var stages = new List<ExecutionStage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            stages.Add(new ExecutionStage(
                StageId: reader.GetString(0),
                PipelineId: reader.GetString(1),
                Kind: Enum.Parse<ExecutionStageKind>(reader.GetString(2)),
                Status: Enum.Parse<ExecutionStageStatus>(reader.GetString(3)),
                Sequence: reader.GetInt32(4),
                Title: reader.GetString(5),
                Description: reader.GetString(6),
                RequiresApproval: reader.GetInt32(7) == 1,
                CreatedUtc: DateTime.Parse(reader.GetString(8)).ToUniversalTime(),
                UpdatedUtc: DateTime.Parse(reader.GetString(9)).ToUniversalTime(),
                StartedUtc: reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)).ToUniversalTime(),
                CompletedUtc: reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)).ToUniversalTime(),
                FailureReason: reader.IsDBNull(12) ? null : reader.GetString(12),
                Metadata: reader.IsDBNull(13) ? null : reader.GetString(13)));
        }
        return stages;
    }

    private static async Task<IReadOnlyList<ExecutionCheckpoint>> ReadCheckpointsAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, StageId, Kind, IsSatisfied, CreatedUtc, UpdatedUtc, Description, Metadata FROM ExecutionCheckpoints WHERE PipelineId = @PipelineId ORDER BY CreatedUtc;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionCheckpoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ExecutionCheckpoint(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), Enum.Parse<ExecutionCheckpointKind>(reader.GetString(3)), reader.GetInt32(4) == 1, DateTime.Parse(reader.GetString(5)).ToUniversalTime(), DateTime.Parse(reader.GetString(6)).ToUniversalTime(), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        return items;
    }

    private static async Task<IReadOnlyList<ExecutionArtifact>> ReadArtifactsAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, Kind, Name, Content, CreatedUtc, StageId, Metadata FROM ExecutionArtifacts WHERE PipelineId = @PipelineId ORDER BY CreatedUtc;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionArtifact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ExecutionArtifact(reader.GetString(0), reader.GetString(1), Enum.Parse<ExecutionArtifactKind>(reader.GetString(2)), reader.GetString(3), reader.GetString(4), DateTime.Parse(reader.GetString(5)).ToUniversalTime(), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return items;
    }

    private static async Task<IReadOnlyList<ExecutionFailure>> ReadFailuresAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, StageId, Message, RawOutput, ParsedEventJson, CreatedUtc, Metadata FROM ExecutionFailures WHERE PipelineId = @PipelineId ORDER BY CreatedUtc;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionFailure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var parsedEvent = reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<ExecutionEvent>(reader.GetString(5), JsonOptions);
            items.Add(new ExecutionFailure(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), parsedEvent, DateTime.Parse(reader.GetString(6)).ToUniversalTime(), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return items;
    }

    private static async Task<IReadOnlyList<ExecutionValidationResult>> ReadValidationsAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, IsValid, MessagesJson, DiagnosticsJson, CreatedUtc, StageId, Metadata FROM ExecutionValidations WHERE PipelineId = @PipelineId ORDER BY CreatedUtc;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionValidationResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ExecutionValidationResult(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1, DeserializeList(reader.GetString(3)), JsonSerializer.Deserialize<IReadOnlyList<NormalizedDiagnostic>>(reader.GetString(4), JsonOptions) ?? Array.Empty<NormalizedDiagnostic>(), DateTime.Parse(reader.GetString(5)).ToUniversalTime(), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return items;
    }

    private static async Task<IReadOnlyList<ExecutionRollbackSnapshot>> ReadRollbackSnapshotsAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, RepositoryPath, TargetPathsJson, CreatedUtc, Metadata FROM ExecutionRollbackSnapshots WHERE PipelineId = @PipelineId ORDER BY CreatedUtc;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionRollbackSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ExecutionRollbackSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), DeserializeList(reader.GetString(3)), DateTime.Parse(reader.GetString(4)).ToUniversalTime(), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return items;
    }

    private static async Task<IReadOnlyList<ExecutionTimelineEvent>> ReadTimelineAsync(DbConnection connection, string pipelineId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PipelineId, EventType, TimestampUtc, Description, StageId, Metadata FROM ExecutionTimeline WHERE PipelineId = @PipelineId ORDER BY TimestampUtc, Id;";
        command.AddParameter("@PipelineId", pipelineId);
        var items = new List<ExecutionTimelineEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ExecutionTimelineEvent(reader.GetString(0), reader.GetString(1), Enum.Parse<ExecutionTimelineEventType>(reader.GetString(2)), DateTime.Parse(reader.GetString(3)).ToUniversalTime(), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return items;
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();
    }
}
