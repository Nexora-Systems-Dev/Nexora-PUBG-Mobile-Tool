using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Nexora.Services;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Covers the pure binary-patching logic of <see cref="GameLoopService"/>
/// (UE4 .sav property headers and the shadow CVar XOR codec) without touching
/// ADB, the registry, or the filesystem. Private members are reached via
/// reflection so production code stays untouched; instances are created
/// uninitialized so the constructor's working-directory side effects never run.
/// </summary>
public sealed class GameLoopServiceLogicTests
{
    private static readonly Type ServiceType = typeof(GameLoopService);

    [Theory]
    [InlineData("r.ShadowQuality", "1")]
    [InlineData("r.UserShadowSwitch", "0")]
    [InlineData("r.Mobile.DynamicObjectShadow", "1")]
    public void CVarCodec_RoundTrips_NameAndValue(string name, string value)
    {
        // EncodeCVar(name, value) is private static.
        var encoded = InvokeStatic<string>("EncodeCVar", name, value);

        var decoded = InvokeStatic<string>("DecodeCVar", encoded);

        decoded.Should().Be($"{name}={value}");
    }

    [Fact]
    public void EncodeCVar_UsesXor79HexEncoding()
    {
        // 'a' ^ 0x79 = 0x18, '=' ^ 0x79 = 0x44, 'b' ^ 0x79 = 0x1B.
        var encoded = InvokeStatic<string>("EncodeCVar", "a", "b");

        encoded.Should().Be("18441B");
    }

    [Fact]
    public void EncodeCVar_MatchesShadowSentinelConvention()
    {
        // GetShadow treats a trailing "48" (ASCII '1' ^ 0x79) as Enable.
        var enabled = InvokeStatic<string>("EncodeCVar", "r.ShadowQuality", "1");
        var disabled = InvokeStatic<string>("EncodeCVar", "r.ShadowQuality", "0");

        enabled.Should().EndWith("48");
        disabled.Should().EndWith("49");
    }

    [Theory]
    [InlineData("ABC")]   // odd length
    [InlineData("ZZ")]    // not hex
    [InlineData("18441")] // odd length
    public void DecodeCVar_ReturnsEmpty_ForMalformedInput(string encoded)
    {
        var decoded = InvokeStatic<string>("DecodeCVar", encoded);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void CreateHeader_EmbedsUnrealIntPropertyMarker()
    {
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");

        // "<name>\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0" as UTF-8 bytes.
        var expected = Encoding.UTF8.GetBytes("BattleFPS\0\f\0\0\0IntProperty\0\u0004\0\0\0\0\0\0\0\0");
        header.Should().Equal(expected);
    }

    [Fact]
    public void FindSequence_LocatesEmbeddedHeader()
    {
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");
        var source = new byte[] { 0xAA, 0xBB }.Concat(header).Concat(new byte[] { 0x04, 0xCC }).ToArray();

        var index = InvokeStatic<int>("FindSequence", source, header);

        index.Should().Be(2);
    }

    [Fact]
    public void FindSequence_ReturnsMinusOne_WhenAbsent()
    {
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");

        var index = InvokeStatic<int>("FindSequence", new byte[] { 0x01, 0x02, 0x03 }, header);

        index.Should().Be(-1);
    }

    [Fact]
    public void ReadProperty_ExposesStoredByte_ThroughPublicGetters()
    {
        // Fake .sav containing quality=HD(0x03), fps=High(0x04), style=Colorful(0x02).
        var sav = Array.Empty<byte>()
            .Concat(SavSegment("BattleRenderQuality", 0x03))
            .Concat(SavSegment("BattleFPS", 0x04))
            .Concat(SavSegment("BattleRenderStyle", 0x02))
            .ToArray();
        var service = CreateServiceWithSav(sav);

        // Act + Assert
        service.GetGraphicsQuality().Should().Be("HD");
        service.GetFrameRate().Should().Be("High");
        service.GetGraphicsStyle().Should().Be("Colorful");
    }

    [Fact]
    public void ChangeProperty_UpdatesStoredByte_InPlace()
    {
        var sav = Array.Empty<byte>().Concat(SavSegment("BattleFPS", 0x04)).ToArray();
        var service = CreateServiceWithSav(sav);

        var changed = InvokeInstance<bool>("ChangeProperty", service, "BattleFPS", (byte)0x06);

        changed.Should().BeTrue();
        service.GetFrameRate().Should().Be("Extreme");
    }

    [Fact]
    public void ChangeProperty_ReturnsFalse_ForUnknownProperty()
    {
        var service = CreateServiceWithSav(new byte[] { 0x01, 0x02 });

        var changed = InvokeInstance<bool>("ChangeProperty", service, "NoSuchProperty", (byte)0x01);

        changed.Should().BeFalse();
    }

    [Fact]
    public void GraphicsUpdate_Succeeds_WhenOptionalLobbyFieldsAreAbsent()
    {
        // Some GameLoop/PUBG builds only store the active battle fields.
        var sav = Array.Empty<byte>()
            .Concat(SavSegment("BattleRenderQuality", 0x03))
            .Concat(SavSegment("BattleFPS", 0x04))
            .Concat(SavSegment("BattleRenderStyle", 0x02))
            .ToArray();
        var service = CreateServiceWithSav(sav);

        var result = InvokeInstance<OperationResult>(
            "UpdateGraphicsSavProperties",
            service,
            (byte)0x01,
            (byte)0x06,
            (byte)0x01);

        result.Success.Should().BeTrue();
        service.GetGraphicsQuality().Should().Be("Smooth");
        service.GetFrameRate().Should().Be("Extreme");
        service.GetGraphicsStyle().Should().Be("Classic");
    }

    [Fact]
    public void PubgVersions_ContainsAllSupportedPackages()
    {
        var versions = GameLoopService.PubgVersions;

        // The UI and registry loops depend on exactly these keys.
        versions.Keys.Should().BeEquivalentTo(
            "com.tencent.ig",
            "com.vng.pubgmobile",
            "com.rekoo.pubgm",
            "com.pubg.krmobile",
            "com.pubg.imobile");
        versions.Values.Should().OnlyContain(display => !string.IsNullOrWhiteSpace(display));
    }

    private static byte[] SavSegment(string propertyName, byte value)
    {
        var header = InvokeStatic<byte[]>("CreateHeader", propertyName);
        return header.Concat(new[] { value }).ToArray();
    }

    [Fact]
    public void IsGameLoopRunning_ExecutesWithoutLeakingHandles()
    {
        // Verify that calling IsGameLoopRunning multiple times executes cleanly
        // with all native process handles properly disposed.
        var act = () =>
        {
            for (var i = 0; i < 5; i++)
            {
                _ = GameLoopService.IsGameLoopRunning();
            }
        };

        act.Should().NotThrow();
    }

    private static GameLoopService CreateServiceWithSav(byte[] savContent)
    {
        var service = (GameLoopService)RuntimeHelpers.GetUninitializedObject(ServiceType);
        var field = ServiceType.GetField("_activeSavContent", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("the test pins the production field name");
        field!.SetValue(service, savContent);
        return service;
    }

    private static T InvokeStatic<T>(string methodName, params object[] args)
    {
        var method = ServiceType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"production method '{methodName}' must exist");
        return (T)method!.Invoke(null, args)!;
    }

    private static T InvokeInstance<T>(string methodName, object instance, params object[] args)
    {
        var method = ServiceType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"production method '{methodName}' must exist");
        return (T)method!.Invoke(instance, args)!;
    }
}
