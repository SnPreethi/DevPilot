using System;
using DevPilot.Contracts;
using DevPilot.Core.Memory;
using DevPilot.RAG;
using Xunit;

namespace DevPilot.RAG.Tests;

public sealed class MemoryPromptTests
{
    [Fact]
    public void MemoryAwarePromptBuilder_FormatsEnrichedPrompt()
    {
        var builder = new MemoryAwarePromptBuilder();
        var conventions = new RepositoryConventions(
            PrefixInterfacesWithI: true,
            SuffixAsyncMethods: true,
            PrivateFieldPrefix: "_",
            LoggingLibrary: "Microsoft.Extensions.Logging",
            DiStyle: "Microsoft.Extensions.DependencyInjection"
        );

        var recentFixes = new[]
        {
            new WorkspaceEvent("repo1", "fix", DateTime.UtcNow, "src/Program.cs", "Main", "Fixed startup null exception", "success")
        };

        var layers = new[]
        {
            new ArchitecturalLayer("DevPilot.Core", "src/DevPilot.Core", new[] { "DevPilot.Contracts" })
        };

        var memoryContext = new MemoryContext(recentFixes, conventions, layers);

        var prompt = builder.EnrichPromptWithMemory("Explain the method.", memoryContext);

        Assert.Contains("=== SYSTEM WORKSPACE MEMORY & GUIDELINES ===", prompt);
        Assert.Contains("Suffix async methods with 'Async': True", prompt);
        Assert.Contains("Fixed startup null exception", prompt);
        Assert.Contains("Layer 'DevPilot.Core'", prompt);
        Assert.Contains("Explain the method.", prompt);
    }
}
