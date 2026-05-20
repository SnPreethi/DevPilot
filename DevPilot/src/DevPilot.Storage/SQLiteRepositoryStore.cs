using DevPilot.Contracts;

namespace DevPilot.Storage;

public sealed class SQLiteRepositoryStore : IRepositoryStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteRepositoryStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(RepositoryDocument repository, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Repositories (Id, Name, RootPath, IndexedAtUtc)
            VALUES (@Id, @Name, @RootPath, @IndexedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                RootPath = excluded.RootPath,
                IndexedAtUtc = excluded.IndexedAtUtc;
            """;
        command.AddParameter("@Id", repository.RepositoryId);
        command.AddParameter("@Name", repository.RepositoryName);
        command.AddParameter("@RootPath", repository.RootPath);
        command.AddParameter("@IndexedAtUtc", repository.IndexedAtUtc.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepositoryDocument?> GetAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, RootPath, IndexedAtUtc
            FROM Repositories
            WHERE Id = @Id;
            """;
        command.AddParameter("@Id", repositoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RepositoryDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3)));
    }

    public async Task<IReadOnlyList<RepositoryDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, RootPath, IndexedAtUtc
            FROM Repositories
            ORDER BY Name;
            """;

        var repositories = new List<RepositoryDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            repositories.Add(new RepositoryDocument(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return repositories;
    }

    public async Task DeleteAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Repositories WHERE Id = @Id;";
        command.AddParameter("@Id", repositoryId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
