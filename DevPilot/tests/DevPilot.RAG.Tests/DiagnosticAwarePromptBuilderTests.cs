using System;
using System.Collections.Generic;
using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class DiagnosticAwarePromptBuilderTests
{
    private readonly ITokenEstimator _tokenEstimator;

    public DiagnosticAwarePromptBuilderTests()
    {
        _tokenEstimator = new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings
        {
            CharactersPerToken = 4,
            MinimumTokens = 1
        }));
    }

    [Fact]
    public void BuildDiagnosticFixPrompt_IncludesRequiredMetadataAndContext()
    {
        var builder = new DiagnosticAwarePromptBuilder(_tokenEstimator);
        var diagnostic = new NormalizedDiagnostic(
            FilePath: "Program.cs",
            Line: 10,
            Column: 4,
            Severity: DiagnosticSeverity.Error,
            Message: "Cannot convert int to string",
            Code: "CS0029",
            Source: "Roslyn"
        );

        var prompt = builder.BuildDiagnosticFixPrompt(
            diagnostic: diagnostic,
            surroundingCode: "void Main() { string x = 10; }",
            activeSymbolContent: "class Program { void Main() { string x = 10; } }",
            siblingSymbols: new[] { "void OtherMethod()", "int value" }
        );

        // Normalize line endings for tests
        var normalizedPrompt = prompt.Replace("\r\n", "\n");

        Assert.Contains("Source: Roslyn", normalizedPrompt);
        Assert.Contains("Code: CS0029", normalizedPrompt);
        Assert.Contains("Message: Cannot convert int to string", normalizedPrompt);
        Assert.Contains("void Main() { string x = 10; }", normalizedPrompt);
        Assert.Contains("class Program { void Main() { string x = 10; } }", normalizedPrompt);
        Assert.Contains("void OtherMethod()", normalizedPrompt);
        Assert.Contains("int value", normalizedPrompt);
    }

    [Fact]
    public void BuildTestFailureAnalysisPrompt_IncludesRequiredMetadataAndCode()
    {
        var builder = new DiagnosticAwarePromptBuilder(_tokenEstimator);
        var prompt = builder.BuildTestFailureAnalysisPrompt(
            testName: "Test_Addition_Fails",
            errorMessage: "Expected 5 but was 4",
            stackTrace: "at App.Tests.Test_Addition_Fails() in C:\\App.Tests.cs:line 12",
            targetMethodCode: "public int Add(int a, int b) => a - b;"
        );

        var normalizedPrompt = prompt.Replace("\r\n", "\n");

        Assert.Contains("Test Name: Test_Addition_Fails", normalizedPrompt);
        Assert.Contains("Error Message: Expected 5 but was 4", normalizedPrompt);
        Assert.Contains("at App.Tests.Test_Addition_Fails() in C:\\App.Tests.cs:line 12", normalizedPrompt);
        Assert.Contains("public int Add(int a, int b) => a - b;", normalizedPrompt);
    }
}
