namespace DevPilot.Contracts;

public interface ISemanticSearchService
{
    Task<SemanticSearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);
}
