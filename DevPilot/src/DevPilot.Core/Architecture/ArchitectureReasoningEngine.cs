using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Architecture;

public sealed class ArchitectureReasoningEngine : IArchitectureReasoningEngine
{
    private readonly IDependencyBoundaryAnalyzer _boundaryAnalyzer;
    private readonly IConventionViolationAnalyzer _conventionAnalyzer;
    private readonly ILogger<ArchitectureReasoningEngine> _logger;

    public ArchitectureReasoningEngine(
        IDependencyBoundaryAnalyzer boundaryAnalyzer,
        IConventionViolationAnalyzer conventionAnalyzer,
        ILogger<ArchitectureReasoningEngine> logger)
    {
        _boundaryAnalyzer = boundaryAnalyzer;
        _conventionAnalyzer = conventionAnalyzer;
        _logger = logger;
    }

    public async Task<ArchitectureAnalysisSummary> RunFullAnalysisAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running full architecture integrity analysis for {RepositoryId}.", repositoryId);

        // Standard layer boundary rules for DevPilot:
        // 1. Contracts is pure (can reference nothing)
        // 2. Core can reference Contracts
        // 3. LocalService can reference Contracts and Core
        var rules = new List<LayerBoundaryRule>
        {
            new LayerBoundaryRule("Contracts", Array.Empty<string>()),
            new LayerBoundaryRule("Core", new[] { "Contracts" }),
            new LayerBoundaryRule("LocalService", new[] { "Contracts", "Core" })
        };

        var violations = await _boundaryAnalyzer.AnalyzeBoundariesAsync(repositoryId, rules, cancellationToken).ConfigureAwait(false);
        var conventionViolations = await _conventionAnalyzer.AnalyzeConventionsAsync(repositoryId, cancellationToken).ConfigureAwait(false);

        // Score drift based on violation severity
        double violationWeight = violations.Sum(v => v.SeverityScore);
        double conventionWeight = conventionViolations.Count * 0.1;
        double driftScore = Math.Clamp((violationWeight + conventionWeight) / 10.0, 0.0, 1.0);

        string explanation = driftScore > 0.0
            ? $"Repository has detected {violations.Count} dependency boundary violations and {conventionViolations.Count} convention discrepancies. Code integrity is at risk."
            : "Repository is fully pristine. All architectural layers and coding style rules are strictly followed.";

        return new ArchitectureAnalysisSummary(
            RepositoryId: repositoryId,
            Violations: violations,
            ConventionViolations: conventionViolations,
            ArchitecturalDriftScore: driftScore,
            SummaryExplanation: explanation
        );
    }
}
