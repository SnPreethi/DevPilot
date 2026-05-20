namespace DevPilot.AI.Tests;

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

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
