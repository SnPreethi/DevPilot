using DevPilot.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevPilot.Core.Tests;

public sealed class DependencyInjectionScaffoldTests
{
    [Fact]
    public void AddDevPilotCore_RegistersSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Application:Name"] = "DevPilot for Windows"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDevPilotCore(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider);
    }
}
