using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.RAG;

public sealed class RepositoryAwarePromptBuilder : IPromptBuilder
{
    private readonly PromptingSettings _settings;
    private readonly ITokenEstimator _tokenEstimator;

    public RepositoryAwarePromptBuilder(
        IOptions<PromptingSettings> settings,
        ITokenEstimator tokenEstimator)
    {
        _settings = settings.Value;
        _tokenEstimator = tokenEstimator;
    }

    public Task<GroundedPrompt> BuildAsync(
        string question,
        IReadOnlyList<RetrievedContext> context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isEditPlan = question.Contains("[EDIT_REQUEST]");
        var cleanQuestion = question.Replace("[EDIT_REQUEST]", "").Trim();

        var builder = new StringBuilder();
        if (isEditPlan)
        {
            builder.AppendLine("You are an offline local AI coding assistant integrated into VS Code.");
            builder.AppendLine("You must generate a structured edit plan to satisfy the user's refactoring request.");
            builder.AppendLine("You MUST return the output inside a markdown code block starting with ```json and ending with ```.");
            builder.AppendLine("No other text should be output outside the json block.");
            builder.AppendLine("The JSON must strictly match the following schema:");
            builder.AppendLine("{");
            builder.AppendLine("  \"reasoningSummary\": \"Reasoning for these changes\",");
            builder.AppendLine("  \"fileEdits\": [");
            builder.AppendLine("    {");
            builder.AppendLine("      \"filePath\": \"relative path to the file on disk\",");
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
        }
        else
        {
            builder.AppendLine("You are an offline local AI coding assistant integrated into VS Code.");
            builder.AppendLine("Answer the user's question using ONLY the codebase context provided below.");
            builder.AppendLine("If the context does not contain enough details to answer, state that clearly.");
        }
        builder.AppendLine();

        // Group context items by their type
        var activeFile = context.Where(c => c.ChunkType == "active_file").ToList();
        var activeSymbol = context.Where(c => c.ChunkType == "active_symbol").ToList();
        var siblingSymbols = context.Where(c => c.ChunkType == "sibling_symbol").ToList();
        var definitions = context.Where(c => c.ChunkType == "definition").ToList();
        var semantic = context.Where(c => c.ChunkType == "semantic").ToList();

        // 1. Active Editor / Code Selection
        if (activeFile.Count > 0)
        {
            builder.AppendLine("=== ACTIVE EDITOR CONTEXT ===");
            foreach (var item in activeFile)
            {
                builder.AppendLine($"File: {item.FilePath}");
                builder.AppendLine(item.Content);
                builder.AppendLine();
            }
        }

        // 2. Enclosing Cursor Symbol
        if (activeSymbol.Count > 0)
        {
            builder.AppendLine("=== ENCLOSING CODE SYMBOL ===");
            foreach (var item in activeSymbol)
            {
                builder.AppendLine($"Symbol: {item.SymbolName} in {item.FilePath} (Lines {item.StartLine}-{item.EndLine})");
                builder.AppendLine(item.Content);
                builder.AppendLine();
            }
        }

        // 3. Sibling Scope Methods
        if (siblingSymbols.Count > 0)
        {
            builder.AppendLine("=== SIBLING CLASS MEMBERS ===");
            foreach (var item in siblingSymbols)
            {
                builder.AppendLine($"Member: {item.SymbolName} in {item.FilePath}");
                builder.AppendLine(item.Content);
                builder.AppendLine();
            }
        }

        // 4. Referenced Type and Method Definitions
        if (definitions.Count > 0)
        {
            builder.AppendLine("=== REFERENCED TYPE / METHOD DEFINITIONS ===");
            foreach (var item in definitions)
            {
                builder.AppendLine($"Definition: {item.SymbolName} in {item.FilePath}");
                builder.AppendLine(item.Content);
                builder.AppendLine();
            }
        }

        // 5. Semantic Search Matches
        if (semantic.Count > 0)
        {
            builder.AppendLine("=== ADDITIONAL RELEVANT CODE SNIPPETS ===");
            foreach (var item in semantic)
            {
                builder.AppendLine($"Location: {item.FilePath} (Lines {item.StartLine}-{item.EndLine})");
                builder.AppendLine(item.Content);
                builder.AppendLine();
            }
        }

        builder.AppendLine("=== USER QUESTION ===");
        builder.AppendLine(cleanQuestion);

        var promptText = Truncate(builder.ToString(), _settings.MaxPromptCharacters);
        var prompt = new GroundedPrompt(promptText, context, _tokenEstimator.Estimate(promptText));

        return Task.FromResult(prompt);
    }

    private static string Truncate(string value, int maxCharacters)
    {
        if (maxCharacters <= 0 || value.Length <= maxCharacters)
        {
            return value;
        }
        return value[..maxCharacters];
    }
}
