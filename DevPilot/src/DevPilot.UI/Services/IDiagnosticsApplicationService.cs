using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public interface IDiagnosticsApplicationService
{
    Task<DiagnosticsSummary> InspectAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
