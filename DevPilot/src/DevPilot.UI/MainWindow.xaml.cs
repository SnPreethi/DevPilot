using Microsoft.UI.Xaml;

namespace DevPilot.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
    }
}
