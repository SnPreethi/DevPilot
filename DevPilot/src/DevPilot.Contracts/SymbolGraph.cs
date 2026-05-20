using System;
using System.Collections.Generic;
using System.Linq;

namespace DevPilot.Contracts;

public sealed class SymbolGraph
{
    public IReadOnlyList<SymbolIndexEntry> Nodes { get; }
    public IReadOnlyList<SymbolRelation> Edges { get; }

    private readonly Dictionary<string, SymbolIndexEntry> _nodesById;
    private readonly Dictionary<string, List<SymbolRelation>> _outgoingEdges;
    private readonly Dictionary<string, List<SymbolRelation>> _incomingEdges;

    public SymbolGraph(IReadOnlyList<SymbolIndexEntry> nodes, IReadOnlyList<SymbolRelation> edges)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Edges = edges ?? throw new ArgumentNullException(nameof(edges));

        _nodesById = nodes.ToDictionary(n => n.SymbolId, StringComparer.OrdinalIgnoreCase);
        _outgoingEdges = edges.GroupBy(e => e.FromSymbolId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _incomingEdges = edges.GroupBy(e => e.ToSymbolId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public SymbolIndexEntry? FindSymbolById(string id)
    {
        return _nodesById.TryGetValue(id, out var node) ? node : null;
    }

    public IReadOnlyList<SymbolIndexEntry> GetReferences(string symbolId)
    {
        if (_outgoingEdges.TryGetValue(symbolId, out var edges))
        {
            return edges.Where(e => e.RelationType == "reference")
                .Select(e => FindSymbolById(e.ToSymbolId))
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();
        }
        return Array.Empty<SymbolIndexEntry>();
    }

    public IReadOnlyList<SymbolIndexEntry> GetReferencedBy(string symbolId)
    {
        if (_incomingEdges.TryGetValue(symbolId, out var edges))
        {
            return edges.Where(e => e.RelationType == "reference")
                .Select(e => FindSymbolById(e.FromSymbolId))
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();
        }
        return Array.Empty<SymbolIndexEntry>();
    }

    public IReadOnlyList<SymbolIndexEntry> GetChildren(string parentSymbolId)
    {
        if (_incomingEdges.TryGetValue(parentSymbolId, out var edges))
        {
            return edges.Where(e => e.RelationType == "membership")
                .Select(e => FindSymbolById(e.FromSymbolId))
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();
        }
        return Array.Empty<SymbolIndexEntry>();
    }

    public SymbolIndexEntry? GetParent(string symbolId)
    {
        if (_outgoingEdges.TryGetValue(symbolId, out var edges))
        {
            var edge = edges.FirstOrDefault(e => e.RelationType == "membership");
            return edge != null ? FindSymbolById(edge.ToSymbolId) : null;
        }
        return null;
    }
}
