using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Architecture;

public sealed class MigrationImpactAnalyzer : IMigrationImpactAnalyzer
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<MigrationImpactAnalyzer> _logger;

    public MigrationImpactAnalyzer(IGraphStore graphStore, ILogger<MigrationImpactAnalyzer> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<MigrationImpactResult> AnalyzeMigrationImpactAsync(
        string sourceModuleId,
        string targetModuleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing migration impact from {SourceModuleId} to {TargetModuleId}.", sourceModuleId, targetModuleId);

        var steps = new List<MigrationStep>();

        // Query active symbols in EKG
        var nodes = await _graphStore.QueryNodesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var sourceNode = nodes.FirstOrDefault(n => string.Equals(n.NodeId, sourceModuleId, StringComparison.OrdinalIgnoreCase));

        if (sourceNode != null)
        {
            // Direct modified symbol
            steps.Add(new MigrationStep(
                SymbolNodeId: sourceNode.NodeId,
                SymbolLabel: sourceNode.Label,
                ActionRequired: "Deprecate signature and re-route references to target module",
                RiskScore: 0.7
            ));

            // Transitive callers
            var callers = await _graphStore.GetNeighborNodesAsync(sourceNode.NodeId, GraphDirection.Incoming, new[] { GraphRelationshipKind.Calls }, cancellationToken).ConfigureAwait(false);
            foreach (var caller in callers)
            {
                steps.Add(new MigrationStep(
                    SymbolNodeId: caller.NodeId,
                    SymbolLabel: caller.Label,
                    ActionRequired: "Update dependency call-site signature",
                    RiskScore: 0.4
                ));
            }
        }
        else
        {
            // Placeholder step
            steps.Add(new MigrationStep(
                SymbolNodeId: sourceModuleId,
                SymbolLabel: sourceModuleId,
                ActionRequired: "Update generic dependency package reference",
                RiskScore: 0.2
            ));
        }

        double totalComplexity = steps.Sum(s => s.RiskScore);

        return new MigrationImpactResult(
            SourceModuleId: sourceModuleId,
            TargetModuleId: targetModuleId,
            Steps: steps,
            TotalMigrationComplexityScore: totalComplexity
        );
    }
}
