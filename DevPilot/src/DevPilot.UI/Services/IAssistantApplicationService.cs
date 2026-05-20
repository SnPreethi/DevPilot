using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public interface IAssistantApplicationService
{
    Task<AssistantResponseItem> AskAsync(
        string question,
        int maxContextChunks,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamResponseAsync(
        string question,
        int maxContextChunks,
        CancellationToken cancellationToken = default);
}
