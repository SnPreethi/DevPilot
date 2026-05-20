using System.Security.Cryptography;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.Core.Workflow;

public sealed class TaskGraphOrchestrator : ITaskGraphOrchestrator
{
    private readonly IEngineeringWorkflowPlanner _planner;
    private readonly IWorkflowStateStore _store;

    public TaskGraphOrchestrator(IEngineeringWorkflowPlanner planner, IWorkflowStateStore store)
    {
        _planner = planner;
        _store = store;
    }

    public async Task<WorkflowState> StartAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = await _planner.PlanAsync(request.PlanningRequest, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var workflowId = CreateStableId($"workflow:{plan.PlanId}:{plan.RepositoryId}:{plan.Objective}");

        var instance = new WorkflowInstance(
            WorkflowId: workflowId,
            PlanId: plan.PlanId,
            Kind: plan.Kind,
            Status: WorkflowInstanceStatus.Active,
            RiskLevel: plan.RiskLevel,
            Objective: plan.Objective,
            Summary: plan.Summary,
            RepositoryId: plan.RepositoryId,
            RepositoryPath: plan.RepositoryPath,
            CreatedUtc: now,
            UpdatedUtc: now,
            StartedUtc: now);

        var tasks = plan.Steps
            .Select((step, index) => ToWorkflowTask(workflowId, step, index + 1, now))
            .ToList();

        var dependencies = plan.Steps
            .SelectMany(step => step.DependsOn.Select(dependsOn => new WorkflowDependency(
                WorkflowId: workflowId,
                TaskId: step.StepId,
                DependsOnTaskId: dependsOn,
                DependencyType: "FinishToStart")))
            .ToList();

        ValidateAcyclic(tasks, dependencies);
        tasks = ResolveTaskReadiness(tasks, dependencies, now);
        instance = instance with { ActiveTaskId = tasks.FirstOrDefault(t => t.Status == WorkflowTaskStatus.Ready)?.TaskId };

        var history = new[]
        {
            CreateEvent(workflowId, WorkflowExecutionEventType.Created, "Workflow instance created from engineering workflow plan.", now),
            CreateEvent(workflowId, WorkflowExecutionEventType.Started, "Workflow started and initial task readiness resolved.", now)
        };

        var state = new WorkflowState(instance, tasks, dependencies, history);
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(workflowId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowInstance>> ListAsync(
        string? repositoryId = null,
        CancellationToken cancellationToken = default)
    {
        return _store.ListAsync(repositoryId, cancellationToken);
    }

    public async Task<WorkflowState> AdvanceAsync(
        AdvanceWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(request.WorkflowId, cancellationToken).ConfigureAwait(false);
        if (state.Instance.Status != WorkflowInstanceStatus.Active)
        {
            throw new InvalidOperationException($"Workflow '{request.WorkflowId}' is not active.");
        }

        var tasks = state.Tasks.ToList();
        var taskIndex = tasks.FindIndex(task => task.TaskId == request.TaskId);
        if (taskIndex < 0)
        {
            throw new InvalidOperationException($"Workflow task '{request.TaskId}' was not found.");
        }

        var task = tasks[taskIndex];
        ValidateDependencyOrder(task, request.TargetStatus, tasks, state.Dependencies);
        WorkflowTaskStateMachine.ValidateTransition(task, request.TargetStatus, request.ApprovalGranted);

        var now = DateTime.UtcNow;
        var updatedTask = task with
        {
            Status = request.TargetStatus,
            UpdatedUtc = now,
            StartedUtc = request.TargetStatus == WorkflowTaskStatus.InProgress && task.StartedUtc == null ? now : task.StartedUtc,
            CompletedUtc = IsTerminal(request.TargetStatus) ? now : task.CompletedUtc,
            FailureReason = request.TargetStatus == WorkflowTaskStatus.Failed ? request.Reason : task.FailureReason,
            Metadata = request.Metadata ?? task.Metadata
        };

        tasks[taskIndex] = updatedTask;
        tasks = ResolveTaskReadiness(tasks, state.Dependencies, now);

        var instanceStatus = ResolveInstanceStatus(state.Instance.Status, tasks);
        var activeTaskId = tasks
            .FirstOrDefault(task => task.Status is WorkflowTaskStatus.InProgress or WorkflowTaskStatus.WaitingApproval)?.TaskId
            ?? tasks.FirstOrDefault(task => task.Status == WorkflowTaskStatus.Ready)?.TaskId;
        var updatedInstance = state.Instance with
        {
            Status = instanceStatus,
            UpdatedUtc = now,
            CompletedUtc = instanceStatus is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Cancelled
                ? now
                : state.Instance.CompletedUtc,
            ActiveTaskId = activeTaskId
        };

        var eventType = ToEventType(request.TargetStatus, request.ApprovalGranted);
        var ev = CreateEvent(
            request.WorkflowId,
            eventType,
            request.Reason ?? $"Task '{request.TaskId}' advanced to {request.TargetStatus}.",
            now,
            request.TaskId,
            request.Metadata);

        await _store.UpdateTaskAsync(updatedTask, cancellationToken).ConfigureAwait(false);
        foreach (var readinessTask in tasks.Where(t => t.TaskId != updatedTask.TaskId))
        {
            var original = state.Tasks.First(t => t.TaskId == readinessTask.TaskId);
            if (original.Status != readinessTask.Status)
            {
                await _store.UpdateTaskAsync(readinessTask, cancellationToken).ConfigureAwait(false);
            }
        }

        await _store.UpdateInstanceAsync(updatedInstance, cancellationToken).ConfigureAwait(false);
        await _store.AddExecutionEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return new WorkflowState(
            updatedInstance,
            tasks,
            state.Dependencies,
            state.ExecutionHistory.Concat(new[] { ev }).ToList());
    }

    public async Task<WorkflowState> PauseAsync(
        string workflowId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(workflowId, cancellationToken).ConfigureAwait(false);
        if (state.Instance.Status != WorkflowInstanceStatus.Active)
        {
            throw new InvalidOperationException($"Only active workflows can be paused. Current status: {state.Instance.Status}.");
        }

        return await UpdateWorkflowStatusAsync(
            state,
            WorkflowInstanceStatus.Paused,
            WorkflowExecutionEventType.Paused,
            reason ?? "Workflow paused.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowState> ResumeAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(workflowId, cancellationToken).ConfigureAwait(false);
        if (state.Instance.Status != WorkflowInstanceStatus.Paused)
        {
            throw new InvalidOperationException($"Only paused workflows can be resumed. Current status: {state.Instance.Status}.");
        }

        var now = DateTime.UtcNow;
        var tasks = ResolveTaskReadiness(state.Tasks.ToList(), state.Dependencies, now);
        var instance = state.Instance with
        {
            Status = WorkflowInstanceStatus.Active,
            UpdatedUtc = now,
            ActiveTaskId = tasks.FirstOrDefault(t => t.Status is WorkflowTaskStatus.InProgress or WorkflowTaskStatus.WaitingApproval)?.TaskId
                ?? tasks.FirstOrDefault(t => t.Status == WorkflowTaskStatus.Ready)?.TaskId
        };
        var ev = CreateEvent(workflowId, WorkflowExecutionEventType.Resumed, "Workflow resumed and task readiness restored.", now);

        foreach (var task in tasks)
        {
            var original = state.Tasks.First(t => t.TaskId == task.TaskId);
            if (original.Status != task.Status)
            {
                await _store.UpdateTaskAsync(task, cancellationToken).ConfigureAwait(false);
            }
        }

        await _store.UpdateInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        await _store.AddExecutionEventAsync(ev, cancellationToken).ConfigureAwait(false);
        return new WorkflowState(instance, tasks, state.Dependencies, state.ExecutionHistory.Concat(new[] { ev }).ToList());
    }

    public async Task<WorkflowState> CancelAsync(
        string workflowId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(workflowId, cancellationToken).ConfigureAwait(false);
        if (state.Instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Cancelled)
        {
            throw new InvalidOperationException($"Terminal workflow '{workflowId}' cannot be cancelled.");
        }

        var now = DateTime.UtcNow;
        var tasks = state.Tasks
            .Select(task => IsTerminal(task.Status)
                ? task
                : task with { Status = WorkflowTaskStatus.Cancelled, UpdatedUtc = now, CompletedUtc = now })
            .ToList();

        foreach (var task in tasks)
        {
            var original = state.Tasks.First(t => t.TaskId == task.TaskId);
            if (original.Status != task.Status)
            {
                await _store.UpdateTaskAsync(task, cancellationToken).ConfigureAwait(false);
            }
        }

        var instance = state.Instance with
        {
            Status = WorkflowInstanceStatus.Cancelled,
            UpdatedUtc = now,
            CompletedUtc = now,
            ActiveTaskId = null
        };
        var ev = CreateEvent(workflowId, WorkflowExecutionEventType.Cancelled, reason ?? "Workflow cancelled.", now);
        await _store.UpdateInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        await _store.AddExecutionEventAsync(ev, cancellationToken).ConfigureAwait(false);
        return new WorkflowState(instance, tasks, state.Dependencies, state.ExecutionHistory.Concat(new[] { ev }).ToList());
    }

    public WorkflowProgressSnapshot GetProgressSnapshot(WorkflowState state)
    {
        var tasks = state.Tasks;
        var completed = tasks.Count(t => t.Status == WorkflowTaskStatus.Completed);
        return new WorkflowProgressSnapshot(
            WorkflowId: state.Instance.WorkflowId,
            Status: state.Instance.Status,
            TotalTasks: tasks.Count,
            PendingTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.Pending),
            ReadyTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.Ready),
            BlockedTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.Blocked),
            InProgressTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.InProgress),
            WaitingApprovalTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.WaitingApproval),
            CompletedTasks: completed,
            FailedTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.Failed),
            CancelledTasks: tasks.Count(t => t.Status == WorkflowTaskStatus.Cancelled),
            CompletionRatio: tasks.Count == 0 ? 0 : completed / (double)tasks.Count,
            ReadyTaskList: tasks.Where(t => t.Status == WorkflowTaskStatus.Ready).OrderBy(t => t.Sequence).ToList(),
            BlockedTaskList: tasks.Where(t => t.Status == WorkflowTaskStatus.Blocked).OrderBy(t => t.Sequence).ToList(),
            ApprovalCheckpoints: tasks.Where(t => t.RequiresApproval && t.Status != WorkflowTaskStatus.Completed).OrderBy(t => t.Sequence).ToList());
    }

    private async Task<WorkflowState> UpdateWorkflowStatusAsync(
        WorkflowState state,
        WorkflowInstanceStatus status,
        WorkflowExecutionEventType eventType,
        string description,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var instance = state.Instance with { Status = status, UpdatedUtc = now };
        var ev = CreateEvent(state.Instance.WorkflowId, eventType, description, now);
        await _store.UpdateInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        await _store.AddExecutionEventAsync(ev, cancellationToken).ConfigureAwait(false);
        return state with
        {
            Instance = instance,
            ExecutionHistory = state.ExecutionHistory.Concat(new[] { ev }).ToList()
        };
    }

    private async Task<WorkflowState> LoadRequiredAsync(string workflowId, CancellationToken cancellationToken)
    {
        var state = await _store.GetAsync(workflowId, cancellationToken).ConfigureAwait(false);
        return state ?? throw new InvalidOperationException($"Workflow '{workflowId}' was not found.");
    }

    private static WorkflowTask ToWorkflowTask(
        string workflowId,
        EngineeringWorkflowStep step,
        int sequence,
        DateTime now)
    {
        return new WorkflowTask(
            TaskId: step.StepId,
            WorkflowId: workflowId,
            Title: step.Title,
            Kind: step.Kind,
            Status: WorkflowTaskStatus.Pending,
            Sequence: sequence,
            Description: step.Description,
            Inputs: step.Inputs,
            Outputs: step.Outputs,
            RequiresApproval: step.RequiresApproval,
            ApprovalReason: step.ApprovalReason,
            CreatedUtc: now,
            UpdatedUtc: now);
    }

    private static List<WorkflowTask> ResolveTaskReadiness(
        List<WorkflowTask> tasks,
        IReadOnlyList<WorkflowDependency> dependencies,
        DateTime now)
    {
        var completed = tasks
            .Where(task => task.Status == WorkflowTaskStatus.Completed)
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        return tasks.Select(task =>
        {
            if (task.Status is WorkflowTaskStatus.Completed
                or WorkflowTaskStatus.Failed
                or WorkflowTaskStatus.Cancelled
                or WorkflowTaskStatus.InProgress
                or WorkflowTaskStatus.WaitingApproval)
            {
                return task;
            }

            var taskDependencies = dependencies.Where(dep => dep.TaskId == task.TaskId).ToList();
            var allComplete = taskDependencies.All(dep => completed.Contains(dep.DependsOnTaskId));
            var resolvedStatus = allComplete
                ? task.RequiresApproval ? WorkflowTaskStatus.WaitingApproval : WorkflowTaskStatus.Ready
                : WorkflowTaskStatus.Blocked;
            return task.Status == resolvedStatus ? task : task with { Status = resolvedStatus, UpdatedUtc = now };
        }).OrderBy(task => task.Sequence).ToList();
    }

    private static void ValidateDependencyOrder(
        WorkflowTask task,
        WorkflowTaskStatus targetStatus,
        IReadOnlyList<WorkflowTask> tasks,
        IReadOnlyList<WorkflowDependency> dependencies)
    {
        if (targetStatus != WorkflowTaskStatus.InProgress && targetStatus != WorkflowTaskStatus.Completed)
        {
            return;
        }

        var incomplete = dependencies
            .Where(dep => dep.TaskId == task.TaskId)
            .Select(dep => tasks.FirstOrDefault(candidate => candidate.TaskId == dep.DependsOnTaskId))
            .Where(depTask => depTask == null || depTask.Status != WorkflowTaskStatus.Completed)
            .ToList();

        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException($"Task '{task.TaskId}' cannot advance before prerequisites are completed.");
        }
    }

    public static void ValidateAcyclic(
        IReadOnlyList<WorkflowTask> tasks,
        IReadOnlyList<WorkflowDependency> dependencies)
    {
        var taskIds = tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        foreach (var dep in dependencies)
        {
            if (!taskIds.Contains(dep.TaskId) || !taskIds.Contains(dep.DependsOnTaskId))
            {
                throw new InvalidOperationException("Workflow dependency references an unknown task.");
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            Visit(task.TaskId);
        }

        void Visit(string taskId)
        {
            if (visited.Contains(taskId))
            {
                return;
            }

            if (!visiting.Add(taskId))
            {
                throw new InvalidOperationException("Workflow dependencies contain a cycle.");
            }

            foreach (var dep in dependencies.Where(dep => dep.DependsOnTaskId == taskId))
            {
                Visit(dep.TaskId);
            }

            visiting.Remove(taskId);
            visited.Add(taskId);
        }
    }

    private static WorkflowInstanceStatus ResolveInstanceStatus(
        WorkflowInstanceStatus current,
        IReadOnlyList<WorkflowTask> tasks)
    {
        if (tasks.Any(task => task.Status == WorkflowTaskStatus.Failed))
        {
            return WorkflowInstanceStatus.Failed;
        }

        if (tasks.All(task => task.Status == WorkflowTaskStatus.Completed))
        {
            return WorkflowInstanceStatus.Completed;
        }

        return current == WorkflowInstanceStatus.Paused ? WorkflowInstanceStatus.Paused : WorkflowInstanceStatus.Active;
    }

    private static WorkflowExecutionEventType ToEventType(WorkflowTaskStatus status, bool approvalGranted)
    {
        if (approvalGranted)
        {
            return WorkflowExecutionEventType.Approved;
        }

        return status switch
        {
            WorkflowTaskStatus.Completed => WorkflowExecutionEventType.Completed,
            WorkflowTaskStatus.Failed => WorkflowExecutionEventType.Failed,
            WorkflowTaskStatus.Cancelled => WorkflowExecutionEventType.Cancelled,
            _ => WorkflowExecutionEventType.Advanced
        };
    }

    private static bool IsTerminal(WorkflowTaskStatus status)
    {
        return status is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Failed or WorkflowTaskStatus.Cancelled;
    }

    private static WorkflowExecutionEvent CreateEvent(
        string workflowId,
        WorkflowExecutionEventType eventType,
        string description,
        DateTime now,
        string? taskId = null,
        string? metadata = null)
    {
        return new WorkflowExecutionEvent(
            EventId: CreateStableId($"{workflowId}:{eventType}:{taskId}:{description}:{now:O}"),
            WorkflowId: workflowId,
            EventType: eventType,
            TimestampUtc: now,
            Description: description,
            TaskId: taskId,
            Metadata: metadata);
    }

    private static string CreateStableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
