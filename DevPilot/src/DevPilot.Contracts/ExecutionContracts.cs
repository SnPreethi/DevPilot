using System.Collections.Generic;

namespace DevPilot.Contracts;

public enum ExecutionEventType
{
    BuildFailure,
    TestFailure,
    RuntimeException
}

public sealed record ExecutionEvent(
    ExecutionEventType Type,
    string Message,
    string RawOutput,
    string? TargetFilePath = null,
    int? TargetLine = null,
    string? StackTrace = null,
    string? ProjectName = null);

public sealed record AnalyzeExecutionEventRequest(
    ExecutionEvent Event,
    string? RepositoryId = null,
    string? RepositoryPath = null);
