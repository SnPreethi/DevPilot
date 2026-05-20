using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public enum ModernizationType
{
    DotNetUpgrade,
    PackageMigration,
    AuthMigration,
    ApiModernization,
    FrameworkReplacement,
    DependencyCleanup
}

public enum ModernizationPlanStatus
{
    Planned,
    Approved,
    Executing,
    Completed,
    RolledBack,
    Failed
}

public sealed record ModernizationStep(
    string StepId,
    string Title,
    string Description,
    string ActionPayload, // e.g. target package name or upgrade version
    bool RequiresApproval,
    bool Completed = false,
    string? ErrorMessage = null);

public sealed record ModernizationPlan(
    string PlanId,
    string RepositoryId,
    ModernizationType Type,
    ModernizationPlanStatus Status,
    IReadOnlyList<ModernizationStep> Steps,
    IReadOnlyList<string> RollbackSequence,
    double RiskScore,
    string PlanExplanation);

public sealed record ModernizationImpact(
    string TargetElement,
    string ImpactDetails,
    string DependencyDepthLabel,
    double ComplexityWeight);

public interface IDependencyImpactAnalyzer
{
    Task<IReadOnlyList<ModernizationImpact>> AnalyzeModernizationImpactAsync(
        string repositoryId,
        ModernizationType type,
        string targetPayload,
        CancellationToken cancellationToken = default);
}

public interface IModernizationPlanner
{
    Task<ModernizationPlan> GeneratePlanAsync(
        string repositoryId,
        ModernizationType type,
        string targetPayload,
        CancellationToken cancellationToken = default);
}

public interface IModernizationEngine
{
    Task<ModernizationPlan> ApprovePlanAsync(string planId, CancellationToken cancellationToken = default);
    Task<ModernizationPlan> ExecuteStepAsync(string planId, string stepId, CancellationToken cancellationToken = default);
    Task<ModernizationPlan> RollbackPlanAsync(string planId, CancellationToken cancellationToken = default);
    Task<ModernizationPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken = default);
}
