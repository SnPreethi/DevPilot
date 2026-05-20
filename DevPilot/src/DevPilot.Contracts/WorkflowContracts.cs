using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public enum EngineeringWorkflowKind
{
    General,
    FeatureImplementation,
    BugFix,
    Refactoring,
    RepositoryMigration,
    TestStabilization,
    Documentation
}

public enum EngineeringWorkflowStepKind
{
    RepositoryInspection,
    ContextRetrieval,
    ImpactAnalysis,
    PlanDrafting,
    PatchPreview,
    ApprovalGate,
    PatchApplication,
    Verification,
    MemoryUpdate
}

public enum EngineeringWorkflowStepStatus
{
    Planned,
    WaitingForApproval,
    Blocked,
    Completed
}

public enum EngineeringWorkflowRiskLevel
{
    Low,
    Medium,
    High
}

public enum WorkflowInstanceStatus
{
    Created,
    Active,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum WorkflowTaskStatus
{
    Pending,
    Ready,
    Blocked,
    InProgress,
    WaitingApproval,
    Completed,
    Failed,
    Cancelled
}

public enum WorkflowExecutionEventType
{
    Created,
    Started,
    Advanced,
    Paused,
    Resumed,
    Approved,
    Completed,
    Failed,
    Cancelled,
    Reverted
}

public sealed record EngineeringWorkflowRequest(
    string Objective,
    string? RepositoryId = null,
    string? RepositoryPath = null,
    IReadOnlyList<NormalizedDiagnostic>? Diagnostics = null,
    IReadOnlyList<ExecutionEvent>? ExecutionEvents = null,
    IReadOnlyList<string>? Constraints = null);

public sealed record EngineeringWorkflowStep(
    string StepId,
    string Title,
    EngineeringWorkflowStepKind Kind,
    EngineeringWorkflowStepStatus Status,
    string Description,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> DependsOn,
    bool RequiresApproval,
    string? ApprovalReason = null);

public sealed record EngineeringWorkflowPlan(
    string PlanId,
    EngineeringWorkflowKind Kind,
    EngineeringWorkflowRiskLevel RiskLevel,
    string Objective,
    string Summary,
    string? RepositoryId,
    string? RepositoryPath,
    DateTime CreatedUtc,
    IReadOnlyList<EngineeringWorkflowStep> Steps);

public interface IEngineeringWorkflowPlanner
{
    Task<EngineeringWorkflowPlan> PlanAsync(
        EngineeringWorkflowRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowInstance(
    string WorkflowId,
    string PlanId,
    EngineeringWorkflowKind Kind,
    WorkflowInstanceStatus Status,
    EngineeringWorkflowRiskLevel RiskLevel,
    string Objective,
    string Summary,
    string? RepositoryId,
    string? RepositoryPath,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? StartedUtc = null,
    DateTime? CompletedUtc = null,
    string? ActiveTaskId = null);

public sealed record WorkflowTask(
    string TaskId,
    string WorkflowId,
    string Title,
    EngineeringWorkflowStepKind Kind,
    WorkflowTaskStatus Status,
    int Sequence,
    string Description,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    bool RequiresApproval,
    string? ApprovalReason,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? StartedUtc = null,
    DateTime? CompletedUtc = null,
    string? FailureReason = null,
    string? Metadata = null);

public sealed record WorkflowDependency(
    string WorkflowId,
    string TaskId,
    string DependsOnTaskId,
    string DependencyType,
    string? Metadata = null);

public sealed record WorkflowExecutionEvent(
    string EventId,
    string WorkflowId,
    WorkflowExecutionEventType EventType,
    DateTime TimestampUtc,
    string Description,
    string? TaskId = null,
    string? Metadata = null);

public sealed record WorkflowProgressSnapshot(
    string WorkflowId,
    WorkflowInstanceStatus Status,
    int TotalTasks,
    int PendingTasks,
    int ReadyTasks,
    int BlockedTasks,
    int InProgressTasks,
    int WaitingApprovalTasks,
    int CompletedTasks,
    int FailedTasks,
    int CancelledTasks,
    double CompletionRatio,
    IReadOnlyList<WorkflowTask> ReadyTaskList,
    IReadOnlyList<WorkflowTask> BlockedTaskList,
    IReadOnlyList<WorkflowTask> ApprovalCheckpoints);

public sealed record WorkflowState(
    WorkflowInstance Instance,
    IReadOnlyList<WorkflowTask> Tasks,
    IReadOnlyList<WorkflowDependency> Dependencies,
    IReadOnlyList<WorkflowExecutionEvent> ExecutionHistory);

public sealed record StartWorkflowRequest(
    EngineeringWorkflowRequest PlanningRequest);

public sealed record AdvanceWorkflowRequest(
    string WorkflowId,
    string TaskId,
    WorkflowTaskStatus TargetStatus,
    string? Reason = null,
    bool ApprovalGranted = false,
    string? Metadata = null);

public interface IWorkflowStateStore
{
    Task SaveAsync(WorkflowState state, CancellationToken cancellationToken = default);
    Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> ListAsync(string? repositoryId = null, CancellationToken cancellationToken = default);
    Task AddExecutionEventAsync(WorkflowExecutionEvent ev, CancellationToken cancellationToken = default);
    Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    Task UpdateTaskAsync(WorkflowTask task, CancellationToken cancellationToken = default);
}

public interface ITaskGraphOrchestrator
{
    Task<WorkflowState> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowState?> GetAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> ListAsync(string? repositoryId = null, CancellationToken cancellationToken = default);
    Task<WorkflowState> AdvanceAsync(AdvanceWorkflowRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowState> PauseAsync(string workflowId, string? reason = null, CancellationToken cancellationToken = default);
    Task<WorkflowState> ResumeAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<WorkflowState> CancelAsync(string workflowId, string? reason = null, CancellationToken cancellationToken = default);
    WorkflowProgressSnapshot GetProgressSnapshot(WorkflowState state);
}
