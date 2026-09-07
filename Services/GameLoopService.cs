using System.Text;
using Nexora.Models;

namespace Nexora.Services;

public sealed class GameLoopService
{
    public static readonly IReadOnlyDictionary<string, string> PubgVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.tencent.ig"] = "PUBG Mobile Global",
            ["com.vng.pubgmobile"] = "PUBG Mobile VN",
            ["com.rekoo.pubgm"] = "PUBG Mobile TW",
            ["com.pubg.krmobile"] = "PUBG Mobile KR",
            ["com.pubg.imobile"] = "Battlegrounds Mobile India"
        };

    private static readonly IReadOnlyDictionary<string, byte> QualityValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Smooth"] = 0x01,
            ["Balanced"] = 0x02,
            ["HD"] = 0x03,
            ["HDR"] = 0x04,
            ["Ultra HDR"] = 0x05,
            ["Extreme HDR"] = 0x06
        };

    private static readonly IReadOnlyDictionary<string, byte> FrameRateValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = 0x02,
            ["Medium"] = 0x03,
            ["High"] = 0x04,
            ["Ultra"] = 0x05,
            ["Extreme"] = 0x06,
            ["Extreme+"] = 0x07,
            ["Ultra Extreme"] = 0x08
        };

    private static readonly IReadOnlyDictionary<string, byte> StyleValues =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Classic"] = 0x01,
            ["Colorful"] = 0x02,
            ["Realistic"] = 0x03,
            ["Soft"] = 0x04,
            ["Movie"] = 0x06
        };

    private readonly RegistryService _registry;
    private readonly AdbClient _adb;
    private readonly string _assetRoot;
    private readonly string _workRoot;
    private byte[]? _activeSavContent;

    public GameLoopService(RegistryService registry, AdbClient adb)
    {
        _registry = registry;
        _adb = adb;
        _assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        _workRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nexora PUBG Mobile Tool");
        Directory.CreateDirectory(_workRoot);
    }

    public string? CurrentPackage { get; private set; }
    public bool IsConnected => _activeSavContent is not null && !string.IsNullOrWhiteSpace(CurrentPackage);
    public string AdbPath => _adb.AdbPath;

    public void Disconnect()
    {
        _activeSavContent = null;
        CurrentPackage = null;
    }

    public ConnectionResult Connect(CancellationToken cancellationToken)
    {
        var adbStatus = _registry.GetUserDword("AdbDisable");
        if (adbStatus is null)
        {
            return new ConnectionResult(false, "Could not read GameLoop ADB status.", Array.Empty<PubgVersion>());
        }

        if (adbStatus == 1)
        {
            _registry.SetUserDword("AdbDisable", 0);
            return new ConnectionResult(false, "ADB was enabled. Restart GameLoop and try again.", Array.Empty<PubgVersion>());
        }

        if (!IsGameLoopRunning())
        {
            return new ConnectionResult(false, "GameLoop is not running.", Array.Empty<PubgVersion>());
        }

        if (!_adb.WaitForBoot(cancellationToken))
        {
            AdbClient.KillAdb();
            return new ConnectionResult(false, "GameLoop ADB did not finish booting.", Array.Empty<PubgVersion>());
        }

        var testFile = Path.Combine(_workRoot, "testADB.mkvip");
        if (!_adb.Pull("/default.prop", testFile))
        {
            AdbClient.KillAdb();
            return new ConnectionResult(false, "Could not connect to GameLoop ADB.", Array.Empty<PubgVersion>());
        }

        var installedPackages = _adb.FindInstalledPackages(PubgVersions.Keys);
        var installedVersions = installedPackages
            .Where(PubgVersions.ContainsKey)
            .Select(package => new PubgVersion(package, PubgVersions[package]))
            .ToList();

        if (installedVersions.Count == 0)
        {
            return new ConnectionResult(false, "No supported PUBG Mobile version was found.", installedVersions);
        }

        if (installedVersions.Count == 1)
        {
            var load = LoadVersion(installedVersions[0].PackageName);
            return load.Success
                ? new ConnectionResult(true, $"Using {installedVersions[0].DisplayName}.", installedVersions)
                : new ConnectionResult(false, load.Message, installedVersions);
        }

        return new ConnectionResult(true, "Select the PUBG Mobile version to use.", installedVersions);
    }

    public OperationResult LoadVersion(string packageName)
    {
        if (!PubgVersions.ContainsKey(packageName))
        {
            return OperationResult.Fail("Unsupported PUBG Mobile version.");
        }

        PrepareWorkingFiles();
        var activePath = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/SaveGames/Active.sav";
        var localActivePath = Path.Combine(_workRoot, "old.mkvip");

        if (!_adb.Pull(activePath, localActivePath))
        {
            return OperationResult.Fail("Could not read the PUBG graphics file from GameLoop.");
        }

        try
        {
            _activeSavContent = File.ReadAllBytes(localActivePath);
            CurrentPackage = packageName;

            var shadowPath = $"/sdcard/Android/data/{packageName}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
            _adb.Pull(shadowPath, Path.Combine(_workRoot, "user.mkvip"));
            return OperationResult.Ok($"Connected to {PubgVersions[packageName]}.");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Could not read the graphics file: {ex.Message}");
        }
    }

    public string GetGraphicsQuality() => ReadProperty("BattleRenderQuality") switch
    {
        0x01 => "Smooth",
        0x02 => "Balanced",
        0x03 => "HD",
        0x04 => "HDR",
        0x05 => "Ultra HDR",
        0x06 => "Extreme HDR",
        _ => "Smooth"
    };

    public string GetFrameRate() => ReadProperty("BattleFPS") switch
    {
        0x02 => "Low",
        0x03 => "Medium",
        0x04 => "High",
        0x05 => "Ultra",
        0x06 => "Extreme",
        0x07 => "Extreme+",
        0x08 => "Ultra Extreme",
        _ => "Low"
    };

    public string GetGraphicsStyle() => ReadProperty("BattleRenderStyle") switch
    {
        0x01 => "Classic",
        0x02 => "Colorful",
        0x03 => "Realistic",
        0x04 => "Soft",
        0x06 => "Movie",
        _ => "Classic"
    };

    public string GetShadow()
    {
        if (string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return "Disable";
        }

        var remotePath = $"/sdcard/Android/data/{CurrentPackage}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
        var localPath = Path.Combine(_workRoot, "user.mkvip");
        if (!File.Exists(localPath) && !_adb.Pull(remotePath, localPath))
        {
            return "Disable";
        }

        foreach (var line in File.ReadLines(localPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("+CVars=0B572A11181D160E280C1815100D0044", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed.EndsWith("48", StringComparison.Ordinal) ? "Enable" : "Disable";
        }

        return "Disable";
    }

    public OperationResult ApplyGraphics(GraphicsSelection selection)
    {
        if (!IsConnected || _activeSavContent is null || string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return OperationResult.Fail("Connect to GameLoop first.");
        }

        if (!QualityValues.TryGetValue(selection.Quality, out var qualityValue) ||
            !FrameRateValues.TryGetValue(selection.FrameRate, out var fpsValue) ||
            !StyleValues.TryGetValue(selection.Style, out var styleValue))
        {
            return OperationResult.Fail("One or more graphics settings are invalid.");
        }

        foreach (var property in new[] { "ArtQuality", "LobbyRenderQuality", "BattleRenderQuality" })
        {
            if (!ChangeProperty(property, qualityValue))
            {
                return OperationResult.Fail($"Could not update {property}.");
            }
        }

        foreach (var property in new[] { "FPSLevel", "BattleFPS", "LobbyFPS" })
        {
            if (!ChangeProperty(property, fpsValue))
            {
                return OperationResult.Fail($"Could not update {property}.");
            }
        }

        if (!ChangeProperty("BattleRenderStyle", styleValue))
        {
            return OperationResult.Fail("Could not update the graphics style.");
        }

        var shadowResult = UpdateShadow(selection.EnableShadow);
        if (!shadowResult.Success)
        {
            return shadowResult;
        }

        PrepareWorkingFiles();
        var localNewPath = Path.Combine(_workRoot, "new.mkvip");
        File.WriteAllBytes(localNewPath, _activeSavContent);

        var dataRoot = $"/sdcard/Android/data/{CurrentPackage}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved";
        _adb.Shell($"am force-stop {CurrentPackage}");
        Thread.Sleep(200);

        if (!_adb.Push(localNewPath, $"{dataRoot}/SaveGames/Active.sav"))
        {
            return OperationResult.Fail("Could not apply the graphics file to GameLoop.");
        }

        var userPath = Path.Combine(_workRoot, "user.mkvip");
        if (File.Exists(userPath) && !_adb.Push(userPath, $"{dataRoot}/Config/Android/UserCustom.ini"))
        {
            return OperationResult.Fail("Could not apply the shadow setting to GameLoop.");
        }

        if (selection.EnableKoreanFullHd && CurrentPackage.Equals("com.pubg.krmobile", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyKoreanFullHd();
        }

        _adb.Shell($"am start -n {CurrentPackage}/com.epicgames.ue4.SplashActivity");
        return OperationResult.Ok("Graphics settings applied successfully.");
    }

    private OperationResult UpdateShadow(bool enable)
    {
        var localPath = Path.Combine(_workRoot, "user.mkvip");
        if (!File.Exists(localPath))
        {
            return OperationResult.Fail("Could not read the PUBG shadow settings.");
        }

        var values = enable
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["r.UserShadowSwitch"] = "1",
                ["r.ShadowQuality"] = "1",
                ["r.Mobile.DynamicObjectShadow"] = "1",
                ["r.Shadow.MaxCSMResolution"] = "1",
                ["r.Shadow.DistanceScale"] = "1",
                ["r.Shadow.CSM.MaxMobileCascades"] = "1"
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["r.UserShadowSwitch"] = "0",
                ["r.ShadowQuality"] = "0",
                ["r.Mobile.DynamicObjectShadow"] = "0",
                ["r.Shadow.MaxCSMResolution"] = "0",
                ["r.Shadow.DistanceScale"] = "0",
                ["r.Shadow.CSM.MaxMobileCascades"] = "0"
            };

        var lines = File.ReadAllLines(localPath);
        var changed = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("+CVars=", StringComparison.Ordinal))
            {
                continue;
            }

            var decoded = DecodeCVar(trimmed[7..]);
            var separator = decoded.IndexOf('=');
            if (separator < 0 || !values.TryGetValue(decoded[..separator], out var value))
            {
                continue;
            }

            var indentationLength = lines[index].Length - lines[index].TrimStart().Length;
            lines[index] = lines[index][..indentationLength] + "+CVars=" + EncodeCVar(decoded[..separator], value);
            changed = true;
        }

        if (!changed)
        {
            return OperationResult.Fail("The PUBG shadow setting was not found.");
        }

        File.WriteAllLines(localPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return OperationResult.Ok(enable ? "Shadow enabled." : "Shadow disabled.");
    }

    private static string DecodeCVar(string encoded)
    {
        if (encoded.Length % 2 != 0)
        {
            return string.Empty;
        }

        var decoded = new StringBuilder(encoded.Length / 2);
        for (var index = 0; index < encoded.Length; index += 2)
        {
            if (!byte.TryParse(encoded.Substring(index, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                return string.Empty;
            }

            decoded.Append((char)(value ^ 0x79));
        }

        return decoded.ToString();
    }

    private static string EncodeCVar(string name, string value)
    {
        var plainText = name + "=" + value;
        var encoded = new StringBuilder(plainText.Length * 2);
        foreach (var character in plainText)
        {
            encoded.Append(((byte)character ^ 0x79).ToString("X2"));
        }

        return encoded.ToString();
    }

    public static bool IsGameLoopRunning()
    {
        var processNames = new[] { "AndroidEmulatorEx", "AndroidEmulatorEn", "AndroidEmulator" };
        return processNames.Any(name => Process.GetProcessesByName(name).Length > 0);
    }

    private byte ReadProperty(string name)
    {
        if (_activeSavContent is null)
        {
            return 0;
        }

        var header = CreateHeader(name);
        var headerIndex = FindSequence(_activeSavContent, header);
        return headerIndex >= 0 && headerIndex + header.Length < _activeSavContent.Length
            ? _activeSavContent[headerIndex + header.Length]
            : (byte)0;
    }

    private bool ChangeProperty(string name, byte value)
    {
        if (_activeSavContent is null)
        {
            return false;
        }

        var header = CreateHeader(name);
        var headerIndex = FindSequence(_activeSavContent, header);
        if (headerIndex < 0 || headerIndex + header.Length >= _activeSavContent.Length)
        {
            return false;
        }

        _activeSavContent[headerIndex + header.Length] = value;
        return true;
    }

    private OperationResult ApplyKoreanFullHd()
    {
        if (string.IsNullOrWhiteSpace(CurrentPackage))
        {
            return OperationResult.Fail("PUBG Mobile KR is not connected.");
        }

        var dataPath = $"/sdcard/Android/data/{CurrentPackage}";
        var obbPath = $"/sdcard/Android/obb/{CurrentPackage}";
        var configPath = $"{dataPath}/files/UE4Game/ShadowTrackerExtra/ShadowTrackerExtra/Saved/Config/Android/UserCustom.ini";
        var safePath = "/sdcard/mk_safe_folder";
        var accountPath = $"/data/data/{CurrentPackage}";

        var krIni = Path.Combine(_assetRoot, "mk_kr.ini");
        if (!File.Exists(krIni) || !_adb.Push(krIni, configPath))
        {
            return OperationResult.Fail("Could not apply the PUBG KR resolution file.");
        }

        _adb.Shell($"mkdir -p {safePath}");
        _adb.Shell($"cp -r {accountPath}/shared_prefs {safePath}/shared_prefs");
        _adb.Shell($"cp -r {accountPath}/databases {safePath}/databases");

        BackupRemoteFolder(dataPath);
        BackupRemoteFolder(obbPath);
        _adb.Shell($"pm clear {CurrentPackage}");
        _adb.Shell($"pm grant {CurrentPackage} android.permission.READ_EXTERNAL_STORAGE");
        _adb.Shell($"pm grant {CurrentPackage} android.permission.WRITE_EXTERNAL_STORAGE");
        RestoreRemoteFolder(dataPath);
        RestoreRemoteFolder(obbPath);
        _adb.Shell($"cp -r {safePath}/shared_prefs {accountPath}/shared_prefs");
        _adb.Shell($"cp -r {safePath}/databases {accountPath}/databases");
        _adb.Shell($"am start -n {CurrentPackage}/com.epicgames.ue4.SplashActivity");
        _adb.Shell($"rm -r {safePath}");

        return OperationResult.Ok("Graphics settings applied and PUBG KR set to 1080p.");
    }

    private void BackupRemoteFolder(string remotePath)
    {
        var backupPath = remotePath + ".MKbackup";
        var exists = _adb.Shell($"[ -d {remotePath} ] && echo 1 || echo 0").Trim() == "1";
        var backupExists = _adb.Shell($"[ -d {backupPath} ] && echo 1 || echo 0").Trim() == "1";
        if (!backupExists && exists)
        {
            _adb.Shell($"mv {remotePath} {backupPath}");
        }
        else if (backupExists && exists)
        {
            _adb.Shell($"rm -r {remotePath}");
        }
    }

    private void RestoreRemoteFolder(string remotePath)
    {
        var backupPath = remotePath + ".MKbackup";
        if (_adb.Shell($"[ -d {backupPath} ] && echo 1 || echo 0").Trim() == "1")
        {
            _adb.Shell($"mv {backupPath} {remotePath}");
        }
    }

    private void PrepareWorkingFiles()
    {
        Directory.CreateDirectory(_workRoot);
        foreach (var name in new[] { "old.mkvip", "new.mkvip", "user.mkvip", "testADB.mkvip" })
        {
            var source = Path.Combine(_assetRoot, name);
            var destination = Path.Combine(_workRoot, name);
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }
    }

    private static byte[] CreateHeader(string propertyName)
    {
        return Encoding.UTF8.GetBytes(
            propertyName + "\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0");
    }

    private static int FindSequence(byte[] source, byte[] sequence)
    {
        for (var i = 0; i <= source.Length - sequence.Length; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (source[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
