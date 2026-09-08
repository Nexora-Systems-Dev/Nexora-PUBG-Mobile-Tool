namespace Nexora.Features.SystemTools.Network;

public sealed record DnsEntry(string Label, string Primary, string Secondary)
{
    public string ShortName => Label.Split(" - ")[0];
}

public static class DnsCatalog
{
    public static IReadOnlyList<DnsEntry> Entries { get; } = new[]
    {
        new DnsEntry("Google DNS - 8.8.8.8", "8.8.8.8", "8.8.4.4"),
        new DnsEntry("Cloudflare DNS - 1.1.1.1", "1.1.1.1", "1.0.0.1"),
        new DnsEntry("Quad9 DNS - 9.9.9.9", "9.9.9.9", "149.112.112.112"),
        new DnsEntry("Cisco Umbrella - 208.67.222.222", "208.67.222.222", "208.67.220.220"),
        new DnsEntry("Yandex DNS - 77.88.8.1", "77.88.8.1", "77.88.8.8")
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
