namespace DevPilot.Contracts;

public interface ICodeChunker
{
    IAsyncEnumerable<CodeChunk> ChunkAsync(
        RepositoryDescriptor repository,
        RepositoryFile file,
        CancellationToken cancellationToken = default);
}
