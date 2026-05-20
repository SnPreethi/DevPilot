namespace DevPilot.Contracts;

public interface IPromptBuilder
{
    Task<GroundedPrompt> BuildAsync(
        string question,
        IReadOnlyList<RetrievedContext> context,
        CancellationToken cancellationToken = default);
}
