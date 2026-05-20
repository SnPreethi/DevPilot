using DevPilot.UI.Models;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using Xunit;

namespace DevPilot.UI.Tests;

public sealed class RepositoriesViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesRepositories()
    {
        var viewModel = new RepositoriesViewModel(new FakeRepositoryApplicationService());

        await viewModel.LoadAsync();

        Assert.Single(viewModel.Repositories);
        Assert.Equal("Loaded 1 repositories.", viewModel.StatusMessage);
    }

    private sealed class FakeRepositoryApplicationService : IRepositoryApplicationService
    {
        public Task<IReadOnlyList<RepositoryItem>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RepositoryItem> repositories =
            [
                new RepositoryItem("repo-1", "Repo", "C:\\Repo", DateTimeOffset.UtcNow, 3, 8)
            ];
            return Task.FromResult(repositories);
        }

        public Task<RepositoryItem> IndexAsync(
            string repositoryPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RepositoryItem("repo-1", "Repo", repositoryPath, DateTimeOffset.UtcNow, 3, 8));
        }

        public Task RemoveAsync(string repositoryId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
