using DevPilot.Contracts;
using DevPilot.Core.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class EngineeringWorkflowPlannerTests
{
    [Fact]
    public async Task PlanAsync_CreatesApprovalAwareFeatureWorkflow()
    {
        var planner = new EngineeringWorkflowPlanner();

        var plan = await planner.PlanAsync(new EngineeringWorkflowRequest(
            Objective: "Implement workflow planning for repository-aware engineering tasks",
            RepositoryId: "repo-1",
            RepositoryPath: "C:\\src\\DevPilot"));

        Assert.Equal(EngineeringWorkflowKind.FeatureImplementation, plan.Kind);
        Assert.Equal(EngineeringWorkflowRiskLevel.Medium, plan.RiskLevel);
        Assert.Contains(plan.Steps, step => step.Kind == EngineeringWorkflowStepKind.ContextRetrieval);
        Assert.Contains(plan.Steps, step => step.Kind == EngineeringWorkflowStepKind.PatchPreview);
        Assert.Contains(plan.Steps, step =>
            step.Kind == EngineeringWorkflowStepKind.ApprovalGate &&
            step.RequiresApproval &&
            step.Status == EngineeringWorkflowStepStatus.WaitingForApproval);
    }

    [Fact]
    public async Task PlanAsync_ClassifiesMigrationAsHighRisk()
    {
        var planner = new EngineeringWorkflowPlanner();

        var plan = await planner.PlanAsync(new EngineeringWorkflowRequest(
            Objective: "Migrate repository from legacy storage contracts to versioned workflow storage"));

        Assert.Equal(EngineeringWorkflowKind.RepositoryMigration, plan.Kind);
        Assert.Equal(EngineeringWorkflowRiskLevel.High, plan.RiskLevel);
        Assert.Contains(plan.Steps, step => step.Title == "Build migration inventory");
        Assert.Contains(plan.Steps, step =>
            step.Kind == EngineeringWorkflowStepKind.Verification &&
            step.RequiresApproval);
    }

    [Fact]
    public async Task PlanAsync_UsesStablePlanIdForSameObjectiveAndRepository()
    {
        var planner = new EngineeringWorkflowPlanner();
        var request = new EngineeringWorkflowRequest(
            Objective: "Fix compiler diagnostic in execution-aware prompt builder",
            RepositoryId: "repo-1",
            RepositoryPath: "C:\\src\\DevPilot");

        var first = await planner.PlanAsync(request);
        var second = await planner.PlanAsync(request);

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.Steps.Select(step => step.StepId), second.Steps.Select(step => step.StepId));
    }

    [Fact]
    public void AddDevPilotCore_RegistersEngineeringWorkflowPlanner()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddDevPilotCore(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IEngineeringWorkflowPlanner>());
    }
}
