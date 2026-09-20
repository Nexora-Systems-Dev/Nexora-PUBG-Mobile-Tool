using System.Reflection;
using System.Windows;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
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
using Xunit;
using Nexora.Features.Tuning.Application;
using Nexora.Features.Tuning.Presentation;
using Nexora.Features.Shortcuts.Application;
using Nexora.Features.Shortcuts.Presentation;
using Nexora.Features.About.Presentation;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;

namespace Nexora.Tests.Architecture;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ConfigureServices_RegistersAllCoreServiceContracts()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(IProcessRunner));
        services.Should().Contain(d => d.ServiceType == typeof(IUserRegistry));
        services.Should().Contain(d => d.ServiceType == typeof(IMachineRegistry));
        services.Should().Contain(d => d.ServiceType == typeof(IFileSystem));
        services.Should().Contain(d => d.ServiceType == typeof(IWorkRootProvider));
        services.Should().Contain(d => d.ServiceType == typeof(IGameLoopPathResolver));
        services.Should().Contain(d => d.ServiceType == typeof(IAdbClient));
        services.Should().Contain(d => d.ServiceType == typeof(IGameLoopConnection));
        services.Should().Contain(d => d.ServiceType == typeof(IGraphicsProfileStore));
        services.Should().Contain(d => d.ServiceType == typeof(IUpdateService));
        services.Should().Contain(d => d.ServiceType == typeof(IEmulatorSettingsService));
        services.Should().Contain(d => d.ServiceType == typeof(ITempCleanupService));
        services.Should().Contain(d => d.ServiceType == typeof(IIpadLayoutService));
        services.Should().Contain(d => d.ServiceType == typeof(INetworkToolsService));
        services.Should().Contain(d => d.ServiceType == typeof(IShortcutService));
        services.Should().Contain(d => d.ServiceType == typeof(IGameLoopPerformanceEngine));
        services.Should().Contain(d => d.ServiceType == typeof(IPageOperationBus));
        services.Should().Contain(d => d.ServiceType == typeof(IProcessPrioritySnapshotStore));
        services.Should().Contain(d => d.ServiceType == typeof(IProcessPriorityApplier));
        services.Should().Contain(d => d.ServiceType == typeof(IProcessPriorityMonitor));
        services.Should().Contain(d => d.ServiceType == typeof(ProcessPriorityService));
        services.Should().Contain(d => d.ServiceType == typeof(TempCleanupOptions));
        services.Should().Contain(d => d.ServiceType == typeof(IpadLayoutOptions));
        services.Should().Contain(d => d.ServiceType == typeof(GameLoopOptions));
        services.Should().Contain(d => d.ServiceType == typeof(EmulatorOptions));
        services.Should().Contain(d => d.ServiceType == typeof(UpdateOptions));
        services.Should().Contain(d => d.ServiceType == typeof(MainWindow));
    }

    [Fact]
    public void BuildServiceProvider_ResolvesAllInterfacesSuccessfully()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProcessRunner>().Should().BeOfType<ProcessRunner>();
        provider.GetRequiredService<IUserRegistry>().Should().BeOfType<RegistryService>();
        provider.GetRequiredService<IMachineRegistry>().Should().BeOfType<RegistryService>();
        provider.GetRequiredService<IFileSystem>().Should().BeOfType<PhysicalFileSystem>();
        provider.GetRequiredService<IWorkRootProvider>().Should().BeOfType<GameLoopWorkRootProvider>();
        provider.GetRequiredService<IGameLoopPathResolver>().Should().BeOfType<GameLoopPathResolver>();
        provider.GetRequiredService<IGameLoopProcessService>().Should().BeOfType<GameLoopProcessService>();
        provider.GetRequiredService<IAdbClient>().Should().BeOfType<AdbClient>();
        provider.GetRequiredService<IGameLoopConnection>().Should().BeOfType<GameLoopService>();
        provider.GetRequiredService<IGraphicsProfileStore>().Should().BeOfType<GameLoopService>();
        provider.GetRequiredService<IUpdateService>().Should().BeOfType<UpdateService>();
        provider.GetRequiredService<IEmulatorSettingsService>().Should().BeOfType<EmulatorSettingsService>();
        provider.GetRequiredService<ITempCleanupService>().Should().BeOfType<TempCleanupService>();
        provider.GetRequiredService<IIpadLayoutService>().Should().BeOfType<IpadLayoutService>();
        provider.GetRequiredService<INetworkToolsService>().Should().BeOfType<NetworkToolsService>();
        provider.GetRequiredService<IShortcutService>().Should().BeOfType<ShortcutService>();
        provider.GetRequiredService<IGameLoopPerformanceEngine>().Should().BeOfType<PerformanceEngineFacade>();
        provider.GetRequiredService<IPageOperationBus>().Should().BeOfType<PageOperationBus>();
        provider.GetRequiredService<IProcessPrioritySnapshotStore>().Should().BeOfType<ProcessPrioritySnapshotStore>();
        provider.GetRequiredService<IProcessPriorityApplier>().Should().BeOfType<ProcessPriorityApplier>();
        provider.GetRequiredService<IProcessPriorityMonitor>().Should().BeOfType<ProcessPriorityMonitor>();
        provider.GetRequiredService<ProcessPriorityService>().Should().NotBeNull();
        provider.GetRequiredService<TempCleanupOptions>().Should().NotBeNull();
        provider.GetRequiredService<IpadLayoutOptions>().Should().NotBeNull();
        provider.GetRequiredService<GameLoopOptions>().Should().NotBeNull();
        provider.GetRequiredService<EmulatorOptions>().Should().NotBeNull();
        provider.GetRequiredService<UpdateOptions>().Should().NotBeNull();
    }

    [Fact]
    public void UserAndMachineRegistry_ResolveToSameSingletonInstance()
    {
        // Both facets forward to one RegistryService: a naive dual
        // AddSingleton<Iface, Impl> would fork two instances. Dual-hive
        // consumers (GameLoopRegistryOptimizer) must see one registry.
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var user = provider.GetRequiredService<IUserRegistry>();
        var machine = provider.GetRequiredService<IMachineRegistry>();

        user.Should().BeSameAs(machine, "both registry facets must forward to the same singleton");
    }

    [Fact]
    public void ConnectionAndGraphicsStore_ResolveToSameSingletonInstance()
    {
        // Both facets forward to one GameLoopService: a naive dual
        // AddSingleton<Iface, Impl> would fork two instances, splitting the
        // shared gate/session. Connect/load on one facet and read/apply on
        // the other must observe one session.
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var connection = provider.GetRequiredService<IGameLoopConnection>();
        var graphics = provider.GetRequiredService<IGraphicsProfileStore>();

        graphics.Should().BeSameAs(connection, "both GameLoop facets must forward to the same singleton");
    }

    [Fact]
    public void ServiceCollection_AllowsInterfaceSubstitution()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        var fakeUpdate = new StubUpdateService();
        services.AddSingleton<IUpdateService>(fakeUpdate);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IUpdateService>();
        resolved.Should().BeSameAs(fakeUpdate);
    }

    [Fact]
    public void ProcessPriorityTrio_IsRegisteredAsSingleton()
    {
        // The applier, monitor, and service must observe the same snapshot
        // state, so every registration in the graph is a singleton.
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        foreach (var serviceType in new[]
        {
            typeof(IProcessPrioritySnapshotStore),
            typeof(IProcessPriorityApplier),
            typeof(IProcessPriorityMonitor),
            typeof(ProcessPriorityService)
        })
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);
            descriptor.Should().NotBeNull($"{serviceType.Name} must be registered");
            descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton, $"{serviceType.Name} must share snapshot state");
        }

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProcessPriorityService>()
            .Should().BeSameAs(provider.GetRequiredService<ProcessPriorityService>());
    }

    [Fact]
    public void MainWindow_IsRegisteredAsTransientWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MainWindow));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);

        // Verify all parameters of MainWindow's DI constructor can be resolved
        var ctor = typeof(MainWindow).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        // Phase 3: the shell takes only the contracts it truly needs — the
        // engine (shutdown session restore), the update handoff flow, and the
        // timeout options. Every page owns its own services.
        ctor.GetParameters().Select(p => p.ParameterType).Should().BeEquivalentTo(new[]
        {
            typeof(IGameLoopPerformanceEngine),
            typeof(IUpdateService),
            typeof(GameLoopOptions),
        });

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }
    }

    /// <summary>
    /// The extracted Graphics page's three registrations must all be present,
    /// and every parameter the page's DI constructor asks for must resolve —
    /// the page is XAML-instantiated with the parameterless ctor, so this is
    /// what guarantees its resolved graph is wired. The controls themselves are
    /// deliberately not constructed here: a UserControl needs an STA thread and
    /// a running WPF Application host, which has no place in the CI suite (see
    /// <see cref="DeviceIdentityTests"/>).
    /// </summary>
    [Fact]
    public void GraphicsPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(GraphicsViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(GraphicsView)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(IGraphicsSettingsService)
            && d.Lifetime == ServiceLifetime.Singleton);

        var ctor = typeof(GraphicsView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        // The ViewModel takes both facets through DI; it must land on the same
        // forwarded GameLoopService singleton rather than a locally built one
        // (see ConnectionAndGraphicsStore_ResolveToSameSingletonInstance).
        var viewModel = provider.GetRequiredService<GraphicsViewModel>();
        GetField<IGameLoopConnection>(viewModel, "_connection")
            .Should().BeSameAs(provider.GetRequiredService<IGameLoopConnection>());
    }

    /// <summary>
    /// The extracted Tuning page's two registrations must both be present, and
    /// every parameter the page's DI constructor asks for must resolve — the
    /// page is XAML-instantiated with the parameterless ctor, so this is what
    /// guarantees its resolved graph is wired. The control itself is
    /// deliberately not constructed here: a UserControl needs an STA thread
    /// and a running WPF Application host, which has no place in the CI suite.
    /// </summary>
    [Fact]
    public void TuningPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(TuningViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(TuningView)
            && d.Lifetime == ServiceLifetime.Transient);

        var ctor = typeof(TuningView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        // The ViewModel must land on the shared emulator-settings singleton
        // rather than a locally built one.
        var viewModel = provider.GetRequiredService<TuningViewModel>();
        GetField<IEmulatorSettingsService>(viewModel, "_tuning")
            .Should().BeSameAs(provider.GetRequiredService<IEmulatorSettingsService>());
    }

    /// <summary>
    /// The extracted Network page's two registrations must both be present,
    /// and every parameter the page's DI constructor asks for must resolve —
    /// the page is XAML-instantiated with the parameterless ctor, so this is
    /// what guarantees its resolved graph is wired. The control itself is
    /// deliberately not constructed here: a UserControl needs an STA thread
    /// and a running WPF Application host, which has no place in the CI suite.
    /// </summary>
    [Fact]
    public void NetworkPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(NetworkViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(NetworkView)
            && d.Lifetime == ServiceLifetime.Transient);

        var ctor = typeof(NetworkView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        // The ViewModel must land on the shared network/iPad singletons rather
        // than locally built ones.
        var viewModel = provider.GetRequiredService<NetworkViewModel>();
        GetField<INetworkToolsService>(viewModel, "_networkTools")
            .Should().BeSameAs(provider.GetRequiredService<INetworkToolsService>());
        GetField<IIpadLayoutService>(viewModel, "_ipadLayout")
            .Should().BeSameAs(provider.GetRequiredService<IIpadLayoutService>());
    }

    /// <summary>
    /// The extracted Optimizer page's two registrations must both be present,
    /// and every parameter the page's DI constructor asks for must resolve —
    /// the page is XAML-instantiated with the parameterless ctor, so this is
    /// what guarantees its resolved graph is wired. The control itself is
    /// deliberately not constructed here: a UserControl needs an STA thread
    /// and a running WPF Application host, which has no place in the CI suite.
    /// Per D1 the page is Presentation-only: the engine behind it lives in
    /// Features/Performance and is asserted to be the shared singleton below.
    /// </summary>
    [Fact]
    public void OptimizerPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(OptimizerViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(OptimizerView)
            && d.Lifetime == ServiceLifetime.Transient);

        var ctor = typeof(OptimizerView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        // The ViewModel must land on the shared engine/temp/process singletons
        // rather than locally built ones.
        var viewModel = provider.GetRequiredService<OptimizerViewModel>();
        GetField<IGameLoopPerformanceEngine>(viewModel, "_engine")
            .Should().BeSameAs(provider.GetRequiredService<IGameLoopPerformanceEngine>());
        GetField<ITempCleanupService>(viewModel, "_tempCleanup")
            .Should().BeSameAs(provider.GetRequiredService<ITempCleanupService>());
        GetField<IGameLoopProcessService>(viewModel, "_processService")
            .Should().BeSameAs(provider.GetRequiredService<IGameLoopProcessService>());
    }

    /// <summary>
    /// The extracted Shortcuts page's two registrations must both be present,
    /// and every parameter the page's DI constructor asks for must resolve —
    /// the page is XAML-instantiated with the parameterless ctor, so this is
    /// what guarantees its resolved graph is wired. The control itself is
    /// deliberately not constructed here: a UserControl needs an STA thread
    /// and a running WPF Application host, which has no place in the CI suite.
    /// The preview text lives in the Features/Shortcuts Domain model and needs
    /// no service; creation and icons stay behind the shared shortcut
    /// singleton asserted below.
    /// </summary>
    [Fact]
    public void ShortcutsPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(ShortcutsViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(ShortcutsView)
            && d.Lifetime == ServiceLifetime.Transient);

        var ctor = typeof(ShortcutsView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        // The ViewModel must land on the shared shortcut singleton rather
        // than a locally built one.
        var viewModel = provider.GetRequiredService<ShortcutsViewModel>();
        GetField<IShortcutService>(viewModel, "_shortcuts")
            .Should().BeSameAs(provider.GetRequiredService<IShortcutService>());
    }

    /// <summary>
    /// The extracted About page's two registrations must both be present, and
    /// every parameter the page's DI constructor asks for must resolve — the
    /// page is XAML-instantiated with the parameterless ctor, so this is what
    /// guarantees its resolved graph is wired. The control itself is
    /// deliberately not constructed here: a UserControl needs an STA thread
    /// and a running WPF Application host, which has no place in the CI suite.
    /// The ViewModel is dependency-free (the version string is a code
    /// constant), so there are no shared singletons to pin — only that the
    /// resolved instance actually carries the pill text the view paints.
    /// </summary>
    [Fact]
    public void AboutPage_IsRegisteredWithResolvableDependencies()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.Should().Contain(d => d.ServiceType == typeof(AboutViewModel)
            && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(AboutView)
            && d.Lifetime == ServiceLifetime.Transient);

        var ctor = typeof(AboutView).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }

        provider.GetRequiredService<AboutViewModel>().VersionDisplay
            .Should().NotBeNullOrWhiteSpace();
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull($"expected {target.GetType().Name} to hold a '{name}' field");
        return (T)field!.GetValue(target)!;
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateInfo(false, "v1.0.0", "", "", ""));

        public Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Stub"));
    }
}