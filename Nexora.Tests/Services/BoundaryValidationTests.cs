using System.Globalization;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies boundary validation for package names, network configurations, and coordinate formatting.
/// </summary>
public sealed class BoundaryValidationTests
{
    [Theory]
    [InlineData("com.tencent.ig")]
    [InlineData("com.pubg.krmobile")]
    [InlineData("com.vng.pubgmobile")]
    [InlineData("a.b")]
    [InlineData("com.example_app.game1")]
    public void IsValidAndroidPackageName_AcceptsWellFormedNames(string packageName)
    {
        var valid = AppConstants.Validation.IsValidAndroidPackageName(packageName);

        valid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]                      // no dot segment
    [InlineData("Com.tencent.ig")]         // uppercase segment start
    [InlineData("com.tencent.ig ")]        // trailing space
    [InlineData(" com.tencent.ig")]        // leading space
    [InlineData("com..ig")]                // empty segment
    [InlineData(".com.ig")]                // leading dot
    [InlineData("com.ig.")]                // trailing dot
    [InlineData("1com.ig")]                // segment starts with digit
    [InlineData("com.1abc")]               // segment starts with digit
    [InlineData("com.tencent-ig")]         // hyphen not allowed
    [InlineData("../evil")]                // path traversal
    [InlineData("com.evil; rm -rf /")]     // shell injection
    [InlineData("com.evil && id")]         // shell chaining
    [InlineData("com.evil|id")]            // shell piping
    [InlineData("com.evil$(id)")]          // command substitution
    public void IsValidAndroidPackageName_RejectsMalformedNames(string? packageName)
    {
        var valid = AppConstants.Validation.IsValidAndroidPackageName(packageName);

        valid.Should().BeFalse();
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("com.evil; rm -rf /")]
    [InlineData("")]
    [InlineData("Com.tencent.ig")]
    [InlineData("a")]
    public async Task LoadVersionAsync_FailsFast_ForInvalidPackageName(string packageName)
    {
        var service = new GameLoopService(new RegistryService(), new AdbClient(new ProcessRunner(), new RegistryService()));

        // Must return before PrepareWorkingFiles, PullAsync, or any ADB spawn.
        var result = await service.LoadVersionAsync(packageName, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid");
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("com.evil; rm -rf /")]
    [InlineData("")]
    public void FindInstalledPackages_Throws_ForInvalidPackageName(string packageName)
    {
        var adb = new AdbClient(new ProcessRunner(), new RegistryService());

        // Validation precedes every Run call, so no ADB process spawns.
        var act = () => adb.FindInstalledPackages(new[] { packageName }, CancellationToken.None);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FindInstalledPackages_ReturnsEmpty_ForEmptyInput()
    {
        var adb = new AdbClient(new ProcessRunner(), new RegistryService());

        var installed = adb.FindInstalledPackages(Array.Empty<string>(), CancellationToken.None);

        installed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-an-ip", "8.8.8.8")]
    [InlineData("8.8.8.8", "not-an-ip")]
    [InlineData("", "8.8.8.8")]
    [InlineData("999.1.1.1", "8.8.8.8")]
    [InlineData("8.8.8.8", "8.8.8.8.8")]
    public void ChangeDns_FailsFast_ForInvalidAddresses(string primary, string secondary)
    {
        var network = new NetworkToolsService(new ProcessRunner());

        // Must return before any PowerShell process spawns.
        var result = network.ChangeDns(primary, secondary);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid");
    }

    [Fact]
    public void ShiftCoordinate_UsesInvariantCulture_UnderCommaDecimalLocale()
    {
        // fr-FR uses ',' as the decimal separator; ensures values like "1.25" parse correctly.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            var shifted = IpadLayoutService.ShiftCoordinate("1.25", 0.25);

            shifted.Should().Be("1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ShiftCoordinate_ReturnsOriginal_ForUnparsableValue()
    {
        var shifted = IpadLayoutService.ShiftCoordinate("not-a-number", 0.1);

        // Best-effort nudge must never corrupt the attribute.
        shifted.Should().Be("not-a-number");
    }
}
