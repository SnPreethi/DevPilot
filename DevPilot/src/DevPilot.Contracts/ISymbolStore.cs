using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public interface ISymbolStore
{
    Task SaveManyAsync(IReadOnlyCollection<SymbolIndexEntry> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SymbolIndexEntry>> ListByRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SymbolIndexEntry>> ListByFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task<int> DeleteByFileAsync(string fileId, CancellationToken cancellationToken = default);
}
