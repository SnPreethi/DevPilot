using System;

namespace DevPilot.AI.PromptPipeline;

public sealed class PromptSanitizer
{
    private static readonly string[] ControlTokens = 
    {
        "<|system|>",
        "<|user|>",
        "<|assistant|>",
        "<|end|>",
        "<|endoftext|>"
    };

    public string Sanitize(string promptText)
    {
        if (string.IsNullOrEmpty(promptText))
        {
            return string.Empty;
        }

        var sanitized = promptText;
        foreach (var controlToken in ControlTokens)
        {
            sanitized = sanitized.Replace(controlToken, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return sanitized.Trim();
    }
}
