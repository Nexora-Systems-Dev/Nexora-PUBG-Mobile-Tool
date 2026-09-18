using System.Globalization;
using System.Text;

namespace Nexora.Features.GameLoop;

/// <summary>
/// Pure codec for XOR-79 obfuscated Unreal Engine Console Variables (CVars):
/// encodes, decodes, and applies shadow presets to in-memory UserCustom.ini lines.
/// File side effects live in <see cref="ShadowSettingsStore"/>.
/// </summary>
public static class UnrealCVarCodec
{
    public const byte XorCipherKey = 0x79;
    public const string CVarPrefix = "+CVars=";

    private static readonly IReadOnlySet<string> ShadowCVarNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "r.UserShadowSwitch",
            "r.ShadowQuality",
            "r.Mobile.DynamicObjectShadow",
            "r.Shadow.MaxCSMResolution",
            "r.Shadow.DistanceScale",
            "r.Shadow.CSM.MaxMobileCascades"
        };

    /// <summary>
    /// Encodes a CVar name and value pair into an XOR-79 hexadecimal string.
    /// ASCII only: the XOR cipher operates on single bytes, so non-ASCII
    /// characters cannot round-trip and are rejected rather than silently truncated.
    /// </summary>
    public static string EncodeCVar(string name, string value)
    {
        var plainText = name + "=" + value;
        var encoded = new StringBuilder(plainText.Length * 2);
        foreach (var character in plainText)
        {
            if (character > 127)
            {
                throw new ArgumentException($"CVar text must be ASCII; U+{(int)character:X4} is not encodable by the XOR-79 byte cipher.");
            }

            encoded.Append(((byte)character ^ XorCipherKey).ToString("X2"));
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Decodes an XOR-79 hexadecimal string into its plain-text CVar representation.
    /// </summary>
    public static string DecodeCVar(string encoded)
    {
        if (encoded.Length % 2 != 0)
        {
            return string.Empty;
        }

        var decoded = new StringBuilder(encoded.Length / 2);
        for (var index = 0; index < encoded.Length; index += 2)
        {
            if (!byte.TryParse(encoded.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return string.Empty;
            }

            decoded.Append((char)(value ^ XorCipherKey));
        }

        return decoded.ToString();
    }

    /// <summary>
    /// Updates shadow CVars across an array of UserCustom.ini lines according to the specified preset.
    /// </summary>
    public static bool TryApplyShadowPreset(string[] lines, bool enable, out string[] updatedLines)
    {
        var targetValue = enable ? "1" : "0";
        var result = (string[])lines.Clone();
        var changed = false;

        for (var index = 0; index < result.Length; index++)
        {
            var trimmed = result[index].Trim();
            if (!trimmed.StartsWith(CVarPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var encoded = trimmed[CVarPrefix.Length..];
            var decoded = DecodeCVar(encoded);
            var separator = decoded.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var varName = decoded[..separator];
            if (!ShadowCVarNames.Contains(varName))
            {
                continue;
            }

            var indentationLength = result[index].Length - result[index].TrimStart().Length;
            result[index] = result[index][..indentationLength] + CVarPrefix + EncodeCVar(varName, targetValue);
            changed = true;
        }

        updatedLines = result;
        return changed;
    }
}
