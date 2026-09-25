using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;
using Nexora.Features.Graphics.Domain;
using Nexora.Infrastructure.Processes;
using Nexora.Infrastructure.GameLoop;
using Nexora.Infrastructure.Files;

namespace Nexora.Tests.Features.Graphics;

/// <summary>
/// Pins the P1.4 contract: unknown save bytes and unreadable shadow state
/// surface as null (rendered as the neutral "—" placeholder) instead of
/// masquerading as confident Smooth/Low/Classic/Disable defaults.
/// </summary>
public sealed class SaveProfileReaderTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0xFF)]
    public void QualityName_ReturnsNull_ForUnknownByte(byte value)
    {
        PubgVersionCatalog.QualityName(value).Should().BeNull();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x09)]
    [InlineData(0xFF)]
    public void FrameRateName_ReturnsNull_ForUnknownByte(byte value)
    {
        PubgVersionCatalog.FrameRateName(value).Should().BeNull();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x05)]
    [InlineData(0xFF)]
    public void StyleName_ReturnsNull_ForUnknownByte(byte value)
    {
        PubgVersionCatalog.StyleName(value).Should().BeNull();
    }

    [Fact]
    public void Getters_ReturnNull_ForUnknownSaveBytes()
    {
        // 0x09 quality, 0x09 fps, and 0x05 style are all outside the catalog maps.
        var sav = SavSegment("BattleRenderQuality", 0x09)
            .Concat(SavSegment("BattleFPS", 0x09))
            .Concat(SavSegment("BattleRenderStyle", 0x05))
            .ToArray();
        var reader = CreateReader(SessionWithSav(sav));

        reader.GetGraphicsQuality().Should().BeNull();
        reader.GetFrameRate().Should().BeNull();
        reader.GetGraphicsStyle().Should().BeNull();
    }

    [Fact]
    public void Getters_ReturnNull_WhenNoSaveLoaded()
    {
        var reader = CreateReader(new GameLoopSession());

        reader.GetGraphicsQuality().Should().BeNull();
        reader.GetFrameRate().Should().BeNull();
        reader.GetGraphicsStyle().Should().BeNull();
    }

    [Fact]
    public async Task GetShadowAsync_ReturnsNull_WhenNoPackageSelected()
    {
        var reader = CreateReader(new GameLoopSession());

        (await reader.GetShadowAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetShadowAsync_ReturnsNull_WhenPullFails()
    {
        var reader = CreateReader(
            SessionWithPackage("com.tencent.ig"),
            pullSucceeds: false,
            fileExists: false,
            lines: []);

        (await reader.GetShadowAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetShadowAsync_ReturnsNull_WhenMarkerAbsent()
    {
        var reader = CreateReader(
            SessionWithPackage("com.tencent.ig"),
            pullSucceeds: true,
            fileExists: true,
            lines: ["[Shadow]", "+CVars=unrelated"]);

        (await reader.GetShadowAsync(CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("1", "Enable")]
    [InlineData("0", "Disable")]
    public async Task GetShadowAsync_ReadsMarker_WhenPresent(string shadowValue, string expected)
    {
        // Build the marker line through the real codec so the test follows the
        // same sentinel convention production code scans for.
        var marker = "+CVars=" + UnrealCVarCodec.EncodeCVar("r.ShadowQuality", shadowValue);
        var reader = CreateReader(
            SessionWithPackage("com.tencent.ig"),
            pullSucceeds: true,
            fileExists: true,
            lines: [marker]);

        (await reader.GetShadowAsync(CancellationToken.None)).Should().Be(expected);
    }

    [Theory]
    [InlineData("+CVars=0B572A11181D160E280C1815100D0044ZZ")] // non-hex tail
    [InlineData("+CVars=0B572A11181D160E280C1815100D0044F")] // odd-length tail
    [InlineData("+CVars=")] // empty payload
    public async Task GetShadowAsync_ReturnsNull_ForCorruptShadowLine(string corruptLine)
    {
        // Corrupt lines must surface as unreadable (null), never as confident Disable.
        var reader = CreateReader(
            SessionWithPackage("com.tencent.ig"),
            pullSucceeds: true,
            fileExists: true,
            lines: [corruptLine]);

        (await reader.GetShadowAsync(CancellationToken.None)).Should().BeNull();
    }

    private static byte[] SavSegment(string propertyName, byte value)
    {
        var header = Ue4SavEditor.CreateHeader(propertyName);
        return header.Concat(new[] { value }).ToArray();
    }

    private static GameLoopSession SessionWithSav(byte[] sav)
    {
        var session = new GameLoopSession();
        session.LoadVersion(sav, packageName: null);
        return session;
    }

    private static GameLoopSession SessionWithPackage(string packageName)
    {
        var session = new GameLoopSession();
        session.LoadVersion(savContent: null, packageName);
        return session;
    }

    private static SaveProfileReader CreateReader(
        GameLoopSession session,
        bool pullSucceeds = true,
        bool fileExists = false,
        string[]? lines = null) =>
        new(
            new FakeAdb(pullSucceeds),
            new GameLoopWorkingStorage(new PhysicalFileSystem(), new GameLoopWorkRootProvider()),
            new FakeFileSystem(fileExists, lines ?? []),
            session);

    private sealed class FakeAdb(bool pullSucceeds) : IAdbClient
    {
        public string DeviceSerial => "emulator-5554";

        public void RefreshAdbPath()
        {
        }

        public ProcessResult Run(params string[] arguments) => new(0, "", "", false);

        public string Shell(string command) => string.Empty;

        public string Shell(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Shell(command);
        }

        public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, Shell(command, cancellationToken), string.Empty, false));

        public Task<bool> PullAsync(string remotePath, string localPath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(pullSucceeds);

        public Task<bool> PushAsync(string localPath, string remotePath, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(false);

        public Task<bool> WaitForBootAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => Task.FromResult(true);

        public IReadOnlyList<string> FindInstalledPackages(IEnumerable<string> packageNames, CancellationToken cancellationToken) =>
            [];

        public Task<IReadOnlyList<string>> FindInstalledPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            Task.FromResult(FindInstalledPackages(packageNames, cancellationToken));

        public void StopAdb()
        {
        }

        public Task StopAdbAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFileSystem(bool exists, string[] lines) : IFileSystem
    {
        public bool Exists(string path) => exists;

        public byte[] ReadAllBytes(string path) => [];

        public void WriteAllBytes(string path, byte[] bytes)
        {
        }

        public string ReadAllText(string path) => string.Empty;

        public void WriteAllText(string path, string contents)
        {
        }

        public string[] ReadAllLines(string path) => lines;

        public void WriteAllLines(string path, IEnumerable<string> lines)
        {
        }

        public IEnumerable<string> ReadLines(string path) => lines;

        public void Copy(string sourceFileName, string destinationFileName, bool overwrite = false)
        {
        }

        public void Delete(string path)
        {
        }

        public void CreateDirectory(string path)
        {
        }
    }
}
