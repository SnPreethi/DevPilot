using Microsoft.Extensions.DependencyInjection;

namespace DevPilot.Patching;

public static class PatchingRegistration
{
    public static IServiceCollection AddDevPilotPatching(this IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceEditService, WorkspaceEditService>();
        return services;
    }
}
