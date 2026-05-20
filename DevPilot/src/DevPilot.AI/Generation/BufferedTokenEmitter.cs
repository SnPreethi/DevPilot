using System;
using System.Collections.Generic;

namespace DevPilot.AI.Generation;

public sealed class BufferedTokenEmitter
{
    private readonly StreamTokenAssembler _assembler = new();
    private readonly UtfSafeTextAccumulator _utfAccumulator = new();
    private string _carryOverText = string.Empty;

    public IEnumerable<string> ProcessToken(int tokenId, ILlmTokenizer tokenizer)
    {
        var rawChunks = _assembler.Append(tokenId, tokenizer);
        
        foreach (var chunk in rawChunks)
        {
            var combined = _carryOverText + chunk;
            var safePrefix = _utfAccumulator.GetSafePrefix(combined, out var unsafeSuffix);
            _carryOverText = unsafeSuffix;

            if (!string.IsNullOrEmpty(safePrefix))
            {
                yield return safePrefix;
            }
        }
    }

    public string Flush(ILlmTokenizer tokenizer)
    {
        var remainingRaw = _assembler.Flush(tokenizer);
        var combined = _carryOverText + remainingRaw;
        _carryOverText = string.Empty;
        return combined;
    }
}
