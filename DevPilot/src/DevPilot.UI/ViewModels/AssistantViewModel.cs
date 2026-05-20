using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPilot.UI.Models;
using DevPilot.UI.Services;
using System.Collections.ObjectModel;

namespace DevPilot.UI.ViewModels;

public sealed partial class AssistantViewModel : BaseViewModel
{
    private readonly IAssistantApplicationService _assistantService;
    private CancellationTokenSource? _askCts;

    public ObservableCollection<string> ReferencedFiles { get; } = [];

    public ObservableCollection<SearchResultItem> RetrievedContext { get; } = [];

    [ObservableProperty]
    private string question = string.Empty;

    [ObservableProperty]
    private string answer = string.Empty;

    [ObservableProperty]
    private int maxContextChunks = 5;

    [ObservableProperty]
    private string inferenceSummary = string.Empty;

    public AssistantViewModel(IAssistantApplicationService assistantService)
    {
        _assistantService = assistantService;
    }

    [RelayCommand(CanExecute = nameof(CanAsk))]
    public async Task AskAsync()
    {
        ClearError();
        IsBusy = true;
        Answer = string.Empty;
        ReferencedFiles.Clear();
        RetrievedContext.Clear();
        _askCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Running grounded local assistant...";
            var response = await _assistantService.AskAsync(Question, MaxContextChunks, _askCts.Token).ConfigureAwait(true);
            Answer = response.Answer;
            foreach (var file in response.ReferencedFiles)
            {
                ReferencedFiles.Add(file);
            }

            foreach (var context in response.RetrievedContext)
            {
                RetrievedContext.Add(context);
            }

            InferenceSummary = $"{response.InferenceDuration.TotalMilliseconds:0} ms, prompt {response.PromptTokens} tokens, output {response.OutputTokens} tokens";
            StatusMessage = "Assistant response complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Assistant request cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ex, "Assistant request failed.");
        }
        finally
        {
            IsBusy = false;
            _askCts.Dispose();
            _askCts = null;
            AskCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _askCts?.Cancel();
    }

    partial void OnQuestionChanged(string value)
    {
        AskCommand.NotifyCanExecuteChanged();
    }

    private bool CanAsk() => !IsBusy && !string.IsNullOrWhiteSpace(Question);
}
