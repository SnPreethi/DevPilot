using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public enum ExecutionPipelineStatus
{
    Pending,
    Running,
    WaitingApproval,
    Validating,
    Applying,
    RollingBack,
    Failed,
    Completed,
    Cancelled
}

public enum ExecutionStageKind
{
    PrepareRollback,
    ValidateWorkflow,
    ValidatePatchPreview,
    ValidateDiagnostics,
    AwaitApproval,
    ApplyPatch,
    RunVerification,
    CaptureArtifacts,
    Complete,
    Rollback
}

public enum ExecutionStageStatus
{
    Pending,
    Running,
    WaitingApproval,
    Completed,
    Failed,
    Skipped,
    Cancelled
}

public enum ExecutionCheckpointKind
{
    SafetyValidation,
    PatchPreview,
    UserApproval,
    Verification,
    Rollback
}

public enum ExecutionArtifactKind
{
    PatchPreview,
    DiffPreview,
    ValidationReport,
    BuildOutput,
    TestOutput,
    DiagnosticReport,
    RollbackSnapshot,
    ApprovalDecision,
    ExecutionLog
}

public enum ExecutionTimelineEventType
{
    Created,
    Started,
    StageStarted,
    ValidationPassed,
    ValidationFailed,
    ApprovalGranted,
    PatchApplied,
    VerificationPassed,
    VerificationFailed,
    RollbackPrepared,
    RollbackTriggered,
    RollbackCompleted,
    Completed,
    Failed,
    Cancelled
}

public sealed record ExecutionPipeline(
    string PipelineId,
    string WorkflowId,
    string? WorkflowTaskId,
    ExecutionPipelineStatus Status,
    string Objective,
    string? RepositoryId,
    string? RepositoryPath,
    bool DryRun,
    bool ValidationOnly,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? StartedUtc = null,
    DateTime? CompletedUtc = null,
    string? ActiveStageId = null,
    string? FailureReason = null);

public sealed record ExecutionStage(
    string StageId,
    string PipelineId,
    ExecutionStageKind Kind,
    ExecutionStageStatus Status,
    int Sequence,
    string Title,
    string Description,
    bool RequiresApproval,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? StartedUtc = null,
    DateTime? CompletedUtc = null,
    string? FailureReason = null,
    string? Metadata = null);

public sealed record ExecutionCheckpoint(
    string CheckpointId,
    string PipelineId,
    string? StageId,
    ExecutionCheckpointKind Kind,
    bool IsSatisfied,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string Description,
    string? Metadata = null);

public sealed record ExecutionArtifact(
    string ArtifactId,
    string PipelineId,
    ExecutionArtifactKind Kind,
    string Name,
    string Content,
    DateTime CreatedUtc,
    string? StageId = null,
    string? Metadata = null);

public sealed record ExecutionFailure(
    string FailureId,
    string PipelineId,
    string? StageId,
    string Message,
    string? RawOutput,
    ExecutionEvent? ParsedEvent,
    DateTime CreatedUtc,
    string? Metadata = null);

public sealed record ExecutionValidationResult(
    string ValidationId,
    string PipelineId,
    bool IsValid,
    IReadOnlyList<string> Messages,
    IReadOnlyList<NormalizedDiagnostic> Diagnostics,
    DateTime CreatedUtc,
    string? StageId = null,
    string? Metadata = null);

public sealed record ExecutionRollbackSnapshot(
    string SnapshotId,
    string PipelineId,
    string RepositoryPath,
    IReadOnlyList<string> TargetPaths,
    DateTime CreatedUtc,
    string? Metadata = null);

public sealed record ExecutionTimelineEvent(
    string EventId,
    string PipelineId,
    ExecutionTimelineEventType EventType,
    DateTime TimestampUtc,
    string Description,
    string? StageId = null,
    string? Metadata = null);

public sealed record ExecutionPipelineState(
    ExecutionPipeline Pipeline,
    IReadOnlyList<ExecutionStage> Stages,
    IReadOnlyList<ExecutionCheckpoint> Checkpoints,
    IReadOnlyList<ExecutionArtifact> Artifacts,
    IReadOnlyList<ExecutionFailure> Failures,
    IReadOnlyList<ExecutionValidationResult> Validations,
    IReadOnlyList<ExecutionRollbackSnapshot> RollbackSnapshots,
    IReadOnlyList<ExecutionTimelineEvent> Timeline);

public sealed record StartExecutionPipelineRequest(
    string WorkflowId,
    string? WorkflowTaskId,
    string Objective,
    string? RepositoryId = null,
    string? RepositoryPath = null,
    bool DryRun = true,
    bool ValidationOnly = false);

public sealed record ApproveExecutionPipelineRequest(
    string PipelineId,
    string ApprovedBy,
    string? Notes = null);

public sealed record ApplyExecutionPipelineRequest(
    string PipelineId,
    string? ArtifactContent = null,
    string? Metadata = null);

public sealed record CompleteExecutionValidationRequest(
    string PipelineId,
    bool IsValid,
    IReadOnlyList<string>? Messages = null,
    IReadOnlyList<NormalizedDiagnostic>? Diagnostics = null,
    string? RawOutput = null,
    string? Metadata = null);

public interface IExecutionPipelineStore
{
    Task SaveAsync(ExecutionPipelineState state, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState?> GetAsync(string pipelineId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionPipeline>> ListAsync(string? workflowId = null, CancellationToken cancellationToken = default);
    Task UpdatePipelineAsync(ExecutionPipeline pipeline, CancellationToken cancellationToken = default);
    Task UpdateStageAsync(ExecutionStage stage, CancellationToken cancellationToken = default);
    Task AddCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task AddArtifactAsync(ExecutionArtifact artifact, CancellationToken cancellationToken = default);
    Task AddFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default);
    Task AddValidationAsync(ExecutionValidationResult validation, CancellationToken cancellationToken = default);
    Task AddRollbackSnapshotAsync(ExecutionRollbackSnapshot snapshot, CancellationToken cancellationToken = default);
    Task AddTimelineEventAsync(ExecutionTimelineEvent ev, CancellationToken cancellationToken = default);
}

public interface IExecutionPipelineOrchestrator
{
    Task<ExecutionPipelineState> StartAsync(StartExecutionPipelineRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState?> GetAsync(string pipelineId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionPipeline>> ListAsync(string? workflowId = null, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> ApproveAsync(ApproveExecutionPipelineRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> CompleteValidationAsync(CompleteExecutionValidationRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> MarkAppliedAsync(ApplyExecutionPipelineRequest request, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> PrepareRollbackAsync(string pipelineId, string repositoryPath, IReadOnlyList<string> targetPaths, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> TriggerRollbackAsync(string pipelineId, string? reason = null, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> MarkRollbackCompletedAsync(string pipelineId, string? metadata = null, CancellationToken cancellationToken = default);
    Task<ExecutionPipelineState> CancelAsync(string pipelineId, string? reason = null, CancellationToken cancellationToken = default);
}
