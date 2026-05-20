using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Storage.Tests;

public sealed class GraphStoreTests : IDisposable
{
    private readonly TemporaryWorkspace _workspace;
    private readonly SQLiteGraphStore _store;
    private readonly DatabaseInitializer _initializer;

    public GraphStoreTests()
    {
        _workspace = TemporaryWorkspace.Create();
        var dbPath = Path.Combine(_workspace.RootPath, "test.db");

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
        _initializer = new DatabaseInitializer(factory, vectorSettings, NullLogger<DatabaseInitializer>.Instance);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _store = new SQLiteGraphStore(factory, NullLogger<SQLiteGraphStore>.Instance);
    }

    public void Dispose() => _workspace.Dispose();

    private static GraphNode MakeNode(string id, GraphNodeKind kind = GraphNodeKind.Symbol, string? label = null) =>
        new(id, kind, $"entity-{id}", label ?? $"Label {id}", DateTime.UtcNow);

    private static GraphRelationship MakeEdge(string id, string source, string target, GraphRelationshipKind kind = GraphRelationshipKind.DependsOn) =>
        new(id, source, target, kind, DateTime.UtcNow);

    [Fact]
    public async Task SaveAndRetrieveNode()
    {
        var node = MakeNode("node-1", GraphNodeKind.File, "Program.cs");
        await _store.SaveNodeAsync(node);

        var retrieved = await _store.GetNodeAsync("node-1");

        Assert.NotNull(retrieved);
        Assert.Equal("node-1", retrieved.NodeId);
        Assert.Equal(GraphNodeKind.File, retrieved.Kind);
        Assert.Equal("entity-node-1", retrieved.EntityId);
        Assert.Equal("Program.cs", retrieved.Label);
    }

    [Fact]
    public async Task SaveAndRetrieveRelationship()
    {
        var nodeA = MakeNode("a");
        var nodeB = MakeNode("b");
        await _store.SaveNodeAsync(nodeA);
        await _store.SaveNodeAsync(nodeB);

        var edge = MakeEdge("edge-1", "a", "b", GraphRelationshipKind.Calls);
        await _store.SaveRelationshipAsync(edge);

        var relationships = await _store.GetRelationshipsAsync("a", GraphDirection.Outgoing);

        Assert.Single(relationships);
        Assert.Equal("edge-1", relationships[0].RelationshipId);
        Assert.Equal(GraphRelationshipKind.Calls, relationships[0].Kind);
    }

    [Fact]
    public async Task GetRelationships_DirectionFiltering()
    {
        var nodeA = MakeNode("dir-a");
        var nodeB = MakeNode("dir-b");
        var nodeC = MakeNode("dir-c");
        await _store.SaveNodeAsync(nodeA);
        await _store.SaveNodeAsync(nodeB);
        await _store.SaveNodeAsync(nodeC);

        await _store.SaveRelationshipAsync(MakeEdge("dir-e1", "dir-a", "dir-b"));
        await _store.SaveRelationshipAsync(MakeEdge("dir-e2", "dir-c", "dir-a"));

        var outgoing = await _store.GetRelationshipsAsync("dir-a", GraphDirection.Outgoing);
        Assert.Single(outgoing);
        Assert.Equal("dir-e1", outgoing[0].RelationshipId);

        var incoming = await _store.GetRelationshipsAsync("dir-a", GraphDirection.Incoming);
        Assert.Single(incoming);
        Assert.Equal("dir-e2", incoming[0].RelationshipId);

        var both = await _store.GetRelationshipsAsync("dir-a", GraphDirection.Both);
        Assert.Equal(2, both.Count);
    }

    [Fact]
    public async Task GetNodeByEntity()
    {
        var node = MakeNode("ent-1", GraphNodeKind.Repository, "MyRepo");
        await _store.SaveNodeAsync(node);

        var found = await _store.GetNodeByEntityAsync("entity-ent-1", GraphNodeKind.Repository);

        Assert.NotNull(found);
        Assert.Equal("ent-1", found.NodeId);
    }

    [Fact]
    public async Task GetNodeByEntity_WrongKind_ReturnsNull()
    {
        var node = MakeNode("ent-2", GraphNodeKind.File);
        await _store.SaveNodeAsync(node);

        var notFound = await _store.GetNodeByEntityAsync("entity-ent-2", GraphNodeKind.Symbol);

        Assert.Null(notFound);
    }

    [Fact]
    public async Task QueryNodesByKind()
    {
        await _store.SaveNodeAsync(MakeNode("qk-1", GraphNodeKind.File));
        await _store.SaveNodeAsync(MakeNode("qk-2", GraphNodeKind.Symbol));
        await _store.SaveNodeAsync(MakeNode("qk-3", GraphNodeKind.File));

        var files = await _store.QueryNodesAsync(kind: GraphNodeKind.File);

        Assert.Equal(2, files.Count);
        Assert.All(files, n => Assert.Equal(GraphNodeKind.File, n.Kind));
    }

    [Fact]
    public async Task QueryNodesByLabelContains()
    {
        await _store.SaveNodeAsync(MakeNode("ql-1", label: "UserController"));
        await _store.SaveNodeAsync(MakeNode("ql-2", label: "OrderService"));
        await _store.SaveNodeAsync(MakeNode("ql-3", label: "UserService"));

        var userNodes = await _store.QueryNodesAsync(labelContains: "User");

        Assert.Equal(2, userNodes.Count);
    }

    [Fact]
    public async Task DeleteNode_CascadesEdges()
    {
        var nodeA = MakeNode("del-a");
        var nodeB = MakeNode("del-b");
        await _store.SaveNodeAsync(nodeA);
        await _store.SaveNodeAsync(nodeB);
        await _store.SaveRelationshipAsync(MakeEdge("del-e1", "del-a", "del-b"));

        await _store.DeleteNodeAsync("del-a");

        var deletedNode = await _store.GetNodeAsync("del-a");
        Assert.Null(deletedNode);

        var orphanedEdges = await _store.GetRelationshipsAsync("del-b", GraphDirection.Both);
        Assert.Empty(orphanedEdges);
    }

    [Fact]
    public async Task DeleteRelationship()
    {
        var nodeA = MakeNode("delr-a");
        var nodeB = MakeNode("delr-b");
        await _store.SaveNodeAsync(nodeA);
        await _store.SaveNodeAsync(nodeB);
        await _store.SaveRelationshipAsync(MakeEdge("delr-e1", "delr-a", "delr-b"));

        await _store.DeleteRelationshipAsync("delr-e1");

        var rels = await _store.GetRelationshipsAsync("delr-a", GraphDirection.Both);
        Assert.Empty(rels);

        // Nodes should still exist
        Assert.NotNull(await _store.GetNodeAsync("delr-a"));
        Assert.NotNull(await _store.GetNodeAsync("delr-b"));
    }

    [Fact]
    public async Task UpsertNode_UpdatesOnConflict()
    {
        var original = MakeNode("upsert-1", GraphNodeKind.File, "original.cs");
        await _store.SaveNodeAsync(original);

        var updated = new GraphNode("upsert-1", GraphNodeKind.File, "entity-upsert-1", "updated.cs", DateTime.UtcNow);
        await _store.SaveNodeAsync(updated);

        var retrieved = await _store.GetNodeAsync("upsert-1");
        Assert.NotNull(retrieved);
        Assert.Equal("updated.cs", retrieved.Label);
    }

    [Fact]
    public async Task GetNeighborNodes()
    {
        await _store.SaveNodeAsync(MakeNode("nb-center"));
        await _store.SaveNodeAsync(MakeNode("nb-a"));
        await _store.SaveNodeAsync(MakeNode("nb-b"));
        await _store.SaveRelationshipAsync(MakeEdge("nb-e1", "nb-center", "nb-a", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("nb-e2", "nb-center", "nb-b", GraphRelationshipKind.Calls));

        var allNeighbors = await _store.GetNeighborNodesAsync("nb-center", GraphDirection.Outgoing);
        Assert.Equal(2, allNeighbors.Count);

        var dependsOnOnly = await _store.GetNeighborNodesAsync("nb-center", GraphDirection.Outgoing, new[] { GraphRelationshipKind.DependsOn });
        Assert.Single(dependsOnOnly);
        Assert.Equal("nb-a", dependsOnOnly[0].NodeId);
    }

    [Fact]
    public async Task QueryRelationshipsByKind()
    {
        await _store.SaveNodeAsync(MakeNode("qr-a"));
        await _store.SaveNodeAsync(MakeNode("qr-b"));
        await _store.SaveNodeAsync(MakeNode("qr-c"));
        await _store.SaveRelationshipAsync(MakeEdge("qr-e1", "qr-a", "qr-b", GraphRelationshipKind.DependsOn));
        await _store.SaveRelationshipAsync(MakeEdge("qr-e2", "qr-a", "qr-c", GraphRelationshipKind.Calls));

        var dependsOnEdges = await _store.QueryRelationshipsAsync(kind: GraphRelationshipKind.DependsOn);
        Assert.Single(dependsOnEdges);
        Assert.Equal("qr-e1", dependsOnEdges[0].RelationshipId);
    }

    [Fact]
    public async Task GetNode_NotFound_ReturnsNull()
    {
        var result = await _store.GetNodeAsync("nonexistent");
        Assert.Null(result);
    }
}
