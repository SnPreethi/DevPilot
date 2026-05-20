namespace DevPilot.Contracts;

public interface IRepositoryIndexingService
{
    Task<IndexingResult> IndexAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
