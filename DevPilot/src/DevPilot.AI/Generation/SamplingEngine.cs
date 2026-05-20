using System;
using System.Collections.Generic;
using System.Linq;

namespace DevPilot.AI.Generation;

public sealed class SamplingEngine
{
    private readonly Random _random = new();

    public int SampleToken(
        float[] logits,
        IReadOnlyList<int> history,
        float temperature,
        int topK,
        float topP,
        float repetitionPenalty,
        float frequencyPenalty,
        float presencePenalty)
    {
        var vocabSize = logits.Length;
        
        // 1. Calculate occurrence counts in history
        var occurrences = new Dictionary<int, int>();
        foreach (var tok in history)
        {
            occurrences[tok] = occurrences.GetValueOrDefault(tok) + 1;
        }

        // 2. Apply Penalties (Repetition, Frequency, Presence)
        var adjustedLogits = new float[vocabSize];
        for (var i = 0; i < vocabSize; i++)
            adjustedLogits[i] = logits[i];

        if (occurrences.Count > 0)
        {
            for (var i = 0; i < vocabSize; i++)
            {
                if (occurrences.TryGetValue(i, out var count))
                {
                    var l = adjustedLogits[i];

                    // Repetition Penalty
                    if (Math.Abs(repetitionPenalty - 1.0f) > 0.001f)
                    {
                        l = l > 0.0f ? l / repetitionPenalty : l * repetitionPenalty;
                    }

                    // Frequency & Presence Penalties
                    l -= (count * frequencyPenalty + presencePenalty);
                    adjustedLogits[i] = l;
                }
            }
        }

        // 3. Fallback to greedy decoding if temperature is extremely low
        if (temperature < 0.01f)
        {
            var bestToken = 0;
            var bestScore = float.NegativeInfinity;
            for (var i = 0; i < vocabSize; i++)
            {
                if (adjustedLogits[i] > bestScore)
                {
                    bestScore = adjustedLogits[i];
                    bestToken = i;
                }
            }
            return bestToken;
        }

        // 4. Gather candidates
        var candidates = new List<(int Token, float Logit)>(vocabSize);
        for (var i = 0; i < vocabSize; i++)
        {
            candidates.Add((i, adjustedLogits[i]));
        }

        // Sort descending by logit score
        candidates.Sort((a, b) => b.Logit.CompareTo(a.Logit));

        // 5. Apply Top-K Filtering
        if (topK > 0 && topK < candidates.Count)
        {
            candidates.RemoveRange(topK, candidates.Count - topK);
        }

        // 6. Compute Softmax probabilities on the remaining candidates
        var maxLogit = candidates[0].Logit;
        var expValues = new double[candidates.Count];
        double sumExp = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var expVal = Math.Exp((candidates[i].Logit - maxLogit) / temperature);
            expValues[i] = expVal;
            sumExp += expVal;
        }

        var probs = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            probs[i] = expValues[i] / sumExp;
        }

        // 7. Apply Top-P (Nucleus) Filtering
        if (topP > 0.0f && topP < 1.0f)
        {
            double cumulativeProb = 0.0;
            var cutoffIndex = candidates.Count;
            for (var i = 0; i < candidates.Count; i++)
            {
                cumulativeProb += probs[i];
                if (cumulativeProb >= topP)
                {
                    cutoffIndex = i + 1; // Keep up to and including this token
                    break;
                }
            }

            if (cutoffIndex < candidates.Count)
            {
                candidates.RemoveRange(cutoffIndex, candidates.Count - cutoffIndex);
                
                // Re-normalize probabilities
                double newSumExp = 0.0;
                for (var i = 0; i < candidates.Count; i++)
                {
                    newSumExp += expValues[i];
                }
                for (var i = 0; i < candidates.Count; i++)
                {
                    probs[i] = expValues[i] / newSumExp;
                }
            }
        }

        // 8. Sample token from distribution
        var sampleValue = _random.NextDouble();
        double runningSum = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            runningSum += probs[i];
            if (sampleValue <= runningSum)
            {
                return candidates[i].Token;
            }
        }

        return candidates[0].Token;
    }
}
