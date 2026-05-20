using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public interface IContextOrchestrator
{
    Task<IReadOnlyList<RetrievedContext>> OrchestrateContextAsync(
        string question,
        string repositoryId,
        string? activeFilePath,
        int? cursorLine,
        string? selectedCode,
        int maxTokenBudget,
        CancellationToken cancellationToken = default);
}
