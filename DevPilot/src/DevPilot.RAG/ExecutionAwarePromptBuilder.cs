using System.Collections.Generic;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.RAG;

public sealed class ExecutionAwarePromptBuilder
{
    private readonly ITokenEstimator _tokenEstimator;

    public ExecutionAwarePromptBuilder(ITokenEstimator tokenEstimator)
    {
        _tokenEstimator = tokenEstimator;
    }

    public string BuildExecutionFixPrompt(
        ExecutionEvent ev,
        string? surroundingCode = null,
        string? activeSymbolContent = null,
        IReadOnlyList<string>? siblingSymbols = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an offline local AI coding assistant integrated into VS Code.");
        builder.AppendLine("You must resolve an execution/terminal error in the codebase and output a structured edit plan.");
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

        builder.AppendLine("=== EXECUTION FAILURE DETAILS ===");
        builder.AppendLine($"Event Type: {ev.Type}");
        builder.AppendLine($"Message: {ev.Message}");
        if (!string.IsNullOrEmpty(ev.TargetFilePath))
        {
            builder.AppendLine($"Failing File: {ev.TargetFilePath} (Line: {ev.TargetLine})");
        }
        if (!string.IsNullOrEmpty(ev.StackTrace))
        {
            builder.AppendLine();
            builder.AppendLine("=== STACK TRACE / RAW LOG ===");
            builder.AppendLine(ev.StackTrace);
        }
        builder.AppendLine();

        if (!string.IsNullOrEmpty(surroundingCode))
        {
            builder.AppendLine("=== TARGET FILE CONTEXT (Failing Line Surrounding Code) ===");
            builder.AppendLine(surroundingCode);
            builder.AppendLine();
        }

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
        builder.AppendLine("Generate a minimal, safe, and syntactically correct fix for the above execution failure.");

        return builder.ToString();
    }
}
