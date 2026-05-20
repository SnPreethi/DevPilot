using DevPilot.Contracts;
using DevPilot.Core.Execution;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ExecutionPipelineOrchestratorTests
{
    [Fact]
    public async Task StartAsync_CreatesSupervisedPipelineWithSafetyStages()
    {
        var orchestrator = CreateOrchestrator();

        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply"));

        Assert.Equal(ExecutionPipelineStatus.Running, state.Pipeline.Status);
        Assert.Contains(state.Stages, stage => stage.Kind == ExecutionStageKind.PrepareRollback);
        Assert.Contains(state.Stages, stage => stage.Kind == ExecutionStageKind.AwaitApproval && stage.RequiresApproval);
        Assert.Contains(state.Checkpoints, checkpoint => checkpoint.Kind == ExecutionCheckpointKind.UserApproval && !checkpoint.IsSatisfied);
    }

    [Fact]
    public async Task ApproveAsync_RequiresWaitingApprovalState()
    {
        var orchestrator = CreateOrchestrator();
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ApproveAsync(
            new ApproveExecutionPipelineRequest(state.Pipeline.PipelineId, "tester")));
    }

    [Fact]
    public async Task ValidationProgression_ReachesApprovalGateAndAllowsApproval()
    {
        var orchestrator = CreateOrchestrator();
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply",
            DryRun: false));

        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);

        Assert.Equal(ExecutionPipelineStatus.WaitingApproval, state.Pipeline.Status);

        state = await orchestrator.ApproveAsync(new ApproveExecutionPipelineRequest(state.Pipeline.PipelineId, "tester"));

        Assert.Equal(ExecutionPipelineStatus.Applying, state.Pipeline.Status);
        Assert.Contains(state.Artifacts, artifact => artifact.Kind == ExecutionArtifactKind.ApprovalDecision);
    }

    [Fact]
    public async Task MarkAppliedAsync_RejectsDryRunPipelines()
    {
        var orchestrator = CreateOrchestrator();
        var state = await MoveToApprovedApplyAsync(orchestrator, dryRun: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.MarkAppliedAsync(
            new ApplyExecutionPipelineRequest(state.Pipeline.PipelineId, "{}")));
    }

    [Fact]
    public async Task FailedValidation_RecordsFailureAndParsedExecutionEvent()
    {
        var orchestrator = CreateOrchestrator();
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply"));

        var rawOutput = @"C:\repo\Program.cs(12,5): error CS0246: The type or namespace name 'Missing' could not be found";
        state = await orchestrator.CompleteValidationAsync(new CompleteExecutionValidationRequest(
            state.Pipeline.PipelineId,
            false,
            new[] { "Build failed" },
            RawOutput: rawOutput));

        Assert.Equal(ExecutionPipelineStatus.Failed, state.Pipeline.Status);
        Assert.Single(state.Failures);
        Assert.Equal(ExecutionEventType.BuildFailure, state.Failures[0].ParsedEvent?.Type);
    }

    [Fact]
    public async Task Rollback_RequiresPreparedSnapshot()
    {
        var orchestrator = CreateOrchestrator();
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.TriggerRollbackAsync(state.Pipeline.PipelineId));
    }

    [Fact]
    public async Task Rollback_CompletesAfterPreparedSnapshot()
    {
        var orchestrator = CreateOrchestrator();
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply"));

        state = await orchestrator.PrepareRollbackAsync(state.Pipeline.PipelineId, "C:\\repo", new[] { "Program.cs" });
        state = await orchestrator.TriggerRollbackAsync(state.Pipeline.PipelineId);
        state = await orchestrator.MarkRollbackCompletedAsync(state.Pipeline.PipelineId);

        Assert.Equal(ExecutionPipelineStatus.Completed, state.Pipeline.Status);
        Assert.Contains(state.Timeline, ev => ev.EventType == ExecutionTimelineEventType.RollbackCompleted);
    }

    private static async Task<ExecutionPipelineState> MoveToApprovedApplyAsync(
        ExecutionPipelineOrchestrator orchestrator,
        bool dryRun)
    {
        var state = await orchestrator.StartAsync(new StartExecutionPipelineRequest(
            "workflow-1",
            "task-1",
            "Validate patch before apply",
            DryRun: dryRun));

        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);
        state = await PassActiveValidationAsync(orchestrator, state);
        return await orchestrator.ApproveAsync(new ApproveExecutionPipelineRequest(state.Pipeline.PipelineId, "tester"));
    }

    private static Task<ExecutionPipelineState> PassActiveValidationAsync(
        ExecutionPipelineOrchestrator orchestrator,
        ExecutionPipelineState state)
    {
        return orchestrator.CompleteValidationAsync(new CompleteExecutionValidationRequest(
            state.Pipeline.PipelineId,
            true,
            new[] { "passed" }));
    }

    private static ExecutionPipelineOrchestrator CreateOrchestrator()
    {
        return new ExecutionPipelineOrchestrator(
            new InMemoryExecutionPipelineStore(),
            new StubTaskGraphOrchestrator(),
            new TerminalOrchestrator());
    }

    private sealed class StubTaskGraphOrchestrator : ITaskGraphOrchestrator
    {
        private readonly WorkflowState _state;

        public StubTaskGraphOrchestrator()
        {
            var now = DateTime.UtcNow;
            var instance = new WorkflowInstance("workflow-1", "plan-1", EngineeringWorkflowKind.FeatureImplementation, WorkflowInstanceStatus.Active, EngineeringWorkflowRiskLevel.Medium, "Workflow", "Summary", "repo-1", "C:\\repo", now, now);
            var task = new WorkflowTask("task-1", "workflow-1", "Patch preview", EngineeringWorkflowStepKind.PatchPreview, WorkflowTaskStatus.InProgress, 1, "Preview", Array.Empty<string>(), Array.Empty<string>(), false, null, now, now);
            _state = new WorkflowState(instance, new[] { task }, Array.Empty<WorkflowDependency>(), Array.Empty<WorkflowExecutionEvent>());
        }

        public Task<WorkflowState> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(_state);
        public Task<IReadOnlyList<WorkflowInstance>> ListAsync(string? repositoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowInstance>>(new[] { _state.Instance });
        public Task<WorkflowState> AdvanceAsync(AdvanceWorkflowRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task<WorkflowState> PauseAsync(string workflowId, string? reason = null, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task<WorkflowState> ResumeAsync(string workflowId, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task<WorkflowState> CancelAsync(string workflowId, string? reason = null, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public WorkflowProgressSnapshot GetProgressSnapshot(WorkflowState state) => new(state.Instance.WorkflowId, state.Instance.Status, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, Array.Empty<WorkflowTask>(), Array.Empty<WorkflowTask>(), Array.Empty<WorkflowTask>());
    }

    private sealed class InMemoryExecutionPipelineStore : IExecutionPipelineStore
    {
        private readonly Dictionary<string, ExecutionPipelineState> _states = new(StringComparer.Ordinal);

        public Task SaveAsync(ExecutionPipelineState state, CancellationToken cancellationToken = default)
        {
            _states[state.Pipeline.PipelineId] = state;
            return Task.CompletedTask;
        }

        public Task<ExecutionPipelineState?> GetAsync(string pipelineId, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(pipelineId, out var state);
            return Task.FromResult(state);
        }

        public Task<IReadOnlyList<ExecutionPipeline>> ListAsync(string? workflowId = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExecutionPipeline> pipelines = _states.Values.Select(state => state.Pipeline).ToList();
            return Task.FromResult(pipelines);
        }

        public Task UpdatePipelineAsync(ExecutionPipeline pipeline, CancellationToken cancellationToken = default)
        {
            var state = _states[pipeline.PipelineId];
            _states[pipeline.PipelineId] = state with { Pipeline = pipeline };
            return Task.CompletedTask;
        }

        public Task UpdateStageAsync(ExecutionStage stage, CancellationToken cancellationToken = default)
        {
            var state = _states[stage.PipelineId];
            _states[stage.PipelineId] = state with { Stages = state.Stages.Select(s => s.StageId == stage.StageId ? stage : s).ToList() };
            return Task.CompletedTask;
        }

        public Task AddCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            var state = _states[checkpoint.PipelineId];
            _states[checkpoint.PipelineId] = state with { Checkpoints = state.Checkpoints.Where(cp => cp.CheckpointId != checkpoint.CheckpointId).Concat(new[] { checkpoint }).ToList() };
            return Task.CompletedTask;
        }

        public Task AddArtifactAsync(ExecutionArtifact artifact, CancellationToken cancellationToken = default)
        {
            var state = _states[artifact.PipelineId];
            _states[artifact.PipelineId] = state with { Artifacts = state.Artifacts.Concat(new[] { artifact }).ToList() };
            return Task.CompletedTask;
        }

        public Task AddFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default)
        {
            var state = _states[failure.PipelineId];
            _states[failure.PipelineId] = state with { Failures = state.Failures.Concat(new[] { failure }).ToList() };
            return Task.CompletedTask;
        }

        public Task AddValidationAsync(ExecutionValidationResult validation, CancellationToken cancellationToken = default)
        {
            var state = _states[validation.PipelineId];
            _states[validation.PipelineId] = state with { Validations = state.Validations.Concat(new[] { validation }).ToList() };
            return Task.CompletedTask;
        }

        public Task AddRollbackSnapshotAsync(ExecutionRollbackSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var state = _states[snapshot.PipelineId];
            _states[snapshot.PipelineId] = state with { RollbackSnapshots = state.RollbackSnapshots.Concat(new[] { snapshot }).ToList() };
            return Task.CompletedTask;
        }

        public Task AddTimelineEventAsync(ExecutionTimelineEvent ev, CancellationToken cancellationToken = default)
        {
            var state = _states[ev.PipelineId];
            _states[ev.PipelineId] = state with { Timeline = state.Timeline.Concat(new[] { ev }).ToList() };
            return Task.CompletedTask;
        }
    }
}
