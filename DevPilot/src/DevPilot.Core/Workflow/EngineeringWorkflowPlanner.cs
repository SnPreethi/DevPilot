using System.Security.Cryptography;
using System.Text;
using DevPilot.Contracts;

namespace DevPilot.Core.Workflow;

public sealed class EngineeringWorkflowPlanner : IEngineeringWorkflowPlanner
{
    public Task<EngineeringWorkflowPlan> PlanAsync(
        EngineeringWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            throw new ArgumentException("Workflow objective is required.", nameof(request));
        }

        var objective = request.Objective.Trim();
        var kind = ClassifyKind(objective);
        var risk = ClassifyRisk(kind, request);
        var steps = BuildSteps(kind, request, risk);
        var planId = CreateStableId($"{objective}:{request.RepositoryId}:{request.RepositoryPath}:{kind}");

        var plan = new EngineeringWorkflowPlan(
            PlanId: planId,
            Kind: kind,
            RiskLevel: risk,
            Objective: objective,
            Summary: BuildSummary(kind, risk, steps),
            RepositoryId: request.RepositoryId,
            RepositoryPath: request.RepositoryPath,
            CreatedUtc: DateTime.UtcNow,
            Steps: steps);

        return Task.FromResult(plan);
    }

    private static IReadOnlyList<EngineeringWorkflowStep> BuildSteps(
        EngineeringWorkflowKind kind,
        EngineeringWorkflowRequest request,
        EngineeringWorkflowRiskLevel risk)
    {
        var steps = new List<EngineeringWorkflowStep>();

        AddStep(
            steps,
            "Inspect repository scope",
            EngineeringWorkflowStepKind.RepositoryInspection,
            "Confirm repository boundaries, relevant projects, existing abstractions, and files likely to be affected.",
            new[] { "Repository path", "Solution structure", "Module responsibility documents" },
            new[] { "Scoped repository map", "Candidate modules" });

        AddStep(
            steps,
            "Retrieve engineering context",
            EngineeringWorkflowStepKind.ContextRetrieval,
            "Gather indexed symbols, semantic matches, diagnostics, execution events, and recent workspace memory related to the objective.",
            new[] { "Repository index", "Symbol graph", "Semantic retrieval", "Workspace memory" },
            new[] { "Grounded implementation context" },
            dependsOn: new[] { steps[^1].StepId });

        if (kind == EngineeringWorkflowKind.RepositoryMigration)
        {
            AddStep(
                steps,
                "Build migration inventory",
                EngineeringWorkflowStepKind.ImpactAnalysis,
                "Identify source and target patterns, compatibility risks, impacted projects, and verification commands before drafting edits.",
                new[] { "Current APIs", "Target migration constraints", "Dependency graph" },
                new[] { "Migration inventory", "Compatibility risks" },
                dependsOn: new[] { steps[^1].StepId });
        }
        else
        {
            AddStep(
                steps,
                "Analyze implementation impact",
                EngineeringWorkflowStepKind.ImpactAnalysis,
                "Determine the smallest safe set of contracts, services, storage, prompts, tests, and UI surfaces affected by the work.",
                new[] { "Grounded implementation context", "Diagnostics", "Execution events" },
                new[] { "Impact summary", "Risk notes" },
                dependsOn: new[] { steps[^1].StepId });
        }

        AddStep(
            steps,
            "Draft execution plan",
            EngineeringWorkflowStepKind.PlanDrafting,
            "Create an ordered, dependency-aware workstream with expected file ownership and verification points.",
            new[] { "Impact summary", "Constraints" },
            new[] { "Ordered workstream plan" },
            dependsOn: new[] { steps[^1].StepId });

        AddStep(
            steps,
            "Preview proposed edits",
            EngineeringWorkflowStepKind.PatchPreview,
            "Generate structured patch previews and diffs without writing to the workspace.",
            new[] { "Ordered workstream plan", "Patch engine" },
            new[] { "Diff preview", "Patch validation result" },
            dependsOn: new[] { steps[^1].StepId });

        AddStep(
            steps,
            "Wait for edit approval",
            EngineeringWorkflowStepKind.ApprovalGate,
            "Require explicit approval before applying workspace modifications or running commands with side effects.",
            new[] { "Diff preview", "Verification plan" },
            new[] { "Approval decision" },
            dependsOn: new[] { steps[^1].StepId },
            requiresApproval: true,
            status: EngineeringWorkflowStepStatus.WaitingForApproval,
            approvalReason: "Workspace edits and side-effecting execution must remain user-controlled.");

        AddStep(
            steps,
            "Apply approved changes",
            EngineeringWorkflowStepKind.PatchApplication,
            "Apply only the approved structured edits through the workspace editing engine.",
            new[] { "Approval decision", "Validated edit plan" },
            new[] { "Applied edit result" },
            dependsOn: new[] { steps[^1].StepId });

        AddStep(
            steps,
            "Verify workflow outcome",
            EngineeringWorkflowStepKind.Verification,
            "Run scoped build, tests, diagnostics, or inspection commands appropriate to the changed modules.",
            new[] { "Applied edit result", "Verification commands" },
            new[] { "Verification report" },
            dependsOn: new[] { steps[^1].StepId },
            requiresApproval: risk == EngineeringWorkflowRiskLevel.High,
            status: risk == EngineeringWorkflowRiskLevel.High
                ? EngineeringWorkflowStepStatus.WaitingForApproval
                : EngineeringWorkflowStepStatus.Planned,
            approvalReason: risk == EngineeringWorkflowRiskLevel.High
                ? "High-risk workflows require approval before broader verification commands."
                : null);

        AddStep(
            steps,
            "Record workspace memory",
            EngineeringWorkflowStepKind.MemoryUpdate,
            "Persist the objective, outcome, touched areas, and verification summary for future repository-aware workflows.",
            new[] { "Verification report", "Applied edit result" },
            new[] { "Workspace memory event" },
            dependsOn: new[] { steps[^1].StepId });

        return steps;
    }

    private static void AddStep(
        List<EngineeringWorkflowStep> steps,
        string title,
        EngineeringWorkflowStepKind kind,
        string description,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        IReadOnlyList<string>? dependsOn = null,
        bool requiresApproval = false,
        EngineeringWorkflowStepStatus status = EngineeringWorkflowStepStatus.Planned,
        string? approvalReason = null)
    {
        var index = steps.Count + 1;
        var stepId = $"step-{index:00}-{CreateStableId(title)[..8]}";
        steps.Add(new EngineeringWorkflowStep(
            StepId: stepId,
            Title: title,
            Kind: kind,
            Status: status,
            Description: description,
            Inputs: inputs,
            Outputs: outputs,
            DependsOn: dependsOn ?? Array.Empty<string>(),
            RequiresApproval: requiresApproval,
            ApprovalReason: approvalReason));
    }

    private static EngineeringWorkflowKind ClassifyKind(string objective)
    {
        var text = objective.ToLowerInvariant();

        if (ContainsAny(text, "migrate", "migration", "upgrade", "port "))
            return EngineeringWorkflowKind.RepositoryMigration;
        if (ContainsAny(text, "bug", "fix", "failure", "exception", "crash", "diagnostic"))
            return EngineeringWorkflowKind.BugFix;
        if (ContainsAny(text, "refactor", "restructure", "cleanup", "decouple"))
            return EngineeringWorkflowKind.Refactoring;
        if (ContainsAny(text, "test", "flaky", "coverage"))
            return EngineeringWorkflowKind.TestStabilization;
        if (ContainsAny(text, "document", "docs", "readme"))
            return EngineeringWorkflowKind.Documentation;
        if (ContainsAny(text, "add", "implement", "feature", "workflow", "engine"))
            return EngineeringWorkflowKind.FeatureImplementation;

        return EngineeringWorkflowKind.General;
    }

    private static EngineeringWorkflowRiskLevel ClassifyRisk(
        EngineeringWorkflowKind kind,
        EngineeringWorkflowRequest request)
    {
        if (kind == EngineeringWorkflowKind.RepositoryMigration)
            return EngineeringWorkflowRiskLevel.High;

        if ((request.ExecutionEvents?.Count ?? 0) > 0 || (request.Diagnostics?.Count ?? 0) >= 3)
            return EngineeringWorkflowRiskLevel.Medium;

        if (kind is EngineeringWorkflowKind.Refactoring or EngineeringWorkflowKind.FeatureImplementation)
            return EngineeringWorkflowRiskLevel.Medium;

        return EngineeringWorkflowRiskLevel.Low;
    }

    private static string BuildSummary(
        EngineeringWorkflowKind kind,
        EngineeringWorkflowRiskLevel risk,
        IReadOnlyCollection<EngineeringWorkflowStep> steps)
    {
        return $"{kind} workflow with {steps.Count} deterministic steps, {risk} risk, and explicit approval before workspace edits.";
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.Ordinal));
    }

    private static string CreateStableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
