using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public interface ISearchApplicationService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
