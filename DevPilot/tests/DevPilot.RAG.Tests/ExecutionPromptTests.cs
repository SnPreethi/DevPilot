using System;
using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.RAG;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class ExecutionPromptTests
{
    private readonly ITokenEstimator _tokenEstimator;

    public ExecutionPromptTests()
    {
        _tokenEstimator = new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings
        {
            CharactersPerToken = 4,
            MinimumTokens = 1
        }));
    }

    [Fact]
    public void BuildExecutionFixPrompt_IncludesFailureDetailsAndContext()
    {
        var builder = new ExecutionAwarePromptBuilder(_tokenEstimator);
        var ev = new ExecutionEvent(
            Type: ExecutionEventType.TestFailure,
            Message: "Values differ",
            RawOutput: "Failed raw trace output",
            TargetFilePath: "C:\\Dev\\Tests.cs",
            TargetLine: 25,
            StackTrace: "at TestMethod() in C:\\Dev\\Tests.cs:line 25"
        );

        var prompt = builder.BuildExecutionFixPrompt(
            ev,
            surroundingCode: "void TestMethod() { Assert.Equal(1, 2); }",
            activeSymbolContent: "class Tests { void TestMethod() { Assert.Equal(1, 2); } }",
            siblingSymbols: new[] { "void OtherTest()" }
        );

        var normalizedPrompt = prompt.Replace("\r\n", "\n");

        Assert.Contains("Event Type: TestFailure", normalizedPrompt);
        Assert.Contains("Message: Values differ", normalizedPrompt);
        Assert.Contains("Failing File: C:\\Dev\\Tests.cs (Line: 25)", normalizedPrompt);
        Assert.Contains("at TestMethod() in C:\\Dev\\Tests.cs:line 25", normalizedPrompt);
        Assert.Contains("void TestMethod() { Assert.Equal(1, 2); }", normalizedPrompt);
        Assert.Contains("class Tests { void TestMethod() { Assert.Equal(1, 2); } }", normalizedPrompt);
        Assert.Contains("void OtherTest()", normalizedPrompt);
    }
}
