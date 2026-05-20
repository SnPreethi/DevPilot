using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevPilot.Contracts;
using DevPilot.Core.Diagnostics;

namespace DevPilot.Core.Execution;

public sealed class TerminalOrchestrator
{
    private static readonly Regex DotNetBuildErrorRegex = new(
        @"(?<file>[a-zA-Z]\:[\\\/\s\.\w-]+\.[a-zA-Z0-9]+)\((?<line>\d+),(?<col>\d+)\)\:\s+(?<severity>error|warning)\s+(?<code>[a-zA-Z0-9]+)\:\s+(?<message>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NodeTsErrorRegex = new(
        @"(?<file>[^\s\:]+)\:(?<line>\d+)\:(?<col>\d+)\s+-\s+error\s+(?<code>TS\d+)\:\s+(?<message>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExecutionEvent ParseTerminalOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return new ExecutionEvent(ExecutionEventType.BuildFailure, "Empty output", rawOutput);
        }

        // 1. Try to detect dotnet build errors
        var buildMatch = DotNetBuildErrorRegex.Match(rawOutput);
        if (buildMatch.Success)
        {
            return new ExecutionEvent(
                Type: ExecutionEventType.BuildFailure,
                Message: buildMatch.Groups["message"].Value.Trim(),
                RawOutput: rawOutput,
                TargetFilePath: buildMatch.Groups["file"].Value.Trim(),
                TargetLine: int.Parse(buildMatch.Groups["line"].Value)
            );
        }

        // 2. Try to detect Node/TypeScript compiler errors
        var tsMatch = NodeTsErrorRegex.Match(rawOutput);
        if (tsMatch.Success)
        {
            return new ExecutionEvent(
                Type: ExecutionEventType.BuildFailure,
                Message: tsMatch.Groups["message"].Value.Trim(),
                RawOutput: rawOutput,
                TargetFilePath: tsMatch.Groups["file"].Value.Trim(),
                TargetLine: int.Parse(tsMatch.Groups["line"].Value)
            );
        }

        // 3. Try to detect dotnet test failure
        if (rawOutput.Contains("Failed ") && (rawOutput.Contains("Stack Trace:") || rawOutput.Contains("Error Message:")))
        {
            var frames = StackTraceParser.Parse(rawOutput);
            string? file = null;
            int? line = null;
            if (frames.Count > 0)
            {
                file = frames[0].FilePath;
                line = frames[0].Line;
            }

            var msg = "Test assertion failed";
            var msgIndex = rawOutput.IndexOf("Error Message:", StringComparison.OrdinalIgnoreCase);
            if (msgIndex != -1)
            {
                var remaining = rawOutput.Substring(msgIndex + 14);
                var traceIndex = remaining.IndexOf("Stack Trace:", StringComparison.OrdinalIgnoreCase);
                if (traceIndex != -1)
                {
                    msg = remaining.Substring(0, traceIndex).Trim();
                }
                else
                {
                    msg = remaining.Trim();
                }
            }

            return new ExecutionEvent(
                Type: ExecutionEventType.TestFailure,
                Message: msg,
                RawOutput: rawOutput,
                TargetFilePath: file,
                TargetLine: line,
                StackTrace: rawOutput
            );
        }

        // 4. Try parsing general stack trace to classify as RuntimeException
        var parsedFrames = StackTraceParser.Parse(rawOutput);
        if (parsedFrames.Count > 0)
        {
            return new ExecutionEvent(
                Type: ExecutionEventType.RuntimeException,
                Message: "Runtime exception occurred.",
                RawOutput: rawOutput,
                TargetFilePath: parsedFrames[0].FilePath,
                TargetLine: parsedFrames[0].Line,
                StackTrace: rawOutput
            );
        }

        // Default fallback to BuildFailure
        return new ExecutionEvent(
            Type: ExecutionEventType.BuildFailure,
            Message: "Unknown compilation or execution error.",
            RawOutput: rawOutput
        );
    }
}
