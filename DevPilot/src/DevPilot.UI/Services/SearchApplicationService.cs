using DevPilot.Contracts;
using DevPilot.Storage;
using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public sealed class SearchApplicationService : ISearchApplicationService
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly ISemanticSearchService _semanticSearchService;

    public SearchApplicationService(
        DatabaseInitializer databaseInitializer,
        ISemanticSearchService semanticSearchService)
    {
        _databaseInitializer = databaseInitializer;
        _semanticSearchService = semanticSearchService;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var result = await _semanticSearchService.SearchAsync(
            new SearchRequest(query, maxResults),
            cancellationToken).ConfigureAwait(false);

        return result.Matches.Select(match => new SearchResultItem(
            match.Rank,
            match.Chunk.ChunkId,
            match.Chunk.FilePath,
            match.Chunk.SymbolName,
            match.Chunk.ChunkType,
            match.Chunk.StartLine,
            match.Chunk.EndLine,
            match.Chunk.RelevanceScore,
            match.Chunk.ContentPreview)).ToList();
    }
}
