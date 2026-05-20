namespace DevPilot.Contracts;

public interface ILLMService
{
    Task<InferenceResult> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default);
}
