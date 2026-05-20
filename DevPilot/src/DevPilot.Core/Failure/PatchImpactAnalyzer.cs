using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Failure;

public sealed class PatchImpactAnalyzer : IPatchImpactAnalyzer
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<PatchImpactAnalyzer> _logger;

    public PatchImpactAnalyzer(IGraphStore graphStore, ILogger<PatchImpactAnalyzer> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<PatchImpactResult> AnalyzePatchImpactAsync(
        string patchNodeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing blast radius for patch {PatchNodeId}.", patchNodeId);

        var patchNode = await _graphStore.GetNodeAsync(patchNodeId, cancellationToken).ConfigureAwait(false);
        if (patchNode == null)
        {
            throw new ArgumentException($"Patch node '{patchNodeId}' not found.", nameof(patchNodeId));
        }

        var affectedSymbols = new List<AffectedSymbol>();
        var affectedFiles = new List<string>();

        // Query modified items
        var relationships = await _graphStore.GetRelationshipsAsync(patchNodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
        var modifiedRels = relationships.Where(r => r.Kind == GraphRelationshipKind.ModifiedBy);

        foreach (var rel in modifiedRels)
        {
            var targetNode = await _graphStore.GetNodeAsync(rel.TargetNodeId, cancellationToken).ConfigureAwait(false);
            if (targetNode != null)
            {
                if (targetNode.Kind == GraphNodeKind.File)
                {
                    affectedFiles.Add(targetNode.Label);

                    // Find symbols belonging to this file
                    var fileNeighbors = await _graphStore.GetNeighborNodesAsync(targetNode.NodeId, GraphDirection.Incoming, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var symbol in fileNeighbors.Where(n => n.Kind == GraphNodeKind.Symbol))
                    {
                        affectedSymbols.Add(new AffectedSymbol(
                            SymbolNodeId: symbol.NodeId,
                            SymbolLabel: symbol.Label,
                            FilePath: targetNode.Label,
                            ImpactType: "Directly Modified",
                            TransitiveDependencyDepth: 0
                        ));

                        // Query downstream transitive callers (Symbol -> calls -> Symbol)
                        var callers = await _graphStore.GetNeighborNodesAsync(symbol.NodeId, GraphDirection.Incoming, new[] { GraphRelationshipKind.Calls }, cancellationToken).ConfigureAwait(false);
                        foreach (var caller in callers)
                        {
                            if (!affectedSymbols.Any(s => string.Equals(s.SymbolNodeId, caller.NodeId, StringComparison.OrdinalIgnoreCase)))
                            {
                                affectedSymbols.Add(new AffectedSymbol(
                                    SymbolNodeId: caller.NodeId,
                                    SymbolLabel: caller.Label,
                                    FilePath: targetNode.Label,
                                    ImpactType: "Downstream Dependency Caller",
                                    TransitiveDependencyDepth: 1
                                ));
                            }
                        }
                    }
                }
                else if (targetNode.Kind == GraphNodeKind.Symbol)
                {
                    affectedSymbols.Add(new AffectedSymbol(
                        SymbolNodeId: targetNode.NodeId,
                        SymbolLabel: targetNode.Label,
                        FilePath: targetNode.Metadata ?? "Unknown",
                        ImpactType: "Directly Modified",
                        TransitiveDependencyDepth: 0
                    ));
                }
            }
        }

        // Blast radius metric = direct modified files + directly modified symbols + (downstream callers * 0.5)
        double blastRadius = affectedFiles.Count + 
                             affectedSymbols.Count(s => s.TransitiveDependencyDepth == 0) +
                             (affectedSymbols.Count(s => s.TransitiveDependencyDepth > 0) * 0.5);

        return new PatchImpactResult(
            PatchNodeId: patchNodeId,
            AffectedSymbols: affectedSymbols,
            AffectedFiles: affectedFiles,
            TotalBlastRadiusMetric: blastRadius
        );
    }
}
