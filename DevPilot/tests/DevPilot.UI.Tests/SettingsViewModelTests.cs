using DevPilot.UI.Models;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using Xunit;

namespace DevPilot.UI.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void LoadCommand_LoadsSettings()
    {
        var viewModel = new SettingsViewModel(new FakeSettingsApplicationService());

        viewModel.Load();

        Assert.NotNull(viewModel.Settings);
        Assert.True(viewModel.Settings.OfflineOnly);
    }

    private sealed class FakeSettingsApplicationService : ISettingsApplicationService
    {
        public RuntimeSettingsView GetSettings()
        {
            return new RuntimeSettingsView("embedding.onnx", "llm.onnx", 5, 12000, 2000, true, "Ready", "Test hardware");
        }
    }
}
