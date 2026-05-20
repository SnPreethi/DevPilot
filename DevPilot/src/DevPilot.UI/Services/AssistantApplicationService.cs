using DevPilot.Contracts;
using DevPilot.Storage;
using DevPilot.UI.Models;
using System.Runtime.CompilerServices;

namespace DevPilot.UI.Services;

public sealed class AssistantApplicationService : IAssistantApplicationService
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IRagPipeline _ragPipeline;

    public AssistantApplicationService(
        DatabaseInitializer databaseInitializer,
        IRagPipeline ragPipeline)
    {
        _databaseInitializer = databaseInitializer;
        _ragPipeline = ragPipeline;
    }

    public async Task<AssistantResponseItem> AskAsync(
        string question,
        int maxContextChunks,
        CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var response = await _ragPipeline.AskAsync(
            new RagRequest(question, null, maxContextChunks),
            cancellationToken).ConfigureAwait(false);

        var context = response.ReferencedContext.Select((item, index) => new SearchResultItem(
            index + 1,
            item.ChunkId,
            item.FilePath,
            item.SymbolName,
            item.ChunkType,
            item.StartLine,
            item.EndLine,
            item.RelevanceScore,
            item.Content)).ToList();

        return new AssistantResponseItem(
            response.Answer,
            response.ReferencedContext.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            context,
            response.InferenceDuration,
            response.PromptTokenCount,
            response.OutputTokenCount);
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string question,
        int maxContextChunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await AskAsync(question, maxContextChunks, cancellationToken).ConfigureAwait(false);
        yield return response.Answer;
    }
}
