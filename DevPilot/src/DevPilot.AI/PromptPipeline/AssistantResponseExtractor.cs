using System;

namespace DevPilot.AI.PromptPipeline;

public sealed class AssistantResponseExtractor
{
    public string ExtractResponse(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var cleaned = rawText.Trim();

        // 1. Remove echoed tags if model leaked them
        var tags = new[] { "<|assistant|>", "<|user|>", "<|system|>", "<|end|>" };
        foreach (var tag in tags)
        {
            if (cleaned.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[tag.Length..].Trim();
            }
            if (cleaned.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^tag.Length].Trim();
            }
        }

        // 2. Strip standard markdown wrappers if they're too literal
        if (cleaned.StartsWith("```") && cleaned.EndsWith("```"))
        {
            // Find first newline
            var firstNewlineIdx = cleaned.IndexOf('\n');
            if (firstNewlineIdx >= 0)
            {
                cleaned = cleaned[(firstNewlineIdx + 1)..^3].Trim();
            }
        }

        return cleaned;
    }
}
