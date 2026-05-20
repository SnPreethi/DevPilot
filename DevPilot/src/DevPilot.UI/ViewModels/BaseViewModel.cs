using CommunityToolkit.Mvvm.ComponentModel;

namespace DevPilot.UI.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string statusMessage = "Ready";

    protected void SetError(Exception exception, string fallbackMessage)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(exception.Message)
            ? fallbackMessage
            : exception.Message;
        StatusMessage = fallbackMessage;
    }

    protected void ClearError()
    {
        ErrorMessage = null;
    }
}
