using DevPilot.UI.Services;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class AppShell : UserControl
{
    private readonly INavigationService _navigationService;

    public AppShell()
    {
        InitializeComponent();
        _navigationService = App.GetService<INavigationService>();
        _navigationService.Initialize(ContentFrame);
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        _navigationService.Navigate("Repositories");
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _navigationService.Navigate("Settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string pageKey)
        {
            _navigationService.Navigate(pageKey);
        }
    }
}
