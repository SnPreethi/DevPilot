namespace DevPilot.Core;

public sealed class VectorSearchSettings
{
    public int DefaultTopK { get; init; } = 5;

    public string SimilarityMetric { get; init; } = "cosine";

    public bool UseSqliteVss { get; init; }

    public string? SqliteVssExtensionPath { get; init; }
}
