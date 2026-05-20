using DevPilot.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        DataContext = App.GetService<SearchViewModel>();
    }
}
