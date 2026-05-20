namespace DevPilot.Core;

public sealed class IndexingSettings
{
    public int MaxFileSizeInBytes { get; init; } = 1_048_576;

    public IReadOnlyList<string> ExcludedDirectories { get; init; } =
    [
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "packages",
        ".venv",
        "venv",
        "env",
        "__pycache__",
        ".pytest_cache",
        ".mypy_cache",
        ".ruff_cache",
        ".next",
        "out",
        "coverage"
    ];
}
