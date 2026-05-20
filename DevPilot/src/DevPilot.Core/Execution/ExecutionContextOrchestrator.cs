using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;

namespace DevPilot.Core.Execution;

public sealed class ExecutionContextOrchestrator
{
    private readonly ISymbolStore _symbolStore;
    private readonly IChunkStore _chunkStore;

    public ExecutionContextOrchestrator(ISymbolStore symbolStore, IChunkStore chunkStore)
    {
        _symbolStore = symbolStore;
        _chunkStore = chunkStore;
    }

    public async Task<(string? surroundingCode, string? activeSymbolCode, IReadOnlyList<string> siblingSymbols)> ResolveContextAsync(
        ExecutionEvent ev,
        string? repositoryId,
        string? repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string? surroundingCode = null;
        string? activeSymbolCode = null;
        var siblingSymbols = new List<string>();

        if (string.IsNullOrEmpty(ev.TargetFilePath))
        {
            return (null, null, siblingSymbols);
        }

        var path = ev.TargetFilePath;
        if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(repositoryPath))
        {
            path = Path.Combine(repositoryPath, path);
        }

        if (File.Exists(path))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
                var targetLine = ev.TargetLine ?? 1;
                var startLine = Math.Max(0, targetLine - 11);
                var endLine = Math.Min(lines.Length - 1, targetLine + 9);

                var builder = new StringBuilder();
                for (int i = startLine; i <= endLine; i++)
                {
                    var prefix = (i + 1) == targetLine ? "=> " : "   ";
                    builder.AppendLine($"{prefix}{i + 1}: {lines[i]}");
                }
                surroundingCode = builder.ToString();
            }
            catch
            {
                // Proceed without surrounding lines if reading fails
            }
        }

        if (!string.IsNullOrEmpty(repositoryId) && !string.IsNullOrEmpty(repositoryPath))
        {
            try
            {
                var relativePath = ev.TargetFilePath;
                if (relativePath.StartsWith(repositoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(repositoryPath.Length).TrimStart('\\', '/');
                }
                relativePath = relativePath.Replace("\\", "/");

                var fileId = DeterministicId($"{repositoryId}:{relativePath}");
                var symbols = await _symbolStore.ListByFileAsync(fileId, cancellationToken).ConfigureAwait(false);

                if (symbols.Count > 0 && ev.TargetLine.HasValue)
                {
                    var lineNum = ev.TargetLine.Value;
                    var activeSymbol = symbols
                        .Where(s => lineNum >= s.StartLine && lineNum <= s.EndLine)
                        .OrderBy(s => s.EndLine - s.StartLine)
                        .FirstOrDefault();

                    if (activeSymbol != null)
                    {
                        var chunk = await _chunkStore.GetAsync(activeSymbol.SymbolId, cancellationToken).ConfigureAwait(false);
                        if (chunk != null)
                        {
                            activeSymbolCode = chunk.Content;
                        }

                        var siblings = symbols.Where(s => s.ParentSymbol == activeSymbol.ParentSymbol && s.SymbolId != activeSymbol.SymbolId);
                        foreach (var sib in siblings.Take(3))
                        {
                            siblingSymbols.Add($"{sib.Kind} {sib.Name} (Lines {sib.StartLine}-{sib.EndLine})");
                        }
                    }
                }
            }
            catch
            {
                // Fallback gracefully on DB errors
            }
        }

        return (surroundingCode, activeSymbolCode, siblingSymbols);
    }

    private static string DeterministicId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
