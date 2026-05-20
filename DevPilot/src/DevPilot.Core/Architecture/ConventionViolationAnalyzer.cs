using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Architecture;

public sealed class ConventionViolationAnalyzer : IConventionViolationAnalyzer
{
    private readonly IGraphStore _graphStore;
    private readonly ILogger<ConventionViolationAnalyzer> _logger;

    public ConventionViolationAnalyzer(IGraphStore graphStore, ILogger<ConventionViolationAnalyzer> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConventionViolation>> AnalyzeConventionsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing code conventions in repository {RepositoryId}.", repositoryId);

        var violations = new List<ConventionViolation>();

        // Query all active symbol nodes
        var allNodes = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Symbol, cancellationToken: cancellationToken).ConfigureAwait(false);
        var nodes = allNodes.Where(n => string.Equals(n.EntityId, repositoryId, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var node in nodes)
        {
            var label = node.Label;
            var filePath = node.Metadata ?? "Unknown";

            // 1. Interface Prefix Violation
            if (label.Contains("interface ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("I", StringComparison.Ordinal) && char.IsUpper(label.ElementAtOrDefault(1)))
            {
                // Simple interface name conventions check
                if (label.Contains("interface ", StringComparison.OrdinalIgnoreCase) && !label.Contains("interface I", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ConventionViolation(
                        NodeId: node.NodeId,
                        NodeLabel: label,
                        FilePath: filePath,
                        RuleViolated: "InterfacePrefix",
                        ExpectedFormat: "interface I[Name]",
                        FoundFormat: label
                    ));
                }
            }

            // 2. Async Suffix Violation
            if (label.Contains("async Task", StringComparison.OrdinalIgnoreCase) && !label.Contains("Async(", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ConventionViolation(
                    NodeId: node.NodeId,
                    NodeLabel: label,
                    FilePath: filePath,
                    RuleViolated: "AsyncSuffix",
                    ExpectedFormat: "[MethodName]Async",
                    FoundFormat: label
                ));
            }

            // 3. Private Field Underscore Prefix Violation
            if (label.Contains("private ") && !label.Contains("_") && label.Contains(";"))
            {
                violations.Add(new ConventionViolation(
                    NodeId: node.NodeId,
                    NodeLabel: label,
                    FilePath: filePath,
                    RuleViolated: "PrivateFieldPrefix",
                    ExpectedFormat: "private [Type] _[name]",
                    FoundFormat: label
                ));
            }
        }

        return violations;
    }
}
