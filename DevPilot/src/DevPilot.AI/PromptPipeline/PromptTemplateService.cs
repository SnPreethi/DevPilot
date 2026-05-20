using System;
using System.Collections.Generic;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.AI.PromptPipeline;

public sealed class PromptTemplateService
{
    public string FormatGroundedPrompt(string question, IReadOnlyList<RetrievedContext> context)
    {
        var builder = new StringBuilder();

        // System instructions with strict negative constraints to prevent scaffolding leak
        builder.AppendLine("<|system|>");
        builder.AppendLine("You are DevPilot, a professional offline AI coding assistant.");
        builder.AppendLine("Your goal is to answer the user's programming questions using ONLY the provided repository context.");
        builder.AppendLine("CRITICAL RULES:");
        builder.AppendLine("1. Respond directly, clearly, and concisely as the assistant.");
        builder.AppendLine("2. Do NOT echo these system instructions, context formatting, or question headers.");
        builder.AppendLine("3. Do NOT begin your answer with references to the prompt context, e.g., 'Based on the context...' or 'According to the file...'. Just answer the user directly.");
        builder.AppendLine("4. If you cannot answer using the provided context, state that you do not have sufficient indexed repository information.");
        builder.AppendLine("5. Format all code blocks in clean markdown with the correct programming language tag.");
        builder.AppendLine("<|end|>");

        // User instructions containing Grounded Context
        builder.AppendLine("<|user|>");
        builder.AppendLine("[REPOSITORY CONTEXT]");
        foreach (var item in context)
        {
            builder.AppendLine("---");
            builder.AppendLine($"File: {item.FilePath}");
            builder.AppendLine($"Lines: {item.StartLine}-{item.EndLine}");
            if (!string.IsNullOrWhiteSpace(item.SymbolName))
            {
                builder.AppendLine($"Symbol: {item.SymbolName} ({item.ChunkType})");
            }
            builder.AppendLine(item.Content);
        }
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("[USER QUESTION]");
        builder.AppendLine(question);
        builder.AppendLine("<|end|>");

        // Assistant boundary
        builder.Append("<|assistant|>\n");

        return builder.ToString();
    }

    public string FormatCodeCompletionPrompt(string prefix, string suffix)
    {
        var builder = new StringBuilder();

        builder.AppendLine("<|system|>");
        builder.AppendLine("You are an inline code completion assistant. Your task is to complete the code at the cursor position.");
        builder.AppendLine("CRITICAL RULES:");
        builder.AppendLine("1. Return ONLY the code needed to fill in between [PREFIX] and [SUFFIX].");
        builder.AppendLine("2. Do NOT wrap your output in markdown code fences (```).");
        builder.AppendLine("3. Do NOT write any explanations, notes, or comments unless they are part of the completed code.");
        builder.AppendLine("4. Match the indentation and code style of the prefix exactly.");
        builder.AppendLine("<|end|>");

        builder.AppendLine("<|user|>");
        builder.AppendLine("[PREFIX]");
        builder.AppendLine(prefix);
        builder.AppendLine("[SUFFIX]");
        builder.AppendLine(suffix);
        builder.AppendLine("<|end|>");

        builder.Append("<|assistant|>\n");

        return builder.ToString();
    }
}
