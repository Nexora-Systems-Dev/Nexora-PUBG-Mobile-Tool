using FluentAssertions;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.UI;

/// <summary>
/// The bus is the only thing standing between a click on one page and an
/// operation another page already started, so its acquire/release contract is
/// load-bearing, not incidental.
/// </summary>
public sealed class PageOperationBusTests
{
    [Fact]
    public void TryAcquire_OnIdleBus_MarksTheBusBusy()
    {
        var bus = new PageOperationBus();

        bus.TryAcquire().Should().BeTrue();
        bus.IsBusy.Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_OnBusyBus_RefusesWithoutDisturbingTheOwner()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire();

        bus.TryAcquire().Should().BeFalse("the bus is not re-entrant");
        bus.IsBusy.Should().BeTrue("a refused acquire must never evict the running operation");
    }

    [Fact]
    public void Release_ReturnsTheBusToIdle()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire();

        bus.Release();

        bus.IsBusy.Should().BeFalse();
        bus.TryAcquire().Should().BeTrue("the bus is reusable after a release");
    }

    [Fact]
    public void Release_OnIdleBus_IsSafeToCallFromAFinally()
    {
        var bus = new PageOperationBus();

        var act = () => bus.Release();

        act.Should().NotThrow();
        bus.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void BusyChanged_FiresOncePerTransitionAndNeverForAnIdleRelease()
    {
        var bus = new PageOperationBus();
        var raises = 0;
        bus.BusyChanged += (_, _) => raises++;

        bus.TryAcquire();
        bus.TryAcquire();
        bus.Release();
        bus.Release();

        raises.Should().Be(2, "only the idle->busy and busy->idle transitions signal; a refused acquire and an idle release stay silent");
    }
}
