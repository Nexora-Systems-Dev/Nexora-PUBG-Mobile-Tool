namespace Nexora.Features.Network.Domain;

public static class IpadPresetCatalog
{
    public static IReadOnlyList<IpadResolutionPreset> Presets { get; } = new[]
    {
        new IpadResolutionPreset("Competitive 4:3 (Recommended)", 1920, 1440, "Best balance of clarity and emulator performance"),
        new IpadResolutionPreset("Balanced 4:3", 1600, 1200, "Lower load for entry-level systems"),
        new IpadResolutionPreset("Classic iPad 4:3", 2048, 1536, "Native-style 4:3 iPad canvas"),
        new IpadResolutionPreset("iPad 10.2-inch", 2160, 1620, "Apple 4:3 display profile"),
        new IpadResolutionPreset("iPad Air 11-inch", 2360, 1640, "Wide iPad Air display profile"),
        new IpadResolutionPreset("iPad Pro 11-inch (classic)", 2388, 1668, "Previous-generation Pro display profile"),
        new IpadResolutionPreset("iPad Pro 11-inch (current)", 2420, 1668, "Current Pro display profile"),
        new IpadResolutionPreset("iPad mini 8.3-inch", 2266, 1488, "Compact iPad display profile"),
        new IpadResolutionPreset("iPad Pro 12.9-inch", 2732, 2048, "High-detail 4:3 Pro canvas"),
        new IpadResolutionPreset("iPad Pro 13-inch (current)", 2752, 2064, "Maximum detail; highest emulator load")
    };

    public static IpadResolutionPreset? FindByDisplayName(string? displayName) =>
        displayName is null
            ? null
            : Presets.FirstOrDefault(preset => string.Equals(preset.DisplayName, displayName, StringComparison.Ordinal));
}
