using DevPilot.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class RepositoriesPage : Page
{
    public RepositoriesViewModel ViewModel { get; }

    public RepositoriesPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<RepositoriesViewModel>();
        DataContext = ViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Repositories.Count == 0)
        {
            await ViewModel.LoadAsync().ConfigureAwait(true);
        }
    }
}
