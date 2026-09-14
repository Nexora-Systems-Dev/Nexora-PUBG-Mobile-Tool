using System.Globalization;
using System.Text;
using Nexora.Shared.Kernel;

namespace Nexora.Features.GameLoop;

/// <summary>
/// Encodes, decodes, and updates XOR-79 obfuscated Unreal Engine Console Variables (CVars) in UserCustom.ini files.
/// </summary>
public static class UnrealCVarCodec
{
    public const byte XorCipherKey = 0x79;
    public const string CVarPrefix = "+CVars=";

    private static readonly IReadOnlyDictionary<string, string> EnabledShadowCVars =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["r.UserShadowSwitch"] = "1",
            ["r.ShadowQuality"] = "1",
            ["r.Mobile.DynamicObjectShadow"] = "1",
            ["r.Shadow.MaxCSMResolution"] = "1",
            ["r.Shadow.DistanceScale"] = "1",
            ["r.Shadow.CSM.MaxMobileCascades"] = "1"
        };

    private static readonly IReadOnlyDictionary<string, string> DisabledShadowCVars =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["r.UserShadowSwitch"] = "0",
            ["r.ShadowQuality"] = "0",
            ["r.Mobile.DynamicObjectShadow"] = "0",
            ["r.Shadow.MaxCSMResolution"] = "0",
            ["r.Shadow.DistanceScale"] = "0",
            ["r.Shadow.CSM.MaxMobileCascades"] = "0"
        };

    /// <summary>
    /// Encodes a CVar name and value pair into an XOR-79 hexadecimal string.
    /// </summary>
    public static string EncodeCVar(string name, string value)
    {
        var plainText = name + "=" + value;
        var encoded = new StringBuilder(plainText.Length * 2);
        foreach (var character in plainText)
        {
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
        var values = enable ? EnabledShadowCVars : DisabledShadowCVars;
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
            if (separator < 0 || !values.TryGetValue(decoded[..separator], out var targetValue))
            {
                continue;
            }

            var indentationLength = result[index].Length - result[index].TrimStart().Length;
            var varName = decoded[..separator];
            result[index] = result[index][..indentationLength] + CVarPrefix + EncodeCVar(varName, targetValue);
            changed = true;
        }

        updatedLines = result;
        return changed;
    }

    /// <summary>
    /// Updates the shadow settings in a local UserCustom.ini file.
    /// </summary>
    public static OperationResult UpdateShadowFile(string filePath, bool enable)
    {
        if (!File.Exists(filePath))
        {
            return OperationResult.Fail("Could not read the PUBG shadow settings.");
        }

        var lines = File.ReadAllLines(filePath);
        if (!TryApplyShadowPreset(lines, enable, out var updatedLines))
        {
            return OperationResult.Fail("The PUBG shadow setting was not found.");
        }

        File.WriteAllLines(filePath, updatedLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return OperationResult.Ok(enable ? "Shadow enabled." : "Shadow disabled.");
    }
}
