using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Modernization;

public sealed class ModernizationPlanner : IModernizationPlanner
{
    private readonly ILogger<ModernizationPlanner> _logger;

    public ModernizationPlanner(ILogger<ModernizationPlanner> logger)
    {
        _logger = logger;
    }

    public Task<ModernizationPlan> GeneratePlanAsync(
        string repositoryId,
        ModernizationType type,
        string targetPayload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating modernization blueprint plan for {Type} targeting {Payload}.", type, targetPayload);

        var steps = new List<ModernizationStep>();
        var rollbacks = new List<string>();
        double risk = 0.3;
        string explanation = "";

        switch (type)
        {
            case ModernizationType.DotNetUpgrade:
                risk = 0.8;
                explanation = $"Upgrade codebase targets to .NET {targetPayload} cleanly.";
                steps.Add(new ModernizationStep("step-up-1", "Update project target frameworks", $"Change TargetFramework element in csproj to net{targetPayload}", targetPayload, RequiresApproval: true));
                steps.Add(new ModernizationStep("step-up-2", "Update Nuget dependencies compatibility", "Revise all external libraries to support upgraded frameworks", targetPayload, RequiresApproval: false));
                rollbacks.Add("Revert TARGET_FRAMEWORK tags inside csproj projects using negative patch edits");
                rollbacks.Add("Restore previous package version configs");
                break;

            case ModernizationType.PackageMigration:
                risk = 0.6;
                explanation = $"Migrate package dependency references targeting {targetPayload}.";
                steps.Add(new ModernizationStep("step-pkg-1", "Swap out legacy imports", $"Re-route project references using {targetPayload}", targetPayload, RequiresApproval: true));
                steps.Add(new ModernizationStep("step-pkg-2", "Resolve signature call-sites", "Correct deprecated method configurations", targetPayload, RequiresApproval: false));
                rollbacks.Add("Revert package reference substitutions and imports");
                break;

            case ModernizationType.AuthMigration:
                risk = 0.9;
                explanation = $"Migrate authentication mechanisms to use {targetPayload}.";
                steps.Add(new ModernizationStep("step-auth-1", "Swap active middleware configurations", $"Register authentication handlers for {targetPayload}", targetPayload, RequiresApproval: true));
                steps.Add(new ModernizationStep("step-auth-2", "Update identity claims handling", "Update token parsers and controller mappings", targetPayload, RequiresApproval: false));
                rollbacks.Add("Restore legacy security middlewares");
                break;

            default:
                explanation = $"Modernization execution for {type} targeting {targetPayload}.";
                steps.Add(new ModernizationStep("step-gen-1", "Draft core changes", $"Apply modifications for {type}", targetPayload, RequiresApproval: true));
                rollbacks.Add("Discard modernization staging edits");
                break;
        }

        var planId = $"modplan-{type.ToString().ToLower()}-{Guid.NewGuid().ToString()[..8]}";

        return Task.FromResult(new ModernizationPlan(
            PlanId: planId,
            RepositoryId: repositoryId,
            Type: type,
            Status: ModernizationPlanStatus.Planned,
            Steps: steps,
            RollbackSequence: rollbacks,
            RiskScore: risk,
            PlanExplanation: explanation
        ));
    }
}
