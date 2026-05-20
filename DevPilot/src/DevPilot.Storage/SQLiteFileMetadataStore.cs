using DevPilot.Contracts;

namespace DevPilot.Storage;

public sealed class SQLiteFileMetadataStore : IFileMetadataStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SQLiteFileMetadataStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(FileMetadata fileMetadata, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var deleteStaleCommand = connection.CreateCommand();
        deleteStaleCommand.Transaction = transaction;
        deleteStaleCommand.CommandText = """
            DELETE FROM Files
            WHERE RepositoryId = @RepositoryId
              AND RelativePath = @RelativePath
              AND Id <> @Id;
            """;
        deleteStaleCommand.AddParameter("@RepositoryId", fileMetadata.RepositoryId);
        deleteStaleCommand.AddParameter("@RelativePath", fileMetadata.RelativePath);
        deleteStaleCommand.AddParameter("@Id", fileMetadata.Id);
        await deleteStaleCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Files (Id, RepositoryId, RelativePath, Extension, Language, SHA256Hash, FileSize, LastModifiedUtc)
            VALUES (@Id, @RepositoryId, @RelativePath, @Extension, @Language, @SHA256Hash, @FileSize, @LastModifiedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                RepositoryId = excluded.RepositoryId,
                RelativePath = excluded.RelativePath,
                Extension = excluded.Extension,
                Language = excluded.Language,
                SHA256Hash = excluded.SHA256Hash,
                FileSize = excluded.FileSize,
                LastModifiedUtc = excluded.LastModifiedUtc;
            """;
        AddFileParameters(command, fileMetadata);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileMetadata?> GetAsync(string fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.Id, f.RepositoryId, r.Name, r.RootPath, f.RelativePath, f.Extension, f.Language,
                   f.FileSize, f.SHA256Hash, f.LastModifiedUtc
            FROM Files f
            INNER JOIN Repositories r ON r.Id = f.RepositoryId
            WHERE f.Id = @Id;
            """;
        command.AddParameter("@Id", fileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadFileMetadata(reader)
            : null;
    }

    public async Task<IReadOnlyList<FileMetadata>> ListByRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.Id, f.RepositoryId, r.Name, r.RootPath, f.RelativePath, f.Extension, f.Language,
                   f.FileSize, f.SHA256Hash, f.LastModifiedUtc
            FROM Files f
            INNER JOIN Repositories r ON r.Id = f.RepositoryId
            WHERE f.RepositoryId = @RepositoryId
            ORDER BY f.RelativePath;
            """;
        command.AddParameter("@RepositoryId", repositoryId);

        var files = new List<FileMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(ReadFileMetadata(reader));
        }

        return files;
    }

    public async Task<int> DeleteMissingAsync(
        string repositoryId,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        var existing = await ListByRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        var scanned = relativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = 0;

        foreach (var file in existing.Where(file => !scanned.Contains(file.RelativePath)))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Files WHERE Id = @Id;";
            command.AddParameter("@Id", file.Id);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private static void AddFileParameters(System.Data.Common.DbCommand command, FileMetadata fileMetadata)
    {
        command.AddParameter("@Id", fileMetadata.Id);
        command.AddParameter("@RepositoryId", fileMetadata.RepositoryId);
        command.AddParameter("@RelativePath", fileMetadata.RelativePath);
        command.AddParameter("@Extension", fileMetadata.Extension);
        command.AddParameter("@Language", fileMetadata.Language);
        command.AddParameter("@SHA256Hash", fileMetadata.SHA256Hash);
        command.AddParameter("@FileSize", fileMetadata.FileSize);
        command.AddParameter("@LastModifiedUtc", fileMetadata.LastModifiedUtc.UtcDateTime.ToString("O"));
    }

    private static FileMetadata ReadFileMetadata(System.Data.Common.DbDataReader reader)
    {
        var root = reader.GetString(3);
        var relativePath = reader.GetString(4);
        return new FileMetadata(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Path.Combine(root, relativePath),
            relativePath,
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)));
    }
}
