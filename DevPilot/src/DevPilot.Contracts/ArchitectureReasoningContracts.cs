using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

/// <summary>
/// A rule outlining which target layers this layer is allowed to reference.
/// </summary>
public sealed record LayerBoundaryRule(
    string SourceLayerName,
    IReadOnlyList<string> AllowedTargetLayers);

/// <summary>
/// A diagnosed violation of the layered architecture rules.
/// </summary>
public sealed record ArchitectureViolation(
    string SourceNodeId,
    string SourceLabel,
    string TargetNodeId,
    string TargetLabel,
    string RuleDescription,
    string ViolationType, // e.g. "Direct Layer Bypass", "Cyclic Reference"
    double SeverityScore); // 0.0 (low) to 1.0 (critical)

/// <summary>
/// A code convention violation mapped to a specific EKG node.
/// </summary>
public sealed record ConventionViolation(
    string NodeId,
    string NodeLabel,
    string FilePath,
    string RuleViolated, // e.g. "InterfacePrefix", "AsyncSuffix", "PrivateFieldPrefix"
    string ExpectedFormat,
    string FoundFormat);

/// <summary>
/// Project step projection when migrating a dependency module.
/// </summary>
public sealed record MigrationStep(
    string SymbolNodeId,
    string SymbolLabel,
    string ActionRequired, // e.g. "Re-route call", "Refactor interface", "Deprecate signature"
    double RiskScore);

/// <summary>
/// Transitive impact projection when migrating a specific library or package dependency.
/// </summary>
public sealed record MigrationImpactResult(
    string SourceModuleId,
    string TargetModuleId,
    IReadOnlyList<MigrationStep> Steps,
    double TotalMigrationComplexityScore);

/// <summary>
/// Complete overview of the architecture integrity and repository drift state.
/// </summary>
public sealed record ArchitectureAnalysisSummary(
    string RepositoryId,
    IReadOnlyList<ArchitectureViolation> Violations,
    IReadOnlyList<ConventionViolation> ConventionViolations,
    double ArchitecturalDriftScore, // 0.0 (pristine) to 1.0 (highly degraded)
    string SummaryExplanation);

/// <summary>
/// Interface for detecting layer dependency boundary violations.
/// </summary>
public interface IDependencyBoundaryAnalyzer
{
    Task<IReadOnlyList<ArchitectureViolation>> AnalyzeBoundariesAsync(string repositoryId, IReadOnlyList<LayerBoundaryRule> rules, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for finding coding style/convention naming violations.
/// </summary>
public interface IConventionViolationAnalyzer
{
    Task<IReadOnlyList<ConventionViolation>> AnalyzeConventionsAsync(string repositoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for forecasting migration impact steps across components.
/// </summary>
public interface IMigrationImpactAnalyzer
{
    Task<MigrationImpactResult> AnalyzeMigrationImpactAsync(string sourceModuleId, string targetModuleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrator for coordinating all architectural reasoning services.
/// </summary>
public interface IArchitectureReasoningEngine
{
    Task<ArchitectureAnalysisSummary> RunFullAnalysisAsync(string repositoryId, CancellationToken cancellationToken = default);
}
