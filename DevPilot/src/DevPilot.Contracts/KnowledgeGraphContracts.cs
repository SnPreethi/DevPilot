using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

/// <summary>
/// Classifies the kind of entity a graph node represents.
/// Each value maps 1:1 to an existing DevPilot subsystem entity.
/// </summary>
public enum GraphNodeKind
{
    Repository,
    File,
    Symbol,
    Workflow,
    Task,
    ExecutionPipeline,
    Diagnostic,
    Failure,
    Patch,
    MemoryEvent
}

/// <summary>
/// Classifies the semantic meaning of a directed edge between two graph nodes.
/// </summary>
public enum GraphRelationshipKind
{
    DependsOn,
    Calls,
    ModifiedBy,
    FailedIn,
    GeneratedBy,
    IntroducedBy,
    FixedBy,
    RelatedTo,
    Violates,
    BelongsTo
}

/// <summary>
/// Direction hint used when querying relationships relative to a node.
/// </summary>
public enum GraphDirection
{
    Outgoing,
    Incoming,
    Both
}

/// <summary>
/// Controls the traversal algorithm used by the graph traversal service.
/// </summary>
public enum GraphTraversalStrategy
{
    BreadthFirst,
    DepthFirst
}

/// <summary>
/// A lightweight node in the Engineering Knowledge Graph.
/// It is a reference-projection of an existing entity, not a data duplicate.
/// </summary>
public sealed record GraphNode(
    string NodeId,
    GraphNodeKind Kind,
    string EntityId,
    string Label,
    DateTime CreatedUtc,
    string? Metadata = null);

/// <summary>
/// A directed, typed edge between two graph nodes.
/// </summary>
public sealed record GraphRelationship(
    string RelationshipId,
    string SourceNodeId,
    string TargetNodeId,
    GraphRelationshipKind Kind,
    DateTime CreatedUtc,
    string? Metadata = null);

/// <summary>
/// Flexible traversal request for exploring the graph from a starting node.
/// </summary>
public sealed record GraphTraversalRequest(
    string StartNodeId,
    int MaxDepth = 3,
    GraphTraversalStrategy Strategy = GraphTraversalStrategy.BreadthFirst,
    GraphDirection Direction = GraphDirection.Outgoing,
    IReadOnlyList<GraphRelationshipKind>? RelationshipKindFilter = null,
    IReadOnlyList<GraphNodeKind>? NodeKindFilter = null);

/// <summary>
/// Result of a graph traversal, containing discovered nodes and edges.
/// </summary>
public sealed record GraphTraversalResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphRelationship> Edges,
    int MaxDepthReached,
    int TotalNodesVisited);

/// <summary>
/// Lineage request: trace provenance chains upstream or downstream from a node.
/// </summary>
public sealed record GraphLineageRequest(
    string NodeId,
    GraphDirection Direction = GraphDirection.Both,
    int MaxDepth = 10,
    IReadOnlyList<GraphRelationshipKind>? RelationshipKindFilter = null);

/// <summary>
/// A single step in a lineage chain, representing one hop in the provenance path.
/// </summary>
public sealed record GraphLineageStep(
    GraphNode Node,
    GraphRelationship? IncomingEdge,
    int Depth);

/// <summary>
/// Result of a lineage resolution, containing the ordered provenance chain.
/// </summary>
public sealed record GraphLineageResult(
    GraphNode Origin,
    IReadOnlyList<GraphLineageStep> Chain,
    int TotalSteps);

/// <summary>
/// Rich query request for the POST /graph/query endpoint.
/// Supports filtering by node kind, relationship kind, label, and metadata substring.
/// </summary>
public sealed record GraphQueryRequest(
    GraphNodeKind? NodeKindFilter = null,
    GraphRelationshipKind? RelationshipKindFilter = null,
    string? LabelContains = null,
    string? MetadataContains = null,
    string? EntityId = null,
    int MaxResults = 50);

/// <summary>
/// Result of a graph query, containing matched nodes and their relationships.
/// </summary>
public sealed record GraphQueryResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphRelationship> Relationships,
    int TotalMatches);

/// <summary>
/// Persistence contract for graph nodes and edges stored in SQLite.
/// </summary>
public interface IGraphStore
{
    Task SaveNodeAsync(GraphNode node, CancellationToken cancellationToken = default);
    Task SaveRelationshipAsync(GraphRelationship relationship, CancellationToken cancellationToken = default);
    Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    Task<GraphNode?> GetNodeByEntityAsync(string entityId, GraphNodeKind kind, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(string nodeId, GraphDirection direction = GraphDirection.Both, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNode>> QueryNodesAsync(GraphNodeKind? kind = null, string? labelContains = null, string? metadataContains = null, string? entityId = null, int maxResults = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(GraphRelationshipKind? kind = null, string? sourceNodeId = null, string? targetNodeId = null, int maxResults = 50, CancellationToken cancellationToken = default);
    Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    Task DeleteRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNode>> GetNeighborNodesAsync(string nodeId, GraphDirection direction = GraphDirection.Outgoing, IReadOnlyList<GraphRelationshipKind>? kindFilter = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service contract for graph traversal, lineage resolution, and dependency analysis.
/// </summary>
public interface IGraphTraversalService
{
    Task<GraphTraversalResult> TraverseAsync(GraphTraversalRequest request, CancellationToken cancellationToken = default);
    Task<GraphLineageResult> GetLineageAsync(GraphLineageRequest request, CancellationToken cancellationToken = default);
    Task<GraphTraversalResult> GetDependencyTreeAsync(string nodeId, int maxDepth = 5, CancellationToken cancellationToken = default);
    Task<GraphTraversalResult> GetImpactAnalysisAsync(string nodeId, int maxDepth = 5, CancellationToken cancellationToken = default);
}
