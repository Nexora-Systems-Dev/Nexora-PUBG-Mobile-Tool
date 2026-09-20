using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Xunit;
using Nexora.Features.Graphics.Domain;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Pins the P2.3 encapsulation closure: session state changes only through
/// explicit methods (no public setters, so no unsynchronized mutation
/// outside the operation gate), and <see cref="Ue4SavEditor"/> no longer
/// exposes its live buffer.
/// </summary>
public sealed class GameLoopSessionEncapsulationTests
{
    [Theory]
    [InlineData(nameof(GameLoopSession.ActiveSavContent))]
    [InlineData(nameof(GameLoopSession.CurrentPackage))]
    [InlineData(nameof(GameLoopSession.IsAdbConnected))]
    public void SessionState_HasNoPublicSetter(string propertyName)
    {
        var setMethod = typeof(GameLoopSession).GetProperty(propertyName)?.SetMethod;

        setMethod.Should().NotBeNull($"{propertyName} must keep a setter for the explicit methods");
        setMethod!.IsPublic.Should().BeFalse($"{propertyName} must not be settable outside the session");
    }

    [Fact]
    public void Ue4SavEditor_DoesNotExposeLiveBuffer()
    {
        typeof(Ue4SavEditor).GetProperty("RawBuffer").Should().BeNull("callers must use ToBytes(), which clones");
    }

    [Fact]
    public void LoadVersion_PublishesBufferAndPackageTogether()
    {
        var session = new GameLoopSession();
        var sav = new byte[] { 0x01, 0x02 };

        session.LoadVersion(sav, "com.tencent.ig");

        session.ActiveSavContent.Should().BeSameAs(sav, "the session keeps the buffer reference for gate-held in-place editing");
        session.CurrentPackage.Should().Be("com.tencent.ig");
        session.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void MarkAdbConnected_SetsFlagWithoutTouchingSaveState()
    {
        var session = new GameLoopSession();

        session.MarkAdbConnected();

        session.IsAdbConnected.Should().BeTrue();
        session.ActiveSavContent.Should().BeNull();
        session.CurrentPackage.Should().BeNull();
        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var session = new GameLoopSession();
        session.LoadVersion(new byte[] { 0x01 }, "com.tencent.ig");
        session.MarkAdbConnected();

        session.Reset();

        session.ActiveSavContent.Should().BeNull();
        session.CurrentPackage.Should().BeNull();
        session.IsAdbConnected.Should().BeFalse();
        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void ConcurrentLoadVersionAndReset_NeverLeavesTornPair()
    {
        // Disconnect (Reset) runs outside the operation gate by design; the
        // session lock must keep every mutation atomic so no torn
        // (buffer, package) pair survives the storm.
        var session = new GameLoopSession();
        var sav = new byte[] { 0x01 };

        Parallel.For(0, 500, i =>
        {
            if (i % 2 == 0) session.LoadVersion(sav, "com.tencent.ig");
            else session.Reset();
        });

        ((session.ActiveSavContent is null) == (session.CurrentPackage is null))
            .Should().BeTrue("the final state must be a whole LoadVersion or a whole Reset");
    }
}
