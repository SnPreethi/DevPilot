using DevPilot.Indexer;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Xunit;

namespace DevPilot.Indexer.Tests;

public sealed class CodeChunkerTests
{
    [Fact]
    public void Chunk_CSharp_CreatesSymbolChunks()
    {
        const string content = """
            public interface IExample
            {
                void Run();
            }

            public sealed class Example : IExample
            {
                public void Run()
                {
                }
            }
            """;

        var chunker = new CodeChunker(NullLogger<CodeChunker>.Instance);

        var chunks = chunker.Chunk("repo-1", "file-1", "Example.cs", content, "csharp");

        Assert.Contains(chunks, chunk => chunk.ChunkType == "interface" && chunk.SymbolName == "IExample");
        Assert.Contains(chunks, chunk => chunk.ChunkType == "class" && chunk.SymbolName == "Example");
        Assert.Contains(chunks, chunk => chunk.ChunkType == "method" && chunk.SymbolName == "Run");
        Assert.All(chunks, chunk => Assert.True(chunk.StartLine > 0));
    }

    [Fact]
    public void Chunk_CSharp_ExtractsRichMetadata()
    {
        const string content = """
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public class MyClass
                {
                    public void ExecuteTask()
                    {
                        var helper = new Helper();
                        helper.DoWork();
                    }
                }
            }
            """;

        var chunker = new CodeChunker(NullLogger<CodeChunker>.Instance);
        var chunks = chunker.Chunk("repo-1", "file-1", "MyClass.cs", content, "csharp");

        var classChunk = chunks.FirstOrDefault(c => c.ChunkType == "class" && c.SymbolName == "MyClass");
        Assert.NotNull(classChunk);
        Assert.Equal("TestNamespace", classChunk.Namespace);
        Assert.Contains("System", classChunk.ImportedNamespaces);
        Assert.Contains("System.Threading.Tasks", classChunk.ImportedNamespaces);

        var methodChunk = chunks.FirstOrDefault(c => c.ChunkType == "method" && c.SymbolName == "ExecuteTask");
        Assert.NotNull(methodChunk);
        Assert.Equal("MyClass", methodChunk.ParentSymbol);
        Assert.Contains("Helper", methodChunk.ReferencedSymbols);
    }

    [Fact]
    public void Chunk_TypeScriptJavaScript_ExtractsRichMetadata()
    {
        const string content = """
            import { Helper } from "./helper";
            
            class MyService {
                constructor() {}
                async performAction() {
                    const helper = new Helper();
                    await helper.run();
                }
            }
            """;

        var chunker = new CodeChunker(NullLogger<CodeChunker>.Instance);
        var chunks = chunker.Chunk("repo-1", "file-1", "service.ts", content, "typescript");

        var classChunk = chunks.FirstOrDefault(c => c.ChunkType == "class" && c.SymbolName == "MyService");
        Assert.NotNull(classChunk);
        Assert.Contains("./helper", classChunk.ImportedNamespaces);

        var methodChunk = chunks.FirstOrDefault(c => c.ChunkType == "method" && c.SymbolName == "performAction");
        Assert.NotNull(methodChunk);
        Assert.Equal("MyService", methodChunk.ParentSymbol);
        Assert.Contains("Helper", methodChunk.ReferencedSymbols);
    }

    [Fact]
    public void Chunk_Markdown_CreatesHeadingSections()
    {
        const string content = """
            # Intro
            hello
            ## Details
            more
            """;

        var chunker = new CodeChunker(NullLogger<CodeChunker>.Instance);

        var chunks = chunker.Chunk("repo-1", "file-1", "README.md", content, "markdown");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Intro", chunks[0].SymbolName);
        Assert.Equal("section", chunks[0].ChunkType);
    }
}
