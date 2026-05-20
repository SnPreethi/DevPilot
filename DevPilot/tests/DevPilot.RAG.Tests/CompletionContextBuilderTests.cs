using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class CompletionContextBuilderTests
{
    private readonly ITokenEstimator _tokenEstimator = new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings()));

    [Fact]
    public async Task BuildCompletionPromptAsync_AssemblesCorrectFimStructure()
    {
        // Arrange
        var symbolStore = new MockSymbolStore();
        var chunkStore = new MockChunkStore();
        var builder = new CompletionContextBuilder(symbolStore, chunkStore, _tokenEstimator);

        var request = new CompletionRequest(
            FilePath: "src/Service.cs",
            LanguageId: "csharp",
            CursorLine: 10,
            CursorColumn: 5,
            PrefixContent: "public class Service {\n",
            SuffixContent: "\n}"
        );

        // Act
        var prompt = (await builder.BuildCompletionPromptAsync(request, CancellationToken.None)).Replace("\r\n", "\n");

        // Assert
        Assert.Contains("You are an inline code completion assistant", prompt);
        Assert.Contains("[PREFIX]\npublic class Service {", prompt);
        Assert.Contains("[SUFFIX]\n\n}", prompt);
    }

    [Fact]
    public async Task BuildCompletionPromptAsync_IncludesOptionalParameters()
    {
        // Arrange
        var symbolStore = new MockSymbolStore();
        var chunkStore = new MockChunkStore();
        var builder = new CompletionContextBuilder(symbolStore, chunkStore, _tokenEstimator);

        var request = new CompletionRequest(
            FilePath: "src/Service.cs",
            LanguageId: "csharp",
            CursorLine: 10,
            CursorColumn: 5,
            PrefixContent: "public class Service {\n",
            SuffixContent: "\n}",
            ActiveSymbol: "Service.Calculate",
            Imports: new[] { "System", "System.Text" },
            NearbySymbols: new[] { "void Print()", "int GetVal()" }
        );

        // Act
        var prompt = (await builder.BuildCompletionPromptAsync(request, CancellationToken.None)).Replace("\r\n", "\n");

        // Assert
        Assert.Contains("Active symbol scope: Service.Calculate", prompt);
        Assert.Contains("Visible imports/usings: System, System.Text", prompt);
        Assert.Contains("Nearby symbol declarations: void Print(), int GetVal()", prompt);
    }

    [Fact]
    public async Task BuildCompletionPromptAsync_TrimsOverbudgetPrefixAndSuffix()
    {
        // Arrange
        var symbolStore = new MockSymbolStore();
        var chunkStore = new MockChunkStore();
        var builder = new CompletionContextBuilder(symbolStore, chunkStore, _tokenEstimator);

        // Create a prefix with 1000 lines (which will exceed 1500 tokens)
        var largePrefixLines = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            largePrefixLines.Add($"// Line content {i} representing some code that adds token count");
        }
        var prefixText = string.Join("\n", largePrefixLines);

        var request = new CompletionRequest(
            FilePath: "src/Service.cs",
            LanguageId: "csharp",
            CursorLine: 1005,
            CursorColumn: 5,
            PrefixContent: prefixText,
            SuffixContent: "var x = 1;"
        );

        // Act
        var prompt = (await builder.BuildCompletionPromptAsync(request, CancellationToken.None)).Replace("\r\n", "\n");

        // Assert
        // The prompt should be smaller than prefixText
        Assert.True(prompt.Length < prefixText.Length);
        // The suffix should still be present
        Assert.Contains("[SUFFIX]\nvar x = 1;", prompt);
    }

    private class MockSymbolStore : ISymbolStore
    {
        public Task SaveManyAsync(IReadOnlyCollection<SymbolIndexEntry> entries, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SymbolIndexEntry>> ListByRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SymbolIndexEntry>>(Array.Empty<SymbolIndexEntry>());

        public Task<IReadOnlyList<SymbolIndexEntry>> ListByFileAsync(string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SymbolIndexEntry>>(Array.Empty<SymbolIndexEntry>());

        public Task<int> DeleteByFileAsync(string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private class MockChunkStore : IChunkStore
    {
        public Task SaveAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveManyAsync(IReadOnlyCollection<CodeChunk> chunks, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<CodeChunk?> GetAsync(string chunkId, CancellationToken cancellationToken = default)
            => Task.FromResult<CodeChunk?>(null);

        public Task<IReadOnlyList<CodeChunk>> ListByFileAsync(string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CodeChunk>>(Array.Empty<CodeChunk>());

        public Task<IReadOnlyList<CodeChunk>> ListByRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CodeChunk>>(Array.Empty<CodeChunk>());

        public Task ReplaceFileChunksAsync(string fileId, IReadOnlyCollection<CodeChunk> chunks, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteMissingByFileAsync(string fileId, IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
