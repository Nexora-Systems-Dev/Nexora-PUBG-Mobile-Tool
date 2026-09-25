using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Updates.Domain;
using Nexora.Features.Updates.Infrastructure;
using Nexora.Infrastructure.Files;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Pins the U-05c download half without a network or an emulator: a close
/// landing mid-download aborts the fetch through the caller's token, and a
/// feed that no longer offers the prompted version aborts the install. Stub
/// handlers only; the handoff prompt itself stays manual-only.
/// </summary>
public sealed class UpdateDownloadCancellationTests
{
    private const string DownloadUrl = "https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/releases/download/v1.0.14/update.zip";

    [Fact]
    public async Task DownloadAndLaunchAsync_CancelMidDownload_SettlesFailedWithoutLaunching()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraCancelDownloadTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            var enteredDownload = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // The gate never completes: the only way out is the caller's token.
            var downloadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new GatedDownloadHandler(enteredDownload, downloadGate.Task));
            var runner = new RecordingProcessRunner();
            var service = new UpdateService(runner, http, targetExe, stagingBaseDirectory: tempRoot, signatureCheck: _ => true);
            using var canceled = new CancellationTokenSource();

            var update = new UpdateInfo(true, "v1.0.14", "Nexora-v1.0.14-win-x64.zip", DownloadUrl, "Changelog");
            var run = service.DownloadAndLaunchAsync(update, canceled.Token);
            (await Task.WhenAny(enteredDownload.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(enteredDownload.Task, "the fetch must reach the wire before canceling");
            canceled.Cancel();

            var result = await run;
            result.Success.Should().BeFalse("a close mid-download must abort the fetch, never throw");
            runner.StartDetachedElevatedCalls.Should().Be(0);
            Directory.GetDirectories(tempRoot, "NexoraUpdate-*").Should().BeEmpty("an aborted fetch must not leak its staging tree");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_SupersededRelease_AbortsBeforeLaunch()
    {
        // U-05c freshness: the prompt named v9.9.9, but by install time the
        // feed offers v9.9.10 — the hash-verified staged payload must not
        // launch a version the feed no longer confirms. (Far-future tags keep
        // the test immune to real version bumps.)
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraStaleFeedTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            var zipBytes = BuildUpdateZip("v9.9.9", out var expectedSha256);
            using var http = new HttpClient(new FeedThenZipHandler(CannedFeedJson("v9.9.10"), zipBytes));
            var runner = new RecordingProcessRunner();
            var service = new UpdateService(runner, http, targetExe, stagingBaseDirectory: tempRoot, signatureCheck: _ => true);

            var update = new UpdateInfo(true, "v9.9.9", "Nexora-v9.9.9-win-x64.zip", DownloadUrl, $"SHA256: {expectedSha256}", expectedSha256);
            var result = await service.DownloadAndLaunchAsync(update);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("no longer available");
            runner.StartDetachedElevatedCalls.Should().Be(0, "a stale install must never reach the handoff launch");
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task DownloadAndLaunchAsync_FeedStillConfirms_LaunchesHandoff()
    {
        // The freshness re-check must not break the happy path: the feed
        // still offers the prompted version, so staging proceeds to launch.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"NexoraFreshFeedTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var targetExe = Path.Combine(tempRoot, "Nexora PUBG Mobile Tool.exe");
            File.WriteAllText(targetExe, "Original exe");

            var zipBytes = BuildUpdateZip("v9.9.9", out var expectedSha256);
            using var http = new HttpClient(new FeedThenZipHandler(CannedFeedJson("v9.9.9"), zipBytes));
            var runner = new RecordingProcessRunner();
            var service = new UpdateService(runner, http, targetExe, stagingBaseDirectory: tempRoot, signatureCheck: _ => true);

            var update = new UpdateInfo(true, "v9.9.9", "Nexora-v9.9.9-win-x64.zip", DownloadUrl, $"SHA256: {expectedSha256}", expectedSha256);
            var result = await service.DownloadAndLaunchAsync(update);

            result.Success.Should().BeTrue();
            runner.StartDetachedElevatedCalls.Should().Be(1);
        }
        finally
        {
            FileUtilities.TryDeleteDirectory(tempRoot);
        }
    }

    private static string CannedFeedJson(string tag) =>
        $"{{\"tag_name\": \"{tag}\", \"body\": \"\", \"assets\": []}}";

    private static byte[] BuildUpdateZip(string version, out string sha256)
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

    /// <summary>
    /// Blocks the archive fetch on a gate the test never completes, honoring
    /// the request token — the close path out of a stalled download.
    /// </summary>
    private sealed class GatedDownloadHandler(TaskCompletionSource<bool> entered, Task<bool> gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.TrySetResult(true);
            await gate.WaitAsync(cancellationToken);
            throw new InvalidOperationException("The test gate must never complete; cancellation is the only exit.");
        }
    }

    /// <summary>
    /// Serves feed JSON to the releases endpoint and zip bytes to every other
    /// (trusted) URL, so the install-time re-check and the download each see
    /// their own canned payload through the single injected client.
    /// </summary>
    private sealed class FeedThenZipHandler(string feedJson, byte[] zipBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true
                ? new StringContent(feedJson, Encoding.UTF8, "application/json")
                : new ByteArrayContent(zipBytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public int StartDetachedElevatedCalls { get; private set; }

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            new(0, string.Empty, string.Empty, false);

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken) =>
            Task.FromResult(Run(fileName, arguments, timeout));

        public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null) =>
            new(0, string.Empty, string.Empty, false);

        public bool StartDetachedElevated(string fileName, string? arguments = null)
        {
            StartDetachedElevatedCalls++;
            return true;
        }
    }
}
