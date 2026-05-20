using System.Security.Cryptography;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.Core.Execution;

public sealed class ExecutionPipelineOrchestrator : IExecutionPipelineOrchestrator
{
    private readonly IExecutionPipelineStore _store;
    private readonly ITaskGraphOrchestrator _taskGraphOrchestrator;
    private readonly TerminalOrchestrator _terminalOrchestrator;

    public ExecutionPipelineOrchestrator(
        IExecutionPipelineStore store,
        ITaskGraphOrchestrator taskGraphOrchestrator,
        TerminalOrchestrator terminalOrchestrator)
    {
        _store = store;
        _taskGraphOrchestrator = taskGraphOrchestrator;
        _terminalOrchestrator = terminalOrchestrator;
    }

    public async Task<ExecutionPipelineState> StartAsync(
        StartExecutionPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var workflow = await _taskGraphOrchestrator.GetAsync(request.WorkflowId, cancellationToken).ConfigureAwait(false);
        if (workflow == null)
        {
            throw new InvalidOperationException($"Workflow '{request.WorkflowId}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.WorkflowTaskId) &&
            !workflow.Tasks.Any(task => task.TaskId == request.WorkflowTaskId))
        {
            throw new InvalidOperationException($"Workflow task '{request.WorkflowTaskId}' was not found.");
        }

        var now = DateTime.UtcNow;
        var pipelineId = CreateStableId($"execution:{request.WorkflowId}:{request.WorkflowTaskId}:{request.Objective}");
        var pipeline = new ExecutionPipeline(
            PipelineId: pipelineId,
            WorkflowId: request.WorkflowId,
            WorkflowTaskId: request.WorkflowTaskId,
            Status: ExecutionPipelineStatus.Running,
            Objective: request.Objective.Trim(),
            RepositoryId: request.RepositoryId ?? workflow.Instance.RepositoryId,
            RepositoryPath: request.RepositoryPath ?? workflow.Instance.RepositoryPath,
            DryRun: request.DryRun,
            ValidationOnly: request.ValidationOnly,
            CreatedUtc: now,
            UpdatedUtc: now,
            StartedUtc: now);

        var stages = BuildStages(pipelineId, request.DryRun, request.ValidationOnly, now);
        var activeStage = stages.OrderBy(stage => stage.Sequence).First();
        stages[0] = activeStage with
        {
            Status = ExecutionStageStatus.Running,
            StartedUtc = now,
            UpdatedUtc = now
        };
        pipeline = pipeline with { ActiveStageId = stages[0].StageId };

        var checkpoints = new List<ExecutionCheckpoint>
        {
            CreateCheckpoint(pipelineId, stages[0].StageId, ExecutionCheckpointKind.Rollback, "Rollback snapshot must be prepared before applying changes.", now),
            CreateCheckpoint(pipelineId, stages[1].StageId, ExecutionCheckpointKind.SafetyValidation, "Workflow and repository safety validation must pass.", now),
            CreateCheckpoint(pipelineId, stages[2].StageId, ExecutionCheckpointKind.PatchPreview, "Patch preview must be valid before approval.", now),
            CreateCheckpoint(pipelineId, stages[4].StageId, ExecutionCheckpointKind.UserApproval, "User approval is required before any apply stage.", now)
        };

        var timeline = new[]
        {
            CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.Created, "Execution pipeline created.", now),
            CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.Started, "Execution pipeline started in supervised coordination mode.", now),
            CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.StageStarted, $"Stage started: {stages[0].Title}.", now, stages[0].StageId)
        };

        var state = new ExecutionPipelineState(
            pipeline,
            stages,
            checkpoints,
            Array.Empty<ExecutionArtifact>(),
            Array.Empty<ExecutionFailure>(),
            Array.Empty<ExecutionValidationResult>(),
            Array.Empty<ExecutionRollbackSnapshot>(),
            timeline);

        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public Task<ExecutionPipelineState?> GetAsync(string pipelineId, CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(pipelineId, cancellationToken);
    }

    public Task<IReadOnlyList<ExecutionPipeline>> ListAsync(string? workflowId = null, CancellationToken cancellationToken = default)
    {
        return _store.ListAsync(workflowId, cancellationToken);
    }

    public async Task<ExecutionPipelineState> ApproveAsync(
        ApproveExecutionPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(request.PipelineId, cancellationToken).ConfigureAwait(false);
        if (state.Pipeline.Status != ExecutionPipelineStatus.WaitingApproval)
        {
            throw new InvalidOperationException($"Pipeline '{request.PipelineId}' is not waiting for approval.");
        }

        ExecutionPipelineStateMachine.ValidateTransition(state.Pipeline, ExecutionPipelineStatus.Applying, approvalGranted: true);
        var now = DateTime.UtcNow;
        var pipeline = state.Pipeline with
        {
            Status = ExecutionPipelineStatus.Applying,
            UpdatedUtc = now
        };
        var approvalStage = state.Stages.FirstOrDefault(stage => stage.Kind == ExecutionStageKind.AwaitApproval);
        var updatedStages = state.Stages.Select(stage => stage.StageId == approvalStage?.StageId
            ? stage with { Status = ExecutionStageStatus.Completed, CompletedUtc = now, UpdatedUtc = now }
            : stage).ToList();
        updatedStages = StartNextStage(updatedStages, ExecutionStageKind.ApplyPatch, now, out var activeStageId);
        pipeline = pipeline with { ActiveStageId = activeStageId };

        var approvalArtifact = CreateArtifact(
            pipeline.PipelineId,
            ExecutionArtifactKind.ApprovalDecision,
            "approval-decision.json",
            $"{{\"approvedBy\":\"{EscapeJson(request.ApprovedBy)}\",\"notes\":\"{EscapeJson(request.Notes ?? string.Empty)}\"}}",
            now,
            approvalStage?.StageId);
        var checkpoint = state.Checkpoints.First(cp => cp.Kind == ExecutionCheckpointKind.UserApproval) with
        {
            IsSatisfied = true,
            UpdatedUtc = now,
            Metadata = request.Notes
        };
        var ev = CreateTimelineEvent(pipeline.PipelineId, ExecutionTimelineEventType.ApprovalGranted, "Execution approval granted.", now, approvalStage?.StageId, request.Notes);

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in updatedStages)
        {
            var original = state.Stages.First(s => s.StageId == stage.StageId);
            if (!Equals(original, stage))
            {
                await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
            }
        }

        await _store.AddArtifactAsync(approvalArtifact, cancellationToken).ConfigureAwait(false);
        await _store.AddCheckpointAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = updatedStages,
            Checkpoints = state.Checkpoints.Select(cp => cp.CheckpointId == checkpoint.CheckpointId ? checkpoint : cp).ToList(),
            Artifacts = state.Artifacts.Concat(new[] { approvalArtifact }).ToList(),
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> CompleteValidationAsync(
        CompleteExecutionValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(request.PipelineId, cancellationToken).ConfigureAwait(false);
        if (state.Pipeline.Status is ExecutionPipelineStatus.Completed or ExecutionPipelineStatus.Cancelled or ExecutionPipelineStatus.Failed)
        {
            throw new InvalidOperationException($"Pipeline '{request.PipelineId}' is terminal.");
        }

        var now = DateTime.UtcNow;
        var activeStage = state.Stages.FirstOrDefault(stage => stage.StageId == state.Pipeline.ActiveStageId)
            ?? state.Stages.OrderBy(stage => stage.Sequence).First(stage => stage.Status == ExecutionStageStatus.Running);
        var validation = new ExecutionValidationResult(
            ValidationId: CreateStableId($"{request.PipelineId}:validation:{activeStage.StageId}:{now:O}"),
            PipelineId: request.PipelineId,
            IsValid: request.IsValid,
            Messages: request.Messages ?? Array.Empty<string>(),
            Diagnostics: request.Diagnostics ?? Array.Empty<NormalizedDiagnostic>(),
            CreatedUtc: now,
            StageId: activeStage.StageId,
            Metadata: request.Metadata);

        var artifacts = new List<ExecutionArtifact>();
        if (!string.IsNullOrWhiteSpace(request.RawOutput))
        {
            artifacts.Add(CreateArtifact(
                request.PipelineId,
                ExecutionArtifactKind.ExecutionLog,
                "validation-output.log",
                request.RawOutput,
                now,
                activeStage.StageId));
        }

        var failures = new List<ExecutionFailure>();
        ExecutionPipeline pipeline;
        List<ExecutionStage> stages;
        ExecutionTimelineEvent ev;

        if (!request.IsValid)
        {
            var parsed = string.IsNullOrWhiteSpace(request.RawOutput)
                ? null
                : _terminalOrchestrator.ParseTerminalOutput(request.RawOutput);
            var failure = new ExecutionFailure(
                FailureId: CreateStableId($"{request.PipelineId}:failure:{activeStage.StageId}:{now:O}"),
                PipelineId: request.PipelineId,
                StageId: activeStage.StageId,
                Message: request.Messages?.FirstOrDefault() ?? "Execution validation failed.",
                RawOutput: request.RawOutput,
                ParsedEvent: parsed,
                CreatedUtc: now,
                Metadata: request.Metadata);
            failures.Add(failure);
            pipeline = state.Pipeline with
            {
                Status = ExecutionPipelineStatus.Failed,
                UpdatedUtc = now,
                CompletedUtc = now,
                FailureReason = failure.Message
            };
            stages = state.Stages.Select(stage => stage.StageId == activeStage.StageId
                ? stage with { Status = ExecutionStageStatus.Failed, FailureReason = failure.Message, CompletedUtc = now, UpdatedUtc = now }
                : stage).ToList();
            ev = CreateTimelineEvent(request.PipelineId, ExecutionTimelineEventType.ValidationFailed, failure.Message, now, activeStage.StageId, request.Metadata);
        }
        else
        {
            var next = NextAfterValidation(activeStage.Kind, state.Pipeline);
            pipeline = state.Pipeline with
            {
                Status = next.pipelineStatus,
                UpdatedUtc = now
            };
            stages = state.Stages.Select(stage => stage.StageId == activeStage.StageId
                ? stage with { Status = ExecutionStageStatus.Completed, CompletedUtc = now, UpdatedUtc = now }
                : stage).ToList();
            stages = StartNextStage(stages, next.nextStage, now, out var activeStageId);
            pipeline = pipeline with { ActiveStageId = activeStageId };
            ev = CreateTimelineEvent(request.PipelineId, ExecutionTimelineEventType.ValidationPassed, "Execution validation passed.", now, activeStage.StageId, request.Metadata);
        }

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in stages)
        {
            var original = state.Stages.First(s => s.StageId == stage.StageId);
            if (!Equals(original, stage))
            {
                await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
            }
        }

        await _store.AddValidationAsync(validation, cancellationToken).ConfigureAwait(false);
        foreach (var artifact in artifacts)
        {
            await _store.AddArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
        }

        foreach (var failure in failures)
        {
            await _store.AddFailureAsync(failure, cancellationToken).ConfigureAwait(false);
        }

        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = stages,
            Artifacts = state.Artifacts.Concat(artifacts).ToList(),
            Failures = state.Failures.Concat(failures).ToList(),
            Validations = state.Validations.Concat(new[] { validation }).ToList(),
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> MarkAppliedAsync(
        ApplyExecutionPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(request.PipelineId, cancellationToken).ConfigureAwait(false);
        if (state.Pipeline.Status != ExecutionPipelineStatus.Applying)
        {
            throw new InvalidOperationException($"Pipeline '{request.PipelineId}' is not in apply state.");
        }

        if (state.Pipeline.DryRun || state.Pipeline.ValidationOnly)
        {
            throw new InvalidOperationException("Dry-run and validation-only pipelines cannot apply repository changes.");
        }

        var approval = state.Checkpoints.FirstOrDefault(cp => cp.Kind == ExecutionCheckpointKind.UserApproval);
        if (approval == null || !approval.IsSatisfied)
        {
            throw new InvalidOperationException("Pipeline cannot apply without a satisfied approval checkpoint.");
        }

        var now = DateTime.UtcNow;
        var activeStage = state.Stages.FirstOrDefault(stage => stage.Kind == ExecutionStageKind.ApplyPatch);
        var pipeline = state.Pipeline with
        {
            Status = ExecutionPipelineStatus.Validating,
            UpdatedUtc = now
        };
        var stages = state.Stages.Select(stage => stage.StageId == activeStage?.StageId
            ? stage with { Status = ExecutionStageStatus.Completed, CompletedUtc = now, UpdatedUtc = now }
            : stage).ToList();
        stages = StartNextStage(stages, ExecutionStageKind.RunVerification, now, out var activeStageId);
        pipeline = pipeline with { ActiveStageId = activeStageId };
        var artifact = CreateArtifact(
            request.PipelineId,
            ExecutionArtifactKind.ExecutionLog,
            "apply-result.json",
            request.ArtifactContent ?? "{}",
            now,
            activeStage?.StageId,
            request.Metadata);
        var ev = CreateTimelineEvent(request.PipelineId, ExecutionTimelineEventType.PatchApplied, "Approved patch application recorded.", now, activeStage?.StageId, request.Metadata);

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in stages)
        {
            var original = state.Stages.First(s => s.StageId == stage.StageId);
            if (!Equals(original, stage))
            {
                await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
            }
        }

        await _store.AddArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = stages,
            Artifacts = state.Artifacts.Concat(new[] { artifact }).ToList(),
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> PrepareRollbackAsync(
        string pipelineId,
        string repositoryPath,
        IReadOnlyList<string> targetPaths,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var snapshot = new ExecutionRollbackSnapshot(
            SnapshotId: CreateStableId($"{pipelineId}:rollback:{repositoryPath}:{string.Join("|", targetPaths)}"),
            PipelineId: pipelineId,
            RepositoryPath: repositoryPath,
            TargetPaths: targetPaths,
            CreatedUtc: now);
        var artifact = CreateArtifact(
            pipelineId,
            ExecutionArtifactKind.RollbackSnapshot,
            "rollback-snapshot.json",
            $"{{\"repositoryPath\":\"{EscapeJson(repositoryPath)}\",\"targetPaths\":[{string.Join(",", targetPaths.Select(path => $"\"{EscapeJson(path)}\""))}]}}",
            now);
        var checkpoint = state.Checkpoints.First(cp => cp.Kind == ExecutionCheckpointKind.Rollback) with
        {
            IsSatisfied = true,
            UpdatedUtc = now,
            Metadata = artifact.ArtifactId
        };
        var ev = CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.RollbackPrepared, "Rollback snapshot prepared.", now, Metadata: artifact.ArtifactId);

        await _store.AddRollbackSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _store.AddArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
        await _store.AddCheckpointAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Checkpoints = state.Checkpoints.Select(cp => cp.CheckpointId == checkpoint.CheckpointId ? checkpoint : cp).ToList(),
            Artifacts = state.Artifacts.Concat(new[] { artifact }).ToList(),
            RollbackSnapshots = state.RollbackSnapshots.Concat(new[] { snapshot }).ToList(),
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> MarkRollbackCompletedAsync(
        string pipelineId,
        string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        ExecutionPipelineStateMachine.ValidateTransition(state.Pipeline, ExecutionPipelineStatus.RollingBack);
        var now = DateTime.UtcNow;
        var pipeline = state.Pipeline with
        {
            Status = ExecutionPipelineStatus.Completed,
            UpdatedUtc = now,
            CompletedUtc = now,
            ActiveStageId = null
        };
        var stages = state.Stages.Select(stage => stage.Kind == ExecutionStageKind.Rollback
            ? stage with { Status = ExecutionStageStatus.Completed, StartedUtc = stage.StartedUtc ?? now, CompletedUtc = now, UpdatedUtc = now }
            : stage).ToList();
        var ev = CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.RollbackCompleted, "Rollback completed.", now, Metadata: metadata);

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in stages.Where(stage => stage.Kind == ExecutionStageKind.Rollback))
        {
            await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
        }
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = stages,
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> TriggerRollbackAsync(
        string pipelineId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        if (state.RollbackSnapshots.Count == 0)
        {
            throw new InvalidOperationException("Rollback cannot start before a rollback snapshot is prepared.");
        }

        ExecutionPipelineStateMachine.ValidateTransition(state.Pipeline, ExecutionPipelineStatus.RollingBack);
        var now = DateTime.UtcNow;
        var pipeline = state.Pipeline with
        {
            Status = ExecutionPipelineStatus.RollingBack,
            UpdatedUtc = now
        };
        var stages = StartNextStage(state.Stages.ToList(), ExecutionStageKind.Rollback, now, out var activeStageId);
        pipeline = pipeline with { ActiveStageId = activeStageId };
        var ev = CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.RollbackTriggered, reason ?? "Rollback triggered.", now, activeStageId);

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in stages)
        {
            var original = state.Stages.First(s => s.StageId == stage.StageId);
            if (!Equals(original, stage))
            {
                await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
            }
        }
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = stages,
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    public async Task<ExecutionPipelineState> CancelAsync(
        string pipelineId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadRequiredAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        ExecutionPipelineStateMachine.ValidateTransition(state.Pipeline, ExecutionPipelineStatus.Cancelled);
        var now = DateTime.UtcNow;
        var pipeline = state.Pipeline with
        {
            Status = ExecutionPipelineStatus.Cancelled,
            UpdatedUtc = now,
            CompletedUtc = now,
            ActiveStageId = null,
            FailureReason = reason
        };
        var stages = state.Stages.Select(stage => stage.Status is ExecutionStageStatus.Completed or ExecutionStageStatus.Failed
            ? stage
            : stage with { Status = ExecutionStageStatus.Cancelled, UpdatedUtc = now, CompletedUtc = now }).ToList();
        var ev = CreateTimelineEvent(pipelineId, ExecutionTimelineEventType.Cancelled, reason ?? "Execution pipeline cancelled.", now);

        await _store.UpdatePipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
        foreach (var stage in stages)
        {
            var original = state.Stages.First(s => s.StageId == stage.StageId);
            if (!Equals(original, stage))
            {
                await _store.UpdateStageAsync(stage, cancellationToken).ConfigureAwait(false);
            }
        }
        await _store.AddTimelineEventAsync(ev, cancellationToken).ConfigureAwait(false);

        return state with
        {
            Pipeline = pipeline,
            Stages = stages,
            Timeline = state.Timeline.Concat(new[] { ev }).ToList()
        };
    }

    private async Task<ExecutionPipelineState> LoadRequiredAsync(string pipelineId, CancellationToken cancellationToken)
    {
        var state = await _store.GetAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        return state ?? throw new InvalidOperationException($"Execution pipeline '{pipelineId}' was not found.");
    }

    private static List<ExecutionStage> BuildStages(
        string pipelineId,
        bool dryRun,
        bool validationOnly,
        DateTime now)
    {
        var stageDefinitions = new List<(ExecutionStageKind kind, string title, string description, bool approval)>
        {
            (ExecutionStageKind.PrepareRollback, "Prepare rollback snapshot", "Capture rollback metadata before any possible workspace change.", false),
            (ExecutionStageKind.ValidateWorkflow, "Validate workflow state", "Confirm workflow/task association and repository safety.", false),
            (ExecutionStageKind.ValidatePatchPreview, "Validate patch preview", "Validate structured patch preview and diff artifacts without writing files.", false),
            (ExecutionStageKind.ValidateDiagnostics, "Validate diagnostics", "Parse validation/build/test output and diagnostics before apply.", false),
            (ExecutionStageKind.AwaitApproval, "Await user approval", "Pause for explicit user approval before applying any workspace edit.", true),
            (ExecutionStageKind.ApplyPatch, "Apply approved patch", dryRun || validationOnly ? "Skipped for dry-run or validation-only execution." : "Apply approved patch through the workspace editing engine.", false),
            (ExecutionStageKind.RunVerification, "Run verification", "Coordinate build/test/lint/runtime diagnostic result capture.", false),
            (ExecutionStageKind.CaptureArtifacts, "Capture execution artifacts", "Persist validation reports, patch outputs, rollback snapshots, and logs.", false),
            (ExecutionStageKind.Complete, "Complete pipeline", "Finalize execution timeline and status.", false),
            (ExecutionStageKind.Rollback, "Rollback", "Coordinate deterministic rollback if requested.", false)
        };

        return stageDefinitions.Select((stage, index) => new ExecutionStage(
            StageId: CreateStableId($"{pipelineId}:{index + 1}:{stage.kind}")[..24],
            PipelineId: pipelineId,
            Kind: stage.kind,
            Status: ExecutionStageStatus.Pending,
            Sequence: index + 1,
            Title: stage.title,
            Description: stage.description,
            RequiresApproval: stage.approval,
            CreatedUtc: now,
            UpdatedUtc: now)).ToList();
    }

    private static (ExecutionPipelineStatus pipelineStatus, ExecutionStageKind nextStage) NextAfterValidation(
        ExecutionStageKind current,
        ExecutionPipeline pipeline)
    {
        return current switch
        {
            ExecutionStageKind.PrepareRollback => (ExecutionPipelineStatus.Validating, ExecutionStageKind.ValidateWorkflow),
            ExecutionStageKind.ValidateWorkflow => (ExecutionPipelineStatus.Validating, ExecutionStageKind.ValidatePatchPreview),
            ExecutionStageKind.ValidatePatchPreview => (ExecutionPipelineStatus.Validating, ExecutionStageKind.ValidateDiagnostics),
            ExecutionStageKind.ValidateDiagnostics => (ExecutionPipelineStatus.WaitingApproval, ExecutionStageKind.AwaitApproval),
            ExecutionStageKind.RunVerification => (ExecutionPipelineStatus.Completed, ExecutionStageKind.CaptureArtifacts),
            ExecutionStageKind.CaptureArtifacts => (ExecutionPipelineStatus.Completed, ExecutionStageKind.Complete),
            _ => pipeline.ValidationOnly || pipeline.DryRun
                ? (ExecutionPipelineStatus.Completed, ExecutionStageKind.Complete)
                : (ExecutionPipelineStatus.WaitingApproval, ExecutionStageKind.AwaitApproval)
        };
    }

    private static List<ExecutionStage> StartNextStage(
        List<ExecutionStage> stages,
        ExecutionStageKind nextStage,
        DateTime now,
        out string? activeStageId)
    {
        activeStageId = null;
        var index = stages.FindIndex(stage => stage.Kind == nextStage);
        if (index < 0)
        {
            return stages;
        }

        var next = stages[index];
        if (next.Status is ExecutionStageStatus.Completed or ExecutionStageStatus.Skipped)
        {
            return stages;
        }

        var status = next.RequiresApproval ? ExecutionStageStatus.WaitingApproval : ExecutionStageStatus.Running;
        stages[index] = next with
        {
            Status = status,
            StartedUtc = next.StartedUtc ?? now,
            UpdatedUtc = now
        };
        activeStageId = stages[index].StageId;
        return stages;
    }

    private static ExecutionCheckpoint CreateCheckpoint(
        string pipelineId,
        string? stageId,
        ExecutionCheckpointKind kind,
        string description,
        DateTime now)
    {
        return new ExecutionCheckpoint(
            CheckpointId: CreateStableId($"{pipelineId}:checkpoint:{kind}"),
            PipelineId: pipelineId,
            StageId: stageId,
            Kind: kind,
            IsSatisfied: false,
            CreatedUtc: now,
            UpdatedUtc: now,
            Description: description);
    }

    private static ExecutionArtifact CreateArtifact(
        string pipelineId,
        ExecutionArtifactKind kind,
        string name,
        string content,
        DateTime now,
        string? stageId = null,
        string? metadata = null)
    {
        return new ExecutionArtifact(
            ArtifactId: CreateStableId($"{pipelineId}:artifact:{kind}:{name}:{now:O}"),
            PipelineId: pipelineId,
            Kind: kind,
            Name: name,
            Content: content,
            CreatedUtc: now,
            StageId: stageId,
            Metadata: metadata);
    }

    private static ExecutionTimelineEvent CreateTimelineEvent(
        string pipelineId,
        ExecutionTimelineEventType eventType,
        string description,
        DateTime now,
        string? stageId = null,
        string? Metadata = null)
    {
        return new ExecutionTimelineEvent(
            EventId: CreateStableId($"{pipelineId}:event:{eventType}:{stageId}:{description}:{now:O}"),
            PipelineId: pipelineId,
            EventType: eventType,
            TimestampUtc: now,
            Description: description,
            StageId: stageId,
            Metadata: Metadata);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string CreateStableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
