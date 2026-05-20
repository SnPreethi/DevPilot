using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

/// <summary>
/// Represents a single piece of evidence discovered during engineering correlation or root-cause reasoning.
/// </summary>
public sealed record ReasoningEvidence(
    string FactId,
    string Description,
    double Score,
    string? SourceNodeId = null,
    string? Kind = null,
    DateTime? Timestamp = null);

/// <summary>
/// A traceable chain of evidence showing topological connections, paths, and facts.
/// </summary>
public sealed record EvidenceChain(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphRelationship> Edges,
    IReadOnlyList<ReasoningEvidence> EvidenceList,
    double TotalConfidenceScore);

/// <summary>
/// Represents a discovered correlation between two disparate engineering entities.
/// </summary>
public sealed record CorrelationResult(
    string SourceEntityId,
    GraphNodeKind SourceKind,
    string TargetEntityId,
    GraphNodeKind TargetKind,
    string RelationKind,
    double Confidence,
    string Rationale);

/// <summary>
/// Result of a root cause analysis trace from a failure/diagnostic incident.
/// </summary>
public sealed record RootCauseAnalysisResult(
    string FailureNodeId,
    GraphNode SuspectedRootCauseNode,
    double ConfidenceScore,
    EvidenceChain EvidenceChain,
    IReadOnlyList<string> RecommendedActions);

/// <summary>
/// Represents an item ranked in significance relative to an engineering target.
/// </summary>
public sealed record ContextRankedItem(
    GraphNode Node,
    double RankScore,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Interface for building traceable and explainable reasoning evidence chains.
/// </summary>
public interface IReasoningEvidenceChainBuilder
{
    EvidenceChain BuildChain(
        GraphNode startNode,
        GraphNode endNode,
        IReadOnlyList<GraphRelationship> path,
        IReadOnlyList<ReasoningEvidence> evidence);
}

/// <summary>
/// Correlates different events (failures, patches, repository commits, and architecture violations) deterministically.
/// </summary>
public interface IEngineeringCorrelationEngine
{
    Task<IReadOnlyList<CorrelationResult>> CorrelateFailuresToWorkflowsAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrelationResult>> CorrelatePatchesToDiagnosticsAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrelationResult>> CorrelateExecutionToChangesAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CorrelationResult>> CorrelateArchitectureViolationsAsync(string repositoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs lineage-aware walks of the Engineering Knowledge Graph to isolate the root cause of an incident.
/// </summary>
public interface IRootCauseReasoner
{
    Task<RootCauseAnalysisResult> AnalyzeRootCauseAsync(string failureNodeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Scores and prioritizes context nodes based on topological, temporal, and metadata similarity.
/// </summary>
public interface IContextRankingEngine
{
    Task<IReadOnlyList<ContextRankedItem>> RankContextAsync(
        string targetNodeId,
        IReadOnlyList<GraphNode> candidates,
        CancellationToken cancellationToken = default);
}
