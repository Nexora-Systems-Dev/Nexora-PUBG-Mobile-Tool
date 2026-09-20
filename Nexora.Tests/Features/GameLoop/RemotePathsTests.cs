using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Xunit;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Pins the single UE4 remote-path template: exact on-device strings,
/// derivation of both file paths from the shared root, per-package
/// isolation, and loud failure on a blank package.
/// </summary>
public sealed class RemotePathsTests
{
    [Fact]
    public void For_BuildsExactOnDevicePaths()
    {
        var paths = RemotePaths.For("com.tencent.ig");

        paths.PackageName.Should().Be("com.tencent.ig");
        paths.SavedRoot.Should().Be("/sdcard/Android/data/com.tencent.ig/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved");
        paths.ActiveSavPath.Should().Be("/sdcard/Android/data/com.tencent.ig/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/SaveGames/Active.sav");
        paths.UserCustomIniPath.Should().Be("/sdcard/Android/data/com.tencent.ig/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini");
    }

    [Fact]
    public void For_DerivesFilePathsFromSavedRoot()
    {
        var paths = RemotePaths.For("com.vng.pubgmobile");

        paths.ActiveSavPath.Should().StartWith(paths.SavedRoot + "/");
        paths.UserCustomIniPath.Should().StartWith(paths.SavedRoot + "/");
    }

    [Fact]
    public void For_IsolatesPackages()
    {
        var global = RemotePaths.For("com.tencent.ig");
        var korean = RemotePaths.For("com.pubg.krmobile");

        global.SavedRoot.Should().NotBe(korean.SavedRoot);
        global.SavedRoot.Should().Contain("com.tencent.ig");
        korean.SavedRoot.Should().Contain("com.pubg.krmobile");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_RejectsBlankPackage(string? packageName)
    {
        var act = () => RemotePaths.For(packageName);

        act.Should().Throw<ArgumentException>("a path built from a blank name would address the wrong directory");
    }
}
