using DevPilot.UI.Models;

namespace DevPilot.UI.Services;

public interface ISettingsApplicationService
{
    RuntimeSettingsView GetSettings();
}
