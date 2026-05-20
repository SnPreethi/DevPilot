namespace DevPilot.Contracts;

public sealed record ChatResponse(
    string Content,
    string ModelId);
