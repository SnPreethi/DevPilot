namespace DevPilot.Core;

public sealed class EmbeddingVersioningSettings
{
    public string EmbeddingModelVersion { get; set; } = "1";
    public int EmbeddingSchemaVersion { get; set; } = 1;
    public bool ReembedStaleEmbeddings { get; set; } = true;
}
