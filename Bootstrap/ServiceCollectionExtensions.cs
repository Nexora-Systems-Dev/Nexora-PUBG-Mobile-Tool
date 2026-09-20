using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
using Nexora.Features.About.Presentation;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Graphics.Application;
using Nexora.Features.Graphics.Presentation;
using Nexora.Features.Network.Application;
using Nexora.Features.Network.Domain;
using Nexora.Features.Network.Presentation;
using Nexora.Features.Optimizer.Application;
using Nexora.Features.Optimizer.Presentation;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Tuning.Application;
using Nexora.Features.Tuning.Presentation;
using Nexora.Features.Shortcuts.Application;
using Nexora.Features.Shortcuts.Presentation;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.UI.Presentation;

namespace Nexora.Bootstrap;

/// <summary>
/// Composition root for the application's dependency injection container.
/// </summary>
/// <remarks>
/// The single authoritative place for service registrations. <see cref="App"/>
/// delegates here so that no other component ever constructs services manually.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every application service, option, and view with the container.
    /// </summary>
    /// <param name="services">The collection to configure.</param>
    /// <returns>The configured collection, for chaining.</returns>
    public static IServiceCollection AddNexoraServices(this IServiceCollection services)
    {
        // Options (strongly typed configuration records).
        services.AddSingleton(new TempCleanupOptions());
        services.AddSingleton(sp => new IpadLayoutOptions(sp.GetRequiredService<EmulatorOptions>()));
        services.AddSingleton(new GameLoopOptions());
        services.AddSingleton(new EmulatorOptions());
        services.AddSingleton(new UpdateOptions());

        // Shared kernel and system infrastructure.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<RegistryService>();
        services.AddSingleton<IUserRegistry>(sp => sp.GetRequiredService<RegistryService>());
        services.AddSingleton<IMachineRegistry>(sp => sp.GetRequiredService<RegistryService>());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IWorkRootProvider, GameLoopWorkRootProvider>();
        services.AddSingleton<GameLoopWorkingStorage>();
        services.AddSingleton<IGameLoopProcessService, GameLoopProcessService>();
        services.AddSingleton<IGameLoopPathResolver, GameLoopPathResolver>();

        // Process priority subsystem.
        services.AddSingleton<IProcessPrioritySnapshotStore, ProcessPrioritySnapshotStore>();
        services.AddSingleton<IProcessPriorityApplier, ProcessPriorityApplier>();
        services.AddSingleton<IProcessPriorityMonitor, ProcessPriorityMonitor>();
        services.AddSingleton<ProcessPriorityService>();

        // GameLoop connection and graphics profile: both facets forward to the
        // SAME GameLoopService singleton so the shared gate/session is never forked.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<GameLoopService>();
        services.AddSingleton<IGameLoopConnection>(sp => sp.GetRequiredService<GameLoopService>());
        services.AddSingleton<IGraphicsProfileStore>(sp => sp.GetRequiredService<GameLoopService>());

        // Feature application services.
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IEmulatorSettingsService, EmulatorSettingsService>();
        services.AddSingleton<ITempCleanupService, TempCleanupService>();
        services.AddSingleton<IIpadLayoutService, IpadLayoutService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();
        services.AddSingleton<IGraphicsSettingsService, GraphicsSettingsService>();
        services.AddSingleton<IShortcutService>(sp => new ShortcutService(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IGameLoopPathResolver>(),
            Path.Combine(AppContext.BaseDirectory, sp.GetRequiredService<EmulatorOptions>().Assets.DirectoryName),
            sp.GetRequiredService<EmulatorOptions>()));

        // Window-wide "one long operation at a time" guard: shared by the shell
        // and every page ViewModel so a click on one page cannot stack work on
        // an operation another page already started.
        services.AddSingleton<IPageOperationBus, PageOperationBus>();

        services.AddSingleton<IGameLoopPerformanceEngine, PerformanceEngineFacade>();

        // Graphics page: the ViewModel is resolved by the page (see
        // GraphicsView's designer-tolerant constructor), and both registrations
        // forward to the same connection/profile singletons above.
        services.AddTransient<GraphicsViewModel>();
        services.AddTransient<GraphicsView>();

        // Tuning page: the ViewModel is resolved by the page (see
        // TuningView's designer-tolerant constructor), both Transient.
        services.AddTransient<TuningViewModel>();
        services.AddTransient<TuningView>();

        // Network page: same designer-tolerant pattern, both Transient.
        services.AddTransient<NetworkViewModel>();
        services.AddTransient<NetworkView>();

        // Optimizer page: Presentation-only per D1 (the engine lives in
        // Features/Performance); same designer-tolerant pattern, both Transient.
        services.AddTransient<OptimizerViewModel>();
        services.AddTransient<OptimizerView>();

        // Shortcuts page: same designer-tolerant pattern, both Transient. The
        // preview text lives in the Features/Shortcuts Domain model; creation
        // and icons stay behind IShortcutService.
        services.AddTransient<ShortcutsViewModel>();
        services.AddTransient<ShortcutsView>();

        // About page: static copy plus the version pill from its ViewModel;
        // same designer-tolerant pattern, both Transient.
        services.AddTransient<AboutViewModel>();
        services.AddTransient<AboutView>();

        // Views: transient by default, resolved through the provider.
        services.AddTransient<MainWindow>();

        return services;
    }
}
