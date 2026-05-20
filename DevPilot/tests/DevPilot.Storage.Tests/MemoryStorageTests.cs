using System;
using System.IO;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Storage;
using DevPilot.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Storage.Tests;

public sealed class MemoryStorageTests
{
    private sealed class TempSqliteConnectionFactory : ISqliteConnectionFactory
    {
        private readonly string _connectionString;

        public TempSqliteConnectionFactory(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task<System.Data.Common.DbConnection> CreateOpenConnectionAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    [Fact]
    public async Task SQLiteWorkspaceMemoryStore_SavesAndRetrievesEvents()
    {
        var tempDbFile = Path.GetTempFileName();
        try
        {
            var factory = new TempSqliteConnectionFactory(tempDbFile);
            var initializer = new DatabaseInitializer(
                factory,
                Options.Create(new VectorSearchSettings { UseSqliteVss = false }),
                NullLogger<DatabaseInitializer>.Instance);
            
            await initializer.InitializeAsync();

            var repositoryStore = new SQLiteRepositoryStore(factory);
            await repositoryStore.SaveAsync(new RepositoryDocument("test-repo", "test-repo", "test-path", DateTimeOffset.UtcNow));

            var store = new SQLiteWorkspaceMemoryStore(factory);

            var ev1 = new WorkspaceEvent(
                RepositoryId: "test-repo",
                EventType: "fix",
                TimestampUtc: DateTime.UtcNow.AddMinutes(-5),
                FilePath: "src/Program.cs",
                SymbolName: "Main",
                Description: "Fixed program startup bug",
                Outcome: "success",
                Payload: "{\"edit\": 1}"
            );

            var ev2 = new WorkspaceEvent(
                RepositoryId: "test-repo",
                EventType: "failure",
                TimestampUtc: DateTime.UtcNow,
                FilePath: "src/Utils.cs",
                SymbolName: "Format",
                Description: "Null pointer exception",
                Outcome: "failed",
                Payload: "{\"error\": \"null\"}"
            );

            await store.SaveEventAsync(ev1);
            await store.SaveEventAsync(ev2);

            var events = await store.ListEventsAsync("test-repo", 10);

            Assert.Equal(2, events.Count);
            Assert.Equal("test-repo", events[0].RepositoryId);
            Assert.Equal("failed", events[0].Outcome); // Ordered descending by TimestampUtc
            Assert.Equal("success", events[1].Outcome);
            Assert.Equal("src/Program.cs", events[1].FilePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbFile))
            {
                File.Delete(tempDbFile);
            }
        }
    }
}
