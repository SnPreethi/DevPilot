using System;
using System.IO;
using DevPilot.Core.Memory;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class MemoryTests
{
    [Fact]
    public void ConventionAnalyzer_DetectsCSharpConventions()
    {
        var analyzer = new ConventionAnalyzer();
        
        var sample1 = @"
        namespace Test;
        public interface IRepository {
            Task SaveAsync();
        }
        ";

        var sample2 = @"
        namespace Test;
        public class MyService {
            private readonly string _name;
            public async Task SaveAsync() {
                await Task.CompletedTask;
            }
        }
        ";

        var conventions = analyzer.AnalyzeConventions(new[] { sample1, sample2 });

        Assert.True(conventions.PrefixInterfacesWithI);
        Assert.True(conventions.SuffixAsyncMethods);
        Assert.Equal("_", conventions.PrivateFieldPrefix);
        Assert.Equal("Microsoft.Extensions.Logging", conventions.LoggingLibrary);
    }

    [Fact]
    public void ArchitectureAnalyzer_DetectsLayeringBoundaries()
    {
        var analyzer = new ArchitectureAnalyzer();
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var srcDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(Path.Combine(srcDir, "DevPilot.Contracts"));
            Directory.CreateDirectory(Path.Combine(srcDir, "DevPilot.Core"));

            var layers = analyzer.AnalyzeArchitecture(tempDir);

            Assert.NotEmpty(layers);
            var contractLayer = Assert.Single(layers, l => l.Name == "DevPilot.Contracts");
            Assert.Equal("src/DevPilot.Contracts", contractLayer.FolderPattern);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
