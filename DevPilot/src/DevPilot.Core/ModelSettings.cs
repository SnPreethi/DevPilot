namespace DevPilot.Core;

public sealed class ModelSettings
{
    public string ModelRootPath { get; init; } = "models";

    public bool LazyLoadModels { get; init; } = true;
}
