using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

/// <summary>
/// A traceable evidence block for a specific attributed failure correlation.
/// </summary>
public sealed record AttributionEvidence(
    string NodeId,
    GraphNodeKind NodeKind,
    string Description,
    double ContributionScore);

/// <summary>
/// Result of failure attribution analysis for a targeted failure node.
/// </summary>
public sealed record FailureAttributionResult(
    string FailureNodeId,
    GraphNode? AttributedPatchNode,
    GraphNode? AttributedWorkflowNode,
    double ConfidenceScore,
    IReadOnlyList<AttributionEvidence> EvidenceList,
    string Explanation);

/// <summary>
/// Detail of an affected symbol inside a patch impact analysis.
/// </summary>
public sealed record AffectedSymbol(
    string SymbolNodeId,
    string SymbolLabel,
    string FilePath,
    string ImpactType, // e.g. "Directly Modified", "Downstream Dependency Caller"
    int TransitiveDependencyDepth);

/// <summary>
/// Blast radius and dependency impact projection of a given patch.
/// </summary>
public sealed record PatchImpactResult(
    string PatchNodeId,
    IReadOnlyList<AffectedSymbol> AffectedSymbols,
    IReadOnlyList<string> AffectedFiles,
    double TotalBlastRadiusMetric);

/// <summary>
/// Element step in an execution lineage path.
/// </summary>
public sealed record FailureLineageStep(
    string StageName,
    GraphNode AssociatedNode,
    string RelationshipRole,
    DateTime CreatedUtc);

/// <summary>
/// Result of an execution pipeline lineage trace.
/// </summary>
public sealed record FailureLineageResult(
    string FailureNodeId,
    IReadOnlyList<FailureLineageStep> Steps,
    IReadOnlyList<string> DiagnosticInsights);

/// <summary>
/// Interface for failure attribution logic linking failures to workflows, patches, and symbols.
/// </summary>
public interface IFailureAttributionEngine
{
    Task<FailureAttributionResult> AttributeFailureAsync(string failureNodeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for calculating transitive symbol/module blast-radius of patches.
/// </summary>
public interface IPatchImpactAnalyzer
{
    Task<PatchImpactResult> AnalyzePatchImpactAsync(string patchNodeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for tracing compilation and execution stage lineage steps.
/// </summary>
public interface IFailureLineageResolver
{
    Task<FailureLineageResult> ResolveLineageAsync(string failureNodeId, CancellationToken cancellationToken = default);
}
