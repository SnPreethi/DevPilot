using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;

namespace DevPilot.Core.Memory;

public sealed record MemoryContext(
    IReadOnlyList<WorkspaceEvent> RecentFixes,
    RepositoryConventions Conventions,
    IReadOnlyList<ArchitecturalLayer> Layers);

public sealed class PersistentContextOrchestrator
{
    private readonly IWorkspaceMemoryStore _memoryStore;
    private readonly ConventionAnalyzer _conventionAnalyzer;
    private readonly ArchitectureAnalyzer _architectureAnalyzer;

    public PersistentContextOrchestrator(
        IWorkspaceMemoryStore memoryStore,
        ConventionAnalyzer conventionAnalyzer,
        ArchitectureAnalyzer architectureAnalyzer)
    {
        _memoryStore = memoryStore;
        _conventionAnalyzer = conventionAnalyzer;
        _architectureAnalyzer = architectureAnalyzer;
    }

    public async Task<MemoryContext> LoadMemoryContextAsync(
        string repositoryId,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var allEvents = await _memoryStore.ListEventsAsync(repositoryId, 20, cancellationToken).ConfigureAwait(false);
        var recentFixes = allEvents
            .Where(e => e.EventType.Equals("fix", StringComparison.OrdinalIgnoreCase) && e.Outcome.Equals("success", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        var sampleContents = new List<string>();
        if (Directory.Exists(repositoryPath))
        {
            try
            {
                var files = Directory.GetFiles(repositoryPath, "*.cs", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(repositoryPath, "*.ts", SearchOption.AllDirectories))
                    .Take(10);

                foreach (var file in files)
                {
                    var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    sampleContents.Add(text);
                }
            }
            catch
            {
                // Proceed without conventions if file reading fails
            }
        }

        var conventions = _conventionAnalyzer.AnalyzeConventions(sampleContents);
        var layers = _architectureAnalyzer.AnalyzeArchitecture(repositoryPath);

        return new MemoryContext(recentFixes, conventions, layers);
    }
}
