using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies <see cref="ShadowSettingsStore"/> owns the UserCustom.ini file side
/// effects while the CVar transformation stays in the pure codec.
/// </summary>
public sealed class ShadowSettingsStoreTests
{
    [Fact]
    public void UpdateFile_Fails_WhenFileDoesNotExist()
    {
        var store = new ShadowSettingsStore(new PhysicalFileSystem());
        var missing = Path.Combine(Path.GetTempPath(), "NexoraShadowMissing-" + Guid.NewGuid().ToString("N") + ".ini");

        var result = store.UpdateFile(missing, enable: true);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Could not read the PUBG shadow settings.");
    }

    [Fact]
    public void UpdateFile_Fails_WhenNoShadowCVarIsPresent()
    {
        var store = new ShadowSettingsStore(new PhysicalFileSystem());
        var path = WriteTempIni(["[UserCustom]", "Key=Value"]);
        try
        {
            var result = store.UpdateFile(path, enable: true);

            result.Success.Should().BeFalse();
            result.Message.Should().Be("The PUBG shadow setting was not found.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdateFile_EnablesAndDisablesShadow_InPlace()
    {
        var store = new ShadowSettingsStore(new PhysicalFileSystem());
        var path = WriteTempIni(
        [
            "[UserCustom]",
            "+CVars=" + UnrealCVarCodec.EncodeCVar("r.ShadowQuality", "0")
        ]);
        try
        {
            var enabled = store.UpdateFile(path, enable: true);

            enabled.Success.Should().BeTrue();
            enabled.Message.Should().Be("Shadow enabled.");
            File.ReadAllLines(path)[1].Should().EndWith("48"); // '1' ^ 0x79

            var disabled = store.UpdateFile(path, enable: false);

            disabled.Success.Should().BeTrue();
            disabled.Message.Should().Be("Shadow disabled.");
            File.ReadAllLines(path)[1].Should().EndWith("49"); // '0' ^ 0x79
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempIni(string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "NexoraShadowTest-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllLines(path, lines);
        return path;
    }
}
