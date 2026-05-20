using DevPilot.Contracts;
using System.Security.Cryptography;

namespace DevPilot.Indexer;

public sealed class FileMetadataExtractor
{
    private readonly FileLanguageDetector _languageDetector;

    public FileMetadataExtractor(FileLanguageDetector languageDetector)
    {
        _languageDetector = languageDetector;
    }

    public async Task<FileMetadata> ExtractAsync(
        RepositoryDocument repository,
        RepositoryFile file,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(file.FullPath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var fileId = DeterministicId($"{repository.RepositoryId}:{file.RelativePath}");

        return new FileMetadata(
            fileId,
            repository.RepositoryId,
            repository.RepositoryName,
            file.FullPath,
            file.RelativePath,
            Path.GetExtension(file.FullPath).ToLowerInvariant(),
            _languageDetector.Detect(file.FullPath),
            file.SizeInBytes,
            hash,
            file.LastModifiedAt.ToUniversalTime());
    }

    private static string DeterministicId(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
