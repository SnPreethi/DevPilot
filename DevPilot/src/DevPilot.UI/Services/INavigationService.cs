using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Services;

public interface INavigationService
{
    void Initialize(Frame frame);

    bool Navigate(string pageKey);

    bool GoBack();
}
