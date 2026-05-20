using System;
using System.Collections.Generic;
using System.Linq;

namespace DevPilot.AI.PromptPipeline;

public sealed class StopSequenceDetector
{
    private readonly string[] _stopSequences;
    private readonly int _maxLength;

    public StopSequenceDetector(IEnumerable<string>? stopSequences)
    {
        _stopSequences = stopSequences?
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray() ?? Array.Empty<string>();
        
        _maxLength = _stopSequences.Length > 0 
            ? _stopSequences.Max(s => s.Length) 
            : 0;
    }

    public bool ShouldStop(string accumulatedText, out string matchedSequence)
    {
        matchedSequence = string.Empty;
        if (_stopSequences.Length == 0 || string.IsNullOrEmpty(accumulatedText))
        {
            return false;
        }

        // We check the trailing end of the accumulated text up to the max stop sequence length
        var checkLength = Math.Min(accumulatedText.Length, _maxLength + 5);
        var tail = accumulatedText[^checkLength..];

        foreach (var stopSeq in _stopSequences)
        {
            if (tail.Contains(stopSeq, StringComparison.OrdinalIgnoreCase))
            {
                matchedSequence = stopSeq;
                return true;
            }
        }

        return false;
    }
}
