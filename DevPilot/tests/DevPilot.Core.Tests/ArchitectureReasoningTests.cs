using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Architecture;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ArchitectureReasoningTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SQLiteGraphStore _graphStore;
    private readonly DependencyBoundaryAnalyzer _boundaryAnalyzer;
    private readonly ConventionViolationAnalyzer _conventionAnalyzer;
    private readonly MigrationImpactAnalyzer _migrationAnalyzer;
    private readonly ArchitectureReasoningEngine _reasoningEngine;

    public ArchitectureReasoningTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevPilotArchTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDirectory);
        
        var dbPath = Path.Combine(_tempDirectory, "arch_test.db");

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
        _boundaryAnalyzer = new DependencyBoundaryAnalyzer(_graphStore, NullLogger<DependencyBoundaryAnalyzer>.Instance);
        _conventionAnalyzer = new ConventionViolationAnalyzer(_graphStore, NullLogger<ConventionViolationAnalyzer>.Instance);
        _migrationAnalyzer = new MigrationImpactAnalyzer(_graphStore, NullLogger<MigrationImpactAnalyzer>.Instance);
        _reasoningEngine = new ArchitectureReasoningEngine(_boundaryAnalyzer, _conventionAnalyzer, NullLogger<ArchitectureReasoningEngine>.Instance);
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
    public async Task DependencyBoundary_DetectsIllegalBypassBetweenLayers()
    {
        var repoId = "repo-arch-1";
        
        // Setup nodes: source layer Core calling target layer LocalService (illegal bypass)
        var coreNode = new GraphNode("node-core", GraphNodeKind.Symbol, repoId, "CoreLogicService", DateTime.UtcNow)
        {
            Metadata = "src/DevPilot.Core/Services"
        };
        var serviceNode = new GraphNode("node-service", GraphNodeKind.Symbol, repoId, "LocalServiceEndpoint", DateTime.UtcNow)
        {
            Metadata = "src/DevPilot.LocalService/Controllers"
        };

        await _graphStore.SaveNodeAsync(coreNode);
        await _graphStore.SaveNodeAsync(serviceNode);

        // Core calls LocalService (a bottom-to-top illegal bypass!)
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rela1", "node-core", "node-service", GraphRelationshipKind.Calls, DateTime.UtcNow));

        var rules = new[]
        {
            new LayerBoundaryRule("Contracts", Array.Empty<string>()),
            new LayerBoundaryRule("Core", new[] { "Contracts" }),
            new LayerBoundaryRule("LocalService", new[] { "Contracts", "Core" })
        };

        var violations = await _boundaryAnalyzer.AnalyzeBoundariesAsync(repoId, rules);

        Assert.Single(violations);
        var violation = violations[0];
        Assert.Equal("node-core", violation.SourceNodeId);
        Assert.Equal("node-service", violation.TargetNodeId);
        Assert.Contains("not allowed to reference 'LocalService'", violation.RuleDescription);
        Assert.True(violation.SeverityScore >= 0.7);
    }

    [Fact]
    public async Task ConventionAnalyzer_IdentifiesNamingViolations()
    {
        var repoId = "repo-arch-2";
        
        var badInterface = new GraphNode("sym-i1", GraphNodeKind.Symbol, repoId, "interface BadInterfaceWithoutIPrefix", DateTime.UtcNow)
        {
            Metadata = "src/DevPilot.Contracts/IBad.cs"
        };
        var badAsync = new GraphNode("sym-a1", GraphNodeKind.Symbol, repoId, "async Task RunCalculations(int id)", DateTime.UtcNow)
        {
            Metadata = "src/DevPilot.Core/Engine.cs"
        };

        await _graphStore.SaveNodeAsync(badInterface);
        await _graphStore.SaveNodeAsync(badAsync);

        var violations = await _conventionAnalyzer.AnalyzeConventionsAsync(repoId);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.RuleViolated == "InterfacePrefix");
        Assert.Contains(violations, v => v.RuleViolated == "AsyncSuffix");
    }

    [Fact]
    public async Task MigrationImpact_ProjectsRefactoringRippleSteps()
    {
        var repoId = "repo-arch-3";
        
        var depNode = new GraphNode("sym-dep", GraphNodeKind.Symbol, repoId, "OldLegacyMethod()", DateTime.UtcNow);
        var callerNode = new GraphNode("sym-caller", GraphNodeKind.Symbol, repoId, "MainController()", DateTime.UtcNow);

        await _graphStore.SaveNodeAsync(depNode);
        await _graphStore.SaveNodeAsync(callerNode);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("relm1", "sym-caller", "sym-dep", GraphRelationshipKind.Calls, DateTime.UtcNow));

        var result = await _migrationAnalyzer.AnalyzeMigrationImpactAsync("sym-dep", "sym-new-dep");

        Assert.Equal("sym-dep", result.SourceModuleId);
        Assert.Equal("sym-new-dep", result.TargetModuleId);
        Assert.True(result.Steps.Count >= 2);
        Assert.Contains(result.Steps, s => s.SymbolNodeId == "sym-dep" && s.ActionRequired.Contains("Deprecate"));
        Assert.Contains(result.Steps, s => s.SymbolNodeId == "sym-caller" && s.ActionRequired.Contains("Update"));
        Assert.True(result.TotalMigrationComplexityScore > 0);
    }
}
