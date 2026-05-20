using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Reasoning;

public sealed class RootCauseReasoner : IRootCauseReasoner
{
    private readonly IGraphStore _graphStore;
    private readonly IReasoningEvidenceChainBuilder _evidenceBuilder;
    private readonly ILogger<RootCauseReasoner> _logger;

    public RootCauseReasoner(
        IGraphStore graphStore,
        IReasoningEvidenceChainBuilder evidenceBuilder,
        ILogger<RootCauseReasoner> logger)
    {
        _graphStore = graphStore;
        _evidenceBuilder = evidenceBuilder;
        _logger = logger;
    }

    public async Task<RootCauseAnalysisResult> AnalyzeRootCauseAsync(
        string failureNodeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing root cause for failure incident node {FailureNodeId}.", failureNodeId);

        var failureNode = await _graphStore.GetNodeAsync(failureNodeId, cancellationToken).ConfigureAwait(false);
        if (failureNode == null)
        {
            _logger.LogWarning("Failure node {FailureNodeId} was not found.", failureNodeId);
            throw new ArgumentException($"Failure node '{failureNodeId}' not found.", nameof(failureNodeId));
        }

        // Trace lineages upstream to find recently applied changes (Patches, MemoryEvents, Files)
        var relationships = await _graphStore.GetRelationshipsAsync(failureNodeId, GraphDirection.Incoming, cancellationToken).ConfigureAwait(false);

        var evidenceList = new List<ReasoningEvidence>();
        var path = new List<GraphRelationship>();
        GraphNode? suspectedRoot = null;
        double maxScore = 0.0;

        foreach (var rel in relationships)
        {
            var sourceNode = await _graphStore.GetNodeAsync(rel.SourceNodeId, cancellationToken).ConfigureAwait(false);
            if (sourceNode != null)
            {
                path.Add(rel);

                // Compute deterministic evidence scoring:
                // 1. Base score based on node kind (Patch = 0.8, MemoryEvent = 0.7, File = 0.5)
                double baseScore = sourceNode.Kind switch
                {
                    GraphNodeKind.Patch => 0.8,
                    GraphNodeKind.MemoryEvent => 0.7,
                    GraphNodeKind.File => 0.5,
                    _ => 0.3
                };

                // 2. Temporal correlation decay (within 10 minutes)
                var timeDiff = (failureNode.CreatedUtc - sourceNode.CreatedUtc).Duration();
                double timeFactor = timeDiff.TotalMinutes <= 10.0
                    ? 1.0 - (timeDiff.TotalMinutes / 20.0) // closer in time = higher multiplier
                    : 0.5;

                double score = baseScore * timeFactor;
                evidenceList.Add(new ReasoningEvidence(
                    FactId: $"fact-{sourceNode.NodeId}",
                    Description: $"Upstream {sourceNode.Kind} node {sourceNode.Label} is temporally linked to failure via {rel.Kind}.",
                    Score: score,
                    SourceNodeId: sourceNode.NodeId,
                    Kind: sourceNode.Kind.ToString(),
                    Timestamp: sourceNode.CreatedUtc
                ));

                if (score > maxScore)
                {
                    maxScore = score;
                    suspectedRoot = sourceNode;
                }
            }
        }

        // If no upstream node is directly linked, fallback to the incident node itself as root cause
        suspectedRoot ??= failureNode;

        var evidenceChain = _evidenceBuilder.BuildChain(failureNode, suspectedRoot, path, evidenceList);

        var recommendedActions = new List<string>();
        if (suspectedRoot.Kind == GraphNodeKind.Patch)
        {
            recommendedActions.Add($"Review recent patch '{suspectedRoot.Label}' for syntax or logical errors.");
            recommendedActions.Add("Revert patch changes and re-run compilation pipeline verification.");
        }
        else if (suspectedRoot.Kind == GraphNodeKind.MemoryEvent)
        {
            recommendedActions.Add($"Verify file changes recorded in workspace memory event '{suspectedRoot.Label}'.");
            recommendedActions.Add("Run automated integration suite check over the target path.");
        }
        else
        {
            recommendedActions.Add("Inspect the detailed error diagnostics and stacktrace parser output.");
            recommendedActions.Add("Verify module dependencies to identify breaks or circular layers.");
        }

        return new RootCauseAnalysisResult(
            FailureNodeId: failureNodeId,
            SuspectedRootCauseNode: suspectedRoot,
            ConfidenceScore: evidenceChain.TotalConfidenceScore,
            EvidenceChain: evidenceChain,
            RecommendedActions: recommendedActions);
    }
}
