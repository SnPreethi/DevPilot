using DevPilot.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DevPilot.UI.Views;

public sealed partial class AssistantPage : Page
{
    public AssistantPage()
    {
        InitializeComponent();
        DataContext = App.GetService<AssistantViewModel>();
    }
}
