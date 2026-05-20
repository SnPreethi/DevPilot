using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Contracts;

public sealed record WorkspaceEvent(
    string RepositoryId,
    string EventType,
    DateTime TimestampUtc,
    string? FilePath,
    string? SymbolName,
    string Description,
    string Outcome,
    string? Payload = null);

public interface IWorkspaceMemoryStore
{
    Task SaveEventAsync(WorkspaceEvent ev, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceEvent>> ListEventsAsync(string repositoryId, int limit = 20, CancellationToken cancellationToken = default);
}
