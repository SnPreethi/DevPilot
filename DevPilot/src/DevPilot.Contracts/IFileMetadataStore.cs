namespace DevPilot.Contracts;

public interface IFileMetadataStore
{
    Task SaveAsync(
        FileMetadata fileMetadata,
        CancellationToken cancellationToken = default);

    Task<FileMetadata?> GetAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileMetadata>> ListByRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteMissingAsync(
        string repositoryId,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken cancellationToken = default);
}
