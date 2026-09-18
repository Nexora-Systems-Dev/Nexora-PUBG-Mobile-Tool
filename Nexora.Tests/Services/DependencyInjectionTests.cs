using System.Windows;
using FluentAssertions;
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
using Xunit;

namespace Nexora.Tests.Services;

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

        using var provider = services.BuildServiceProvider();
        foreach (var param in ctor.GetParameters())
        {
            var resolved = provider.GetService(param.ParameterType);
            resolved.Should().NotBeNull($"Parameter '{param.Name}' ({param.ParameterType.Name}) should be resolvable from DI container");
        }
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateInfo(false, "v1.0.0", "", "", ""));

        public Task<OperationResult> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Stub"));
    }
}
