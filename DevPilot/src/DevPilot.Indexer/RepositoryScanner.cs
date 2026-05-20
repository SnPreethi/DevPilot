using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace DevPilot.Indexer;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private readonly FileLanguageDetector _languageDetector;
    private readonly IndexingSettings _settings;
    private readonly ILogger<RepositoryScanner> _logger;

    public RepositoryScanner(
        FileLanguageDetector languageDetector,
        IOptions<IndexingSettings> settings,
        ILogger<RepositoryScanner> logger)
    {
        _languageDetector = languageDetector;
        _settings = settings.Value;
        _logger = logger;
    }

    public int LastIgnoredFileCount { get; private set; }

    public async IAsyncEnumerable<RepositoryFile> ScanAsync(
        RepositoryDescriptor repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastIgnoredFileCount = 0;
        var root = Path.GetFullPath(repository.RootPath);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            foreach (var directory in EnumerateDirectories(current))
            {
                if (ShouldIgnoreDirectory(directory))
                {
                    LastIgnoredFileCount++;
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in EnumerateFiles(current))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_languageDetector.IsSupported(file))
                {
                    LastIgnoredFileCount++;
                    continue;
                }

                var info = new FileInfo(file);
                if (info.Length > _settings.MaxFileSizeInBytes)
                {
                    LastIgnoredFileCount++;
                    continue;
                }

                yield return new RepositoryFile(
                    repository.Id,
                    Path.GetRelativePath(root, file),
                    file,
                    info.Length,
                    info.LastWriteTimeUtc);

                await Task.Yield();
            }
        }
    }

    private IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Skipping inaccessible directory {DirectoryPath}.", path);
            return [];
        }
    }

    private IEnumerable<string> EnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Skipping inaccessible files in directory {DirectoryPath}.", path);
            return [];
        }
    }

    private bool ShouldIgnoreDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return _settings.ExcludedDirectories.Any(ignored =>
            string.Equals(ignored, name, StringComparison.OrdinalIgnoreCase));
    }
}
