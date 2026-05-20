using System;
using System.Collections.Generic;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.Patching;

public static class SearchReplacePatchEngine
{
    public static (string PatchedContent, string DiffContent, bool IsValid, string? ErrorMessage) ApplyPatch(
        string fileContent,
        string searchContent,
        string replacementContent)
    {
        if (string.IsNullOrEmpty(searchContent))
        {
            return (fileContent, "", false, "Search block cannot be empty.");
        }

        var normalizedFile = NormalizeLineEndings(fileContent);
        var normalizedSearch = NormalizeLineEndings(searchContent);
        var normalizedReplacement = NormalizeLineEndings(replacementContent);

        // Find match position
        var index = normalizedFile.IndexOf(normalizedSearch, StringComparison.Ordinal);
        if (index == -1)
        {
            return (fileContent, "", false, "The search block was not found in the file.");
        }

        // Verify uniqueness
        var secondIndex = normalizedFile.IndexOf(normalizedSearch, index + normalizedSearch.Length, StringComparison.Ordinal);
        if (secondIndex != -1)
        {
            return (fileContent, "", false, "The search block matches multiple locations in the file. Please specify a more unique context.");
        }

        var before = normalizedFile.Substring(0, index);
        var after = normalizedFile.Substring(index + normalizedSearch.Length);
        var patchedNormalized = before + normalizedReplacement + after;

        var originalLineEnding = fileContent.Contains("\r\n") ? "\r\n" : "\n";
        var finalContent = patchedNormalized.Replace("\n", originalLineEnding);

        var diff = GenerateUnifiedDiff(fileContent, finalContent);

        return (finalContent, diff, true, null);
    }

    public static string GenerateUnifiedDiff(string oldContent, string newContent)
    {
        if (oldContent == newContent) return "";

        var oldLines = SplitLines(oldContent);
        var newLines = SplitLines(newContent);

        // Prefix common lines
        var prefixLen = 0;
        while (prefixLen < oldLines.Length && prefixLen < newLines.Length && oldLines[prefixLen] == newLines[prefixLen])
        {
            prefixLen++;
        }

        // Suffix common lines
        var suffixLen = 0;
        while (suffixLen < (oldLines.Length - prefixLen) && suffixLen < (newLines.Length - prefixLen) &&
               oldLines[oldLines.Length - 1 - suffixLen] == newLines[newLines.Length - 1 - suffixLen])
        {
            suffixLen++;
        }

        var sb = new StringBuilder();

        // 3 lines of prefix context
        var startContext = Math.Max(0, prefixLen - 3);
        for (var i = startContext; i < prefixLen; i++)
        {
            sb.AppendLine($"  {oldLines[i]}");
        }

        // Changes
        for (var i = prefixLen; i < oldLines.Length - suffixLen; i++)
        {
            sb.AppendLine($"-{oldLines[i]}");
        }
        for (var i = prefixLen; i < newLines.Length - suffixLen; i++)
        {
            sb.AppendLine($"+{newLines[i]}");
        }

        // 3 lines of suffix context
        var endContext = Math.Min(oldLines.Length, oldLines.Length - suffixLen + 3);
        for (var i = oldLines.Length - suffixLen; i < endContext; i++)
        {
            sb.AppendLine($"  {oldLines[i]}");
        }

        return sb.ToString();
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
