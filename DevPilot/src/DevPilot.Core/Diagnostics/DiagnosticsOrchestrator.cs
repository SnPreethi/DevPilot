using DevPilot.Contracts;

namespace DevPilot.Core.Diagnostics;

public sealed class DiagnosticsOrchestrator
{
    public NormalizedDiagnostic NormalizeRoslynDiagnostic(
        string filePath,
        string severity,
        int line,
        int column,
        string message,
        string code)
    {
        var normalizedSeverity = severity.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" or "information" => DiagnosticSeverity.Information,
            _ => DiagnosticSeverity.Hint
        };

        return new NormalizedDiagnostic(
            FilePath: filePath,
            Line: line,
            Column: column,
            Severity: normalizedSeverity,
            Message: message,
            Code: code,
            Source: "Roslyn"
        );
    }

    public NormalizedDiagnostic NormalizeTypeScriptDiagnostic(
        string filePath,
        string category,
        int line,
        int column,
        string message,
        int code)
    {
        var normalizedSeverity = category.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "suggestion" or "info" => DiagnosticSeverity.Information,
            _ => DiagnosticSeverity.Hint
        };

        return new NormalizedDiagnostic(
            FilePath: filePath,
            Line: line,
            Column: column,
            Severity: normalizedSeverity,
            Message: message,
            Code: $"TS{code}",
            Source: "TypeScript"
        );
    }

    public string ClassifyIssue(NormalizedDiagnostic diagnostic)
    {
        var msg = diagnostic.Message.ToLowerInvariant();
        if (msg.Contains("not found") || msg.Contains("does not exist") || msg.Contains("could not be found"))
            return "MissingSymbol";
        if (msg.Contains("cannot convert") || msg.Contains("cannot implicitly convert"))
            return "TypeMismatch";
        if (msg.Contains("null reference") || msg.Contains("nullable") || msg.Contains("dereference"))
            return "NullSafety";
        if (msg.Contains("does not implement") || msg.Contains("interface member"))
            return "InterfaceImplementation";
        if (msg.Contains("cannot await") || msg.Contains("should be async"))
            return "AsyncAwaitMismatch";
        if (msg.Contains("unused") || msg.Contains("is declared but never used"))
            return "UnusedSymbol";

        return "GeneralCompilerError";
    }
}
