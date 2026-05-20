using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Graph;

/// <summary>
/// Provides BFS/DFS traversal, lineage resolution, dependency tree extraction,
/// and impact analysis over the Engineering Knowledge Graph.
/// </summary>
public sealed class GraphTraversalService : IGraphTraversalService
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<GraphTraversalService> _logger;

    public GraphTraversalService(IGraphStore graphStore, ILogger<GraphTraversalService> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<GraphTraversalResult> TraverseAsync(
        GraphTraversalRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting {Strategy} traversal from {StartNodeId} (maxDepth={MaxDepth}).",
            request.Strategy, request.StartNodeId, request.MaxDepth);

        var visitedNodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var collectedEdges = new List<GraphRelationship>();
        int maxDepthReached = 0;

        var startNode = await _graphStore.GetNodeAsync(request.StartNodeId, cancellationToken).ConfigureAwait(false);
        if (startNode == null)
        {
            _logger.LogWarning("Traversal start node {NodeId} not found.", request.StartNodeId);
            return new GraphTraversalResult(
                Array.Empty<GraphNode>(),
                Array.Empty<GraphRelationship>(),
                MaxDepthReached: 0,
                TotalNodesVisited: 0);
        }

        visitedNodes[startNode.NodeId] = startNode;

        if (request.Strategy == GraphTraversalStrategy.BreadthFirst)
        {
            await TraverseBfsAsync(request, visitedNodes, collectedEdges, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await TraverseDfsAsync(request.StartNodeId, 0, request, visitedNodes, collectedEdges, cancellationToken).ConfigureAwait(false);
        }

        maxDepthReached = collectedEdges.Count > 0
            ? CalculateMaxDepth(request.StartNodeId, visitedNodes, collectedEdges)
            : 0;

        var resultNodes = visitedNodes.Values.ToList();
        if (request.NodeKindFilter is { Count: > 0 })
        {
            var kindSet = new HashSet<GraphNodeKind>(request.NodeKindFilter);
            // Always include the start node
            resultNodes = resultNodes
                .Where(n => kindSet.Contains(n.Kind) || string.Equals(n.NodeId, request.StartNodeId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _logger.LogDebug("Traversal complete: {NodeCount} nodes, {EdgeCount} edges, depth={Depth}.",
            resultNodes.Count, collectedEdges.Count, maxDepthReached);

        return new GraphTraversalResult(resultNodes, collectedEdges, maxDepthReached, visitedNodes.Count);
    }

    public async Task<GraphLineageResult> GetLineageAsync(
        GraphLineageRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving lineage for {NodeId} (direction={Direction}, maxDepth={MaxDepth}).",
            request.NodeId, request.Direction, request.MaxDepth);

        var origin = await _graphStore.GetNodeAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
        if (origin == null)
        {
            _logger.LogWarning("Lineage origin node {NodeId} not found.", request.NodeId);
            return new GraphLineageResult(
                new GraphNode("unknown", GraphNodeKind.Repository, "", "Not Found", DateTime.UtcNow),
                Array.Empty<GraphLineageStep>(),
                TotalSteps: 0);
        }

        var chain = new List<GraphLineageStep>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { origin.NodeId };

        // Resolve lineage using BFS to capture all provenance paths
        var queue = new Queue<(string NodeId, GraphRelationship? IncomingEdge, int Depth)>();
        queue.Enqueue((origin.NodeId, null, 0));

        while (queue.Count > 0)
        {
            var (currentId, incomingEdge, depth) = queue.Dequeue();

            if (depth > 0)
            {
                var currentNode = await _graphStore.GetNodeAsync(currentId, cancellationToken).ConfigureAwait(false);
                if (currentNode != null)
                {
                    chain.Add(new GraphLineageStep(currentNode, incomingEdge, depth));
                }
            }

            if (depth >= request.MaxDepth) continue;

            var relationships = await _graphStore.GetRelationshipsAsync(currentId, request.Direction, cancellationToken).ConfigureAwait(false);

            if (request.RelationshipKindFilter is { Count: > 0 })
            {
                var filterSet = new HashSet<GraphRelationshipKind>(request.RelationshipKindFilter);
                relationships = relationships.Where(r => filterSet.Contains(r.Kind)).ToList();
            }

            foreach (var rel in relationships)
            {
                var nextId = string.Equals(rel.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)
                    ? rel.TargetNodeId
                    : rel.SourceNodeId;

                if (visited.Add(nextId))
                {
                    queue.Enqueue((nextId, rel, depth + 1));
                }
            }
        }

        _logger.LogDebug("Lineage resolved: {StepCount} steps from {NodeId}.", chain.Count, request.NodeId);

        return new GraphLineageResult(origin, chain, chain.Count);
    }

    public async Task<GraphTraversalResult> GetDependencyTreeAsync(
        string nodeId,
        int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        return await TraverseAsync(
            new GraphTraversalRequest(
                StartNodeId: nodeId,
                MaxDepth: maxDepth,
                Strategy: GraphTraversalStrategy.BreadthFirst,
                Direction: GraphDirection.Outgoing,
                RelationshipKindFilter: new[] { GraphRelationshipKind.DependsOn }),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GraphTraversalResult> GetImpactAnalysisAsync(
        string nodeId,
        int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        // Reverse traversal: find everything that depends on this node
        return await TraverseAsync(
            new GraphTraversalRequest(
                StartNodeId: nodeId,
                MaxDepth: maxDepth,
                Strategy: GraphTraversalStrategy.BreadthFirst,
                Direction: GraphDirection.Incoming,
                RelationshipKindFilter: new[] { GraphRelationshipKind.DependsOn }),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task TraverseBfsAsync(
        GraphTraversalRequest request,
        Dictionary<string, GraphNode> visitedNodes,
        List<GraphRelationship> collectedEdges,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((request.StartNodeId, 0));

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (depth >= request.MaxDepth) continue;

            var relationships = await _graphStore.GetRelationshipsAsync(currentId, request.Direction, cancellationToken).ConfigureAwait(false);

            if (request.RelationshipKindFilter is { Count: > 0 })
            {
                var filterSet = new HashSet<GraphRelationshipKind>(request.RelationshipKindFilter);
                relationships = relationships.Where(r => filterSet.Contains(r.Kind)).ToList();
            }

            foreach (var rel in relationships)
            {
                collectedEdges.Add(rel);

                var nextId = string.Equals(rel.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)
                    ? rel.TargetNodeId
                    : rel.SourceNodeId;

                if (!visitedNodes.ContainsKey(nextId))
                {
                    var nextNode = await _graphStore.GetNodeAsync(nextId, cancellationToken).ConfigureAwait(false);
                    if (nextNode != null)
                    {
                        visitedNodes[nextId] = nextNode;
                        queue.Enqueue((nextId, depth + 1));
                    }
                }
            }
        }
    }

    private async Task TraverseDfsAsync(
        string currentId,
        int depth,
        GraphTraversalRequest request,
        Dictionary<string, GraphNode> visitedNodes,
        List<GraphRelationship> collectedEdges,
        CancellationToken cancellationToken)
    {
        if (depth >= request.MaxDepth) return;

        var relationships = await _graphStore.GetRelationshipsAsync(currentId, request.Direction, cancellationToken).ConfigureAwait(false);

        if (request.RelationshipKindFilter is { Count: > 0 })
        {
            var filterSet = new HashSet<GraphRelationshipKind>(request.RelationshipKindFilter);
            relationships = relationships.Where(r => filterSet.Contains(r.Kind)).ToList();
        }

        foreach (var rel in relationships)
        {
            collectedEdges.Add(rel);

            var nextId = string.Equals(rel.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)
                ? rel.TargetNodeId
                : rel.SourceNodeId;

            if (!visitedNodes.ContainsKey(nextId))
            {
                var nextNode = await _graphStore.GetNodeAsync(nextId, cancellationToken).ConfigureAwait(false);
                if (nextNode != null)
                {
                    visitedNodes[nextId] = nextNode;
                    await TraverseDfsAsync(nextId, depth + 1, request, visitedNodes, collectedEdges, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static int CalculateMaxDepth(
        string startNodeId,
        Dictionary<string, GraphNode> nodes,
        List<GraphRelationship> edges)
    {
        // Simple BFS depth calculation from start node
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startNodeId };
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((startNodeId, 0));
        int maxDepth = 0;

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!adjacency.ContainsKey(edge.SourceNodeId))
                adjacency[edge.SourceNodeId] = new List<string>();
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        while (queue.Count > 0)
        {
            var (nodeId, depth) = queue.Dequeue();
            maxDepth = Math.Max(maxDepth, depth);

            if (adjacency.TryGetValue(nodeId, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor) && nodes.ContainsKey(neighbor))
                    {
                        queue.Enqueue((neighbor, depth + 1));
                    }
                }
            }
        }

        return maxDepth;
    }
}
