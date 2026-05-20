using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Modernization;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ModernizationWorkflowTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SQLiteGraphStore _graphStore;
    private readonly DependencyImpactAnalyzer _impactAnalyzer;
    private readonly ModernizationPlanner _planner;
    private readonly ModernizationEngine _engine;

    public ModernizationWorkflowTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevPilotModTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDirectory);
        
        var dbPath = Path.Combine(_tempDirectory, "mod_test.db");

        var storageSettings = Options.Create(new StorageSettings
        {
            DatabasePath = dbPath,
            CreateIfMissing = true,
            Pooling = false
        });

        var vectorSettings = Options.Create(new VectorSearchSettings
        {
            UseSqliteVss = false
        });

        var factory = new SqliteConnectionFactory(storageSettings);
        
        var dbInit = new DatabaseInitializer(factory, vectorSettings, NullLogger<DatabaseInitializer>.Instance);
        dbInit.InitializeAsync().GetAwaiter().GetResult();

        _graphStore = new SQLiteGraphStore(factory, NullLogger<SQLiteGraphStore>.Instance);
        _impactAnalyzer = new DependencyImpactAnalyzer(_graphStore, NullLogger<DependencyImpactAnalyzer>.Instance);
        _planner = new ModernizationPlanner(NullLogger<ModernizationPlanner>.Instance);
        _engine = new ModernizationEngine(_planner, NullLogger<ModernizationEngine>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore clean up
        }
    }

    [Fact]
    public async Task DependencyImpact_ModelsTargetUpgradeRipples()
    {
        var repoId = "repo-mod-1";
        
        var projNode = new GraphNode("node-proj", GraphNodeKind.Repository, repoId, "DevPilot.Core", DateTime.UtcNow);
        await _graphStore.SaveNodeAsync(projNode);

        var impacts = await _impactAnalyzer.AnalyzeModernizationImpactAsync(repoId, ModernizationType.DotNetUpgrade, "8.0");

        Assert.NotEmpty(impacts);
        var impact = impacts.FirstOrDefault(i => i.TargetElement.Contains("DevPilot.Core"));
        Assert.NotNull(impact);
        Assert.Contains("framework version updated", impact.ImpactDetails);
    }

    [Fact]
    public async Task Planner_BuildsWellStructuredTemplates()
    {
        var repoId = "repo-mod-2";
        
        var plan = await _planner.GeneratePlanAsync(repoId, ModernizationType.DotNetUpgrade, "9.0");

        Assert.Equal(ModernizationPlanStatus.Planned, plan.Status);
        Assert.Equal(ModernizationType.DotNetUpgrade, plan.Type);
        Assert.Equal(2, plan.Steps.Count);
        Assert.True(plan.Steps[0].RequiresApproval);
        Assert.NotEmpty(plan.RollbackSequence);
    }

    [Fact]
    public async Task Engine_ExecutesApprovalGatesAndStepsAndRollbacks()
    {
        var repoId = "repo-mod-3";
        
        var plan = await _engine.GenerateAndRegisterPlanAsync(repoId, ModernizationType.DotNetUpgrade, "8.0");
        Assert.Equal(ModernizationPlanStatus.Planned, plan.Status);

        // Attempting to run a step before approval must fail!
        await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExecuteStepAsync(plan.PlanId, plan.Steps[0].StepId));

        // Approve plan
        var approved = await _engine.ApprovePlanAsync(plan.PlanId);
        Assert.Equal(ModernizationPlanStatus.Approved, approved.Status);

        // Execute first step
        var step1 = await _engine.ExecuteStepAsync(plan.PlanId, plan.Steps[0].StepId);
        Assert.Equal(ModernizationPlanStatus.Executing, step1.Status);
        Assert.True(step1.Steps[0].Completed);

        // Execute second step -> completes workflow plan!
        var step2 = await _engine.ExecuteStepAsync(plan.PlanId, plan.Steps[1].StepId);
        Assert.Equal(ModernizationPlanStatus.Completed, step2.Status);
        Assert.True(step2.Steps[1].Completed);

        // Rollback plan
        var reverted = await _engine.RollbackPlanAsync(plan.PlanId);
        Assert.Equal(ModernizationPlanStatus.RolledBack, reverted.Status);
        Assert.All(reverted.Steps, s => Assert.False(s.Completed));
    }
}
