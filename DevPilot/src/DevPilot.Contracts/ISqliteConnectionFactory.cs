using System.Data.Common;

namespace DevPilot.Contracts;

public interface ISqliteConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
