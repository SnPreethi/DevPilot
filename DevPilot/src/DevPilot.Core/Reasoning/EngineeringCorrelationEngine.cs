using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core.Memory;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Reasoning;

public sealed class EngineeringCorrelationEngine : IEngineeringCorrelationEngine
{
    private readonly IGraphStore _graphStore;
    private readonly ArchitectureAnalyzer _architectureAnalyzer;
    private readonly ILogger<EngineeringCorrelationEngine> _logger;

    public EngineeringCorrelationEngine(
        IGraphStore graphStore,
        ILogger<EngineeringCorrelationEngine> logger)
    {
        _graphStore = graphStore;
        _architectureAnalyzer = new ArchitectureAnalyzer();
        _logger = logger;
    }

    public async Task<IReadOnlyList<CorrelationResult>> CorrelateFailuresToWorkflowsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Correlating failures to workflows in repository {RepositoryId}.", repositoryId);
        var correlations = new List<CorrelationResult>();

        // Query all diagnostic/failure nodes
        var diagnostics = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Diagnostic, entityId: repositoryId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var failures = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Failure, entityId: repositoryId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var targetNodes = diagnostics.Concat(failures).ToList();

        foreach (var target in targetNodes)
        {
            // Traverse outgoing relationships: target -> failed_in -> ExecutionPipeline -> generated_by -> Task -> belongs_to -> Workflow
            var rels = await _graphStore.GetRelationshipsAsync(target.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
            var pipelineRel = rels.FirstOrDefault(r => r.Kind == GraphRelationshipKind.FailedIn);

            if (pipelineRel != null)
            {
                var pipelineNode = await _graphStore.GetNodeAsync(pipelineRel.TargetNodeId, cancellationToken).ConfigureAwait(false);
                if (pipelineNode != null)
                {
                    var pipelineRels = await _graphStore.GetRelationshipsAsync(pipelineNode.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                    var taskRel = pipelineRels.FirstOrDefault(r => r.Kind == GraphRelationshipKind.GeneratedBy);

                    if (taskRel != null)
                    {
                        var taskNode = await _graphStore.GetNodeAsync(taskRel.TargetNodeId, cancellationToken).ConfigureAwait(false);
                        if (taskNode != null)
                        {
                            var taskRels = await _graphStore.GetRelationshipsAsync(taskNode.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                            var workflowRel = taskRels.FirstOrDefault(r => r.Kind == GraphRelationshipKind.BelongsTo);

                            if (workflowRel != null)
                            {
                                correlations.Add(new CorrelationResult(
                                    SourceEntityId: target.NodeId,
                                    SourceKind: target.Kind,
                                    TargetEntityId: workflowRel.TargetNodeId,
                                    TargetKind: GraphNodeKind.Workflow,
                                    RelationKind: "failed_within_workflow",
                                    Confidence: 0.95,
                                    Rationale: $"Incident {target.Label} was captured inside pipeline execution {pipelineNode.Label}, which ran for task {taskNode.Label} belonging to workflow {workflowRel.TargetNodeId}."
                                ));
                            }
                        }
                    }
                }
            }
        }

        return correlations;
    }

    public async Task<IReadOnlyList<CorrelationResult>> CorrelatePatchesToDiagnosticsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Correlating patches to diagnostics in repository {RepositoryId}.", repositoryId);
        var correlations = new List<CorrelationResult>();

        // Query all patch nodes
        var patches = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Patch, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var patch in patches)
        {
            // Check relationships: Patch -> modified_by -> File/Symbol -> violates -> Diagnostic
            var outgoing = await _graphStore.GetRelationshipsAsync(patch.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
            var modRels = outgoing.Where(r => r.Kind == GraphRelationshipKind.ModifiedBy);

            foreach (var mod in modRels)
            {
                var fileOrSymbol = await _graphStore.GetNodeAsync(mod.TargetNodeId, cancellationToken).ConfigureAwait(false);
                if (fileOrSymbol != null)
                {
                    var fileRels = await _graphStore.GetRelationshipsAsync(fileOrSymbol.NodeId, GraphDirection.Outgoing, cancellationToken).ConfigureAwait(false);
                    var violatesRels = fileRels.Where(r => r.Kind == GraphRelationshipKind.Violates);

                    foreach (var v in violatesRels)
                    {
                        correlations.Add(new CorrelationResult(
                            SourceEntityId: patch.NodeId,
                            SourceKind: GraphNodeKind.Patch,
                            TargetEntityId: v.TargetNodeId,
                            TargetKind: GraphNodeKind.Diagnostic,
                            RelationKind: "patch_addresses_violation",
                            Confidence: 0.85,
                            Rationale: $"Patch {patch.Label} modified {fileOrSymbol.Label} which is associated with active diagnostic {v.TargetNodeId}."
                        ));
                    }
                }
            }
        }

        return correlations;
    }

    public async Task<IReadOnlyList<CorrelationResult>> CorrelateExecutionToChangesAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Correlating execution history to repository changes in repository {RepositoryId}.", repositoryId);
        var correlations = new List<CorrelationResult>();

        // Correlate Pipeline execution to memory events based on temporal closeness (within 5 minutes)
        var pipelines = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.ExecutionPipeline, cancellationToken: cancellationToken).ConfigureAwait(false);
        var memoryEvents = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.MemoryEvent, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var pipeline in pipelines)
        {
            foreach (var memEvent in memoryEvents)
            {
                var timeDiff = (pipeline.CreatedUtc - memEvent.CreatedUtc).Duration();
                if (timeDiff.TotalMinutes <= 5.0)
                {
                    double confidence = 1.0 - (timeDiff.TotalSeconds / 600.0); // decay score
                    confidence = Math.Clamp(confidence, 0.5, 0.95);

                    correlations.Add(new CorrelationResult(
                        SourceEntityId: pipeline.NodeId,
                        SourceKind: GraphNodeKind.ExecutionPipeline,
                        TargetEntityId: memEvent.NodeId,
                        TargetKind: GraphNodeKind.MemoryEvent,
                        RelationKind: "temporally_correlated_with_change",
                        Confidence: confidence,
                        Rationale: $"Pipeline execution {pipeline.Label} occurred within {timeDiff.TotalSeconds:F1} seconds of workspace memory event {memEvent.Label}."
                    ));
                }
            }
        }

        return correlations;
    }

    public async Task<IReadOnlyList<CorrelationResult>> CorrelateArchitectureViolationsAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Correlating architectural layer violations in repository {RepositoryId}.", repositoryId);
        var correlations = new List<CorrelationResult>();

        // Query diagnostic nodes with kind 'Diagnostic' and label containing 'Architecture' or 'Layer'
        var diagnostics = await _graphStore.QueryNodesAsync(kind: GraphNodeKind.Diagnostic, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var diag in diagnostics)
        {
            if (diag.Label.Contains("Architecture", StringComparison.OrdinalIgnoreCase) ||
                diag.Label.Contains("Violation", StringComparison.OrdinalIgnoreCase) ||
                diag.Label.Contains("Layer", StringComparison.OrdinalIgnoreCase))
            {
                correlations.Add(new CorrelationResult(
                    SourceEntityId: diag.NodeId,
                    SourceKind: GraphNodeKind.Diagnostic,
                    TargetEntityId: repositoryId,
                    TargetKind: GraphNodeKind.Repository,
                    RelationKind: "violates_layer_dependency",
                    Confidence: 0.9,
                    Rationale: $"Diagnostic {diag.Label} identifies a strict architectural layer hierarchy violation within the codebase structures."
                ));
            }
        }

        return correlations;
    }
}
