using DevPilot.Contracts;
using DevPilot.Core.Workflow;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class TaskGraphOrchestratorTests
{
    [Fact]
    public async Task StartAsync_CreatesResolvableTaskGraph()
    {
        var store = new InMemoryWorkflowStateStore();
        var orchestrator = new TaskGraphOrchestrator(new EngineeringWorkflowPlanner(), store);

        var state = await orchestrator.StartAsync(new StartWorkflowRequest(
            new EngineeringWorkflowRequest("Implement resumable workflow orchestration", "repo-1", "C:\\src\\repo")));

        Assert.Equal(WorkflowInstanceStatus.Active, state.Instance.Status);
        Assert.NotEmpty(state.Tasks);
        Assert.NotEmpty(state.Dependencies);
        Assert.Equal(WorkflowTaskStatus.Ready, state.Tasks.OrderBy(t => t.Sequence).First().Status);
        Assert.All(state.Dependencies, dependency =>
        {
            Assert.Contains(state.Tasks, task => task.TaskId == dependency.TaskId);
            Assert.Contains(state.Tasks, task => task.TaskId == dependency.DependsOnTaskId);
        });
    }

    [Fact]
    public async Task AdvanceAsync_UnlocksDependentTasksInOrder()
    {
        var store = new InMemoryWorkflowStateStore();
        var orchestrator = new TaskGraphOrchestrator(new EngineeringWorkflowPlanner(), store);
        var state = await orchestrator.StartAsync(new StartWorkflowRequest(
            new EngineeringWorkflowRequest("Implement task graph orchestration")));

        var first = state.Tasks.OrderBy(t => t.Sequence).First();

        state = await orchestrator.AdvanceAsync(new AdvanceWorkflowRequest(
            state.Instance.WorkflowId,
            first.TaskId,
            WorkflowTaskStatus.InProgress));
        state = await orchestrator.AdvanceAsync(new AdvanceWorkflowRequest(
            state.Instance.WorkflowId,
            first.TaskId,
            WorkflowTaskStatus.Completed));

        Assert.Equal(WorkflowTaskStatus.Completed, state.Tasks.Single(t => t.TaskId == first.TaskId).Status);
        Assert.Contains(state.Tasks, task => task.Sequence == 2 && task.Status == WorkflowTaskStatus.Ready);
    }

    [Fact]
    public async Task AdvanceAsync_BlocksOutOfOrderExecution()
    {
        var store = new InMemoryWorkflowStateStore();
        var orchestrator = new TaskGraphOrchestrator(new EngineeringWorkflowPlanner(), store);
        var state = await orchestrator.StartAsync(new StartWorkflowRequest(
            new EngineeringWorkflowRequest("Implement task graph orchestration")));
        var second = state.Tasks.OrderBy(t => t.Sequence).Skip(1).First();

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.AdvanceAsync(
            new AdvanceWorkflowRequest(state.Instance.WorkflowId, second.TaskId, WorkflowTaskStatus.InProgress)));
    }

    [Fact]
    public async Task ApprovalTask_RequiresApprovalBeforeInProgress()
    {
        var store = new InMemoryWorkflowStateStore();
        var orchestrator = new TaskGraphOrchestrator(new EngineeringWorkflowPlanner(), store);
        var state = await orchestrator.StartAsync(new StartWorkflowRequest(
            new EngineeringWorkflowRequest("Implement task graph orchestration")));

        while (!state.Tasks.Any(t => t.Status == WorkflowTaskStatus.WaitingApproval))
        {
            var ready = state.Tasks.OrderBy(t => t.Sequence).First(t => t.Status == WorkflowTaskStatus.Ready);
            state = await orchestrator.AdvanceAsync(new AdvanceWorkflowRequest(state.Instance.WorkflowId, ready.TaskId, WorkflowTaskStatus.InProgress));
            state = await orchestrator.AdvanceAsync(new AdvanceWorkflowRequest(state.Instance.WorkflowId, ready.TaskId, WorkflowTaskStatus.Completed));
        }

        var approval = state.Tasks.Single(t => t.Status == WorkflowTaskStatus.WaitingApproval);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.AdvanceAsync(
            new AdvanceWorkflowRequest(state.Instance.WorkflowId, approval.TaskId, WorkflowTaskStatus.InProgress)));

        state = await orchestrator.AdvanceAsync(new AdvanceWorkflowRequest(
            state.Instance.WorkflowId,
            approval.TaskId,
            WorkflowTaskStatus.InProgress,
            ApprovalGranted: true));

        Assert.Equal(WorkflowTaskStatus.InProgress, state.Tasks.Single(t => t.TaskId == approval.TaskId).Status);
    }

    [Fact]
    public async Task PauseAndResume_RestoresActiveWorkflow()
    {
        var store = new InMemoryWorkflowStateStore();
        var orchestrator = new TaskGraphOrchestrator(new EngineeringWorkflowPlanner(), store);
        var state = await orchestrator.StartAsync(new StartWorkflowRequest(
            new EngineeringWorkflowRequest("Implement task graph orchestration")));

        state = await orchestrator.PauseAsync(state.Instance.WorkflowId, "User paused");
        Assert.Equal(WorkflowInstanceStatus.Paused, state.Instance.Status);

        state = await orchestrator.ResumeAsync(state.Instance.WorkflowId);
        Assert.Equal(WorkflowInstanceStatus.Active, state.Instance.Status);
        Assert.Contains(state.Tasks, task => task.Status == WorkflowTaskStatus.Ready);
    }

    [Fact]
    public void ValidateAcyclic_RejectsCycles()
    {
        var now = DateTime.UtcNow;
        var tasks = new[]
        {
            CreateTask("a", now),
            CreateTask("b", now)
        };
        var dependencies = new[]
        {
            new WorkflowDependency("workflow", "a", "b", "FinishToStart"),
            new WorkflowDependency("workflow", "b", "a", "FinishToStart")
        };

        Assert.Throws<InvalidOperationException>(() => TaskGraphOrchestrator.ValidateAcyclic(tasks, dependencies));
    }

    private static WorkflowTask CreateTask(string id, DateTime now)
    {
        return new WorkflowTask(
            id,
            "workflow",
            id,
            EngineeringWorkflowStepKind.PlanDrafting,
            WorkflowTaskStatus.Pending,
            1,
            id,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            null,
            now,
            now);
    }

    private sealed class InMemoryWorkflowStateStore : IWorkflowStateStore
    {
        private readonly Dictionary<string, WorkflowState> _states = new(StringComparer.Ordinal);

        public Task SaveAsync(WorkflowState state, CancellationToken cancellationToken = default)
        {
            _states[state.Instance.WorkflowId] = state;
            return Task.CompletedTask;
        }

        public Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(workflowId, out var state);
            return Task.FromResult(state);
        }

        public Task<IReadOnlyList<WorkflowInstance>> ListAsync(string? repositoryId = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkflowInstance> instances = _states.Values
                .Select(state => state.Instance)
                .Where(instance => string.IsNullOrEmpty(repositoryId) || instance.RepositoryId == repositoryId)
                .ToList();
            return Task.FromResult(instances);
        }

        public Task AddExecutionEventAsync(WorkflowExecutionEvent ev, CancellationToken cancellationToken = default)
        {
            var state = _states[ev.WorkflowId];
            _states[ev.WorkflowId] = state with { ExecutionHistory = state.ExecutionHistory.Concat(new[] { ev }).ToList() };
            return Task.CompletedTask;
        }

        public Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
        {
            var state = _states[instance.WorkflowId];
            _states[instance.WorkflowId] = state with { Instance = instance };
            return Task.CompletedTask;
        }

        public Task UpdateTaskAsync(WorkflowTask task, CancellationToken cancellationToken = default)
        {
            var state = _states[task.WorkflowId];
            var tasks = state.Tasks.Select(existing => existing.TaskId == task.TaskId ? task : existing).ToList();
            _states[task.WorkflowId] = state with { Tasks = tasks };
            return Task.CompletedTask;
        }
    }
}
