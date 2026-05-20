using DevPilot.UI.Services;
using Xunit;

namespace DevPilot.UI.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public void Navigate_ReturnsFalseBeforeFrameInitialization()
    {
        var service = new NavigationService();

        Assert.False(service.Navigate("Search"));
        Assert.False(service.GoBack());
    }
}
