using DevPilot.Contracts;
using DevPilot.Core.Execution;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public void ParseTerminalOutput_ParsesDotNetBuildError()
    {
        var orchestrator = new TerminalOrchestrator();
        var output = @"
C:\Users\Admin\Desktop\Dev_Assistant\DevPilot\src\DevPilot.LocalService\DevPilotWorker.cs(705,82): error CS0246: The type or namespace name 'TerminalOrchestrator' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\Admin\Desktop\Dev_Assistant\DevPilot\src\DevPilot.LocalService\DevPilot.LocalService.csproj]
        ";

        var ev = orchestrator.ParseTerminalOutput(output);

        Assert.Equal(ExecutionEventType.BuildFailure, ev.Type);
        Assert.Contains("TerminalOrchestrator", ev.Message);
        Assert.Equal(@"C:\Users\Admin\Desktop\Dev_Assistant\DevPilot\src\DevPilot.LocalService\DevPilotWorker.cs", ev.TargetFilePath);
        Assert.Equal(705, ev.TargetLine);
    }

    [Fact]
    public void ParseTerminalOutput_ParsesNodeTypeScriptError()
    {
        var orchestrator = new TerminalOrchestrator();
        var output = @"
src/chatViewProvider.ts:420:10 - error TS2339: Property 'analyzeTerminalSelection' does not exist on type 'ChatViewProvider'.
        ";

        var ev = orchestrator.ParseTerminalOutput(output);

        Assert.Equal(ExecutionEventType.BuildFailure, ev.Type);
        Assert.Contains("analyzeTerminalSelection", ev.Message);
        Assert.Equal("src/chatViewProvider.ts", ev.TargetFilePath);
        Assert.Equal(420, ev.TargetLine);
    }

    [Fact]
    public void ParseTerminalOutput_ParsesDotNetTestFailure()
    {
        var orchestrator = new TerminalOrchestrator();
        var output = @"
  Failed DevPilot.Core.Tests.DiagnosticsTests.StackTraceParser_ParsesDotNetFrames [149 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
  Stack Trace:
     at DevPilot.Core.Tests.DiagnosticsTests.StackTraceParser_ParsesDotNetFrames() in C:\Dev\DiagnosticsTests.cs:line 78
        ";

        var ev = orchestrator.ParseTerminalOutput(output);

        Assert.Equal(ExecutionEventType.TestFailure, ev.Type);
        Assert.Contains("Expected: 2", ev.Message);
        Assert.Equal(@"C:\Dev\DiagnosticsTests.cs", ev.TargetFilePath);
        Assert.Equal(78, ev.TargetLine);
    }

    [Fact]
    public void ParseTerminalOutput_ParsesRuntimeException()
    {
        var orchestrator = new TerminalOrchestrator();
        var output = @"
Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object.
   at DevPilot.Core.Diagnostics.StackTraceParser.Parse(String stackTrace) in C:\Dev\StackTraceParser.cs:line 45
        ";

        var ev = orchestrator.ParseTerminalOutput(output);

        Assert.Equal(ExecutionEventType.RuntimeException, ev.Type);
        Assert.Equal(@"C:\Dev\StackTraceParser.cs", ev.TargetFilePath);
        Assert.Equal(45, ev.TargetLine);
    }
}
