using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPilot.UI.Models;
using DevPilot.UI.Services;
using System.Collections.ObjectModel;

namespace DevPilot.UI.ViewModels;

public sealed partial class SearchViewModel : BaseViewModel
{
    private readonly ISearchApplicationService _searchService;
    private CancellationTokenSource? _searchCts;

    public ObservableCollection<SearchResultItem> Results { get; } = [];

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private int maxResults = 5;

    public SearchViewModel(ISearchApplicationService searchService)
    {
        _searchService = searchService;
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        ClearError();
        IsBusy = true;
        Results.Clear();
        _searchCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Searching local embeddings...";
            var results = await _searchService.SearchAsync(Query, MaxResults, _searchCts.Token).ConfigureAwait(true);
            foreach (var result in results)
            {
                Results.Add(result);
            }

            StatusMessage = $"Found {Results.Count} semantic matches.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Search cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ex, "Search failed.");
        }
        finally
        {
            IsBusy = false;
            _searchCts.Dispose();
            _searchCts = null;
            SearchCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _searchCts?.Cancel();
    }

    partial void OnQueryChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(Query);
}
