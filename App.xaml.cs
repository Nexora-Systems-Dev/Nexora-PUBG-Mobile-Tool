using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Services.Performance;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services => ((App)Current)._serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Purge stale update staging directories left from previous runs.
        _ = Task.Run(() => UpdateService.PurgeStaleStagingDirectories());

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new TempCleanupOptions());
        services.AddSingleton(new IpadLayoutOptions());

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRegistryService, RegistryService>();

        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IGameLoopService, GameLoopService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ITempCleanupService, TempCleanupService>();
        services.AddSingleton<IIpadLayoutService, IpadLayoutService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();

        services.AddSingleton<WindowsToolsService>();
        services.AddSingleton<IWindowsToolsService>(sp => sp.GetRequiredService<WindowsToolsService>());
        services.AddSingleton<IGameLoopPerformanceEngine>(sp => sp.GetRequiredService<WindowsToolsService>());

        services.AddTransient<MainWindow>();
    }
}
