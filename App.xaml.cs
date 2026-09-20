using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Bootstrap;

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
        // Fire-and-forget: failure here must never block startup.
        _ = StartupTasks.PurgeStaleUpdateStagingAsync();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Configures the dependency container. Delegates to the composition root in
    /// <see cref="Bootstrap.ServiceCollectionExtensions"/>; kept as the public
    /// entry point so existing callers (tests, tooling) resolve the same graph.
    /// </summary>
    /// <param name="services">The collection to configure.</param>
    public static void ConfigureServices(IServiceCollection services)
        => services.AddNexoraServices();
}
