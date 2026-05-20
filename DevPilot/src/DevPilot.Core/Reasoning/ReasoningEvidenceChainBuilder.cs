using System;
using System.Collections.Generic;
using System.Linq;
using DevPilot.Contracts;

namespace DevPilot.Core.Reasoning;

public sealed class ReasoningEvidenceChainBuilder : IReasoningEvidenceChainBuilder
{
    public EvidenceChain BuildChain(
        GraphNode startNode,
        GraphNode endNode,
        IReadOnlyList<GraphRelationship> path,
        IReadOnlyList<ReasoningEvidence> evidence)
    {
        var nodes = new List<GraphNode> { startNode };
        var uniqueNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startNode.NodeId };

        foreach (var rel in path)
        {
            if (uniqueNodes.Add(rel.SourceNodeId))
            {
                // In a real traversal, we would lookup the node, but for the evidence trace,
                // we can represent them placeholders if not explicitly present, or simply assume they are collected.
            }
            uniqueNodes.Add(rel.TargetNodeId);
        }

        // Compute total confidence based on the scores of the evidence list.
        // We sum the scores and cap at 1.0, with a floor of 0.0.
        double totalScore = evidence.Sum(e => e.Score);
        double confidence = Math.Clamp(totalScore, 0.0, 1.0);

        return new EvidenceChain(
            Nodes: new List<GraphNode>(nodes),
            Edges: path,
            EvidenceList: evidence,
            TotalConfidenceScore: confidence);
    }
}
