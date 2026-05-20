using System;

namespace DevPilot.AI.Generation;

public sealed class UtfSafeTextAccumulator
{
    public string GetSafePrefix(string text, out string unsafeSuffix)
    {
        if (string.IsNullOrEmpty(text))
        {
            unsafeSuffix = string.Empty;
            return string.Empty;
        }

        // Check for trailing high surrogate characters (surrogate pair split across tokens)
        var lastChar = text[^1];
        if (char.IsHighSurrogate(lastChar))
        {
            unsafeSuffix = text[^1..];
            return text[..^1];
        }

        // Check if string ends with a replacement character indicating a truncated multibyte UTF-8 byte
        if (lastChar == '\uFFFD')
        {
            unsafeSuffix = text[^1..];
            return text[..^1];
        }

        unsafeSuffix = string.Empty;
        return text;
    }
}
