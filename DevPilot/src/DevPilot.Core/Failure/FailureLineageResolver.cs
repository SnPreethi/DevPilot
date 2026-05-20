using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Failure;

public sealed class FailureLineageResolver : IFailureLineageResolver
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<FailureLineageResolver> _logger;

    public FailureLineageResolver(IGraphStore graphStore, ILogger<FailureLineageResolver> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<FailureLineageResult> ResolveLineageAsync(
        string failureNodeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving execution lineage for failure {FailureNodeId}.", failureNodeId);

        var failureNode = await _graphStore.GetNodeAsync(failureNodeId, cancellationToken).ConfigureAwait(false);
        if (failureNode == null)
        {
            throw new ArgumentException($"Failure node '{failureNodeId}' not found.", nameof(failureNodeId));
        }

        var steps = new List<FailureLineageStep>();
        var diagnostics = new List<string>();

        // Step 1: Add failure origin itself
        steps.Add(new FailureLineageStep(
            StageName: "Failure Incident Isolated",
            AssociatedNode: failureNode,
            RelationshipRole: "Origin Incident",
            CreatedUtc: failureNode.CreatedUtc
        ));

        // Step 2: Query direct incoming pipelines or applied patches
        var incomingRels = await _graphStore.GetRelationshipsAsync(failureNodeId, GraphDirection.Incoming, cancellationToken).ConfigureAwait(false);
        
        foreach (var rel in incomingRels)
        {
            var source = await _graphStore.GetNodeAsync(rel.SourceNodeId, cancellationToken).ConfigureAwait(false);
            if (source != null)
            {
                if (source.Kind == GraphNodeKind.ExecutionPipeline)
                {
                    steps.Add(new FailureLineageStep(
                        StageName: "Pipeline Integration Run",
                        AssociatedNode: source,
                        RelationshipRole: "Failed Execution Host",
                        CreatedUtc: source.CreatedUtc
                    ));

                    diagnostics.Add($"Compiler/Test failure captured in pipeline stage execution '{source.Label}'.");

                    // Trace Task
                    var pipelineOutgoing = await _graphStore.GetRelationshipsAsync(source.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                    var taskRel = pipelineOutgoing.FirstOrDefault(r => r.Kind == GraphRelationshipKind.GeneratedBy);
                    if (taskRel != null)
                    {
                        var taskNode = await _graphStore.GetNodeAsync(taskRel.TargetNodeId, cancellationToken).ConfigureAwait(false);
                        if (taskNode != null)
                        {
                            steps.Add(new FailureLineageStep(
                                StageName: "Workflow Task Advanced",
                                AssociatedNode: taskNode,
                                RelationshipRole: "Assigned Task context",
                                CreatedUtc: taskNode.CreatedUtc
                            ));
                        }
                    }
                }
                else if (source.Kind == GraphNodeKind.Patch)
                {
                    steps.Add(new FailureLineageStep(
                        StageName: "Patch Code Modified",
                        AssociatedNode: source,
                        RelationshipRole: "Active applied patch",
                        CreatedUtc: source.CreatedUtc
                    ));

                    diagnostics.Add($"Patch '{source.Label}' was recently written and applied before the failure occurred.");
                }
            }
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add("No direct compiler diagnostics isolated; check general pipeline execution state.");
        }

        return new FailureLineageResult(
            FailureNodeId: failureNodeId,
            Steps: steps.OrderByDescending(s => s.CreatedUtc).ToList(),
            DiagnosticInsights: diagnostics
        );
    }
}
