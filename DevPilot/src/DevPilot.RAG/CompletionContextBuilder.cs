using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;

namespace DevPilot.RAG;

public sealed class CompletionContextBuilder : ICompletionContextBuilder
{
    private readonly ISymbolStore _symbolStore;
    private readonly IChunkStore _chunkStore;
    private readonly ITokenEstimator _tokenEstimator;

    public CompletionContextBuilder(
        ISymbolStore symbolStore,
        IChunkStore chunkStore,
        ITokenEstimator tokenEstimator)
    {
        _symbolStore = symbolStore;
        _chunkStore = chunkStore;
        _tokenEstimator = tokenEstimator;
    }

    public async Task<string> BuildCompletionPromptAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Trim prefix and suffix to token budgets
        var trimmedPrefix = TrimPrefix(request.PrefixContent, 1500);
        var trimmedSuffix = TrimSuffix(request.SuffixContent, 500);

        // 2. Resolve repository symbols if repository info is available
        var contextBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(request.ActiveSymbol))
        {
            contextBuilder.AppendLine($"// Active symbol scope: {request.ActiveSymbol}");
        }
        if (request.Imports != null && request.Imports.Count > 0)
        {
            contextBuilder.AppendLine($"// Visible imports/usings: {string.Join(", ", request.Imports)}");
        }
        if (request.NearbySymbols != null && request.NearbySymbols.Count > 0)
        {
            contextBuilder.AppendLine($"// Nearby symbol declarations: {string.Join(", ", request.NearbySymbols)}");
        }

        if (!string.IsNullOrEmpty(request.RepositoryId) && !string.IsNullOrEmpty(request.RepositoryPath))
        {
            try
            {
                var relativePath = request.FilePath;
                if (relativePath.StartsWith(request.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(request.RepositoryPath.Length).TrimStart('\\', '/');
                }
                relativePath = relativePath.Replace("\\", "/");

                var fileId = DeterministicId($"{request.RepositoryId}:{relativePath}");
                var symbols = await _symbolStore.ListByFileAsync(fileId, cancellationToken).ConfigureAwait(false);

                if (symbols.Count > 0)
                {
                    var activeSymbol = symbols
                        .Where(s => request.CursorLine >= s.StartLine && request.CursorLine <= s.EndLine)
                        .OrderBy(s => s.EndLine - s.StartLine)
                        .FirstOrDefault();

                    var targetSymbols = new List<SymbolIndexEntry>();
                    if (activeSymbol != null)
                    {
                        var siblings = symbols.Where(s => s.ParentSymbol == activeSymbol.ParentSymbol && s.SymbolId != activeSymbol.SymbolId);
                        targetSymbols.AddRange(siblings.Take(3));
                    }
                    else
                    {
                        targetSymbols.AddRange(symbols.Take(3));
                    }

                    var currentContextTokens = 0;
                    var maxContextTokens = 1000;

                    foreach (var sym in targetSymbols)
                    {
                        var chunk = await _chunkStore.GetAsync(sym.SymbolId, cancellationToken).ConfigureAwait(false);
                        if (chunk != null)
                        {
                            var content = $"// Sibling context: {sym.Name} ({sym.Kind})\n{chunk.Content}\n";
                            var tokens = _tokenEstimator.Estimate(content);
                            if (currentContextTokens + tokens > maxContextTokens)
                            {
                                break;
                            }

                            contextBuilder.AppendLine(content);
                            currentContextTokens += tokens;
                        }
                    }
                }
            }
            catch
            {
                // Gracefully fallback on database error
            }
        }

        // 3. Assemble Fill-In-The-Middle prompt
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("<|system|>");
        promptBuilder.AppendLine("You are an inline code completion assistant. Your task is to complete the code at the cursor position.");
        promptBuilder.AppendLine("CRITICAL RULES:");
        promptBuilder.AppendLine("1. Return ONLY the code needed to fill in between [PREFIX] and [SUFFIX].");
        promptBuilder.AppendLine("2. Do NOT wrap your output in markdown code fences (```).");
        promptBuilder.AppendLine("3. Do NOT write any explanations, notes, or comments.");
        promptBuilder.AppendLine("4. Match the indentation and code style of the prefix exactly.");
        promptBuilder.AppendLine("<|end|>");

        promptBuilder.AppendLine("<|user|>");
        if (contextBuilder.Length > 0)
        {
            promptBuilder.AppendLine("[REPOSITORY CONTEXT]");
            promptBuilder.Append(contextBuilder.ToString());
            promptBuilder.AppendLine("---");
        }
        promptBuilder.AppendLine("[PREFIX]");
        promptBuilder.Append(trimmedPrefix);
        promptBuilder.AppendLine("[SUFFIX]");
        promptBuilder.Append(trimmedSuffix);
        promptBuilder.AppendLine("<|end|>");
        promptBuilder.Append("<|assistant|>\n");

        return promptBuilder.ToString();
    }

    private string TrimPrefix(string prefix, int maxTokens)
    {
        var lines = prefix.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var accumulated = new List<string>();
        var currentTokens = 0;

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            var tokens = _tokenEstimator.Estimate(line + "\n");
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }
            accumulated.Add(line);
            currentTokens += tokens;
        }

        accumulated.Reverse();
        return string.Join("\n", accumulated);
    }

    private string TrimSuffix(string suffix, int maxTokens)
    {
        var lines = suffix.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var accumulated = new List<string>();
        var currentTokens = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var tokens = _tokenEstimator.Estimate(line + "\n");
            if (currentTokens + tokens > maxTokens)
            {
                break;
            }
            accumulated.Add(line);
            currentTokens += tokens;
        }

        return string.Join("\n", accumulated);
    }

    private static string DeterministicId(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
