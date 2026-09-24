using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Nexora.Configuration;
using Nexora.Features.Updates.Infrastructure;
using Xunit;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Pins the update-availability truth table end to end through
/// <see cref="UpdateChecker.CheckAsync"/>: "available" means the feed tag is
/// a parsed vX.Y.Z strictly newer than the running build, never merely
/// different. Canned feed JSON per case; no network, no filesystem.
/// </summary>
public sealed class UpdateAvailabilityTests
{
    [Fact]
    public async Task CheckAsync_MarksAvailable_WhenFeedIsNewer()
    {
        var info = await CheckTagAsync("v9.9.9");

        info.Available.Should().BeTrue("a strictly newer feed must prompt");
        info.LatestVersion.Should().Be("v9.9.9");
    }

    [Fact]
    public async Task CheckAsync_MarksUnavailable_WhenFeedEqualsRunningBuild()
    {
        var info = await CheckTagAsync(AppConstants.CurrentVersion);

        info.Available.Should().BeFalse("an equal feed is not an update");
    }

    [Fact]
    public async Task CheckAsync_MarksUnavailable_WhenFeedIsOlder()
    {
        // The downgrade case: a stale feed must never prompt a reinstall.
        var info = await CheckTagAsync("v1.2.0");

        info.Available.Should().BeFalse("an older feed must not prompt");
    }

    [Fact]
    public async Task CheckAsync_MarksUnavailable_WhenFeedDiffersOnlyByCase()
    {
        var info = await CheckTagAsync(AppConstants.CurrentVersion.ToUpperInvariant());

        info.Available.Should().BeFalse("a case-only difference parses equal, not newer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v1.3")]
    [InlineData("v1.3.0.0")]
    [InlineData("release-foo")]
    [InlineData("v1.x.0")]
    public async Task CheckAsync_MarksUnavailable_WhenFeedTagIsMalformed(string tag)
    {
        var info = await CheckTagAsync(tag);

        info.Available.Should().BeFalse("garbage must fail closed and never prompt");
    }

    private static Task<Nexora.Features.Updates.Domain.UpdateInfo> CheckTagAsync(string tag)
    {
        using var httpClient = new HttpClient(new CannedFeedHandler(tag));
        return new UpdateChecker(httpClient).CheckAsync();
    }

    private sealed class CannedFeedHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CannedFeedHandler(string tag) =>
            _json = $"{{\"tag_name\": \"{tag}\", \"body\": \"\", \"assets\": []}}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}
