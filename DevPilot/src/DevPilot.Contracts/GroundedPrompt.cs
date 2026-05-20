namespace DevPilot.Contracts;

public sealed record GroundedPrompt(
    string Text,
    IReadOnlyList<RetrievedContext> Context,
    int EstimatedTokenCount);
