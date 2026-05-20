using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.Core.Graph;
using DevPilot.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class GraphTraversalServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SQLiteGraphStore _store;
    private readonly GraphTraversalService _service;

    public GraphTraversalServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DevPilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var dbPath = Path.Combine(_tempDir, "test.db");
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
        var initializer = new DatabaseInitializer(factory, vectorSettings, NullLogger<DatabaseInitializer>.Instance);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        _store = new SQLiteGraphStore(factory, NullLogger<SQLiteGraphStore>.Instance);
        _service = new GraphTraversalService(_store, NullLogger<GraphTraversalService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static GraphNode MakeNode(string id, GraphNodeKind kind = GraphNodeKind.Symbol) =>
        new(id, kind, $"entity-{id}", $"Label {id}", DateTime.UtcNow);

    private static GraphRelationship MakeEdge(string id, string source, string target, GraphRelationshipKind kind = GraphRelationshipKind.DependsOn) =>
        new(id, source, target, kind, DateTime.UtcNow);

    private async Task SeedLinearChain(string prefix, int length, GraphRelationshipKind kind = GraphRelationshipKind.DependsOn)
    {
        for (int i = 0; i < length; i++)
        {
            await _store.SaveNodeAsync(MakeNode($"{prefix}-{i}"));
        }
        for (int i = 0; i < length - 1; i++)
        {
            await _store.SaveRelationshipAsync(MakeEdge($"{prefix}-e{i}", $"{prefix}-{i}", $"{prefix}-{i + 1}", kind));
        }
    }

    [Fact]
    public async Task BfsTraversal_LinearChain()
    {
        await SeedLinearChain("bfs", 5);

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "bfs-0",
            MaxDepth: 10,
            Strategy: GraphTraversalStrategy.BreadthFirst,
            Direction: GraphDirection.Outgoing));

        Assert.Equal(5, result.Nodes.Count);
        Assert.Equal(4, result.Edges.Count);
    }

    [Fact]
    public async Task DfsTraversal_LinearChain()
    {
        await SeedLinearChain("dfs", 5);

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "dfs-0",
            MaxDepth: 10,
            Strategy: GraphTraversalStrategy.DepthFirst,
            Direction: GraphDirection.Outgoing));

        Assert.Equal(5, result.Nodes.Count);
        Assert.Equal(4, result.Edges.Count);
    }

    [Fact]
    public async Task Traversal_DepthLimit()
    {
        await SeedLinearChain("depth", 10);

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "depth-0",
            MaxDepth: 3,
            Strategy: GraphTraversalStrategy.BreadthFirst,
            Direction: GraphDirection.Outgoing));

        // Start node + 3 depth levels = at most 4 nodes
        Assert.True(result.Nodes.Count <= 4);
        Assert.True(result.Nodes.Count >= 1);
    }

    [Fact]
    public async Task Traversal_CycleDetection()
    {
        // Create A -> B -> C -> A (cycle)
        await _store.SaveNodeAsync(MakeNode("cyc-a"));
        await _store.SaveNodeAsync(MakeNode("cyc-b"));
        await _store.SaveNodeAsync(MakeNode("cyc-c"));
        await _store.SaveRelationshipAsync(MakeEdge("cyc-e1", "cyc-a", "cyc-b"));
        await _store.SaveRelationshipAsync(MakeEdge("cyc-e2", "cyc-b", "cyc-c"));
        await _store.SaveRelationshipAsync(MakeEdge("cyc-e3", "cyc-c", "cyc-a"));

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "cyc-a",
            MaxDepth: 10,
            Strategy: GraphTraversalStrategy.BreadthFirst,
            Direction: GraphDirection.Outgoing));

        // Should visit exactly 3 nodes without infinite loop
        Assert.Equal(3, result.Nodes.Count);
    }

    [Fact]
    public async Task Traversal_RelationshipKindFilter()
    {
        await _store.SaveNodeAsync(MakeNode("filt-a"));
        await _store.SaveNodeAsync(MakeNode("filt-b"));
        await _store.SaveNodeAsync(MakeNode("filt-c"));
        await _store.SaveRelationshipAsync(MakeEdge("filt-e1", "filt-a", "filt-b", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("filt-e2", "filt-a", "filt-c", GraphRelationshipKind.Calls));

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "filt-a",
            MaxDepth: 3,
            Direction: GraphDirection.Outgoing,
            RelationshipKindFilter: new[] { GraphRelationshipKind.DependsOn }));

        // Should only follow DependsOn edges → filt-a + filt-b
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
    }

    [Fact]
    public async Task Traversal_NodeKindFilter()
    {
        await _store.SaveNodeAsync(MakeNode("nkf-a", GraphNodeKind.File));
        await _store.SaveNodeAsync(MakeNode("nkf-b", GraphNodeKind.Symbol));
        await _store.SaveNodeAsync(MakeNode("nkf-c", GraphNodeKind.File));
        await _store.SaveRelationshipAsync(MakeEdge("nkf-e1", "nkf-a", "nkf-b"));
        await _store.SaveRelationshipAsync(MakeEdge("nkf-e2", "nkf-b", "nkf-c"));

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "nkf-a",
            MaxDepth: 5,
            Direction: GraphDirection.Outgoing,
            NodeKindFilter: new[] { GraphNodeKind.File }));

        // Start node (File) + nkf-c (File); nkf-b (Symbol) filtered out of results
        Assert.Equal(2, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Equal(GraphNodeKind.File, n.Kind));
    }

    [Fact]
    public async Task Traversal_NonexistentStartNode()
    {
        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "does-not-exist",
            MaxDepth: 3));

        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.Equal(0, result.TotalNodesVisited);
    }

    [Fact]
    public async Task Traversal_SingleNode_NoEdges()
    {
        await _store.SaveNodeAsync(MakeNode("solo"));

        var result = await _service.TraverseAsync(new GraphTraversalRequest(
            StartNodeId: "solo",
            MaxDepth: 5));

        Assert.Single(result.Nodes);
        Assert.Equal("solo", result.Nodes[0].NodeId);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task Lineage_Upstream()
    {
        // Patch -> introduced_by -> Failure -> failed_in -> ExecutionPipeline
        await _store.SaveNodeAsync(MakeNode("lin-patch", GraphNodeKind.Patch));
        await _store.SaveNodeAsync(MakeNode("lin-failure", GraphNodeKind.Failure));
        await _store.SaveNodeAsync(MakeNode("lin-pipeline", GraphNodeKind.ExecutionPipeline));
        await _store.SaveRelationshipAsync(MakeEdge("lin-e1", "lin-patch", "lin-failure", GraphRelationshipKind.IntroducedBy));
        await _store.SaveRelationshipAsync(MakeEdge("lin-e2", "lin-failure", "lin-pipeline", GraphRelationshipKind.FailedIn));

        var result = await _service.GetLineageAsync(new GraphLineageRequest(
            NodeId: "lin-patch",
            Direction: GraphDirection.Outgoing,
            MaxDepth: 5));

        Assert.Equal("lin-patch", result.Origin.NodeId);
        Assert.Equal(2, result.Chain.Count);
        Assert.Equal("lin-failure", result.Chain[0].Node.NodeId);
        Assert.Equal("lin-pipeline", result.Chain[1].Node.NodeId);
    }

    [Fact]
    public async Task Lineage_Downstream()
    {
        await _store.SaveNodeAsync(MakeNode("ld-a"));
        await _store.SaveNodeAsync(MakeNode("ld-b"));
        await _store.SaveNodeAsync(MakeNode("ld-c"));
        await _store.SaveRelationshipAsync(MakeEdge("ld-e1", "ld-b", "ld-a", GraphRelationshipKind.GeneratedBy));
        await _store.SaveRelationshipAsync(MakeEdge("ld-e2", "ld-c", "ld-a", GraphRelationshipKind.FixedBy));

        var result = await _service.GetLineageAsync(new GraphLineageRequest(
            NodeId: "ld-a",
            Direction: GraphDirection.Incoming,
            MaxDepth: 3));

        Assert.Equal(2, result.Chain.Count);
    }

    [Fact]
    public async Task Lineage_WithRelationshipFilter()
    {
        await _store.SaveNodeAsync(MakeNode("lrf-a"));
        await _store.SaveNodeAsync(MakeNode("lrf-b"));
        await _store.SaveNodeAsync(MakeNode("lrf-c"));
        await _store.SaveRelationshipAsync(MakeEdge("lrf-e1", "lrf-a", "lrf-b", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("lrf-e2", "lrf-a", "lrf-c", GraphRelationshipKind.Calls));

        var result = await _service.GetLineageAsync(new GraphLineageRequest(
            NodeId: "lrf-a",
            Direction: GraphDirection.Outgoing,
            MaxDepth: 5,
            RelationshipKindFilter: new[] { GraphRelationshipKind.DependsOn }));

        Assert.Single(result.Chain);
        Assert.Equal("lrf-b", result.Chain[0].Node.NodeId);
    }

    [Fact]
    public async Task GetDependencyTree()
    {
        await _store.SaveNodeAsync(MakeNode("dep-root", GraphNodeKind.File));
        await _store.SaveNodeAsync(MakeNode("dep-a", GraphNodeKind.File));
        await _store.SaveNodeAsync(MakeNode("dep-b", GraphNodeKind.File));
        await _store.SaveNodeAsync(MakeNode("dep-c", GraphNodeKind.File));
        await _store.SaveRelationshipAsync(MakeEdge("dep-e1", "dep-root", "dep-a", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("dep-e2", "dep-root", "dep-b", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("dep-e3", "dep-a", "dep-c", GraphRelationshipKind.DependsOn));

        var result = await _service.GetDependencyTreeAsync("dep-root");

        Assert.Equal(4, result.Nodes.Count);
        Assert.Equal(3, result.Edges.Count);
    }

    [Fact]
    public async Task GetImpactAnalysis_ReverseDependency()
    {
        await _store.SaveNodeAsync(MakeNode("imp-leaf", GraphNodeKind.Symbol));
        await _store.SaveNodeAsync(MakeNode("imp-mid", GraphNodeKind.Symbol));
        await _store.SaveNodeAsync(MakeNode("imp-root", GraphNodeKind.Symbol));
        await _store.SaveRelationshipAsync(MakeEdge("imp-e1", "imp-mid", "imp-leaf", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("imp-e2", "imp-root", "imp-mid", GraphRelationshipKind.DependsOn));

        var result = await _service.GetImpactAnalysisAsync("imp-leaf");

        // imp-leaf is depended on by imp-mid, which is depended on by imp-root
        Assert.Equal(3, result.Nodes.Count);
    }

    [Fact]
    public async Task Lineage_CycleHandling()
    {
        // Ensure lineage doesn't infinite-loop on cycles
        await _store.SaveNodeAsync(MakeNode("lcyc-a"));
        await _store.SaveNodeAsync(MakeNode("lcyc-b"));
        await _store.SaveNodeAsync(MakeNode("lcyc-c"));
        await _store.SaveRelationshipAsync(MakeEdge("lcyc-e1", "lcyc-a", "lcyc-b", GraphRelationshipKind.RelatedTo));
        await _store.SaveRelationshipAsync(MakeEdge("lcyc-e2", "lcyc-b", "lcyc-c", GraphRelationshipKind.RelatedTo));
        await _store.SaveRelationshipAsync(MakeEdge("lcyc-e3", "lcyc-c", "lcyc-a", GraphRelationshipKind.RelatedTo));

        var result = await _service.GetLineageAsync(new GraphLineageRequest(
            NodeId: "lcyc-a",
            Direction: GraphDirection.Outgoing,
            MaxDepth: 20));

        // Should visit all 3 without looping
        Assert.Equal(2, result.Chain.Count); // b and c (a is origin)
    }

    [Fact]
    public async Task Lineage_NonexistentNode()
    {
        var result = await _service.GetLineageAsync(new GraphLineageRequest(
            NodeId: "ghost",
            MaxDepth: 5));

        Assert.Empty(result.Chain);
        Assert.Equal(0, result.TotalSteps);
    }
}
