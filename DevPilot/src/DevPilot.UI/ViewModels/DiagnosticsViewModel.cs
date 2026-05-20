using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPilot.UI.Models;
using DevPilot.UI.Services;
using System.Collections.ObjectModel;

namespace DevPilot.UI.ViewModels;

public sealed partial class DiagnosticsViewModel : BaseViewModel
{
    private readonly IDiagnosticsApplicationService _diagnosticsService;
    private CancellationTokenSource? _diagnosticsCts;

    public ObservableCollection<SearchResultItem> RetrievalMatches { get; } = [];

    public ObservableCollection<ChunkSummaryItem> Chunks { get; } = [];

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private int maxResults = 5;

    [ObservableProperty]
    private string promptPreview = string.Empty;

    [ObservableProperty]
    private string metricsSummary = string.Empty;

    [ObservableProperty]
    private RuntimeDiagnosticsView? runtime;

    public DiagnosticsViewModel(IDiagnosticsApplicationService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
    }

    [RelayCommand(CanExecute = nameof(CanInspect))]
    public async Task InspectAsync()
    {
        ClearError();
        IsBusy = true;
        RetrievalMatches.Clear();
        Chunks.Clear();
        _diagnosticsCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Collecting diagnostics...";
            var summary = await _diagnosticsService.InspectAsync(Query, MaxResults, _diagnosticsCts.Token).ConfigureAwait(true);
            foreach (var match in summary.RetrievalMatches)
            {
                RetrievalMatches.Add(match);
            }

            foreach (var chunk in summary.Chunks.Take(250))
            {
                Chunks.Add(chunk);
            }

            PromptPreview = summary.PromptPreview;
            Runtime = summary.Runtime;
            MetricsSummary = $"Retrieval {summary.RetrievalDuration.TotalMilliseconds:0} ms, prompt {summary.EstimatedPromptTokens} tokens, chunks {summary.Chunks.Count}";
            StatusMessage = "Diagnostics ready.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Diagnostics cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ex, "Diagnostics failed.");
        }
        finally
        {
            IsBusy = false;
            _diagnosticsCts.Dispose();
            _diagnosticsCts = null;
            InspectCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _diagnosticsCts?.Cancel();
    }

    partial void OnQueryChanged(string value)
    {
        InspectCommand.NotifyCanExecuteChanged();
    }

    private bool CanInspect() => !IsBusy && !string.IsNullOrWhiteSpace(Query);
}
