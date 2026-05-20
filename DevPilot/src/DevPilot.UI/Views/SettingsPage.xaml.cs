using DevPilot.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<SettingsViewModel>();
        DataContext = ViewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadCommand.Execute(null);
    }
}
