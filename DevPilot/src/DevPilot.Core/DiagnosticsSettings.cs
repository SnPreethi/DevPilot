namespace DevPilot.Core;

public sealed class DiagnosticsSettings
{
    public bool EnableRetrievalDiagnostics { get; set; } = true;
    public bool IncludePromptPreview { get; set; } = true;
    public int DefaultInspectionLimit { get; set; } = 25;
}
