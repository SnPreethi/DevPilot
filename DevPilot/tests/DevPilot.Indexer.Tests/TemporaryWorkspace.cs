namespace DevPilot.Indexer.Tests;

internal sealed class TemporaryWorkspace : IDisposable
{
    private TemporaryWorkspace(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static TemporaryWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevPilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TemporaryWorkspace(root);
    }

    public void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(RootPath, relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
