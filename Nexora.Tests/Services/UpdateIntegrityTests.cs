using System.IO.Compression;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Services;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies update integrity checks, ensuring download endpoints use trusted
/// HTTPS hosts and binaries are validated before execution.
/// </summary>
public sealed class UpdateIntegrityTests
{
    [Theory]
    [InlineData("https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/12345/update.zip")]
    [InlineData("https://github-releases.githubusercontent.com/12345/update.zip")]
    public void IsTrustedDownloadUrl_AcceptsTrustedGitHubHosts(string url)
    {
        UpdateService.IsTrustedDownloadUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://github.com/repo/releases/download/v1/update.zip")] // HTTP not HTTPS
    [InlineData("https://evil.com/update.zip")]
    [InlineData("https://attacker.githubusercontent.com.evil.com/update.zip")]
    [InlineData("https://mycdn.net/update.zip")]
    [InlineData("ftp://github.com/update.zip")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void IsTrustedDownloadUrl_RejectsUntrustedOrInsecureUrls(string url)
    {
        UpdateService.IsTrustedDownloadUrl(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData("testhost")]
    [InlineData("testhost.exe")]
    [InlineData("TestHost.dll")]
    [InlineData("C:/Program Files/dotnet/dotnet.exe")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTestHostPath_RejectsHostsAndBlankPaths(string? processPath)
    {
        UpdateService.IsTestHostPath(processPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("Nexora PUBG Mobile Tool.exe")]
    [InlineData("C:/Tools/Nexora/Nexora PUBG Mobile Tool.exe")]
    [InlineData("dotnet-launcher.exe")]
    public void IsTestHostPath_AcceptsApplicationExecutables(string processPath)
    {
        UpdateService.IsTestHostPath(processPath).Should().BeFalse();
    }

    [Fact]
    public void TryExtractSha256_ExtractsHexHashFromChangelog()
    {
        const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var changelog = $"## Release Notes\n\nSHA256: {expected}\nBug fixes and improvements.";

        var extracted = UpdateArchiveValidator.TryExtractSha256(changelog);

        extracted.Should().Be(expected);
    }

    [Fact]
    public void TryExtractSha256_SupportsAlternativeFormats()
    {
        const string expected = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        UpdateArchiveValidator.TryExtractSha256($"SHA-256: {expected}").Should().Be(expected);
        UpdateArchiveValidator.TryExtractSha256($"**SHA256:** {expected}").Should().Be(expected);
        UpdateArchiveValidator.TryExtractSha256($"**SHA-256:** `{expected}`").Should().Be(expected);
        UpdateArchiveValidator.TryExtractSha256($"checksum = {expected}").Should().Be(expected);
        UpdateArchiveValidator.TryExtractSha256($"{expected}  Nexora-v1.0.13-win-x64.exe").Should().Be(expected);
        UpdateArchiveValidator.TryExtractSha256("No hash in this body").Should().BeNull();
        UpdateArchiveValidator.TryExtractSha256(string.Empty).Should().BeNull();
    }

    [Fact]
    public void IsAuthenticodeSigned_ReturnsFalse_ForMissingOrUnsignedFile()
    {
        UpdateArchiveValidator.IsAuthenticodeSigned("Z:\\nexora-nonexistent.exe").Should().BeFalse();

        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraUnsignedTest-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            UpdateArchiveValidator.IsAuthenticodeSigned(tempFile).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ComputeSha256_ReturnsExpectedHashForEmptyFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraShaTest-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempFile, Array.Empty<byte>());
            var hash = UpdateArchiveValidator.ComputeSha256(tempFile);
            hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyExecutableIntegrity_AcceptsUnsignedExecutable_WhenPublishedHashMatches()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraIntegrityTest-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 }); // Mock MZ header
            var expectedHash = UpdateArchiveValidator.ComputeSha256(tempFile);

            var verified = UpdateArchiveValidator.VerifyExecutableIntegrity(tempFile, expectedHash, out var error);

            // Unsigned releases are the only kind the pipeline currently emits;
            // a matching published checksum is their sole evidence of integrity.
            verified.Should().BeTrue();
            error.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyExecutableIntegrity_TrustedSignature_OutscoresHashMismatch()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraIntegrityTest-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            var staleHash = "0000000000000000000000000000000000000000000000000000000000000000";

            // A signed payload must install even when the published checksum is stale.
            var verified = UpdateArchiveValidator.VerifyExecutableIntegrity(
                tempFile, staleHash, out _, signatureCheck: _ => true);

            verified.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyExecutableIntegrity_RejectsMismatchedHash()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraIntegrityTest-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            var mismatchedHash = "0000000000000000000000000000000000000000000000000000000000000000";

            var verified = UpdateArchiveValidator.VerifyExecutableIntegrity(tempFile, mismatchedHash, out var error);

            verified.Should().BeFalse();
            error.Should().Contain("does not match");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyExecutableIntegrity_RejectsUnsignedExecutableWhenNoHashProvided()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraIntegrityTest-{Guid.NewGuid():N}.exe");
        try
        {
            // Unsigned mock file with no expected hash
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

            var verified = UpdateArchiveValidator.VerifyExecutableIntegrity(tempFile, null, out var error);

            verified.Should().BeFalse();
            error.Should().Contain("unverifiable");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_RejectsUntrustedDownloadHost()
    {
        var update = new UpdateInfo(
            Available: true,
            LatestVersion: "v9.9.9",
            AssetName: "Nexora-v9.9.9-win-x64.zip",
            DownloadUrl: "https://untrusted-host.com/update.zip",
            ChangeLog: "Test changelog");

        var service = new UpdateService(new ProcessRunner());
        var result = await service.DownloadAndLaunchAsync(update);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("rejected");
    }
}
