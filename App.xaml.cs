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
        // Options
        services.AddSingleton(new TempCleanupOptions());
        services.AddSingleton(new IpadLayoutOptions());

        // Kernel & Infrastructure
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRegistryService, RegistryService>();

        // Core Services
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IGameLoopService, GameLoopService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ITempCleanupService, TempCleanupService>();
        services.AddSingleton<IIpadLayoutService, IpadLayoutService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();

        // Facade & Performance Engine
        services.AddSingleton<WindowsToolsService>();
        services.AddSingleton<IWindowsToolsService>(sp => sp.GetRequiredService<WindowsToolsService>());
        services.AddSingleton<IGameLoopPerformanceEngine>(sp => sp.GetRequiredService<WindowsToolsService>());

        // View
        services.AddTransient<MainWindow>();
    }
}
