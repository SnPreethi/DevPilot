using DevPilot.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        InitializeComponent();
        DataContext = App.GetService<DiagnosticsViewModel>();
    }
}
