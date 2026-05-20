using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Failure;

public sealed class FailureAttributionEngine : IFailureAttributionEngine
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<FailureAttributionEngine> _logger;

    public FailureAttributionEngine(IGraphStore graphStore, ILogger<FailureAttributionEngine> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<FailureAttributionResult> AttributeFailureAsync(
        string failureNodeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attributing failure {FailureNodeId} deterministically.", failureNodeId);

        var failureNode = await _graphStore.GetNodeAsync(failureNodeId, cancellationToken).ConfigureAwait(false);
        if (failureNode == null)
        {
            throw new ArgumentException($"Failure node '{failureNodeId}' not found.", nameof(failureNodeId));
        }

        var evidence = new List<AttributionEvidence>();
        GraphNode? attributedPatch = null;
        GraphNode? attributedWorkflow = null;

        // Query incoming relationships to trace patch or pipeline triggers
        var incoming = await _graphStore.GetRelationshipsAsync(failureNodeId, GraphDirection.Incoming, cancellationToken).ConfigureAwait(false);

        // 1. Correlate to execution stages & workflows
        var pipelineRel = incoming.FirstOrDefault(r => r.Kind == GraphRelationshipKind.FailedIn);
        if (pipelineRel != null)
        {
            var pipelineNode = await _graphStore.GetNodeAsync(pipelineRel.SourceNodeId, cancellationToken).ConfigureAwait(false);
            if (pipelineNode != null)
            {
                evidence.Add(new AttributionEvidence(
                    NodeId: pipelineNode.NodeId,
                    NodeKind: GraphNodeKind.ExecutionPipeline,
                    Description: $"Execution failure caught inside pipeline stage '{pipelineNode.Label}'",
                    ContributionScore: 0.4
                ));

                // Find Task -> Workflow links
                var pipelineOutgoing = await _graphStore.GetRelationshipsAsync(pipelineNode.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                var taskRel = pipelineOutgoing.FirstOrDefault(r => r.Kind == GraphRelationshipKind.GeneratedBy);
                if (taskRel != null)
                {
                    var taskNode = await _graphStore.GetNodeAsync(taskRel.TargetNodeId, cancellationToken).ConfigureAwait(false);
                    if (taskNode != null)
                    {
                        var taskOutgoing = await _graphStore.GetRelationshipsAsync(taskNode.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                        var workflowRel = taskOutgoing.FirstOrDefault(r => r.Kind == GraphRelationshipKind.BelongsTo);
                        if (workflowRel != null)
                        {
                            attributedWorkflow = await _graphStore.GetNodeAsync(workflowRel.TargetNodeId, cancellationToken).ConfigureAwait(false);
                            if (attributedWorkflow != null)
                            {
                                evidence.Add(new AttributionEvidence(
                                    NodeId: attributedWorkflow.NodeId,
                                    NodeKind: GraphNodeKind.Workflow,
                                    Description: $"Assigned task belongs to active engineering workflow '{attributedWorkflow.Label}'",
                                    ContributionScore: 0.2
                                ));
                            }
                        }
                    }
                }
            }
        }

        // 2. Correlate to active patches
        var patchRel = incoming.FirstOrDefault(r => r.Kind == GraphRelationshipKind.IntroducedBy || r.Kind == GraphRelationshipKind.FailedIn);
        // Fallback: search for any active patch within 10 minutes temporal close window
        var patches = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Patch, cancellationToken: cancellationToken).ConfigureAwait(false);
        var recentPatch = patches
            .Where(p => (failureNode.CreatedUtc - p.CreatedUtc).Duration().TotalMinutes <= 10.0)
            .OrderBy(p => (failureNode.CreatedUtc - p.CreatedUtc).Duration())
            .FirstOrDefault();

        if (recentPatch != null)
        {
            attributedPatch = recentPatch;
            var timeDiff = (failureNode.CreatedUtc - recentPatch.CreatedUtc).Duration();
            double tempScore = 0.4 * (1.0 - (timeDiff.TotalMinutes / 10.0));
            tempScore = Math.Clamp(tempScore, 0.1, 0.4);

            evidence.Add(new AttributionEvidence(
                NodeId: recentPatch.NodeId,
                NodeKind: GraphNodeKind.Patch,
                Description: $"Applied patch '{recentPatch.Label}' was submitted {timeDiff.TotalMinutes:F1} minutes before failure",
                ContributionScore: tempScore
            ));
        }

        double confidence = Math.Clamp(evidence.Sum(e => e.ContributionScore), 0.0, 1.0);
        string explanation = attributedPatch != null
            ? $"Failure '{failureNode.Label}' is attributed to patch '{attributedPatch.Label}' with {confidence * 100:F0}% confidence due to close temporal proximity and stage failure triggers."
            : $"Failure '{failureNode.Label}' is isolated to pipeline execution with {confidence * 100:F0}% confidence; no recent patch correlates directly.";

        return new FailureAttributionResult(
            FailureNodeId: failureNodeId,
            AttributedPatchNode: attributedPatch,
            AttributedWorkflowNode: attributedWorkflow,
            ConfidenceScore: confidence,
            EvidenceList: evidence,
            Explanation: explanation
        );
    }
}
