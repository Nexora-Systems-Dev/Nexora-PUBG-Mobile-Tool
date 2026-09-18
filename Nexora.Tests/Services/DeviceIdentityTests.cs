using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Services;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Guards the single-device-identity invariant: in a DI-driven run,
/// <see cref="MainWindow"/> and <see cref="GameLoopService"/> must operate
/// on the same <see cref="IAdbClient"/> instance, not two clients holding
/// independent device serials. Window construction itself is deliberately
/// not covered here — MainWindow.xaml resolves theme brushes from
/// App.xaml, so instantiating the Window requires a running WPF
/// Application host, which has no place in the CI suite.
/// </summary>
public sealed class DeviceIdentityTests
{
    [Fact]
    public void DiContainer_GameLoopServiceUsesSharedAdbSingleton()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var adb = provider.GetRequiredService<IAdbClient>();
        provider.GetRequiredService<IAdbClient>().Should().BeSameAs(adb);

        var gameLoop = provider.GetRequiredService<IGameLoopConnection>()
            .Should().BeOfType<GameLoopService>().Subject;
        var connector = GetField<GameLoopConnector>(gameLoop, "_connector");
        GetField<IAdbClient>(connector, "_adb").Should().BeSameAs(adb);
    }

    [Fact]
    public void MainWindow_DiConstructorAcceptsInjectedAdbClient()
    {
        var ctor = typeof(MainWindow).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        ctor.GetParameters().Should().Contain(p => p.ParameterType == typeof(IAdbClient));

        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        provider.GetService(typeof(IAdbClient)).Should().NotBeNull();
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull($"expected {target.GetType().Name} to hold a '{name}' field pinning the shared ADB wiring");
        return (T)field!.GetValue(target)!;
    }
}
