using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPilot.UI.Models;
using DevPilot.UI.Services;

namespace DevPilot.UI.ViewModels;

public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsApplicationService _settingsService;

    [ObservableProperty]
    private RuntimeSettingsView? settings;

    public SettingsViewModel(ISettingsApplicationService settingsService)
    {
        _settingsService = settingsService;
    }

    [RelayCommand]
    public void Load()
    {
        try
        {
            ClearError();
            Settings = _settingsService.GetSettings();
            StatusMessage = "Settings loaded.";
        }
        catch (Exception ex)
        {
            SetError(ex, "Settings failed to load.");
        }
    }
}
