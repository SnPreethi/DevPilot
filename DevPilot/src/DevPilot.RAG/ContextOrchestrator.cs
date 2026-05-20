using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.RAG;

public sealed class ContextOrchestrator : IContextOrchestrator
{
    private readonly ISymbolStore _symbolStore;
    private readonly IChunkStore _chunkStore;
    private readonly ISemanticSearchService _semanticSearchService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<ContextOrchestrator> _logger;

    // Cache symbol graphs per repository to maintain high performance
    private static readonly ConcurrentDictionary<string, (SymbolGraph Graph, DateTime CachedAt)> GraphCache = new(StringComparer.OrdinalIgnoreCase);

    public ContextOrchestrator(
        ISymbolStore symbolStore,
        IChunkStore chunkStore,
        ISemanticSearchService semanticSearchService,
        ITokenEstimator tokenEstimator,
        ILogger<ContextOrchestrator> logger)
    {
        _symbolStore = symbolStore;
        _chunkStore = chunkStore;
        _semanticSearchService = semanticSearchService;
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedContext>> OrchestrateContextAsync(
        string question,
        string repositoryId,
        string? activeFilePath,
        int? cursorLine,
        string? selectedCode,
        int maxTokenBudget,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Orchestrating context for query. Budget tokens: {Budget}", maxTokenBudget);

        // 1. Build or Load Symbol Graph
        var graph = await GetOrCreateSymbolGraphAsync(repositoryId, cancellationToken).ConfigureAwait(false);

        var list = new List<RetrievedContext>();
        var seenChunkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 2. Active File Surrounding Context
        if (!string.IsNullOrWhiteSpace(selectedCode))
        {
            var chunkId = $"active_selection_{DeterministicHash(selectedCode)}";
            var text = $"// Selected Code:\n{selectedCode}";
            var score = 1.0;
            list.Add(new RetrievedContext(chunkId, activeFilePath ?? "active_selection", null, "active_file", cursorLine ?? 1, cursorLine ?? 1, text, score));
            seenChunkIds.Add(chunkId);
        }

        // 3. Resolve Cursor Scope and Active Symbol
        SymbolIndexEntry? activeSymbol = null;
        if (!string.IsNullOrWhiteSpace(activeFilePath) && cursorLine.HasValue && graph != null)
        {
            var cleanPath = activeFilePath.Replace("\\", "/");
            activeSymbol = graph.Nodes
                .Where(n => (n.FilePath.Replace("\\", "/").EndsWith(cleanPath, StringComparison.OrdinalIgnoreCase) || cleanPath.EndsWith(n.FilePath.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase)) &&
                            cursorLine >= n.StartLine && cursorLine <= n.EndLine)
                .OrderBy(n => n.EndLine - n.StartLine) // Smallest spanning scope
                .FirstOrDefault();

            if (activeSymbol != null)
            {
                _logger.LogInformation("Cursor identified inside active symbol: {Name} ({Kind})", activeSymbol.Name, activeSymbol.Kind);
                
                // Add active symbol definition to context
                var activeChunk = await _chunkStore.GetAsync(activeSymbol.SymbolId, cancellationToken).ConfigureAwait(false);
                if (activeChunk != null)
                {
                    var rc = MapChunkToContext(activeChunk, "active_symbol", 1.0);
                    if (seenChunkIds.Add(rc.ChunkId)) list.Add(rc);
                }

                // Add Sibling methods / symbols
                var parentSymbol = graph.GetParent(activeSymbol.SymbolId);
                if (parentSymbol != null)
                {
                    var siblings = graph.GetChildren(parentSymbol.SymbolId)
                        .Where(sib => !string.Equals(sib.SymbolId, activeSymbol.SymbolId, StringComparison.OrdinalIgnoreCase));

                    foreach (var sibling in siblings)
                    {
                        var siblingChunk = await _chunkStore.GetAsync(sibling.SymbolId, cancellationToken).ConfigureAwait(false);
                        if (siblingChunk != null)
                        {
                            var rc = MapChunkToContext(siblingChunk, "sibling_symbol", 0.8);
                            if (seenChunkIds.Add(rc.ChunkId)) list.Add(rc);
                        }
                    }
                }

                // Add referenced symbol definitions (Calls / Types used)
                var references = graph.GetReferences(activeSymbol.SymbolId);
                foreach (var r in references.Take(5)) // Limit expansion to prevent budget exhaustion
                {
                    var refChunk = await _chunkStore.GetAsync(r.SymbolId, cancellationToken).ConfigureAwait(false);
                    if (refChunk != null)
                    {
                        var rc = MapChunkToContext(refChunk, "definition", 0.7);
                        if (seenChunkIds.Add(rc.ChunkId)) list.Add(rc);
                    }
                }
            }
        }

        // 4. Semantic Retrieval
        if (!string.IsNullOrWhiteSpace(question))
        {
            var searchResult = await _semanticSearchService.SearchAsync(
                new SearchRequest(question, 15, repositoryId), cancellationToken).ConfigureAwait(false);

            foreach (var match in searchResult.Matches)
            {
                var fullChunk = await _chunkStore.GetAsync(match.Chunk.ChunkId, cancellationToken).ConfigureAwait(false);
                if (fullChunk != null)
                {
                    var rc = MapChunkToContext(fullChunk, "semantic", match.Chunk.RelevanceScore);
                    if (seenChunkIds.Add(rc.ChunkId))
                    {
                        list.Add(rc);

                        // Expand references for high scoring semantic matches (score > 0.75)
                        if (match.Chunk.RelevanceScore > 0.75 && graph != null)
                        {
                            var matchingSymbol = graph.Nodes.FirstOrDefault(n => string.Equals(n.SymbolId, match.Chunk.ChunkId, StringComparison.OrdinalIgnoreCase));
                            if (matchingSymbol != null)
                            {
                                var refs = graph.GetReferences(matchingSymbol.SymbolId);
                                foreach (var r in refs.Take(3))
                                {
                                    var refChunk = await _chunkStore.GetAsync(r.SymbolId, cancellationToken).ConfigureAwait(false);
                                    if (refChunk != null)
                                    {
                                        var refRc = MapChunkToContext(refChunk, "definition", match.Chunk.RelevanceScore * 0.9);
                                        if (seenChunkIds.Add(refRc.ChunkId)) list.Add(refRc);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 5. Token Budget Packing
        var packedContexts = new List<RetrievedContext>();
        var currentTokenCount = 0;

        // Sort by priority type, then relevance score
        var prioritized = list
            .OrderBy(c => GetPriority(c.ChunkType))
            .ThenByDescending(c => c.RelevanceScore)
            .ToList();

        foreach (var ctx in prioritized)
        {
            var estimated = _tokenEstimator.Estimate(ctx.Content);
            if (currentTokenCount + estimated > maxTokenBudget)
            {
                _logger.LogInformation("Context token budget reached. Truncating context. Included tokens: {Count}", currentTokenCount);
                break;
            }

            packedContexts.Add(ctx);
            currentTokenCount += estimated;
        }

        _logger.LogInformation("Orchestrated {Count} context items. Combined token estimation: {Tokens}", packedContexts.Count, currentTokenCount);
        return packedContexts;
    }

    private async Task<SymbolGraph?> GetOrCreateSymbolGraphAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryId)) return null;

        if (GraphCache.TryGetValue(repositoryId, out var cached) && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(5))
        {
            return cached.Graph;
        }

        try
        {
            var nodes = await _symbolStore.ListByRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
            var edges = BuildGraphEdges(nodes);

            var graph = new SymbolGraph(nodes, edges);
            GraphCache[repositoryId] = (graph, DateTime.UtcNow);
            return graph;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load/build symbol graph for repository {RepositoryId}", repositoryId);
            return null;
        }
    }

    private static IReadOnlyList<SymbolRelation> BuildGraphEdges(IReadOnlyList<SymbolIndexEntry> nodes)
    {
        var edges = new List<SymbolRelation>();
        var nodesByName = nodes.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var nodesById = nodes.ToDictionary(n => n.SymbolId, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            // 1. Membership (Children -> Parent)
            if (!string.IsNullOrEmpty(node.ParentSymbol))
            {
                // Find parent symbol in the same namespace or file
                var parent = nodes.FirstOrDefault(p =>
                    string.Equals(p.Name, node.ParentSymbol, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase));

                if (parent != null)
                {
                    edges.Add(new SymbolRelation(node.SymbolId, parent.SymbolId, "membership"));
                }
            }

            // 2. References (Call / Type references)
            foreach (var refName in node.ReferencedSymbols)
            {
                if (nodesByName.TryGetValue(refName, out var matches))
                {
                    foreach (var match in matches)
                    {
                        edges.Add(new SymbolRelation(node.SymbolId, match.SymbolId, "reference"));
                    }
                }
            }
        }

        return edges;
    }

    private static int GetPriority(string chunkType)
    {
        return chunkType switch
        {
            "active_file" => 1,
            "active_symbol" => 2,
            "sibling_symbol" => 3,
            "definition" => 4,
            "semantic" => 5,
            _ => 6
        };
    }

    private static RetrievedContext MapChunkToContext(CodeChunk chunk, string type, double score)
    {
        return new RetrievedContext(
            chunk.ChunkId,
            chunk.FilePath,
            chunk.SymbolName,
            type,
            chunk.StartLine,
            chunk.EndLine,
            chunk.Content,
            score
        );
    }

    private static string DeterministicHash(string value)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
