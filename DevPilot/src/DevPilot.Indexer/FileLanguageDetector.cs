namespace DevPilot.Indexer;

public sealed class FileLanguageDetector
{
    private static readonly IReadOnlyDictionary<string, string> LanguagesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".ts"] = "typescript",
            [".js"] = "javascript",
            [".py"] = "python",
            [".java"] = "java",
            [".md"] = "markdown",
            [".json"] = "json",
            [".yaml"] = "yaml",
            [".yml"] = "yaml"
        };

    public bool IsSupported(string path)
    {
        return LanguagesByExtension.ContainsKey(Path.GetExtension(path));
    }

    public string Detect(string path)
    {
        return LanguagesByExtension.TryGetValue(Path.GetExtension(path), out var language)
            ? language
            : "text";
    }
}
