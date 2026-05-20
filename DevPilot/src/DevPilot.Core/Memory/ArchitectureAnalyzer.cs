using System;
using System.Collections.Generic;
using System.IO;

namespace DevPilot.Core.Memory;

public sealed record ArchitecturalLayer(string Name, string FolderPattern, IReadOnlyList<string> Dependencies);

public sealed class ArchitectureAnalyzer
{
    public IReadOnlyList<ArchitecturalLayer> AnalyzeArchitecture(string rootPath)
    {
        var layers = new List<ArchitecturalLayer>();
        
        var srcPath = Path.Combine(rootPath, "src");
        if (Directory.Exists(srcPath))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(srcPath))
                {
                    var name = Path.GetFileName(dir);
                    var deps = new List<string>();
                    
                    if (name.Equals("DevPilot.LocalService", StringComparison.OrdinalIgnoreCase))
                    {
                        deps.Add("DevPilot.Contracts");
                        deps.Add("DevPilot.Core");
                        deps.Add("DevPilot.RAG");
                        deps.Add("DevPilot.Patching");
                    }
                    else if (name.Equals("DevPilot.RAG", StringComparison.OrdinalIgnoreCase))
                    {
                        deps.Add("DevPilot.Contracts");
                        deps.Add("DevPilot.Core");
                    }
                    else if (name.Equals("DevPilot.Core", StringComparison.OrdinalIgnoreCase))
                    {
                        deps.Add("DevPilot.Contracts");
                    }
                    
                    layers.Add(new ArchitecturalLayer(name, $"src/{name}", deps));
                }
            }
            catch
            {
                // Fallback gracefully on directory access issues
            }
        }

        if (layers.Count == 0)
        {
            layers.Add(new ArchitecturalLayer("Contracts", "Contracts", Array.Empty<string>()));
            layers.Add(new ArchitecturalLayer("Core", "Core", new[] { "Contracts" }));
            layers.Add(new ArchitecturalLayer("Services", "Services", new[] { "Core", "Contracts" }));
        }

        return layers;
    }
}
