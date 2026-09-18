using System.Net;
using FluentAssertions;
using Nexora.Features.Layout;
using Nexora.Features.SystemTools.Network;
using Xunit;

namespace Nexora.Tests.Catalogs;

public sealed class CatalogTests
{
    [Fact]
    public void DnsCatalog_ContainsFiveReachableEntries_WithValidIpPairs()
    {
        var entries = DnsCatalog.Entries;

        entries.Should().HaveCount(5);
        entries.Should().AllSatisfy(entry =>
        {
            IPAddress.TryParse(entry.Primary, out _).Should().BeTrue($"primary '{entry.Primary}' must be a valid IP");
            IPAddress.TryParse(entry.Secondary, out _).Should().BeTrue($"secondary '{entry.Secondary}' must be a valid IP");
        });
        entries.Select(entry => entry.Primary).Should().OnlyHaveUniqueItems();
        DnsCatalog.Labels.Should().BeEquivalentTo(entries.Select(entry => entry.Label));
    }

    [Theory]
    [InlineData("google dns - 8.8.8.8", "8.8.8.8", "8.8.4.4", "Google DNS")]
    [InlineData("CLOUDFLARE DNS - 1.1.1.1", "1.1.1.1", "1.0.0.1", "Cloudflare DNS")]
    public void DnsCatalog_Lookup_IsCaseInsensitive(string label, string primary, string secondary, string shortName)
    {
        var found = DnsCatalog.TryGet(label, out var entry);

        found.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Primary.Should().Be(primary);
        entry.Secondary.Should().Be(secondary);
        entry.ShortName.Should().Be(shortName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown DNS - 0.0.0.0")]
    public void DnsCatalog_Lookup_FailsSafely_ForUnknownLabels(string? label)
    {
        var found = DnsCatalog.TryGet(label, out var entry);

        found.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void IpadPresetCatalog_ContainsTenPresets_WithPositiveDimensions()
    {
        var presets = IpadPresetCatalog.Presets;

        presets.Should().HaveCount(10);
        presets.Should().OnlyContain(preset => preset.Width > 0 && preset.Height > 0);
        presets.Select(preset => preset.DisplayName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IpadPresetCatalog_FindByDisplayName_RoundTrips()
    {
        var expected = IpadPresetCatalog.Presets[0];

        var found = IpadPresetCatalog.FindByDisplayName(expected.DisplayName);

        found.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("No Such Preset")]
    public void IpadPresetCatalog_FindByDisplayName_ReturnsNull_WhenMissing(string? displayName)
    {
        var found = IpadPresetCatalog.FindByDisplayName(displayName);

        found.Should().BeNull();
    }

    [Theory]
    [InlineData("Smooth", (byte)0x01)]
    [InlineData("Balanced", (byte)0x02)]
    [InlineData("Extreme HDR", (byte)0x06)]
    public void PubgCatalog_QualityLookup_MatchesCanonicalBytes(string name, byte expected)
    {
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetQualityValue(name, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("Extreme", (byte)0x06)]
    [InlineData("Ultra Extreme", (byte)0x08)]
    public void PubgCatalog_FrameRateLookup_MatchesCanonicalBytes(string name, byte expected)
    {
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetFrameRateValue(name, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("Movie", (byte)0x06)]
    [InlineData("Classic", (byte)0x01)]
    public void PubgCatalog_StyleLookup_MatchesCanonicalBytes(string name, byte expected)
    {
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetStyleValue(name, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void PubgCatalog_Lookups_FailSafely_ForUnknownNames()
    {
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetQualityValue("Nope", out _).Should().BeFalse();
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetFrameRateValue("Nope", out _).Should().BeFalse();
        Nexora.Features.GameLoop.PubgVersionCatalog.TryGetStyleValue("Nope", out _).Should().BeFalse();
    }
}
