using System.Text;
using DevPilot.Core.Memory;

namespace DevPilot.RAG;

public sealed class MemoryAwarePromptBuilder
{
    public string EnrichPromptWithMemory(
        string basePrompt,
        MemoryContext memory)
    {
        var builder = new StringBuilder();
        
        builder.AppendLine("=== SYSTEM WORKSPACE MEMORY & GUIDELINES ===");
        
        builder.AppendLine("- Detected Repository Conventions:");
        builder.AppendLine($"  * Suffix async methods with 'Async': {memory.Conventions.SuffixAsyncMethods}");
        builder.AppendLine($"  * Prefix interfaces with 'I': {memory.Conventions.PrefixInterfacesWithI}");
        builder.AppendLine($"  * Private field prefix: '{memory.Conventions.PrivateFieldPrefix}'");
        builder.AppendLine($"  * Logging library standard: '{memory.Conventions.LoggingLibrary}'");
        builder.AppendLine($"  * DI registration framework: '{memory.Conventions.DiStyle}'");
        builder.AppendLine("Follow these conventions strictly when writing or modifying code. Do not introduce alternative pattern styles.");
        builder.AppendLine();

        if (memory.RecentFixes.Count > 0)
        {
            builder.AppendLine("- Recent Successful Fixes in Repository (for matching pattern reference):");
            foreach (var fix in memory.RecentFixes)
            {
                builder.AppendLine($"  * File '{fix.FilePath}': {fix.Description} (Outcome: {fix.Outcome})");
            }
            builder.AppendLine();
        }

        if (memory.Layers.Count > 0)
        {
            builder.AppendLine("- Project Architectural Boundaries:");
            foreach (var layer in memory.Layers)
            {
                var depsStr = layer.Dependencies.Count > 0 ? string.Join(", ", layer.Dependencies) : "None";
                builder.AppendLine($"  * Layer '{layer.Name}' ({layer.FolderPattern}) -> Allowed Dependencies: [{depsStr}]");
            }
            builder.AppendLine("Do not introduce circular references or reference dependencies outside the defined boundaries.");
            builder.AppendLine();
        }

        builder.AppendLine("============================================");
        builder.AppendLine();
        builder.Append(basePrompt);

        return builder.ToString();
    }
}
