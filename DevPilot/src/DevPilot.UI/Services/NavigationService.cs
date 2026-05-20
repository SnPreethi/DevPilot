using DevPilot.UI.Views;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<string, Type> _pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Repositories"] = typeof(RepositoriesPage),
        ["Search"] = typeof(SearchPage),
        ["Assistant"] = typeof(AssistantPage),
        ["Diagnostics"] = typeof(DiagnosticsPage),
        ["Settings"] = typeof(SettingsPage)
    };

    private Frame? _frame;

    public void Initialize(Frame frame)
    {
        _frame = frame;
    }

    public bool Navigate(string pageKey)
    {
        if (_frame is null || !_pages.TryGetValue(pageKey, out var pageType))
        {
            return false;
        }

        if (_frame.CurrentSourcePageType == pageType)
        {
            return true;
        }

        return _frame.Navigate(pageType);
    }

    public bool GoBack()
    {
        if (_frame?.CanGoBack != true)
        {
            return false;
        }

        _frame.GoBack();
        return true;
    }
}
