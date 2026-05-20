namespace DevPilot.Storage;

public sealed record MigrationPlan(
    string Id,
    string Description,
    bool IsApplied);
