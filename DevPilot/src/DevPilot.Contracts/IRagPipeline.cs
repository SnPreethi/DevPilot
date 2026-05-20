namespace DevPilot.Contracts;

public interface IRagPipeline
{
    Task<AssistantResponse> AskAsync(
        RagRequest request,
        CancellationToken cancellationToken = default);
}
