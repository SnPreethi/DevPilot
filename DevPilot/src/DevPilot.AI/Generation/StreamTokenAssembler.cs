using System;
using System.Collections.Generic;
using System.Linq;

namespace DevPilot.AI.Generation;

public sealed class StreamTokenAssembler
{
    private readonly List<int> _tokenIds = new();
    private string _flushedText = string.Empty;
    private int _consecutiveNonBoundaryTokens = 0;

    public IEnumerable<string> Append(int tokenId, ILlmTokenizer tokenizer)
    {
        _tokenIds.Add(tokenId);

        // 1. Decode the cumulative history of tokens together to resolve BPE wordpieces and surrogates
        var fullText = tokenizer.Decode(_tokenIds);

        // 2. No new text produced
        if (fullText.Length <= _flushedText.Length)
        {
            yield break;
        }

        // 3. Extract the new candidate text suffix
        var candidate = fullText[_flushedText.Length..];

        // 4. Scan backwards to locate the latest safe boundary (whitespace, newlines, punctuation)
        var lastBoundaryIdx = -1;
        for (var i = candidate.Length - 1; i >= 0; i--)
        {
            var ch = candidate[i];
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                lastBoundaryIdx = i;
                break;
            }
        }

        // 5. If a boundary is found, flush the complete prefix immediately
        if (lastBoundaryIdx >= 0)
        {
            var chunk = candidate[..(lastBoundaryIdx + 1)];
            _flushedText += chunk;
            _consecutiveNonBoundaryTokens = 0;
            yield return chunk;
        }
        else
        {
            _consecutiveNonBoundaryTokens++;

            // To avoid excessive latency during very long words/names/indents,
            // force flush if the buffer exceeds length limits (e.g. 4 tokens or 16 chars)
            if (_consecutiveNonBoundaryTokens >= 4 || candidate.Length >= 16)
            {
                _flushedText += candidate;
                _consecutiveNonBoundaryTokens = 0;
                yield return candidate;
            }
        }
    }

    public string Flush(ILlmTokenizer tokenizer)
    {
        var fullText = tokenizer.Decode(_tokenIds);
        if (fullText.Length > _flushedText.Length)
        {
            var remaining = fullText[_flushedText.Length..];
            _flushedText = fullText;
            _consecutiveNonBoundaryTokens = 0;
            return remaining;
        }
        return string.Empty;
    }
}
