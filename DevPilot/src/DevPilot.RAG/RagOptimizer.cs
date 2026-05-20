using System;
using System.Collections.Generic;
using System.Linq;
using DevPilot.Contracts;

namespace DevPilot.RAG;

public sealed class RagOptimizer
{
    public static IReadOnlyList<RetrievedContext> Optimize(
        string query,
        IReadOnlyList<RankedChunk> chunks,
        int maxChunks,
        int maxPromptCharacters,
        int maxChunkCharacters)
    {
        var optimized = new List<RetrievedContext>();
        var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRanges = new HashSet<(string File, int Start, int End)>();

        // 1. Deduplicate retrieved chunks (by file path & line range or exact content)
        var candidates = new List<RetrievedContext>();
        foreach (var chunk in chunks)
        {
            var cleanContent = chunk.ContentPreview.Trim();
            var rangeKey = (chunk.FilePath, chunk.StartLine, chunk.EndLine);

            if (seenRanges.Contains(rangeKey) || seenContent.Contains(cleanContent))
            {
                continue;
            }

            seenRanges.Add(rangeKey);
            seenContent.Add(cleanContent);

            var truncatedContent = cleanContent.Length > maxChunkCharacters
                ? cleanContent[..maxChunkCharacters]
                : cleanContent;

            candidates.Add(new RetrievedContext(
                chunk.ChunkId,
                chunk.FilePath,
                chunk.SymbolName,
                chunk.ChunkType,
                chunk.StartLine,
                chunk.EndLine,
                truncatedContent,
                chunk.RelevanceScore));
        }

        // 2. Lexical Jaccard Overlap Reranking
        var queryWords = GetUniqueWords(query);
        var reranked = candidates
            .Select(candidate =>
            {
                var chunkWords = GetUniqueWords(candidate.Content);
                double jaccard = 0.0;
                if (queryWords.Count > 0 && chunkWords.Count > 0)
                {
                    var intersection = queryWords.Intersect(chunkWords, StringComparer.OrdinalIgnoreCase).Count();
                    var union = queryWords.Union(chunkWords, StringComparer.OrdinalIgnoreCase).Count();
                    jaccard = (double)intersection / union;
                }

                // Weighted combination: 85% embedding cosine similarity, 15% lexical Jaccard overlap
                var combinedScore = candidate.RelevanceScore * 0.85 + jaccard * 0.15;
                return (Candidate: candidate, CombinedScore: combinedScore);
            })
            .OrderByDescending(x => x.CombinedScore)
            .Select(x => x.Candidate with { RelevanceScore = x.CombinedScore })
            .ToList();

        // 3. Token Budgeting & Context Packing
        var packed = new List<RetrievedContext>();
        var currentLength = 0;

        foreach (var candidate in reranked)
        {
            if (packed.Count >= maxChunks)
            {
                break;
            }

            // Estimate character overhead in prompt layout (approximately 150 chars per chunk metadata)
            var chunkLength = candidate.FilePath.Length + candidate.Content.Length + 150;
            if (currentLength + chunkLength > maxPromptCharacters)
            {
                // Truncate if there's still a reasonable budget left
                var remaining = maxPromptCharacters - currentLength - 150;
                if (remaining > 200)
                {
                    var truncatedContent = candidate.Content[..Math.Min(candidate.Content.Length, remaining)];
                    packed.Add(candidate with { Content = truncatedContent });
                    currentLength += truncatedContent.Length + 150;
                }
                break;
            }

            packed.Add(candidate);
            currentLength += chunkLength;
        }

        return packed;
    }

    private static HashSet<string> GetUniqueWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>();
        }

        var punctuation = text.Where(char.IsPunctuation).Distinct().ToArray();
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        
        return new HashSet<string>(
            words.Select(w => w.Trim(punctuation).ToLowerInvariant())
                 .Where(w => w.Length > 2)
        );
    }
}
