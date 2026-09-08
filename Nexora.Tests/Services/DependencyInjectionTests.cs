using System.Windows;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Configuration;
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
        services.Should().Contain(d => d.ServiceType == typeof(IRegistryService));
        services.Should().Contain(d => d.ServiceType == typeof(IAdbClient));
        services.Should().Contain(d => d.ServiceType == typeof(IGameLoopService));
        services.Should().Contain(d => d.ServiceType == typeof(IUpdateService));
        services.Should().Contain(d => d.ServiceType == typeof(ITempCleanupService));
        services.Should().Contain(d => d.ServiceType == typeof(IIpadLayoutService));
        services.Should().Contain(d => d.ServiceType == typeof(INetworkToolsService));
        services.Should().Contain(d => d.ServiceType == typeof(IWindowsToolsService));
        services.Should().Contain(d => d.ServiceType == typeof(IGameLoopPerformanceEngine));
        services.Should().Contain(d => d.ServiceType == typeof(TempCleanupOptions));
        services.Should().Contain(d => d.ServiceType == typeof(IpadLayoutOptions));
        services.Should().Contain(d => d.ServiceType == typeof(MainWindow));
    }

    [Fact]
    public void BuildServiceProvider_ResolvesAllInterfacesSuccessfully()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProcessRunner>().Should().BeOfType<ProcessRunner>();
        provider.GetRequiredService<IRegistryService>().Should().BeOfType<RegistryService>();
        provider.GetRequiredService<IAdbClient>().Should().BeOfType<AdbClient>();
        provider.GetRequiredService<IGameLoopService>().Should().BeOfType<GameLoopService>();
        provider.GetRequiredService<IUpdateService>().Should().BeOfType<UpdateService>();
        provider.GetRequiredService<ITempCleanupService>().Should().BeOfType<TempCleanupService>();
        provider.GetRequiredService<IIpadLayoutService>().Should().BeOfType<IpadLayoutService>();
        provider.GetRequiredService<INetworkToolsService>().Should().BeOfType<NetworkToolsService>();
        provider.GetRequiredService<IWindowsToolsService>().Should().BeOfType<WindowsToolsService>();
        provider.GetRequiredService<IGameLoopPerformanceEngine>().Should().BeOfType<WindowsToolsService>();
        provider.GetRequiredService<TempCleanupOptions>().Should().NotBeNull();
        provider.GetRequiredService<IpadLayoutOptions>().Should().NotBeNull();
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
