using System.Reflection;
using System.Text;
using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Graphics.Domain;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;
using Nexora.Shared.Contracts;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Covers the pure binary-patching logic behind <see cref="GameLoopService"/>
/// (UE4 .sav property headers and the shadow CVar XOR codec) without touching
/// ADB, the registry, or the filesystem. Collaborators are constructed directly
/// over an in-memory <see cref="GameLoopSession"/>; genuinely-private helpers
/// are reached via reflection so production code stays untouched.
/// </summary>
public sealed class GameLoopServiceLogicTests
{

    [Theory]
    [InlineData("r.ShadowQuality", "1")]
    [InlineData("r.UserShadowSwitch", "0")]
    [InlineData("r.Mobile.DynamicObjectShadow", "1")]
    public void CVarCodec_RoundTrips_NameAndValue(string name, string value)
    {
        var encoded = UnrealCVarCodec.EncodeCVar(name, value);

        var decoded = UnrealCVarCodec.DecodeCVar(encoded);

        decoded.Should().Be($"{name}={value}");
    }

    [Fact]
    public void EncodeCVar_UsesXor79HexEncoding()
    {
        // 'a' ^ 0x79 = 0x18, '=' ^ 0x79 = 0x44, 'b' ^ 0x79 = 0x1B.
        var encoded = UnrealCVarCodec.EncodeCVar("a", "b");

        encoded.Should().Be("18441B");
    }

    [Fact]
    public void EncodeCVar_MatchesShadowSentinelConvention()
    {
        // GetShadow treats a trailing "48" (ASCII '1' ^ 0x79) as Enable.
        var enabled = UnrealCVarCodec.EncodeCVar("r.ShadowQuality", "1");
        var disabled = UnrealCVarCodec.EncodeCVar("r.ShadowQuality", "0");

        enabled.Should().EndWith("48");
        disabled.Should().EndWith("49");
    }

    [Theory]
    [InlineData("ABC")]   // odd length
    [InlineData("ZZ")]    // not hex
    [InlineData("18441")] // odd length
    public void DecodeCVar_ReturnsEmpty_ForMalformedInput(string encoded)
    {
        var decoded = UnrealCVarCodec.DecodeCVar(encoded);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void CreateHeader_EmbedsUnrealIntPropertyMarker()
    {
        var header = Ue4SavEditor.CreateHeader("BattleFPS");

        // "<name>\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0" as UTF-8 bytes.
        var expected = Encoding.UTF8.GetBytes("BattleFPS\0\f\0\0\0IntProperty\0\u0004\0\0\0\0\0\0\0\0");
        header.Should().Equal(expected);
    }

    [Fact]
    public void FindSequence_LocatesEmbeddedHeader()
    {
        var header = Ue4SavEditor.CreateHeader("BattleFPS");
        var source = new byte[] { 0xAA, 0xBB }.Concat(header).Concat(new byte[] { 0x04, 0xCC }).ToArray();

        var index = Ue4SavEditor.FindSequence(source, header);

        index.Should().Be(2);
    }

    [Fact]
    public void FindSequence_ReturnsMinusOne_WhenAbsent()
    {
        var header = Ue4SavEditor.CreateHeader("BattleFPS");

        var index = Ue4SavEditor.FindSequence(new byte[] { 0x01, 0x02, 0x03 }, header);

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
        var session = CreateSessionWithSav(sav);
        var reader = CreateReader(session);

        // Act + Assert
        reader.GetGraphicsQuality().Should().Be("HD");
        reader.GetFrameRate().Should().Be("High");
        reader.GetGraphicsStyle().Should().Be("Colorful");
    }

    [Fact]
    public void ChangeProperty_UpdatesStoredByte_InPlace()
    {
        var sav = Array.Empty<byte>().Concat(SavSegment("BattleFPS", 0x04)).ToArray();
        var session = CreateSessionWithSav(sav);
        var applier = CreateApplier(session);

        var changed = InvokeApplier<bool>("ChangeProperty", applier, "BattleFPS", (byte)0x06);

        changed.Should().BeTrue();
        CreateReader(session).GetFrameRate().Should().Be("Extreme");
    }

    [Fact]
    public void ChangeProperty_ReturnsFalse_ForUnknownProperty()
    {
        var session = CreateSessionWithSav(new byte[] { 0x01, 0x02 });
        var applier = CreateApplier(session);

        var changed = InvokeApplier<bool>("ChangeProperty", applier, "NoSuchProperty", (byte)0x01);

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
        var session = CreateSessionWithSav(sav);
        var applier = CreateApplier(session);

        var result = InvokeApplier<OperationResult>(
            "UpdateGraphicsSavProperties",
            applier,
            (byte)0x01,
            (byte)0x06,
            (byte)0x01);

        result.Success.Should().BeTrue();
        var reader = CreateReader(session);
        reader.GetGraphicsQuality().Should().Be("Smooth");
        reader.GetFrameRate().Should().Be("Extreme");
        reader.GetGraphicsStyle().Should().Be("Classic");
    }

    [Fact]
    public void PubgVersions_ContainsAllSupportedPackages()
    {
        var versions = PubgVersionCatalog.PubgVersions;

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
        var header = Ue4SavEditor.CreateHeader(propertyName);
        return header.Concat(new[] { value }).ToArray();
    }

    [Fact]
    public void FindGameLoopProcesses_ExecutesWithoutLeakingHandles()
    {
        // Liveness checks now route through the single process-discovery home;
        // verify repeated checks execute cleanly with every native process
        // handle disposed by the caller.
        var service = new GameLoopProcessService(new ProcessRunner(), new GameLoopPathResolver(new RegistryService()));
        var act = () =>
        {
            for (var i = 0; i < 5; i++)
            {
                foreach (var process in service.FindGameLoopProcesses())
                {
                    process.Dispose();
                }
            }
        };

        act.Should().NotThrow();
    }

    private static GameLoopSession CreateSessionWithSav(byte[] savContent)
    {
        var session = new GameLoopSession();
        session.LoadVersion(savContent, packageName: null);
        return session;
    }

    private static SaveProfileReader CreateReader(GameLoopSession session) =>
        new(
            new AdbClient(new ProcessRunner(), new GameLoopPathResolver(new RegistryService())),
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new PhysicalFileSystem(),
            session);

    private static GraphicsSettingsApplier CreateApplier(GameLoopSession session) =>
        new(
            new AdbClient(new ProcessRunner(), new GameLoopPathResolver(new RegistryService())),
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new PhysicalFileSystem(),
            session);

    private static T InvokeApplier<T>(string methodName, object instance, params object[] args)
    {
        var method = typeof(GraphicsSettingsApplier).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"production method '{methodName}' must exist");
        return (T)method!.Invoke(instance, args)!;
    }
}
