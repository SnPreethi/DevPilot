using Microsoft.UI.Dispatching;

namespace DevPilot.UI.Helpers;

public static class UiThreadGuard
{
    public static bool HasThreadAccess(DispatcherQueue dispatcherQueue)
    {
        return dispatcherQueue.HasThreadAccess;
    }
}
