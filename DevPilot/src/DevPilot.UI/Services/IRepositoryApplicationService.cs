using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public interface IRepositoryApplicationService
{
    Task<IReadOnlyList<RepositoryItem>> ListAsync(CancellationToken cancellationToken = default);

    Task<RepositoryItem> IndexAsync(
        string repositoryPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string repositoryId, CancellationToken cancellationToken = default);
}
