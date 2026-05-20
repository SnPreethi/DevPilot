using System.Collections.Generic;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.RAG;

public sealed class DiagnosticAwarePromptBuilder
{
    private readonly ITokenEstimator _tokenEstimator;

    public DiagnosticAwarePromptBuilder(ITokenEstimator tokenEstimator)
    {
        _tokenEstimator = tokenEstimator;
    }

    public string BuildDiagnosticFixPrompt(
        NormalizedDiagnostic diagnostic,
        string surroundingCode,
        string? activeSymbolContent = null,
        IReadOnlyList<string>? siblingSymbols = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an offline local AI coding assistant integrated into VS Code.");
        builder.AppendLine("You must resolve a compiler/linter error in the codebase and output a structured edit plan.");
        builder.AppendLine("You MUST return the output inside a markdown code block starting with ```json and ending with ```.");
        builder.AppendLine("No other text should be output outside the json block.");
        builder.AppendLine("The JSON must strictly match the following schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"reasoningSummary\": \"Reasoning for this fix\",");
        builder.AppendLine("  \"fileEdits\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"filePath\": \"relative path to the file on disk or full path as requested\",");
        builder.AppendLine("      \"instructions\": [");
        builder.AppendLine("        {");
        builder.AppendLine("          \"targetSymbol\": \"the name of class or method to modify\",");
        builder.AppendLine("          \"editDescription\": \"brief explanation of this edit step\",");
        builder.AppendLine("          \"searchContent\": \"EXACT original lines of code from the file that must be replaced (include leading spacing, braces, newlines exactly as shown in the source context)\",");
        builder.AppendLine("          \"replacementContent\": \"the new replacement lines of code\"");
        builder.AppendLine("        }");
        builder.AppendLine("      ]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine("Ensure the searchContent matches character-for-character with the source context.");
        builder.AppendLine();

        builder.AppendLine("=== COMPILER DIAGNOSTIC ===");
        builder.AppendLine($"Source: {diagnostic.Source}");
        builder.AppendLine($"Code: {diagnostic.Code}");
        builder.AppendLine($"Severity: {diagnostic.Severity}");
        builder.AppendLine($"Line: {diagnostic.Line}, Column: {diagnostic.Column}");
        builder.AppendLine($"Message: {diagnostic.Message}");
        builder.AppendLine();

        builder.AppendLine("=== TARGET FILE CONTEXT (Squiggle Surrounding Lines) ===");
        builder.AppendLine($"File: {diagnostic.FilePath}");
        builder.AppendLine(surroundingCode);
        builder.AppendLine();

        if (!string.IsNullOrEmpty(activeSymbolContent))
        {
            builder.AppendLine("=== ENCLOSING CODE SYMBOL ===");
            builder.AppendLine(activeSymbolContent);
            builder.AppendLine();
        }

        if (siblingSymbols != null && siblingSymbols.Count > 0)
        {
            builder.AppendLine("=== RELATED SYMBOL DECLARATIONS ===");
            foreach (var sibling in siblingSymbols)
            {
                builder.AppendLine(sibling);
                builder.AppendLine();
            }
        }

        builder.AppendLine("=== RESOLUTION REQUEST ===");
        builder.AppendLine("Generate a minimal, safe, and syntactically correct fix for the above compiler diagnostic.");

        return builder.ToString();
    }

    public string BuildTestFailureAnalysisPrompt(
        string testName,
        string errorMessage,
        string stackTrace,
        string? targetMethodCode = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an offline local AI coding assistant integrated into VS Code.");
        builder.AppendLine("Analyze the following unit test failure and stack trace to explain the root cause and suggest a fix.");
        builder.AppendLine("Provide a clear explanation and the suggested code modifications in markdown format.");
        builder.AppendLine();

        builder.AppendLine("=== FAILING TEST ===");
        builder.AppendLine($"Test Name: {testName}");
        builder.AppendLine($"Error Message: {errorMessage}");
        builder.AppendLine();

        builder.AppendLine("=== STACK TRACE ===");
        builder.AppendLine(stackTrace);
        builder.AppendLine();

        if (!string.IsNullOrEmpty(targetMethodCode))
        {
            builder.AppendLine("=== ASSOCIATED CODE UNDER TEST ===");
            builder.AppendLine(targetMethodCode);
            builder.AppendLine();
        }

        builder.AppendLine("=== REQUEST ===");
        builder.AppendLine("Explain what caused the failure, trace it to the failing line in the stack trace, and show the exact code fix required.");

        return builder.ToString();
    }
}
