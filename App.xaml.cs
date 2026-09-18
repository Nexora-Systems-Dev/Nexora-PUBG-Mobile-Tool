using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
using Nexora.Features.GameLoop;
using Nexora.Features.Layout;
using Nexora.Features.SystemTools;
using Nexora.Features.SystemTools.Network;
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
        _ = Task.Run(() => StagingDirectoryGC.PurgeStaleStagingDirectories());

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
        services.AddSingleton(sp => new IpadLayoutOptions(sp.GetRequiredService<EmulatorOptions>()));
        services.AddSingleton(new GameLoopOptions());
        services.AddSingleton(new EmulatorOptions());
        services.AddSingleton(new UpdateOptions());

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<RegistryService>();
        services.AddSingleton<IUserRegistry>(sp => sp.GetRequiredService<RegistryService>());
        services.AddSingleton<IMachineRegistry>(sp => sp.GetRequiredService<RegistryService>());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IWorkRootProvider, GameLoopWorkRootProvider>();
        services.AddSingleton<GameLoopWorkingStorage>();
        services.AddSingleton<IGameLoopProcessService, GameLoopProcessService>();
        services.AddSingleton<IGameLoopPathResolver, GameLoopPathResolver>();

        services.AddSingleton<IProcessPrioritySnapshotStore, ProcessPrioritySnapshotStore>();
        services.AddSingleton<IProcessPriorityApplier, ProcessPriorityApplier>();
        services.AddSingleton<IProcessPriorityMonitor, ProcessPriorityMonitor>();
        services.AddSingleton<ProcessPriorityService>();

        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<GameLoopService>();
        services.AddSingleton<IGameLoopConnection>(sp => sp.GetRequiredService<GameLoopService>());
        services.AddSingleton<IGraphicsProfileStore>(sp => sp.GetRequiredService<GameLoopService>());
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IEmulatorSettingsService, EmulatorSettingsService>();
        services.AddSingleton<ITempCleanupService, TempCleanupService>();
        services.AddSingleton<IIpadLayoutService, IpadLayoutService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();
        services.AddSingleton<IShortcutService>(sp => new ShortcutService(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IGameLoopPathResolver>(),
            Path.Combine(AppContext.BaseDirectory, sp.GetRequiredService<EmulatorOptions>().Assets.DirectoryName),
            sp.GetRequiredService<EmulatorOptions>()));

        services.AddSingleton<IGameLoopPerformanceEngine, PerformanceEngineFacade>();

        services.AddTransient<MainWindow>();
    }
}
