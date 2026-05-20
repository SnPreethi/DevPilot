using DevPilot.Contracts;
using DevPilot.Core.Diagnostics;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void NormalizeRoslynDiagnostic_NormalizesFieldsCorrectly()
    {
        var orchestrator = new DiagnosticsOrchestrator();
        var diagnostic = orchestrator.NormalizeRoslynDiagnostic(
            filePath: "C:\\src\\Program.cs",
            severity: "Error",
            line: 12,
            column: 5,
            message: "The type or namespace name 'SpecialClass' could not be found",
            code: "CS0246"
        );

        Assert.Equal("C:\\src\\Program.cs", diagnostic.FilePath);
        Assert.Equal(12, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("The type or namespace name 'SpecialClass' could not be found", diagnostic.Message);
        Assert.Equal("CS0246", diagnostic.Code);
        Assert.Equal("Roslyn", diagnostic.Source);
    }

    [Fact]
    public void NormalizeTypeScriptDiagnostic_NormalizesFieldsCorrectly()
    {
        var orchestrator = new DiagnosticsOrchestrator();
        var diagnostic = orchestrator.NormalizeTypeScriptDiagnostic(
            filePath: "/src/index.ts",
            category: "warning",
            line: 45,
            column: 10,
            message: "Property 'val' does not exist on type 'Data'.",
            code: 2339
        );

        Assert.Equal("/src/index.ts", diagnostic.FilePath);
        Assert.Equal(45, diagnostic.Line);
        Assert.Equal(10, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("TS2339", diagnostic.Code);
    }

    [Fact]
    public void ClassifyIssue_IdentifiesKnownIssueClasses()
    {
        var orchestrator = new DiagnosticsOrchestrator();

        var missingSymbol = orchestrator.NormalizeRoslynDiagnostic("", "error", 1, 1, "The type or namespace 'X' was not found", "CS0246");
        Assert.Equal("MissingSymbol", orchestrator.ClassifyIssue(missingSymbol));

        var typeMismatch = orchestrator.NormalizeRoslynDiagnostic("", "error", 1, 1, "cannot implicitly convert type 'int' to 'string'", "CS0029");
        Assert.Equal("TypeMismatch", orchestrator.ClassifyIssue(typeMismatch));

        var nullSafety = orchestrator.NormalizeRoslynDiagnostic("", "warning", 1, 1, "dereference of a possibly null reference", "CS8602");
        Assert.Equal("NullSafety", orchestrator.ClassifyIssue(nullSafety));

        var asyncAwait = orchestrator.NormalizeTypeScriptDiagnostic("", "error", 1, 1, "cannot await 'void' value", 1234);
        Assert.Equal("AsyncAwaitMismatch", orchestrator.ClassifyIssue(asyncAwait));
    }

    [Fact]
    public void StackTraceParser_ParsesDotNetFrames()
    {
        var trace = @"
   at DevPilot.Core.Diagnostics.StackTraceParser.Parse(String stackTrace) in C:\Dev\StackTraceParser.cs:line 45
   at DevPilot.Worker.ExecuteAsync() in C:\Dev\Worker.cs:line 120
        ";

        var frames = StackTraceParser.Parse(trace);
        Assert.Equal(2, frames.Count);

        Assert.Equal("C:\\Dev\\StackTraceParser.cs", frames[0].FilePath);
        Assert.Equal(45, frames[0].Line);
        Assert.Equal("DevPilot.Core.Diagnostics.StackTraceParser.Parse", frames[0].MethodName);

        Assert.Equal("C:\\Dev\\Worker.cs", frames[1].FilePath);
        Assert.Equal(120, frames[1].Line);
        Assert.Equal("DevPilot.Worker.ExecuteAsync", frames[1].MethodName);
    }

    [Fact]
    public void StackTraceParser_ParsesNodeFrames()
    {
        var trace = @"
Error: Failed test
    at Context.<anonymous> (c:/tests/app.spec.ts:15:32)
    at Object.run (c:/tests/runner.ts:245:12)
        ";

        var frames = StackTraceParser.Parse(trace);
        Assert.Equal(2, frames.Count);

        Assert.Equal("c:/tests/app.spec.ts", frames[0].FilePath);
        Assert.Equal(15, frames[0].Line);
        Assert.Equal("Context.<anonymous>", frames[0].MethodName);

        Assert.Equal("c:/tests/runner.ts", frames[1].FilePath);
        Assert.Equal(245, frames[1].Line);
        Assert.Equal("Object.run", frames[1].MethodName);
    }
}
