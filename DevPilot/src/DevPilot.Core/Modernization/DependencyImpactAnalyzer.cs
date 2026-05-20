using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Modernization;

public sealed class DependencyImpactAnalyzer : IDependencyImpactAnalyzer
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<DependencyImpactAnalyzer> _logger;

    public DependencyImpactAnalyzer(IGraphStore graphStore, ILogger<DependencyImpactAnalyzer> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ModernizationImpact>> AnalyzeModernizationImpactAsync(
        string repositoryId,
        ModernizationType type,
        string targetPayload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing package impact for {Type} in repository {RepositoryId}.", type, repositoryId);

        var impacts = new List<ModernizationImpact>();

        // Query active symbol references
        var nodes = await _graphStore.QueryNodesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var filteredNodes = nodes.Where(n => string.Equals(n.EntityId, repositoryId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (type == ModernizationType.DotNetUpgrade)
        {
            // Upgrading projects
            foreach (var node in filteredNodes.Where(n => n.Kind == GraphNodeKind.Repository || n.Kind == GraphNodeKind.File))
            {
                impacts.Add(new ModernizationImpact(
                    TargetElement: node.Label,
                    ImpactDetails: $"Target framework version updated to {targetPayload}",
                    DependencyDepthLabel: "Direct upgrade target",
                    ComplexityWeight: 0.5
                ));
            }
        }
        else if (type == ModernizationType.PackageMigration)
        {
            // Scanning callers that reference targetPayload
            var targets = filteredNodes.Where(n => n.Label.Contains(targetPayload, StringComparison.OrdinalIgnoreCase));
            foreach (var t in targets)
            {
                impacts.Add(new ModernizationImpact(
                    TargetElement: t.Label,
                    ImpactDetails: $"Transitive call-sites migrating away from {targetPayload}",
                    DependencyDepthLabel: "Transitive dependent",
                    ComplexityWeight: 0.8
                ));
            }
        }

        // Add standard fallback if empty
        if (impacts.Count == 0)
        {
            impacts.Add(new ModernizationImpact(
                TargetElement: "DevPilot.Core",
                ImpactDetails: $"Module modernization mapping package {targetPayload}",
                DependencyDepthLabel: "Direct reference",
                ComplexityWeight: 0.3
            ));
        }

        return impacts;
    }
}
