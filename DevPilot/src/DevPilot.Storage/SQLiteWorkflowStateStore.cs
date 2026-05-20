using System.Text.Json;
using DevPilot.Contracts;

namespace DevPilot.Storage;

public sealed class SQLiteWorkflowStateStore : IWorkflowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteWorkflowStateStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertInstanceAsync(connection, transaction, state.Instance, cancellationToken).ConfigureAwait(false);

        foreach (var task in state.Tasks)
        {
            await UpsertTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
        }

        foreach (var dependency in state.Dependencies)
        {
            await UpsertDependencyAsync(connection, transaction, dependency, cancellationToken).ConfigureAwait(false);
        }

        foreach (var ev in state.ExecutionHistory)
        {
            await InsertExecutionEventAsync(connection, transaction, ev, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var instance = await ReadInstanceAsync(connection, workflowId, cancellationToken).ConfigureAwait(false);
        if (instance == null)
        {
            return null;
        }

        var tasks = await ReadTasksAsync(connection, workflowId, cancellationToken).ConfigureAwait(false);
        var dependencies = await ReadDependenciesAsync(connection, workflowId, cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryAsync(connection, workflowId, cancellationToken).ConfigureAwait(false);
        return new WorkflowState(instance, tasks, dependencies, history);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> ListAsync(
        string? repositoryId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(repositoryId)
            ? """
              SELECT Id, PlanId, Kind, Status, RiskLevel, Objective, Summary, RepositoryId, RepositoryPath,
                     CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveTaskId
              FROM WorkflowInstances
              ORDER BY UpdatedUtc DESC;
              """
            : """
              SELECT Id, PlanId, Kind, Status, RiskLevel, Objective, Summary, RepositoryId, RepositoryPath,
                     CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveTaskId
              FROM WorkflowInstances
              WHERE RepositoryId = @RepositoryId
              ORDER BY UpdatedUtc DESC;
              """;

        if (!string.IsNullOrWhiteSpace(repositoryId))
        {
            command.AddParameter("@RepositoryId", repositoryId);
        }

        var instances = new List<WorkflowInstance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            instances.Add(ReadInstance(reader));
        }

        return instances;
    }

    public async Task AddExecutionEventAsync(WorkflowExecutionEvent ev, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertExecutionEventAsync(connection, transaction, ev, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpsertInstanceAsync(connection, transaction, instance, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateTaskAsync(WorkflowTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpsertTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertInstanceAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        WorkflowInstance instance,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO WorkflowInstances
                (Id, PlanId, Kind, Status, RiskLevel, Objective, Summary, RepositoryId, RepositoryPath,
                 CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveTaskId)
            VALUES
                (@Id, @PlanId, @Kind, @Status, @RiskLevel, @Objective, @Summary, @RepositoryId, @RepositoryPath,
                 @CreatedUtc, @UpdatedUtc, @StartedUtc, @CompletedUtc, @ActiveTaskId)
            ON CONFLICT(Id) DO UPDATE SET
                PlanId = excluded.PlanId,
                Kind = excluded.Kind,
                Status = excluded.Status,
                RiskLevel = excluded.RiskLevel,
                Objective = excluded.Objective,
                Summary = excluded.Summary,
                RepositoryId = excluded.RepositoryId,
                RepositoryPath = excluded.RepositoryPath,
                UpdatedUtc = excluded.UpdatedUtc,
                StartedUtc = excluded.StartedUtc,
                CompletedUtc = excluded.CompletedUtc,
                ActiveTaskId = excluded.ActiveTaskId;
            """;
        command.AddParameter("@Id", instance.WorkflowId);
        command.AddParameter("@PlanId", instance.PlanId);
        command.AddParameter("@Kind", instance.Kind.ToString());
        command.AddParameter("@Status", instance.Status.ToString());
        command.AddParameter("@RiskLevel", instance.RiskLevel.ToString());
        command.AddParameter("@Objective", instance.Objective);
        command.AddParameter("@Summary", instance.Summary);
        command.AddParameter("@RepositoryId", instance.RepositoryId);
        command.AddParameter("@RepositoryPath", instance.RepositoryPath);
        command.AddParameter("@CreatedUtc", instance.CreatedUtc.ToString("O"));
        command.AddParameter("@UpdatedUtc", instance.UpdatedUtc.ToString("O"));
        command.AddParameter("@StartedUtc", instance.StartedUtc?.ToString("O"));
        command.AddParameter("@CompletedUtc", instance.CompletedUtc?.ToString("O"));
        command.AddParameter("@ActiveTaskId", instance.ActiveTaskId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertTaskAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO WorkflowTasks
                (Id, WorkflowId, Title, Kind, Status, Sequence, Description, InputsJson, OutputsJson,
                 RequiresApproval, ApprovalReason, CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, FailureReason, Metadata)
            VALUES
                (@Id, @WorkflowId, @Title, @Kind, @Status, @Sequence, @Description, @InputsJson, @OutputsJson,
                 @RequiresApproval, @ApprovalReason, @CreatedUtc, @UpdatedUtc, @StartedUtc, @CompletedUtc, @FailureReason, @Metadata)
            ON CONFLICT(Id, WorkflowId) DO UPDATE SET
                Title = excluded.Title,
                Kind = excluded.Kind,
                Status = excluded.Status,
                Sequence = excluded.Sequence,
                Description = excluded.Description,
                InputsJson = excluded.InputsJson,
                OutputsJson = excluded.OutputsJson,
                RequiresApproval = excluded.RequiresApproval,
                ApprovalReason = excluded.ApprovalReason,
                UpdatedUtc = excluded.UpdatedUtc,
                StartedUtc = excluded.StartedUtc,
                CompletedUtc = excluded.CompletedUtc,
                FailureReason = excluded.FailureReason,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@Id", task.TaskId);
        command.AddParameter("@WorkflowId", task.WorkflowId);
        command.AddParameter("@Title", task.Title);
        command.AddParameter("@Kind", task.Kind.ToString());
        command.AddParameter("@Status", task.Status.ToString());
        command.AddParameter("@Sequence", task.Sequence);
        command.AddParameter("@Description", task.Description);
        command.AddParameter("@InputsJson", JsonSerializer.Serialize(task.Inputs, JsonOptions));
        command.AddParameter("@OutputsJson", JsonSerializer.Serialize(task.Outputs, JsonOptions));
        command.AddParameter("@RequiresApproval", task.RequiresApproval ? 1 : 0);
        command.AddParameter("@ApprovalReason", task.ApprovalReason);
        command.AddParameter("@CreatedUtc", task.CreatedUtc.ToString("O"));
        command.AddParameter("@UpdatedUtc", task.UpdatedUtc.ToString("O"));
        command.AddParameter("@StartedUtc", task.StartedUtc?.ToString("O"));
        command.AddParameter("@CompletedUtc", task.CompletedUtc?.ToString("O"));
        command.AddParameter("@FailureReason", task.FailureReason);
        command.AddParameter("@Metadata", task.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertDependencyAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        WorkflowDependency dependency,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO WorkflowDependencies (WorkflowId, TaskId, DependsOnTaskId, DependencyType, Metadata)
            VALUES (@WorkflowId, @TaskId, @DependsOnTaskId, @DependencyType, @Metadata)
            ON CONFLICT(WorkflowId, TaskId, DependsOnTaskId) DO UPDATE SET
                DependencyType = excluded.DependencyType,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@WorkflowId", dependency.WorkflowId);
        command.AddParameter("@TaskId", dependency.TaskId);
        command.AddParameter("@DependsOnTaskId", dependency.DependsOnTaskId);
        command.AddParameter("@DependencyType", dependency.DependencyType);
        command.AddParameter("@Metadata", dependency.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertExecutionEventAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        WorkflowExecutionEvent ev,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO WorkflowExecutionHistory
                (Id, WorkflowId, EventType, TimestampUtc, Description, TaskId, Metadata)
            VALUES
                (@Id, @WorkflowId, @EventType, @TimestampUtc, @Description, @TaskId, @Metadata);
            """;
        command.AddParameter("@Id", ev.EventId);
        command.AddParameter("@WorkflowId", ev.WorkflowId);
        command.AddParameter("@EventType", ev.EventType.ToString());
        command.AddParameter("@TimestampUtc", ev.TimestampUtc.ToString("O"));
        command.AddParameter("@Description", ev.Description);
        command.AddParameter("@TaskId", ev.TaskId);
        command.AddParameter("@Metadata", ev.Metadata);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkflowInstance?> ReadInstanceAsync(
        System.Data.Common.DbConnection connection,
        string workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PlanId, Kind, Status, RiskLevel, Objective, Summary, RepositoryId, RepositoryPath,
                   CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, ActiveTaskId
            FROM WorkflowInstances
            WHERE Id = @Id;
            """;
        command.AddParameter("@Id", workflowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadInstance(reader) : null;
    }

    private static WorkflowInstance ReadInstance(System.Data.Common.DbDataReader reader)
    {
        return new WorkflowInstance(
            WorkflowId: reader.GetString(0),
            PlanId: reader.GetString(1),
            Kind: Enum.Parse<EngineeringWorkflowKind>(reader.GetString(2)),
            Status: Enum.Parse<WorkflowInstanceStatus>(reader.GetString(3)),
            RiskLevel: Enum.Parse<EngineeringWorkflowRiskLevel>(reader.GetString(4)),
            Objective: reader.GetString(5),
            Summary: reader.GetString(6),
            RepositoryId: reader.IsDBNull(7) ? null : reader.GetString(7),
            RepositoryPath: reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedUtc: DateTime.Parse(reader.GetString(9)).ToUniversalTime(),
            UpdatedUtc: DateTime.Parse(reader.GetString(10)).ToUniversalTime(),
            StartedUtc: reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)).ToUniversalTime(),
            CompletedUtc: reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12)).ToUniversalTime(),
            ActiveTaskId: reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static async Task<IReadOnlyList<WorkflowTask>> ReadTasksAsync(
        System.Data.Common.DbConnection connection,
        string workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkflowId, Title, Kind, Status, Sequence, Description, InputsJson, OutputsJson,
                   RequiresApproval, ApprovalReason, CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc, FailureReason, Metadata
            FROM WorkflowTasks
            WHERE WorkflowId = @WorkflowId
            ORDER BY Sequence;
            """;
        command.AddParameter("@WorkflowId", workflowId);

        var tasks = new List<WorkflowTask>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(new WorkflowTask(
                TaskId: reader.GetString(0),
                WorkflowId: reader.GetString(1),
                Title: reader.GetString(2),
                Kind: Enum.Parse<EngineeringWorkflowStepKind>(reader.GetString(3)),
                Status: Enum.Parse<WorkflowTaskStatus>(reader.GetString(4)),
                Sequence: reader.GetInt32(5),
                Description: reader.GetString(6),
                Inputs: DeserializeList(reader.GetString(7)),
                Outputs: DeserializeList(reader.GetString(8)),
                RequiresApproval: reader.GetInt32(9) == 1,
                ApprovalReason: reader.IsDBNull(10) ? null : reader.GetString(10),
                CreatedUtc: DateTime.Parse(reader.GetString(11)).ToUniversalTime(),
                UpdatedUtc: DateTime.Parse(reader.GetString(12)).ToUniversalTime(),
                StartedUtc: reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)).ToUniversalTime(),
                CompletedUtc: reader.IsDBNull(14) ? null : DateTime.Parse(reader.GetString(14)).ToUniversalTime(),
                FailureReason: reader.IsDBNull(15) ? null : reader.GetString(15),
                Metadata: reader.IsDBNull(16) ? null : reader.GetString(16)));
        }

        return tasks;
    }

    private static async Task<IReadOnlyList<WorkflowDependency>> ReadDependenciesAsync(
        System.Data.Common.DbConnection connection,
        string workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT WorkflowId, TaskId, DependsOnTaskId, DependencyType, Metadata
            FROM WorkflowDependencies
            WHERE WorkflowId = @WorkflowId
            ORDER BY TaskId, DependsOnTaskId;
            """;
        command.AddParameter("@WorkflowId", workflowId);

        var dependencies = new List<WorkflowDependency>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            dependencies.Add(new WorkflowDependency(
                WorkflowId: reader.GetString(0),
                TaskId: reader.GetString(1),
                DependsOnTaskId: reader.GetString(2),
                DependencyType: reader.GetString(3),
                Metadata: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return dependencies;
    }

    private static async Task<IReadOnlyList<WorkflowExecutionEvent>> ReadHistoryAsync(
        System.Data.Common.DbConnection connection,
        string workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkflowId, EventType, TimestampUtc, Description, TaskId, Metadata
            FROM WorkflowExecutionHistory
            WHERE WorkflowId = @WorkflowId
            ORDER BY TimestampUtc, Id;
            """;
        command.AddParameter("@WorkflowId", workflowId);

        var history = new List<WorkflowExecutionEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            history.Add(new WorkflowExecutionEvent(
                EventId: reader.GetString(0),
                WorkflowId: reader.GetString(1),
                EventType: Enum.Parse<WorkflowExecutionEventType>(reader.GetString(2)),
                TimestampUtc: DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                Description: reader.GetString(4),
                TaskId: reader.IsDBNull(5) ? null : reader.GetString(5),
                Metadata: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return history;
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();
    }
}
