using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Application;
using Nexora.Features.Graphics.Domain;
using Nexora.Shared.Contracts;
using Xunit;

namespace Nexora.Tests.Features.Graphics;

/// <summary>
/// The read-back must surface exactly what the save holds — an unread or
/// unrecognized value never becomes a confident setting — and the Korean 1080p
/// path must be gated in one place only.
/// </summary>
public sealed class GraphicsSettingsServiceTests
{
    private static readonly GraphicsSelection Selection =
        new("Smooth", "Low", "Classic", EnableShadow: false, EnableKoreanFullHd: false);

    [Fact]
    public async Task LoadCurrentAsync_WithoutAdbTransport_ReturnsNull()
    {
        var service = new GraphicsSettingsService(new FakeProfileStore(), new FakeConnection(isAdbConnected: false));

        var current = await service.LoadCurrentAsync();

        current.Should().BeNull();
    }

    [Fact]
    public async Task LoadCurrentAsync_ReadsEveryProfileValue()
    {
        var store = new FakeProfileStore { Quality = "HDR", FrameRate = "Extreme", Style = "Movie", Shadow = "Enable" };
        var connection = new FakeConnection(isAdbConnected: true) { CurrentPackage = "com.tencent.ig" };
        var service = new GraphicsSettingsService(store, connection);

        var current = await service.LoadCurrentAsync();

        current.Should().NotBeNull();
        current!.Quality.Should().Be("HDR");
        current.FrameRate.Should().Be("Extreme");
        current.Style.Should().Be("Movie");
        current.ShadowEnabled.Should().BeTrue();
        current.IsKoreanVersion.Should().BeFalse();
    }

    [Fact]
    public async Task LoadCurrentAsync_UnrecognizedValues_StayNull()
    {
        var store = new FakeProfileStore { Quality = null, FrameRate = null, Style = null, Shadow = null };
        var service = new GraphicsSettingsService(store, new FakeConnection(isAdbConnected: true));

        var current = await service.LoadCurrentAsync();

        current.Should().NotBeNull();
        current!.Quality.Should().BeNull();
        current.FrameRate.Should().BeNull();
        current.Style.Should().BeNull();
        current.ShadowEnabled.Should().BeNull();
    }

    [Theory]
    [InlineData("com.pubg.krmobile", true)]
    [InlineData("COM.PUBG.KRMOBILE", true)]
    [InlineData("com.tencent.ig", false)]
    [InlineData(null, false)]
    public async Task LoadCurrentAsync_GatesKoreanFullHd_OnPackageOnly(string? currentPackage, bool expectedKorean)
    {
        var service = new GraphicsSettingsService(
            new FakeProfileStore(), new FakeConnection(isAdbConnected: true) { CurrentPackage = currentPackage });

        var current = await service.LoadCurrentAsync();

        current!.IsKoreanVersion.Should().Be(expectedKorean);
    }

    [Theory]
    [InlineData("Enable", true)]
    [InlineData("DISABLE", false)]
    [InlineData("garbage", null)]
    public async Task LoadCurrentAsync_ParsesShadowMarker_CaseInsensitively(string? shadow, bool? expected)
    {
        var store = new FakeProfileStore { Shadow = shadow };
        var service = new GraphicsSettingsService(store, new FakeConnection(isAdbConnected: true));

        var current = await service.LoadCurrentAsync();

        current!.ShadowEnabled.Should().Be(expected);
    }

    [Fact]
    public async Task ApplyAsync_Delegates_ToTheProfileStore()
    {
        var store = new FakeProfileStore();
        var service = new GraphicsSettingsService(store, new FakeConnection(isAdbConnected: true));

        var result = await service.ApplyAsync(Selection);

        store.LastApplied.Should().BeSameAs(Selection);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_PropagatesCancellation()
    {
        var service = new GraphicsSettingsService(
            new FakeProfileStore(), new FakeConnection(isAdbConnected: true));

        var act = async () => await service.ApplyAsync(Selection, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeProfileStore : IGraphicsProfileStore
    {
        public string? Quality { get; set; }
        public string? FrameRate { get; set; }
        public string? Style { get; set; }
        public string? Shadow { get; set; }
        public GraphicsSelection? LastApplied { get; private set; }

        public string? GetGraphicsQuality() => Quality;
        public string? GetFrameRate() => FrameRate;
        public string? GetGraphicsStyle() => Style;
        public Task<string?> GetShadowAsync(CancellationToken cancellationToken) => Task.FromResult(Shadow);

        public Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastApplied = selection;
            return Task.FromResult(OperationResult.Ok("Settings applied."));
        }
    }

    private sealed class FakeConnection : IGameLoopConnection
    {
        public FakeConnection(bool isAdbConnected)
        {
            IsAdbConnected = isAdbConnected;
        }

        public string? CurrentPackage { get; set; }
        public bool IsAdbConnected { get; }
        public bool IsConnected { get; set; }

        public void Disconnect() { }

        public Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectionResult(true, "Connected", Array.Empty<PubgVersion>()));

        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok("Loaded."));
    }
}
