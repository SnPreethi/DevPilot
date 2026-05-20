using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevPilot.Contracts;

namespace DevPilot.Core.Diagnostics;

public static class StackTraceParser
{
    private static readonly Regex DotNetStackRegex = new(
        @"at\s+(?<method>[^\s\(]+).*\s+in\s+(?<file>.+)\:line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NodeStackRegex = new(
        @"at\s+(?<method>[^\s\(]+).*\((?<file>.+)\:(?<line>\d+)\:(?<col>\d+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RawFileStackRegex = new(
        @"(?<file>[a-zA-Z]\:[\\\/\s\.\w-]+\.[a-zA-Z0-9]+)\:(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<StackFrameInfo> Parse(string stackTrace)
    {
        var frames = new List<StackFrameInfo>();
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return frames;
        }

        var lines = stackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var match = DotNetStackRegex.Match(line);
            if (match.Success)
            {
                frames.Add(new StackFrameInfo(
                    FilePath: match.Groups["file"].Value.Trim(),
                    Line: int.Parse(match.Groups["line"].Value),
                    MethodName: match.Groups["method"].Value.Trim()
                ));
                continue;
            }

            match = NodeStackRegex.Match(line);
            if (match.Success)
            {
                frames.Add(new StackFrameInfo(
                    FilePath: match.Groups["file"].Value.Trim(),
                    Line: int.Parse(match.Groups["line"].Value),
                    MethodName: match.Groups["method"].Value.Trim()
                ));
                continue;
            }

            match = RawFileStackRegex.Match(line);
            if (match.Success)
            {
                frames.Add(new StackFrameInfo(
                    FilePath: match.Groups["file"].Value.Trim(),
                    Line: int.Parse(match.Groups["line"].Value),
                    MethodName: "unknown"
                ));
            }
        }

        return frames;
    }
}
