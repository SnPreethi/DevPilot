using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.Indexer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.Indexer.Tests;

public sealed class RepositoryScannerTests
{
    [Fact]
    public async Task ScanAsync_IgnoresConfiguredDirectoriesAndUnsupportedFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        workspace.WriteFile("src\\Program.cs", "public sealed class Program { }");
        workspace.WriteFile("node_modules\\ignored.js", "console.log('ignored');");
        workspace.WriteFile("notes.txt", "unsupported");

        var scanner = CreateScanner();
        var repository = new RepositoryDescriptor("repo-1", "repo", workspace.RootPath, DateTimeOffset.UtcNow);

        var files = new List<RepositoryFile>();
        await foreach (var file in scanner.ScanAsync(repository))
        {
            files.Add(file);
        }

        Assert.Single(files);
        Assert.Equal(Path.Combine("src", "Program.cs"), files[0].RelativePath);
        Assert.True(scanner.LastIgnoredFileCount >= 2);
    }

    [Fact]
    public void FileLanguageDetector_DetectsSupportedExtensions()
    {
        var detector = new FileLanguageDetector();

        Assert.True(detector.IsSupported("component.ts"));
        Assert.True(detector.IsSupported("README.md"));
        Assert.False(detector.IsSupported("image.png"));
        Assert.Equal("csharp", detector.Detect("Program.cs"));
    }

    private static RepositoryScanner CreateScanner()
    {
        var settings = Options.Create(new IndexingSettings());
        return new RepositoryScanner(new FileLanguageDetector(), settings, NullLogger<RepositoryScanner>.Instance);
    }
}
