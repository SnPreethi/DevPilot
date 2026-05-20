namespace DevPilot.Core;

public sealed class IncrementalIndexingSettings
{
    public bool Enabled { get; set; } = true;
    public bool RemoveDeletedFiles { get; set; } = true;
    public bool SkipUnchangedFiles { get; set; } = true;
}
