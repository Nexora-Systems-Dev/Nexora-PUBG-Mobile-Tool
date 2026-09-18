namespace Nexora.Features.SystemTools.Network;

public sealed record DnsEntry(string Label, string Primary, string Secondary, string ShortName);

public static class DnsCatalog
{
    public static IReadOnlyList<DnsEntry> Entries { get; } = new[]
    {
        new DnsEntry("Google DNS - 8.8.8.8", "8.8.8.8", "8.8.4.4", "Google DNS"),
        new DnsEntry("Cloudflare DNS - 1.1.1.1", "1.1.1.1", "1.0.0.1", "Cloudflare DNS"),
        new DnsEntry("Quad9 DNS - 9.9.9.9", "9.9.9.9", "149.112.112.112", "Quad9 DNS"),
        new DnsEntry("Cisco Umbrella - 208.67.222.222", "208.67.222.222", "208.67.220.220", "Cisco Umbrella"),
        new DnsEntry("Yandex DNS - 77.88.8.1", "77.88.8.1", "77.88.8.8", "Yandex DNS")
    };

    private static readonly IReadOnlyDictionary<string, DnsEntry> ByLabel =
        Entries.ToDictionary(entry => entry.Label, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Labels => Entries.Select(entry => entry.Label).ToList();

    public static bool TryGet(string? label, out DnsEntry? entry)
    {
        entry = null;
        return label is not null && ByLabel.TryGetValue(label, out entry);
    }
}
