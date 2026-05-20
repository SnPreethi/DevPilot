using DevPilot.Contracts;

namespace DevPilot.Core.Workflow;

public static class WorkflowTaskStateMachine
{
    public static bool CanTransition(
        WorkflowTaskStatus current,
        WorkflowTaskStatus target,
        bool approvalGranted = false)
    {
        if (current == target)
        {
            return true;
        }

        if (current is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Failed or WorkflowTaskStatus.Cancelled)
        {
            return false;
        }

        return current switch
        {
            WorkflowTaskStatus.Pending => target is WorkflowTaskStatus.Ready or WorkflowTaskStatus.Blocked or WorkflowTaskStatus.Cancelled,
            WorkflowTaskStatus.Blocked => target is WorkflowTaskStatus.Ready or WorkflowTaskStatus.Cancelled,
            WorkflowTaskStatus.Ready => target is WorkflowTaskStatus.InProgress or WorkflowTaskStatus.WaitingApproval or WorkflowTaskStatus.Cancelled,
            WorkflowTaskStatus.WaitingApproval => target is WorkflowTaskStatus.InProgress && approvalGranted
                || target is WorkflowTaskStatus.Cancelled or WorkflowTaskStatus.Failed,
            WorkflowTaskStatus.InProgress => target is WorkflowTaskStatus.Completed or WorkflowTaskStatus.Failed or WorkflowTaskStatus.WaitingApproval or WorkflowTaskStatus.Cancelled,
            _ => false
        };
    }

    public static void ValidateTransition(
        WorkflowTask task,
        WorkflowTaskStatus target,
        bool approvalGranted = false)
    {
        if (!CanTransition(task.Status, target, approvalGranted))
        {
            throw new InvalidOperationException(
                $"Invalid workflow task transition for '{task.TaskId}': {task.Status} -> {target}.");
        }

        if (task.RequiresApproval &&
            task.Status == WorkflowTaskStatus.WaitingApproval &&
            target == WorkflowTaskStatus.InProgress &&
            !approvalGranted)
        {
            throw new InvalidOperationException(
                $"Task '{task.TaskId}' requires approval before it can start.");
        }
    }
}
