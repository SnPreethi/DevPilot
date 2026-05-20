namespace DevPilot.Core;

public sealed class StorageSettings
{
    public string DatabasePath { get; init; } = "data/devpilot.db";

    public bool CreateIfMissing { get; init; } = true;

    public bool Pooling { get; init; } = true;
}
