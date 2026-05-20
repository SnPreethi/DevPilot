using DevPilot.Contracts;

namespace DevPilot.Core.Execution;

public static class ExecutionPipelineStateMachine
{
    public static bool CanTransition(
        ExecutionPipelineStatus current,
        ExecutionPipelineStatus target,
        bool approvalGranted = false)
    {
        if (current == target)
        {
            return true;
        }

        if (current is ExecutionPipelineStatus.Completed or ExecutionPipelineStatus.Failed or ExecutionPipelineStatus.Cancelled)
        {
            return false;
        }

        return current switch
        {
            ExecutionPipelineStatus.Pending => target is ExecutionPipelineStatus.Running or ExecutionPipelineStatus.Cancelled,
            ExecutionPipelineStatus.Running => target is ExecutionPipelineStatus.Validating or ExecutionPipelineStatus.WaitingApproval or ExecutionPipelineStatus.RollingBack or ExecutionPipelineStatus.Failed or ExecutionPipelineStatus.Cancelled,
            ExecutionPipelineStatus.Validating => target is ExecutionPipelineStatus.WaitingApproval or ExecutionPipelineStatus.RollingBack or ExecutionPipelineStatus.Failed or ExecutionPipelineStatus.Completed or ExecutionPipelineStatus.Cancelled,
            ExecutionPipelineStatus.WaitingApproval => target is ExecutionPipelineStatus.Applying && approvalGranted
                || target is ExecutionPipelineStatus.RollingBack or ExecutionPipelineStatus.Cancelled or ExecutionPipelineStatus.Failed,
            ExecutionPipelineStatus.Applying => target is ExecutionPipelineStatus.Validating or ExecutionPipelineStatus.Completed or ExecutionPipelineStatus.RollingBack or ExecutionPipelineStatus.Failed,
            ExecutionPipelineStatus.RollingBack => target is ExecutionPipelineStatus.Completed or ExecutionPipelineStatus.Failed,
            _ => false
        };
    }

    public static void ValidateTransition(
        ExecutionPipeline pipeline,
        ExecutionPipelineStatus target,
        bool approvalGranted = false)
    {
        if (!CanTransition(pipeline.Status, target, approvalGranted))
        {
            throw new InvalidOperationException(
                $"Invalid execution pipeline transition for '{pipeline.PipelineId}': {pipeline.Status} -> {target}.");
        }

        if (pipeline.Status == ExecutionPipelineStatus.WaitingApproval &&
            target == ExecutionPipelineStatus.Applying &&
            !approvalGranted)
        {
            throw new InvalidOperationException("Execution pipeline requires approval before applying changes.");
        }
    }
}
