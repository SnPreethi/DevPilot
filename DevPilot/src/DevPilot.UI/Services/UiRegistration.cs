using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DevPilot.UI.Services;

public static class UiRegistration
{
    public static IServiceCollection AddDevPilotUi(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IRepositoryApplicationService, RepositoryApplicationService>();
        services.AddSingleton<ISearchApplicationService, SearchApplicationService>();
        services.AddSingleton<IAssistantApplicationService, AssistantApplicationService>();
        services.AddSingleton<IDiagnosticsApplicationService, DiagnosticsApplicationService>();
        services.AddSingleton<ISettingsApplicationService, SettingsApplicationService>();

        services.AddTransient<RepositoriesViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<AssistantViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
