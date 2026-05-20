using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Failure;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class FailureAttributionEngineTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SQLiteGraphStore _graphStore;
    private readonly FailureAttributionEngine _attributionEngine;
    private readonly PatchImpactAnalyzer _patchImpactAnalyzer;
    private readonly FailureLineageResolver _lineageResolver;

    public FailureAttributionEngineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevPilotFailureTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDirectory);
        
        var dbPath = Path.Combine(_tempDirectory, "failure_test.db");

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
        _attributionEngine = new FailureAttributionEngine(_graphStore, NullLogger<FailureAttributionEngine>.Instance);
        _patchImpactAnalyzer = new PatchImpactAnalyzer(_graphStore, NullLogger<PatchImpactAnalyzer>.Instance);
        _lineageResolver = new FailureLineageResolver(_graphStore, NullLogger<FailureLineageResolver>.Instance);
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
    public async Task Attribution_CorrelatesFailureToRecentPatchAndWorkflow()
    {
        var repoId = "repo-attr";
        var failure = new GraphNode("fail-3", GraphNodeKind.Failure, repoId, "Build failure CS0117", DateTime.UtcNow);
        var pipeline = new GraphNode("pipe-3", GraphNodeKind.ExecutionPipeline, repoId, "Pipeline Stage Compile", DateTime.UtcNow);
        var task = new GraphNode("task-3", GraphNodeKind.Task, repoId, "Task compilation", DateTime.UtcNow);
        var workflow = new GraphNode("wf-3", GraphNodeKind.Workflow, repoId, "Workflow main repair", DateTime.UtcNow);
        var patch = new GraphNode("patch-3", GraphNodeKind.Patch, repoId, "Applied namespace patch", DateTime.UtcNow.AddMinutes(-3));

        await _graphStore.SaveNodeAsync(failure);
        await _graphStore.SaveNodeAsync(pipeline);
        await _graphStore.SaveNodeAsync(task);
        await _graphStore.SaveNodeAsync(workflow);
        await _graphStore.SaveNodeAsync(patch);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("r1", "pipe-3", "fail-3", GraphRelationshipKind.FailedIn, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("r2", "pipe-3", "fail-3", GraphRelationshipKind.FailedIn, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("r3", "pipe-3", "task-3", GraphRelationshipKind.GeneratedBy, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("r4", "task-3", "wf-3", GraphRelationshipKind.BelongsTo, DateTime.UtcNow));

        var result = await _attributionEngine.AttributeFailureAsync("fail-3");

        Assert.Equal("fail-3", result.FailureNodeId);
        Assert.NotNull(result.AttributedPatchNode);
        Assert.Equal("patch-3", result.AttributedPatchNode.NodeId);
        Assert.NotNull(result.AttributedWorkflowNode);
        Assert.Equal("wf-3", result.AttributedWorkflowNode.NodeId);
        Assert.True(result.ConfidenceScore > 0.0);
        Assert.Contains("namespace patch", result.Explanation);
    }

    [Fact]
    public async Task PatchImpact_CalculatesDirectAndTransitiveBlastRadius()
    {
        var repoId = "repo-impact";
        var patch = new GraphNode("patch-4", GraphNodeKind.Patch, repoId, "Patch active line", DateTime.UtcNow);
        var file = new GraphNode("file-4", GraphNodeKind.File, repoId, "StorageInitializer.cs", DateTime.UtcNow);
        var symbolA = new GraphNode("sym-a", GraphNodeKind.Symbol, repoId, "InitializeDatabase", DateTime.UtcNow);
        var symbolB = new GraphNode("sym-b", GraphNodeKind.Symbol, repoId, "RunMigrations", DateTime.UtcNow);

        await _graphStore.SaveNodeAsync(patch);
        await _graphStore.SaveNodeAsync(file);
        await _graphStore.SaveNodeAsync(symbolA);
        await _graphStore.SaveNodeAsync(symbolB);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("ri1", "patch-4", "file-4", GraphRelationshipKind.ModifiedBy, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("ri2", "sym-a", "file-4", GraphRelationshipKind.FixedBy, DateTime.UtcNow)); // symbol belongs to file (incoming link)
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("ri3", "sym-b", "sym-a", GraphRelationshipKind.Calls, DateTime.UtcNow)); // B calls A (incoming link for A neighbors)

        var result = await _patchImpactAnalyzer.AnalyzePatchImpactAsync("patch-4");

        Assert.Equal("patch-4", result.PatchNodeId);
        Assert.Single(result.AffectedFiles);
        Assert.Equal("StorageInitializer.cs", result.AffectedFiles[0]);
        Assert.Contains(result.AffectedSymbols, s => string.Equals(s.SymbolNodeId, "sym-a"));
        Assert.Contains(result.AffectedSymbols, s => string.Equals(s.SymbolNodeId, "sym-b"));
        Assert.True(result.TotalBlastRadiusMetric > 0);
    }

    [Fact]
    public async Task FailureLineage_ResolvesExecutionStageSteps()
    {
        var repoId = "repo-lineage";
        var failure = new GraphNode("fail-5", GraphNodeKind.Failure, repoId, "Fatal compilation break", DateTime.UtcNow);
        var pipeline = new GraphNode("pipe-5", GraphNodeKind.ExecutionPipeline, repoId, "MSBuild Compile Stage", DateTime.UtcNow);

        await _graphStore.SaveNodeAsync(failure);
        await _graphStore.SaveNodeAsync(pipeline);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rl1", "pipe-5", "fail-5", GraphRelationshipKind.FailedIn, DateTime.UtcNow));

        var result = await _lineageResolver.ResolveLineageAsync("fail-5");

        Assert.Equal("fail-5", result.FailureNodeId);
        Assert.True(result.Steps.Count >= 2);
        Assert.Contains(result.Steps, s => s.StageName.Contains("Incident Isolated"));
        Assert.Contains(result.Steps, s => s.StageName.Contains("Pipeline Integration"));
        Assert.Contains(result.DiagnosticInsights, d => d.Contains("captured in pipeline stage"));
    }
}
