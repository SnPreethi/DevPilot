using DevPilot.AI;
using DevPilot.Core;
using DevPilot.Indexer;
using DevPilot.LocalService;
using DevPilot.RAG;
using DevPilot.Storage;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DevPilot.UI;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        _host = CreateHost();
    }

    public static T GetService<T>()
        where T : notnull
    {
        return ((App)Current)._host.Services.GetRequiredService<T>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync().ConfigureAwait(true);
        _window = GetService<MainWindow>();
        _window.Activate();
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "DEVPILOT_");

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        builder.Services
            .AddDevPilotCore(builder.Configuration)
            .AddDevPilotStorage()
            .AddDevPilotIndexer()
            .AddDevPilotAi()
            .AddDevPilotRag()
            .AddDevPilotLocalService()
            .AddDevPilotUi();

        return builder.Build();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _host.Services.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Unhandled UI exception.");
        e.Handled = true;
    }
}
