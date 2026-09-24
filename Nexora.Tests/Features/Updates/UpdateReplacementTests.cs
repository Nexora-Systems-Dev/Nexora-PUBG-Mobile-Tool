using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Updates.Application;
using Nexora.Features.Updates.Domain;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.Files;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Unit and integration tests for the self-update replacement handoff,
/// validating script generation, rollback mechanisms, and directory cleanup.
/// </summary>
public sealed class UpdateReplacementTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateTargetPath_RejectsEmptyOrNullPath(string? path)
    {
        var valid = UpdateService.ValidateTargetPath(path!, out var error);

        valid.Should().BeFalse();
        error.Should().Contain("could not be determined");
    }

    [Fact]
    public void ValidateTargetPath_RejectsNonExistentFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"NexoraNonExistent-{Guid.NewGuid():N}.exe");

        var valid = UpdateService.ValidateTargetPath(missingPath, out var error);

        valid.Should().BeFalse();
        error.Should().Contain("not found");
    }

    [Fact]
    public void ValidateTargetPath_AcceptsExistingFileInValidDirectory()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraValidTarget-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(tempFile, "mock exe content");

            var valid = UpdateService.ValidateTargetPath(tempFile, out var error);

            valid.Should().BeTrue();
            error.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateTargetPath_RejectsExecutableInsideUpdateStagingDirectory()
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), new UpdateOptions().StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        var stagingExe = Path.Combine(stagingDir, "Nexora.exe");
        try
        {
            File.WriteAllText(stagingExe, "staging exe");

            var valid = UpdateService.ValidateTargetPath(stagingExe, out var error);

            valid.Should().BeFalse();
            error.Should().Contain("temporary update staging directory");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(stagingDir);
        }
    }

    [Fact]
    public void ValidateTargetPath_RejectsExecutableInsideCustomStagingBase()
    {
        // Guards the fixed mismatch: with a custom staging base, an executable
        // inside that base's staging prefix must be rejected even though it is
        // nowhere under %TEMP%.
        var customBase = Path.Combine(Path.GetTempPath(), $"NexoraCustomBase-{Guid.NewGuid():N}");
        var stagingDir = Path.Combine(customBase, new UpdateOptions().StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        var stagingExe = Path.Combine(stagingDir, "Nexora.exe");
        try
        {
            File.WriteAllText(stagingExe, "staging exe");

            var valid = UpdateService.ValidateTargetPath(stagingExe, out var error, stagingBaseDirectory: customBase);

            valid.Should().BeFalse();
            error.Should().Contain("temporary update staging directory");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(customBase);
        }
    }

    [Fact]
    public void ValidateTargetPath_AcceptsTempRootFile_WhenCustomStagingBaseConfigured()
    {
        // The other half of the mismatch: with a custom staging base, a file
        // directly under %TEMP% is outside the staging prefix and must pass.
        var customBase = Path.Combine(Path.GetTempPath(), $"NexoraCustomBase-{Guid.NewGuid():N}");
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraValidTarget-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(tempFile, "mock exe content");

            var valid = UpdateService.ValidateTargetPath(tempFile, out var error, stagingBaseDirectory: customBase);

            valid.Should().BeTrue();
            error.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetCurrentExecutablePath_UsesConfiguredOverride()
    {
        var customPath = @"C:\Program Files\Nexora\Nexora.exe";
        var service = new UpdateService(new ProcessRunner(), currentExecutablePath: customPath);

        service.GetCurrentExecutablePath().Should().Be(Path.GetFullPath(customPath));
    }

    [Fact]
    public void BuildHandoffScript_ContainsRequiredSafetyElements()
    {
        const int parentPid = 12345;
        const string source = @"C:\Temp\staging\extracted\Nexora-v1.0.14-win-x64.exe";
        const string target = @"C:\Program Files\Nexora\Nexora PUBG Mobile Tool.exe";
        const string extraction = @"C:\Temp\staging\extracted";
        const string staging = @"C:\Temp\staging";

        var script = UpdateHandoffBuilder.BuildHandoffScript(parentPid, source, target, extraction, staging);

        script.Should().Contain("$parentPid = 12345");
        script.Should().Contain("WaitForExit(30000)");
        script.Should().Contain("$backupExe = \"$targetExe.bak\"");
        script.Should().Contain("Copy-Item -LiteralPath $targetExe -Destination $backupExe");
        script.Should().Contain("Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force -ErrorAction Stop");
        script.Should().Contain("Copy-Item -LiteralPath $backupExe -Destination $targetExe -Force -ErrorAction SilentlyContinue");
        script.Should().Contain("Start-Process -FilePath $targetExe -WorkingDirectory $targetDir");
        script.Should().Contain("Remove-Item -LiteralPath $stagingRoot -Recurse -Force");
    }

    [Fact]
    public void BuildPowerShellArguments_ProducesValidEncodedCommand()
    {
        const string originalScript = "Write-Output 'Testing EncodedCommand'";

        var args = UpdateHandoffBuilder.BuildPowerShellArguments(originalScript);

        args.Should().StartWith("-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand ");
        var base64Part = args.Split("-EncodedCommand ")[1];
        var decodedBytes = Convert.FromBase64String(base64Part);
        var decodedText = Encoding.Unicode.GetString(decodedBytes);

        decodedText.Should().Be(originalScript);
    }

    [Fact]
    public void ExecuteHandoffScript_SuccessfullyReplacesOldExecutable_AndCleansStaging()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"NexoraHandoffSuccessTest-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(testRoot, "InstalledApp");
        var stagingRoot = Path.Combine(testRoot, "Staging");
        var extractionRoot = Path.Combine(stagingRoot, "extracted");

        try
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(extractionRoot);

            var targetExe = Path.Combine(targetDir, "Nexora PUBG Mobile Tool.exe");
            var sourceExe = Path.Combine(extractionRoot, "Nexora-v1.0.14-win-x64.exe");
            var readmeFile = Path.Combine(extractionRoot, "RELEASE-README.txt");

            File.WriteAllText(targetExe, "Old Version 1.0.13 Content");
            File.WriteAllText(sourceExe, "New Version 1.0.14 Content");
            File.WriteAllText(readmeFile, "Changelog and release notes");

            // parentPid: 0 bypasses parent wait loop for testing
            var script = UpdateHandoffBuilder.BuildHandoffScript(
                parentPid: 0,
                sourceExecutablePath: sourceExe,
                targetExecutablePath: targetExe,
                extractionRoot: extractionRoot,
                stagingRoot: stagingRoot);

            // Execute script directly via runner
            var runner = new ProcessRunner();
            var result = runner.RunPowerShell(script, TimeSpan.FromSeconds(30));

            result.Succeeded.Should().BeTrue();
            File.Exists(targetExe).Should().BeTrue();
            File.ReadAllText(targetExe).Should().Be("New Version 1.0.14 Content");
            File.Exists(Path.Combine(targetDir, "RELEASE-README.txt")).Should().BeTrue();
            File.Exists(targetExe + ".bak").Should().BeFalse();
            Directory.Exists(stagingRoot).Should().BeFalse();
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(testRoot);
        }
    }

    [Fact]
    public void ExecuteHandoffScript_WhenSourceMissing_LeavesOldVersionIntact_AndCleansStaging()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"NexoraHandoffMissingSourceTest-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(testRoot, "InstalledApp");
        var stagingRoot = Path.Combine(testRoot, "Staging");
        var extractionRoot = Path.Combine(stagingRoot, "extracted");

        try
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(extractionRoot);

            var targetExe = Path.Combine(targetDir, "Nexora PUBG Mobile Tool.exe");
            var missingSourceExe = Path.Combine(extractionRoot, "NonExistentSource.exe");

            File.WriteAllText(targetExe, "Old Version 1.0.13 Content");

            var script = UpdateHandoffBuilder.BuildHandoffScript(
                parentPid: 0,
                sourceExecutablePath: missingSourceExe,
                targetExecutablePath: targetExe,
                extractionRoot: extractionRoot,
                stagingRoot: stagingRoot);

            var runner = new ProcessRunner();
            var result = runner.RunPowerShell(script, TimeSpan.FromSeconds(30));

            // Script exits with code 2 on missing source
            result.ExitCode.Should().Be(2);
            File.Exists(targetExe).Should().BeTrue();
            File.ReadAllText(targetExe).Should().Be("Old Version 1.0.13 Content");
            Directory.Exists(stagingRoot).Should().BeFalse();
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(testRoot);
        }
    }

    [Fact]
    public void ExecuteHandoffScript_WhenCopyFails_RestoresBackup_LeavingOldVersionIntact()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"NexoraHandoffRollbackTest-{Guid.NewGuid():N}");
        var targetDir = Path.Combine(testRoot, "InstalledApp");
        var stagingRoot = Path.Combine(testRoot, "Staging");
        var extractionRoot = Path.Combine(stagingRoot, "extracted");

        try
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(extractionRoot);

            var targetExe = Path.Combine(targetDir, "Nexora PUBG Mobile Tool.exe");
            var sourceExe = Path.Combine(extractionRoot, "Nexora-v1.0.14-win-x64.exe");

            File.WriteAllText(targetExe, "Original Working 1.0.13 Executable");
            File.WriteAllText(sourceExe, "New Extracted Executable");

            // Build a script with a simulated copy failure that triggers the rollback branch
            var baseScript = UpdateHandoffBuilder.BuildHandoffScript(
                parentPid: 0,
                sourceExecutablePath: sourceExe,
                targetExecutablePath: targetExe,
                extractionRoot: extractionRoot,
                stagingRoot: stagingRoot);

            // Replace the copy line with simulated failure to test rollback branch
            var failingScript = baseScript.Replace(
                "Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force -ErrorAction Stop",
                "Set-Content -Path $targetExe -Value 'Partial Broken Write'; throw 'Simulated disk write error'");

            var runner = new ProcessRunner();
            var result = runner.RunPowerShell(failingScript, TimeSpan.FromSeconds(30));

            // Failure exit code is 4
            result.ExitCode.Should().Be(4);
            File.Exists(targetExe).Should().BeTrue();
            // Restored from backup!
            File.ReadAllText(targetExe).Should().Be("Original Working 1.0.13 Executable");
            File.Exists(targetExe + ".bak").Should().BeFalse();
            Directory.Exists(stagingRoot).Should().BeFalse();
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_RejectsUpdate_WhenTargetExecutableDoesNotExist()
    {
        var missingTarget = Path.Combine(Path.GetTempPath(), $"NexoraMissing-{Guid.NewGuid():N}.exe");
        var service = new UpdateService(new ProcessRunner(), currentExecutablePath: missingTarget);

        var update = new UpdateInfo(
            Available: true,
            LatestVersion: "v1.0.14",
            AssetName: "Nexora-v1.0.14-win-x64.zip",
            DownloadUrl: "https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip",
            ChangeLog: "Changelog");

        var result = await service.DownloadAndLaunchAsync(update);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_WhenRunnerFailsToSpawn_CleansUpStagingDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraSpawnFailTest-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            // Mock zip creation
            var zipBytes = CreateMockUpdateZip("v1.0.14", out var expectedSha256);
            var mockHttp = new MockHttpClient(zipBytes);

            // Runner that fails StartDetachedElevated
            var failingRunner = new MockProcessRunner(startDetachedResult: false);
            var service = new UpdateService(failingRunner, mockHttp, targetExe, stagingBaseDirectory: tempRoot, signatureCheck: _ => true);

            var update = new UpdateInfo(
                Available: true,
                LatestVersion: "v1.0.14",
                AssetName: "Nexora-v1.0.14-win-x64.zip",
                DownloadUrl: "https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip",
                ChangeLog: $"SHA256: {expectedSha256}",
                ExpectedSha256: expectedSha256);

            var result = await service.DownloadAndLaunchAsync(update);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("could not be started");
            failingRunner.StartDetachedElevatedCalled.Should().BeTrue();

            // Staging directory must be cleaned up because handoff process did not start
            failingRunner.LastArguments.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_WhenSuccessful_StagesHandoffAndReturnsOk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraSuccessHandoffTest-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            var zipBytes = CreateMockUpdateZip("v1.0.14", out var expectedSha256);
            var mockHttp = new MockHttpClient(zipBytes);
            var successRunner = new MockProcessRunner(startDetachedResult: true);
            // Scoped staging base: the intentionally-retained handoff staging tree lives
            // under tempRoot and is removed by the finally below instead of leaking into %TEMP%.
            // Signature stubbed true: this test covers handoff staging, while
            // UpdateIntegrityTests pins the production dual hash+signature gate.
            var service = new UpdateService(successRunner, mockHttp, targetExe, stagingBaseDirectory: tempRoot, signatureCheck: _ => true);

            var update = new UpdateInfo(
                Available: true,
                LatestVersion: "v1.0.14",
                AssetName: "Nexora-v1.0.14-win-x64.zip",
                DownloadUrl: "https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip",
                ChangeLog: $"SHA256: {expectedSha256}",
                ExpectedSha256: expectedSha256);

            var result = await service.DownloadAndLaunchAsync(update);

            result.Success.Should().BeTrue();
            result.Message.Should().Contain("restart");
            successRunner.StartDetachedElevatedCalled.Should().BeTrue();
            successRunner.LastFileName.Should().Be("powershell.exe");
            successRunner.LastArguments.Should().Contain("-EncodedCommand");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_FailsSafely_WhenNetworkIsUnreachable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraOfflineTest-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            var offlineHttp = new System.Net.Http.HttpClient(new FailingHttpMessageHandler());
            var service = new UpdateService(
                new MockProcessRunner(startDetachedResult: true),
                offlineHttp,
                targetExe,
                stagingBaseDirectory: tempRoot,
                signatureCheck: _ => true);

            var update = new UpdateInfo(
                Available: true,
                LatestVersion: "v1.0.14",
                AssetName: "Nexora-v1.0.14-win-x64.zip",
                DownloadUrl: "https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip",
                ChangeLog: "Changelog");

            var result = await service.DownloadAndLaunchAsync(update);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Update failed");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    private static byte[] CreateMockUpdateZip(string version, out string sha256)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var exeEntry = archive.CreateEntry($"Nexora-{version}-{new UpdateOptions().Runtime}.exe");
            using var entryStream = exeEntry.Open();
            var payload = Encoding.UTF8.GetBytes("MZ simulated verified executable binary content");
            entryStream.Write(payload);

            using var sha = System.Security.Cryptography.SHA256.Create();
            sha256 = Convert.ToHexString(sha.ComputeHash(payload)).ToLowerInvariant();
        }

        return memoryStream.ToArray();
    }

    private sealed class MockHttpClient : System.Net.Http.HttpClient
    {
        public MockHttpClient(byte[] zipResponse)
            : base(new MockHttpMessageHandler(zipResponse))
        {
        }
    }

    private sealed class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly byte[] _payload;

        public MockHttpMessageHandler(byte[] payload) => _payload = payload;

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(_payload)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new System.Net.Http.HttpRequestException("Network unreachable (test).");
    }

    private sealed class MockProcessRunner : IProcessRunner
    {
        private readonly bool _startDetachedResult;

        public bool StartDetachedElevatedCalled { get; private set; }
        public string? LastFileName { get; private set; }
        public string? LastArguments { get; private set; }

        public MockProcessRunner(bool startDetachedResult) => _startDetachedResult = startDetachedResult;

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            new(0, string.Empty, string.Empty, false);

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            Task.FromResult(Run(fileName, arguments, timeout));

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null) =>
            new(0, string.Empty, string.Empty, false);

        public bool StartDetachedElevated(string fileName, string? arguments = null)
        {
            StartDetachedElevatedCalled = true;
            LastFileName = fileName;
            LastArguments = arguments;
            return _startDetachedResult;
        }
    }
}
