using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public interface ICompletionContextBuilder
{
    Task<string> BuildCompletionPromptAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default);
}
