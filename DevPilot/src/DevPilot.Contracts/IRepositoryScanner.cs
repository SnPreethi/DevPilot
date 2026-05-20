namespace DevPilot.Contracts;

public interface IRepositoryScanner
{
    IAsyncEnumerable<RepositoryFile> ScanAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default);
}
