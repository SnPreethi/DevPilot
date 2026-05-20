using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPilot.UI.Models;
using DevPilot.UI.Services;
using System.Collections.ObjectModel;

namespace DevPilot.UI.ViewModels;

public sealed partial class RepositoriesViewModel : BaseViewModel
{
    private readonly IRepositoryApplicationService _repositoryService;
    private CancellationTokenSource? _operationCts;

    public ObservableCollection<RepositoryItem> Repositories { get; } = [];

    [ObservableProperty]
    private string repositoryPath = string.Empty;

    [ObservableProperty]
    private RepositoryItem? selectedRepository;

    public RepositoriesViewModel(IRepositoryApplicationService repositoryService)
    {
        _repositoryService = repositoryService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunAsync(async token =>
        {
            Repositories.Clear();
            foreach (var repository in await _repositoryService.ListAsync(token).ConfigureAwait(true))
            {
                Repositories.Add(repository);
            }

            StatusMessage = $"Loaded {Repositories.Count} repositories.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanIndex))]
    public async Task IndexAsync()
    {
        await RunAsync(async token =>
        {
            var progress = new Progress<string>(message => StatusMessage = message);
            var repository = await _repositoryService.IndexAsync(RepositoryPath, progress, token).ConfigureAwait(true);
            var existing = Repositories.FirstOrDefault(item => item.Id == repository.Id);
            if (existing is not null)
            {
                Repositories.Remove(existing);
            }

            Repositories.Insert(0, repository);
            SelectedRepository = repository;
            StatusMessage = "Indexing complete.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    public async Task RemoveAsync()
    {
        if (SelectedRepository is null)
        {
            return;
        }

        var repository = SelectedRepository;
        await RunAsync(async token =>
        {
            await _repositoryService.RemoveAsync(repository.Id, token).ConfigureAwait(true);
            Repositories.Remove(repository);
            SelectedRepository = null;
            StatusMessage = "Repository removed.";
        });
    }

    [RelayCommand]
    public void Cancel()
    {
        _operationCts?.Cancel();
    }

    partial void OnRepositoryPathChanged(string value)
    {
        IndexCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRepositoryChanged(RepositoryItem? value)
    {
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private bool CanIndex() => !IsBusy && Directory.Exists(RepositoryPath);

    private bool CanRemove() => !IsBusy && SelectedRepository is not null;

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        ClearError();
        IsBusy = true;
        _operationCts = new CancellationTokenSource();
        try
        {
            await operation(_operationCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ex, "Repository operation failed.");
        }
        finally
        {
            IsBusy = false;
            _operationCts.Dispose();
            _operationCts = null;
            IndexCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        }
    }
}
