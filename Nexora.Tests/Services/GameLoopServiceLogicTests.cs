using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Nexora.Services;
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
        // Arrange: EncodeCVar(name, value) is private static.
        var encoded = InvokeStatic<string>("EncodeCVar", name, value);

        // Act
        var decoded = InvokeStatic<string>("DecodeCVar", encoded);

        // Assert
        decoded.Should().Be($"{name}={value}");
    }

    [Fact]
    public void EncodeCVar_UsesXor79HexEncoding()
    {
        // Arrange: 'a' ^ 0x79 = 0x18, '=' ^ 0x79 = 0x44, 'b' ^ 0x79 = 0x1B.
        // Act
        var encoded = InvokeStatic<string>("EncodeCVar", "a", "b");

        // Assert
        encoded.Should().Be("18441B");
    }

    [Fact]
    public void EncodeCVar_MatchesShadowSentinelConvention()
    {
        // Arrange: GetShadow treats a trailing "48" (ASCII '1' ^ 0x79) as Enable.
        // Act
        var enabled = InvokeStatic<string>("EncodeCVar", "r.ShadowQuality", "1");
        var disabled = InvokeStatic<string>("EncodeCVar", "r.ShadowQuality", "0");

        // Assert
        enabled.Should().EndWith("48");
        disabled.Should().EndWith("49");
    }

    [Theory]
    [InlineData("ABC")]   // odd length
    [InlineData("ZZ")]    // not hex
    [InlineData("18441")] // odd length
    public void DecodeCVar_ReturnsEmpty_ForMalformedInput(string encoded)
    {
        // Act
        var decoded = InvokeStatic<string>("DecodeCVar", encoded);

        // Assert
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void CreateHeader_EmbedsUnrealIntPropertyMarker()
    {
        // Act
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");

        // Assert: "<name>\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0" as UTF-8 bytes.
        var expected = Encoding.UTF8.GetBytes("BattleFPS\0\f\0\0\0IntProperty\0\u0004\0\0\0\0\0\0\0\0");
        header.Should().Equal(expected);
    }

    [Fact]
    public void FindSequence_LocatesEmbeddedHeader()
    {
        // Arrange
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");
        var source = new byte[] { 0xAA, 0xBB }.Concat(header).Concat(new byte[] { 0x04, 0xCC }).ToArray();

        // Act
        var index = InvokeStatic<int>("FindSequence", source, header);

        // Assert
        index.Should().Be(2);
    }

    [Fact]
    public void FindSequence_ReturnsMinusOne_WhenAbsent()
    {
        // Arrange
        var header = InvokeStatic<byte[]>("CreateHeader", "BattleFPS");

        // Act
        var index = InvokeStatic<int>("FindSequence", new byte[] { 0x01, 0x02, 0x03 }, header);

        // Assert
        index.Should().Be(-1);
    }

    [Fact]
    public void ReadProperty_ExposesStoredByte_ThroughPublicGetters()
    {
        // Arrange: fake .sav containing quality=HD(0x03), fps=High(0x04), style=Colorful(0x02).
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
        // Arrange
        var sav = Array.Empty<byte>().Concat(SavSegment("BattleFPS", 0x04)).ToArray();
        var service = CreateServiceWithSav(sav);

        // Act
        var changed = InvokeInstance<bool>("ChangeProperty", service, "BattleFPS", (byte)0x06);

        // Assert
        changed.Should().BeTrue();
        service.GetFrameRate().Should().Be("Extreme");
    }

    [Fact]
    public void ChangeProperty_ReturnsFalse_ForUnknownProperty()
    {
        // Arrange
        var service = CreateServiceWithSav(new byte[] { 0x01, 0x02 });

        // Act
        var changed = InvokeInstance<bool>("ChangeProperty", service, "NoSuchProperty", (byte)0x01);

        // Assert
        changed.Should().BeFalse();
    }

    [Fact]
    public void PubgVersions_ContainsAllSupportedPackages()
    {
        // Arrange + Act
        var versions = GameLoopService.PubgVersions;

        // Assert: the UI and registry loops depend on exactly these keys.
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
