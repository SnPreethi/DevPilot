using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Graph;
using DevPilot.Core.Reasoning;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class ReasoningEngineTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SQLiteGraphStore _graphStore;
    private readonly GraphTraversalService _traversalService;
    private readonly ReasoningEvidenceChainBuilder _evidenceBuilder;
    private readonly EngineeringCorrelationEngine _correlationEngine;
    private readonly RootCauseReasoner _rootCauseReasoner;
    private readonly ContextRankingEngine _rankingEngine;

    public ReasoningEngineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevPilotReasoningTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDirectory);
        
        var dbPath = Path.Combine(_tempDirectory, "reasoning_test.db");

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
        _traversalService = new GraphTraversalService(_graphStore, NullLogger<GraphTraversalService>.Instance);
        _evidenceBuilder = new ReasoningEvidenceChainBuilder();
        _correlationEngine = new EngineeringCorrelationEngine(_graphStore, NullLogger<EngineeringCorrelationEngine>.Instance);
        _rootCauseReasoner = new RootCauseReasoner(_graphStore, _evidenceBuilder, NullLogger<RootCauseReasoner>.Instance);
        _rankingEngine = new ContextRankingEngine(_graphStore, NullLogger<ContextRankingEngine>.Instance);
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
            // Ignore clean up errors
        }
    }

    [Fact]
    public async Task Correlation_FailureToWorkflow_ResolvesTopologicalLinkage()
    {
        // 1. Arrange schema nodes
        var repoId = "test-repo";
        var workflow = new GraphNode("wf-1", GraphNodeKind.Workflow, repoId, "Fix compiler breaks", DateTime.UtcNow);
        var task = new GraphNode("task-1", GraphNodeKind.Task, repoId, "Apply resumable edits", DateTime.UtcNow);
        var pipeline = new GraphNode("pipe-1", GraphNodeKind.ExecutionPipeline, repoId, "MSBuild target compile", DateTime.UtcNow);
        var failure = new GraphNode("fail-1", GraphNodeKind.Failure, repoId, "Compilation error CS0246", DateTime.UtcNow);

        await _graphStore.SaveNodeAsync(workflow);
        await _graphStore.SaveNodeAsync(task);
        await _graphStore.SaveNodeAsync(pipeline);
        await _graphStore.SaveNodeAsync(failure);

        // Save linkage relationships
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel1", "fail-1", "pipe-1", GraphRelationshipKind.FailedIn, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel2", "pipe-1", "task-1", GraphRelationshipKind.GeneratedBy, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel3", "task-1", "wf-1", GraphRelationshipKind.BelongsTo, DateTime.UtcNow));

        // 2. Act
        var results = await _correlationEngine.CorrelateFailuresToWorkflowsAsync(repoId);

        // 3. Assert
        Assert.Single(results);
        var corr = results[0];
        Assert.Equal("fail-1", corr.SourceEntityId);
        Assert.Equal("wf-1", corr.TargetEntityId);
        Assert.Equal("failed_within_workflow", corr.RelationKind);
        Assert.True(corr.Confidence >= 0.9);
        Assert.Contains("MSBuild target compile", corr.Rationale);
    }

    [Fact]
    public async Task Correlation_PatchToDiagnostic_TracksModifications()
    {
        var repoId = "test-repo";
        var patch = new GraphNode("patch-1", GraphNodeKind.Patch, repoId, "Patch apply line 22", DateTime.UtcNow);
        var file = new GraphNode("file-1", GraphNodeKind.File, repoId, "DatabaseInitializer.cs", DateTime.UtcNow);
        var diagnostic = new GraphNode("diag-1", GraphNodeKind.Diagnostic, repoId, "CS1002 Missing semicolon", DateTime.UtcNow);

        await _graphStore.SaveNodeAsync(patch);
        await _graphStore.SaveNodeAsync(file);
        await _graphStore.SaveNodeAsync(diagnostic);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel4", "patch-1", "file-1", GraphRelationshipKind.ModifiedBy, DateTime.UtcNow));
        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel5", "file-1", "diag-1", GraphRelationshipKind.Violates, DateTime.UtcNow));

        var results = await _correlationEngine.CorrelatePatchesToDiagnosticsAsync(repoId);

        Assert.Single(results);
        var corr = results[0];
        Assert.Equal("patch-1", corr.SourceEntityId);
        Assert.Equal("diag-1", corr.TargetEntityId);
        Assert.Contains("DatabaseInitializer.cs", corr.Rationale);
    }

    [Fact]
    public async Task RootCause_TombstoneFailure_IdentifiesRecentPatch()
    {
        var repoId = "test-repo";
        var failure = new GraphNode("fail-2", GraphNodeKind.Failure, repoId, "CS0103 The name does not exist", DateTime.UtcNow);
        var patch = new GraphNode("patch-2", GraphNodeKind.Patch, repoId, "Refactored DI scopes", DateTime.UtcNow.AddMinutes(-2));

        await _graphStore.SaveNodeAsync(failure);
        await _graphStore.SaveNodeAsync(patch);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel6", "patch-2", "fail-2", GraphRelationshipKind.FailedIn, DateTime.UtcNow));

        var rootCause = await _rootCauseReasoner.AnalyzeRootCauseAsync("fail-2");

        Assert.Equal("fail-2", rootCause.FailureNodeId);
        Assert.Equal("patch-2", rootCause.SuspectedRootCauseNode.NodeId);
        Assert.True(rootCause.ConfidenceScore > 0.0);
        Assert.NotEmpty(rootCause.RecommendedActions);
        Assert.Contains(rootCause.RecommendedActions, action => action.Contains("Revert patch changes"));
    }

    [Fact]
    public async Task ContextRanking_SortsByTopologicalAndTemporalProximity()
    {
        var repoId = "test-repo";
        var target = new GraphNode("tgt", GraphNodeKind.Symbol, repoId, "Core target", DateTime.UtcNow);
        var cand1 = new GraphNode("cand1", GraphNodeKind.Symbol, repoId, "Direct caller", DateTime.UtcNow.AddMinutes(-5));
        var cand2 = new GraphNode("cand2", GraphNodeKind.Symbol, repoId, "Distant node", DateTime.UtcNow.AddHours(-5));

        await _graphStore.SaveNodeAsync(target);
        await _graphStore.SaveNodeAsync(cand1);
        await _graphStore.SaveNodeAsync(cand2);

        await _graphStore.SaveRelationshipAsync(new GraphRelationship("rel7", "cand1", "tgt", GraphRelationshipKind.Calls, DateTime.UtcNow));

        var candidates = new[] { cand1, cand2 };
        var ranked = await _rankingEngine.RankContextAsync("tgt", candidates);

        Assert.Equal(2, ranked.Count);
        Assert.Equal("cand1", ranked[0].Node.NodeId);
        Assert.True(ranked[0].RankScore > ranked[1].RankScore);
        Assert.Contains("Directly connected neighbor in Knowledge Graph", ranked[0].Reasons);
    }
}
