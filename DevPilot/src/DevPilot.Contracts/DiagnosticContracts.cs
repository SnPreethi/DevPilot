using System.Collections.Generic;

namespace DevPilot.Contracts;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information,
    Hint
}

public sealed record NormalizedDiagnostic(
    string FilePath,
    int Line,
    int Column,
    DiagnosticSeverity Severity,
    string Message,
    string Code,
    string Source);

public sealed record FixDiagnosticRequest(
    string FilePath,
    NormalizedDiagnostic Diagnostic,
    string SurroundingCode,
    string? RepositoryId = null,
    string? RepositoryPath = null);

public sealed record StackFrameInfo(
    string FilePath,
    int Line,
    string MethodName);

public sealed record TestFailureAnalysisRequest(
    string TestName,
    string ErrorMessage,
    string StackTrace,
    string? TargetMethodName = null,
    string? RepositoryId = null,
    string? RepositoryPath = null);
