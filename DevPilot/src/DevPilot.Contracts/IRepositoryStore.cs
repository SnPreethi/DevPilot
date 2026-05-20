namespace DevPilot.Contracts;

public interface IRepositoryStore
{
    Task SaveAsync(
        RepositoryDocument repository,
        CancellationToken cancellationToken = default);

    Task<RepositoryDocument?> GetAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryDocument>> ListAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);
}
