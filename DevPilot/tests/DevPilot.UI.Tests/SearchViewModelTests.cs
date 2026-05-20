using DevPilot.UI.Models;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using Xunit;

namespace DevPilot.UI.Tests;

public sealed class SearchViewModelTests
{
    [Fact]
    public async Task SearchAsync_PopulatesResultsAndStatus()
    {
        var viewModel = new SearchViewModel(new FakeSearchApplicationService())
        {
            Query = "jwt validation"
        };

        await viewModel.SearchAsync();

        Assert.Single(viewModel.Results);
        Assert.Equal("Found 1 semantic matches.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class FakeSearchApplicationService : ISearchApplicationService
    {
        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResultItem> results =
            [
                new SearchResultItem(1, "chunk-1", "AuthService.cs", "Validate", "method", 1, 10, 0.91, "jwt validation")
            ];
            return Task.FromResult(results);
        }
    }
}
