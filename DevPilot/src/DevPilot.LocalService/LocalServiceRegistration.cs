using Microsoft.Extensions.DependencyInjection;
using DevPilot.Patching;

namespace DevPilot.LocalService;

public static class LocalServiceRegistration
{
    public static IServiceCollection AddDevPilotLocalService(this IServiceCollection services)
    {
        services.AddDevPilotPatching();
        services.AddHostedService<DevPilotWorker>();
        return services;
    }
}
