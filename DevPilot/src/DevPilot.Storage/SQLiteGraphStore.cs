using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Storage;

public sealed class SQLiteGraphStore : IGraphStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SQLiteGraphStore> _logger;

    public SQLiteGraphStore(
        ISqliteConnectionFactory connectionFactory,
        ILogger<SQLiteGraphStore> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task SaveNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GraphNodes (Id, Kind, EntityId, Label, CreatedUtc, Metadata)
            VALUES (@Id, @Kind, @EntityId, @Label, @CreatedUtc, @Metadata)
            ON CONFLICT(Id) DO UPDATE SET
                Kind = excluded.Kind,
                EntityId = excluded.EntityId,
                Label = excluded.Label,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@Id", node.NodeId);
        command.AddParameter("@Kind", node.Kind.ToString());
        command.AddParameter("@EntityId", node.EntityId);
        command.AddParameter("@Label", node.Label);
        command.AddParameter("@CreatedUtc", node.CreatedUtc.ToString("O"));
        command.AddParameter("@Metadata", node.Metadata ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Saved graph node {NodeId} (Kind={Kind}).", node.NodeId, node.Kind);
    }

    public async Task SaveRelationshipAsync(GraphRelationship relationship, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GraphEdges (Id, SourceNodeId, TargetNodeId, Kind, CreatedUtc, Metadata)
            VALUES (@Id, @SourceNodeId, @TargetNodeId, @Kind, @CreatedUtc, @Metadata)
            ON CONFLICT(Id) DO UPDATE SET
                SourceNodeId = excluded.SourceNodeId,
                TargetNodeId = excluded.TargetNodeId,
                Kind = excluded.Kind,
                Metadata = excluded.Metadata;
            """;
        command.AddParameter("@Id", relationship.RelationshipId);
        command.AddParameter("@SourceNodeId", relationship.SourceNodeId);
        command.AddParameter("@TargetNodeId", relationship.TargetNodeId);
        command.AddParameter("@Kind", relationship.Kind.ToString());
        command.AddParameter("@CreatedUtc", relationship.CreatedUtc.ToString("O"));
        command.AddParameter("@Metadata", relationship.Metadata ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Saved graph edge {EdgeId} ({Source}->{Target}, Kind={Kind}).",
            relationship.RelationshipId, relationship.SourceNodeId, relationship.TargetNodeId, relationship.Kind);
    }

    public async Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Kind, EntityId, Label, CreatedUtc, Metadata FROM GraphNodes WHERE Id = @Id;";
        command.AddParameter("@Id", nodeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadNode(reader);
        }

        return null;
    }

    public async Task<GraphNode?> GetNodeByEntityAsync(string entityId, GraphNodeKind kind, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Kind, EntityId, Label, CreatedUtc, Metadata FROM GraphNodes WHERE EntityId = @EntityId AND Kind = @Kind LIMIT 1;";
        command.AddParameter("@EntityId", entityId);
        command.AddParameter("@Kind", kind.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadNode(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string nodeId,
        GraphDirection direction = GraphDirection.Both,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = direction switch
        {
            GraphDirection.Outgoing => "SELECT Id, SourceNodeId, TargetNodeId, Kind, CreatedUtc, Metadata FROM GraphEdges WHERE SourceNodeId = @NodeId;",
            GraphDirection.Incoming => "SELECT Id, SourceNodeId, TargetNodeId, Kind, CreatedUtc, Metadata FROM GraphEdges WHERE TargetNodeId = @NodeId;",
            _ => "SELECT Id, SourceNodeId, TargetNodeId, Kind, CreatedUtc, Metadata FROM GraphEdges WHERE SourceNodeId = @NodeId OR TargetNodeId = @NodeId;"
        };

        command.AddParameter("@NodeId", nodeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<GraphRelationship>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadRelationship(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<GraphNode>> QueryNodesAsync(
        GraphNodeKind? kind = null,
        string? labelContains = null,
        string? metadataContains = null,
        string? entityId = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var clauses = new List<string>();
        if (kind.HasValue)
        {
            clauses.Add("Kind = @Kind");
            command.AddParameter("@Kind", kind.Value.ToString());
        }
        if (!string.IsNullOrEmpty(labelContains))
        {
            clauses.Add("Label LIKE @LabelContains");
            command.AddParameter("@LabelContains", $"%{labelContains}%");
        }
        if (!string.IsNullOrEmpty(metadataContains))
        {
            clauses.Add("Metadata LIKE @MetadataContains");
            command.AddParameter("@MetadataContains", $"%{metadataContains}%");
        }
        if (!string.IsNullOrEmpty(entityId))
        {
            clauses.Add("EntityId = @EntityId");
            command.AddParameter("@EntityId", entityId);
        }

        var whereClause = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        command.CommandText = $"SELECT Id, Kind, EntityId, Label, CreatedUtc, Metadata FROM GraphNodes {whereClause} LIMIT @Limit;";
        command.AddParameter("@Limit", maxResults);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<GraphNode>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadNode(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(
        GraphRelationshipKind? kind = null,
        string? sourceNodeId = null,
        string? targetNodeId = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var clauses = new List<string>();
        if (kind.HasValue)
        {
            clauses.Add("Kind = @Kind");
            command.AddParameter("@Kind", kind.Value.ToString());
        }
        if (!string.IsNullOrEmpty(sourceNodeId))
        {
            clauses.Add("SourceNodeId = @SourceNodeId");
            command.AddParameter("@SourceNodeId", sourceNodeId);
        }
        if (!string.IsNullOrEmpty(targetNodeId))
        {
            clauses.Add("TargetNodeId = @TargetNodeId");
            command.AddParameter("@TargetNodeId", targetNodeId);
        }

        var whereClause = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        command.CommandText = $"SELECT Id, SourceNodeId, TargetNodeId, Kind, CreatedUtc, Metadata FROM GraphEdges {whereClause} LIMIT @Limit;";
        command.AddParameter("@Limit", maxResults);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<GraphRelationship>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadRelationship(reader));
        }

        return results;
    }

    public async Task DeleteNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Delete associated edges first (both directions), then the node.
        await using var edgeCommand = connection.CreateCommand();
        edgeCommand.CommandText = "DELETE FROM GraphEdges WHERE SourceNodeId = @NodeId OR TargetNodeId = @NodeId;";
        edgeCommand.AddParameter("@NodeId", nodeId);
        await edgeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var nodeCommand = connection.CreateCommand();
        nodeCommand.CommandText = "DELETE FROM GraphNodes WHERE Id = @Id;";
        nodeCommand.AddParameter("@Id", nodeId);
        await nodeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Deleted graph node {NodeId} and its edges.", nodeId);
    }

    public async Task DeleteRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GraphEdges WHERE Id = @Id;";
        command.AddParameter("@Id", relationshipId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Deleted graph edge {EdgeId}.", relationshipId);
    }

    public async Task<IReadOnlyList<GraphNode>> GetNeighborNodesAsync(
        string nodeId,
        GraphDirection direction = GraphDirection.Outgoing,
        IReadOnlyList<GraphRelationshipKind>? kindFilter = null,
        CancellationToken cancellationToken = default)
    {
        var relationships = await GetRelationshipsAsync(nodeId, direction, cancellationToken).ConfigureAwait(false);

        if (kindFilter is { Count: > 0 })
        {
            var filterSet = new HashSet<GraphRelationshipKind>(kindFilter);
            relationships = relationships.Where(r => filterSet.Contains(r.Kind)).ToList();
        }

        var neighborIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in relationships)
        {
            if (!string.Equals(rel.SourceNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                neighborIds.Add(rel.SourceNodeId);
            if (!string.Equals(rel.TargetNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                neighborIds.Add(rel.TargetNodeId);
        }

        var neighbors = new List<GraphNode>();
        foreach (var nid in neighborIds)
        {
            var neighbor = await GetNodeAsync(nid, cancellationToken).ConfigureAwait(false);
            if (neighbor != null)
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    private static GraphNode ReadNode(System.Data.Common.DbDataReader reader)
    {
        return new GraphNode(
            NodeId: reader.GetString(0),
            Kind: Enum.Parse<GraphNodeKind>(reader.GetString(1), ignoreCase: true),
            EntityId: reader.GetString(2),
            Label: reader.GetString(3),
            CreatedUtc: DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Metadata: reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static GraphRelationship ReadRelationship(System.Data.Common.DbDataReader reader)
    {
        return new GraphRelationship(
            RelationshipId: reader.GetString(0),
            SourceNodeId: reader.GetString(1),
            TargetNodeId: reader.GetString(2),
            Kind: Enum.Parse<GraphRelationshipKind>(reader.GetString(3), ignoreCase: true),
            CreatedUtc: DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Metadata: reader.IsDBNull(5) ? null : reader.GetString(5));
    }
}
