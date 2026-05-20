using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Architecture;

public sealed class DependencyBoundaryAnalyzer : IDependencyBoundaryAnalyzer
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<DependencyBoundaryAnalyzer> _logger;

    public DependencyBoundaryAnalyzer(IGraphStore graphStore, ILogger<DependencyBoundaryAnalyzer> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArchitectureViolation>> AnalyzeBoundariesAsync(
        string repositoryId,
        IReadOnlyList<LayerBoundaryRule> rules,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing architectural boundary rules for repository {RepositoryId}.", repositoryId);

        var violations = new List<ArchitectureViolation>();

        // Query all active symbol calls/references inside the repository EKG
        var allNodes = await _graphStore.QueryNodesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var nodes = allNodes.Where(n => string.Equals(n.EntityId, repositoryId, StringComparison.OrdinalIgnoreCase)).ToList();
        var relationships = await _graphStore.QueryRelationshipsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // Map node ID to layer name
        var layerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var layerName = GetLayerFromNode(node);
            if (layerName != null)
            {
                layerMap[node.NodeId] = layerName;
            }
        }

        // Trace calls/dependencies violating the rules
        foreach (var rel in relationships.Where(r => r.Kind == GraphRelationshipKind.Calls || r.Kind == GraphRelationshipKind.DependsOn))
        {
            if (layerMap.TryGetValue(rel.SourceNodeId, out var srcLayer) &&
                layerMap.TryGetValue(rel.TargetNodeId, out var targetLayer))
            {
                if (string.Equals(srcLayer, targetLayer, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Internal layer references are pristine
                }

                // Check rules
                var rule = rules.FirstOrDefault(r => string.Equals(r.SourceLayerName, srcLayer, StringComparison.OrdinalIgnoreCase));
                if (rule != null)
                {
                    if (!rule.AllowedTargetLayers.Contains(targetLayer, StringComparer.OrdinalIgnoreCase))
                    {
                        var srcNode = nodes.FirstOrDefault(n => n.NodeId == rel.SourceNodeId);
                        var targetNode = nodes.FirstOrDefault(n => n.NodeId == rel.TargetNodeId);

                        violations.Add(new ArchitectureViolation(
                            SourceNodeId: rel.SourceNodeId,
                            SourceLabel: srcNode?.Label ?? rel.SourceNodeId,
                            TargetNodeId: rel.TargetNodeId,
                            TargetLabel: targetNode?.Label ?? rel.TargetNodeId,
                            RuleDescription: $"Layer '{srcLayer}' is not allowed to reference '{targetLayer}'. Allowed: [{string.Join(", ", rule.AllowedTargetLayers)}]",
                            ViolationType: "Direct Layer Bypass",
                            SeverityScore: 0.8
                        ));
                    }
                }
            }
        }

        return violations;
    }

    private string? GetLayerFromNode(GraphNode node)
    {
        // Infer layer from metadata paths (e.g. src/DevPilot.Core -> layer: Core)
        var path = node.Metadata;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = node.Label; // Fallback to label
        }

        if (path.Contains("DevPilot.LocalService", StringComparison.OrdinalIgnoreCase) || path.Contains("LocalService", StringComparison.OrdinalIgnoreCase))
        {
            return "LocalService";
        }
        if (path.Contains("DevPilot.Core", StringComparison.OrdinalIgnoreCase) || path.Contains("Core", StringComparison.OrdinalIgnoreCase))
        {
            return "Core";
        }
        if (path.Contains("DevPilot.Contracts", StringComparison.OrdinalIgnoreCase) || path.Contains("Contracts", StringComparison.OrdinalIgnoreCase))
        {
            return "Contracts";
        }

        return null;
    }
}
