using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Data.Common;

namespace DevPilot.Storage;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly StorageSettings _settings;

    public SqliteConnectionFactory(IOptions<StorageSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = Path.GetFullPath(_settings.DatabasePath);
        var directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrWhiteSpace(directory) && _settings.CreateIfMissing)
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = _settings.CreateIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = _settings.Pooling
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -20000;
            PRAGMA temp_store = MEMORY;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
