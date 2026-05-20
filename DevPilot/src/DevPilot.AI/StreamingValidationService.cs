using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace DevPilot.AI;

public sealed class StreamingValidationService : IStreamingValidationService
{
    private readonly ILLMService _llmService;
    private readonly LLMSettings _settings;

    public StreamingValidationService(
        ILLMService llmService,
        IOptions<LLMSettings> settings)
    {
        _llmService = llmService;
        _settings = settings.Value;
    }

    public async Task<StreamingValidationResult> ValidateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ModelValidationIssue>();
        var stopwatch = Stopwatch.StartNew();
        var partial = new StringBuilder();
        var tokens = 0;
        var cancellationObserved = false;
        using var validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var request = new InferenceRequest(
                prompt,
                _settings.ModelId,
                Math.Min(64, _settings.MaxOutputTokens),
                _settings.Temperature,
                _settings.TopP,
                []);

            await foreach (var token in _llmService.StreamAsync(request, validationCts.Token).ConfigureAwait(false))
            {
                partial.Append(token);
                tokens++;
                if (tokens == 2)
                {
                    validationCts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }

        if (validationCts.IsCancellationRequested)
        {
            cancellationObserved = true;
        }
        stopwatch.Stop();
        if (tokens == 0)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "STREAM_NO_TOKENS",
                "Streaming validation did not receive any tokens before completion or cancellation."));
        }

        if (!cancellationObserved)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Warning,
                "STREAM_CANCELLATION_NOT_OBSERVED",
                "Streaming completed normally before the validation cancellation trigger executed."));
        }

        return new StreamingValidationResult(
            tokens > 0,
            cancellationObserved,
            tokens,
            partial.ToString(),
            stopwatch.Elapsed,
            issues);
    }
}
