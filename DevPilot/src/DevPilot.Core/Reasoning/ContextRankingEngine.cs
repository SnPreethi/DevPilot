using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Reasoning;

public sealed class ContextRankingEngine : IContextRankingEngine
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<ContextRankingEngine> _logger;

    public ContextRankingEngine(IGraphStore graphStore, ILogger<ContextRankingEngine> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ContextRankedItem>> RankContextAsync(
        string targetNodeId,
        IReadOnlyList<GraphNode> candidates,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ranking {Count} context candidates relative to target {TargetNodeId}.", candidates.Count, targetNodeId);

        var targetNode = await _graphStore.GetNodeAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        if (targetNode == null)
        {
            _logger.LogWarning("Target node {TargetNodeId} was not found; returning flat ranks.", targetNodeId);
            return candidates.Select(c => new ContextRankedItem(c, 0.5, new[] { "Target node not resolved" })).ToList();
        }

        // Get direct relationships to calculate topological closeness
        var relationships = await _graphStore.GetRelationshipsAsync(targetNodeId, GraphDirection.Both, cancellationToken).ConfigureAwait(false);
        var directConnectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in relationships)
        {
            directConnectedIds.Add(string.Equals(r.SourceNodeId, targetNodeId, StringComparison.OrdinalIgnoreCase) ? r.TargetNodeId : r.SourceNodeId);
        }

        var ranked = new List<ContextRankedItem>();

        foreach (var cand in candidates)
        {
            double score = 0.0;
            var reasons = new List<string>();

            // 1. Topological factor
            if (string.Equals(cand.NodeId, targetNodeId, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.5;
                reasons.Add("Self identity context match");
            }
            else if (directConnectedIds.Contains(cand.NodeId))
            {
                score += 0.3;
                reasons.Add("Directly connected neighbor in Knowledge Graph");
            }

            // 2. Temporal similarity decay (closeness in time)
            var timeDiff = (cand.CreatedUtc - targetNode.CreatedUtc).Duration();
            if (timeDiff.TotalHours <= 1.0)
            {
                double tempScore = 0.2 * (1.0 - (timeDiff.TotalMinutes / 60.0));
                score += tempScore;
                reasons.Add($"Captured within {timeDiff.TotalMinutes:F1} minutes of target incident");
            }

            // 3. Metadata overlap (similar naming conventions)
            if (!string.IsNullOrEmpty(cand.Metadata) && !string.IsNullOrEmpty(targetNode.Metadata))
            {
                if (cand.Metadata.Contains(targetNode.Metadata, StringComparison.OrdinalIgnoreCase) ||
                    targetNode.Metadata.Contains(cand.Metadata, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.2;
                    reasons.Add("Overlapping directory paths or file metadata signatures");
                }
            }

            if (cand.Label.Split(' ').Any(word => targetNode.Label.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.1;
                reasons.Add("Overlapping terminology in node labels");
            }

            // Cap final score at 1.0, floor at 0.0
            double finalScore = Math.Clamp(score, 0.0, 1.0);
            if (reasons.Count == 0) reasons.Add("Default topological context distance fallback");

            ranked.Add(new ContextRankedItem(cand, finalScore, reasons));
        }

        return ranked.OrderByDescending(r => r.RankScore).ToList();
    }
}
