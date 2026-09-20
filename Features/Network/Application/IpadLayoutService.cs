using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Nexora.Configuration;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;
using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Graphics.Domain;
using Nexora.Features.Network.Domain;
using Nexora.Infrastructure.Registry;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Network.Application;

public sealed class IpadLayoutService : IIpadLayoutService
{
    private readonly IUserRegistry _registry;
    private readonly IFileSystem _fileSystem;
    private readonly IpadLayoutOptions _options;
    private readonly IGameLoopProcessService? _processService;

    public IpadLayoutService(IUserRegistry registry, IFileSystem fileSystem, IpadLayoutOptions? options = null, IGameLoopProcessService? processService = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _options = options ?? new IpadLayoutOptions();
        _processService = processService;
    }

    public OperationResult SetIpadResolution(int width, int height)
    {
        // GameLoop locks the keymap file and overwrites the resolution
        // registry values on exit, so patching while it runs is rejected.
        // The guard lives here (not with callers) so no composition path
        // can silently skip it.
        if (_processService is not null)
        {
            var running = _processService.FindGameLoopProcesses();
            if (running.Count > 0)
            {
                var names = string.Join(", ", running.Select(process => $"{process.ProcessName}.exe").Distinct(StringComparer.OrdinalIgnoreCase));
                foreach (var process in running)
                {
                    process.Dispose();
                }

                return OperationResult.Fail($"Close GameLoop before applying iPad View ({names}), then apply it again.");
            }
        }

        var originalPath = _options.GetKeymapFilePath();
        var backupPath = _options.GetBackupFilePath();
        if (!_fileSystem.Exists(originalPath))
        {
            return OperationResult.Fail("GameLoop keymap file was not found.");
        }

        try
        {
            // An existing legacy backup already preserves the pre-patch
            // original, so only snapshot when neither backup exists.
            if (!_fileSystem.Exists(backupPath) && !_fileSystem.Exists(_options.GetLegacyBackupFilePath())) _fileSystem.Copy(originalPath, backupPath);
            if (_registry.GetAppSettingDword("VMResWidth") is null || _registry.GetAppSettingDword("VMResHeight") is null)
            {
                var currentWidth = _registry.GetUserDword("VMResWidth");
                var currentHeight = _registry.GetUserDword("VMResHeight");
                if (currentWidth is not null) _registry.SetAppSettingDword("VMResWidth", currentWidth.Value);
                if (currentHeight is not null) _registry.SetAppSettingDword("VMResHeight", currentHeight.Value);
            }
            ApplyLayoutMap(originalPath);
            if (!_registry.SetUserDword("VMResWidth", width) || !_registry.SetUserDword("VMResHeight", height))
            {
                return OperationResult.Fail("Could not save the selected iPad resolution to GameLoop.");
            }

            var appliedWidth = _registry.GetUserDword("VMResWidth");
            var appliedHeight = _registry.GetUserDword("VMResHeight");
            if (appliedWidth != width || appliedHeight != height)
            {
                return OperationResult.Fail("GameLoop did not accept the selected iPad resolution.");
            }

            return OperationResult.Ok(FormattableString.Invariant($"Resolution set to {width} x {height}."));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not set iPad resolution: {ex.Message}");
        }
    }

    public OperationResult ResetIpadResolution()
    {
        var originalPath = _options.GetKeymapFilePath();
        var backupPath = _options.GetBackupFilePath();
        var legacyBackupPath = _options.GetLegacyBackupFilePath();

        // New backup wins; legacy backup restores without being deleted so
        // pre-rename users keep a working rollback indefinitely.
        string? restorePath = null;
        var deleteAfterRestore = false;
        if (_fileSystem.Exists(backupPath))
        {
            restorePath = backupPath;
            deleteAfterRestore = true;
        }
        else if (_fileSystem.Exists(legacyBackupPath))
        {
            restorePath = legacyBackupPath;
        }

        if (restorePath is null) return OperationResult.Fail("No saved iPad resolution was found.");

        try
        {
            _fileSystem.Copy(restorePath, originalPath, overwrite: true);
            if (deleteAfterRestore) _fileSystem.Delete(backupPath);
            var savedWidth = _registry.GetAppSettingDword("VMResWidth");
            var savedHeight = _registry.GetAppSettingDword("VMResHeight");
            if (savedWidth is not null) _registry.SetUserDword("VMResWidth", savedWidth.Value);
            if (savedHeight is not null) _registry.SetUserDword("VMResHeight", savedHeight.Value);
            _registry.DeleteAppSetting("VMResWidth");
            _registry.DeleteAppSetting("VMResHeight");
            return OperationResult.Ok("iPad resolution was reset.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not reset iPad resolution: {ex.Message}");
        }
    }

    private void ApplyLayoutMap(string originalPath)
    {
        if (!_fileSystem.Exists(_options.LayoutMapPath)) throw new FileNotFoundException("The iPad layout map is missing.", _options.LayoutMapPath);
        var map = JsonDocument.Parse(_fileSystem.ReadAllText(_options.LayoutMapPath)).RootElement;
        var source = _fileSystem.ReadAllText(originalPath);
        var root = XElement.Parse($"<root>{source}</root>", LoadOptions.PreserveWhitespace);
        var supportedPackages = PubgVersionCatalog.PubgVersions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        ApplyLayoutMapToItems(root, map, supportedPackages);

        var updated = string.Concat(root.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)));
        _fileSystem.WriteAllText(originalPath, updated);
    }

    /// <summary>
    /// Applies the layout map to every keymap item belonging to a supported package.
    /// </summary>
    private static void ApplyLayoutMapToItems(XElement root, JsonElement map, HashSet<string> supportedPackages)
    {
        foreach (var item in root.Descendants("Item"))
        {
            if (!supportedPackages.Contains((string?)item.Attribute("ApkName") ?? "")) continue;
            ApplyLayoutMapToModes(item, map);
        }
    }

    /// <summary>
    /// Applies each mapped key-map mode to its matching KeyMapMode element.
    /// </summary>
    private static void ApplyLayoutMapToModes(XElement item, JsonElement map)
    {
        foreach (var query in map.EnumerateObject())
        {
            var mode = item.Descendants("KeyMapMode").FirstOrDefault(element => (string?)element.Attribute("Name") == query.Name);
            if (mode is null) continue;
            ApplyModeButtons(mode, query.Value);
        }
    }

    /// <summary>
    /// Applies each mapped button to its matching KeyMappingEx/KeyMapping element.
    /// </summary>
    private static void ApplyModeButtons(XElement mode, JsonElement buttons)
    {
        foreach (var button in buttons.EnumerateObject())
        {
            var keyMappingEx = mode.Descendants("KeyMappingEx").FirstOrDefault(element => (string?)element.Attribute("ItemName") == button.Name);
            var keyMapping = mode.Descendants("KeyMapping").FirstOrDefault(element => (string?)element.Attribute("ItemName") == button.Name);
            ApplyButtonSwitches(button.Name, button.Value, keyMappingEx, keyMapping);
        }
    }

    /// <summary>
    /// Applies each switch entry to the extended mapping when present, else the basic one.
    /// </summary>
    private static void ApplyButtonSwitches(string buttonName, JsonElement switches, XElement? keyMappingEx, XElement? keyMapping)
    {
        foreach (var switchEntry in switches.EnumerateObject())
        {
            if (keyMappingEx is not null)
            {
                UpdateExtendedMapping(keyMappingEx, buttonName, switchEntry.Name, switchEntry.Value);
            }
            else if (keyMapping is not null)
            {
                UpdateBasicMapping(keyMapping, switchEntry.Name, switchEntry.Value);
            }
        }
    }

    private static void UpdateExtendedMapping(XElement keyMappingEx, string buttonName, string switchName, JsonElement points)
    {
        var pointMode = keyMappingEx.Descendants("SwitchOperation").Any(operation => operation.Attribute("EnablePositionSwitch") is not null);
        if (pointMode)
        {
            UpdatePositionSwitchMapping(keyMappingEx, switchName, points);
            return;
        }

        UpdateSwitchPointMapping(keyMappingEx, buttonName, switchName, ToPointPairs(points));
    }

    /// <summary>
    /// Rewrites the EnablePositionSwitch coordinates of matching operations,
    /// preserving each operation's outer bounds while applying the new points.
    /// </summary>
    private static void UpdatePositionSwitchMapping(XElement keyMappingEx, string switchName, JsonElement points)
    {
        var operations = keyMappingEx.Descendants("SwitchOperation")
            .Where(operation => (string?)operation.Attribute("Description") == switchName)
            .ToList();
        var pointPairs = ToPointPairs(points);
        string? oldX = null;
        string? oldX2 = null;
        for (var index = 0; index < Math.Min(operations.Count, pointPairs.Count); index++)
        {
            var attribute = (string?)operations[index].Attribute("EnablePositionSwitch") ?? "";
            var split = attribute.Split(':', 2);
            if (split.Length != 2) continue;
            var coordinates = split[1].Split(',');
            if (coordinates.Length != 4) continue;
            oldX ??= coordinates[0];
            oldX2 ??= coordinates[2];
            operations[index].SetAttributeValue("EnablePositionSwitch", $"{split[0]}:{oldX},{pointPairs[index].X},{oldX2},{pointPairs[index].Y}");
        }
    }

    /// <summary>
    /// Applies target points to the key mapping and its point-bearing descendants.
    /// </summary>
    private static void UpdateSwitchPointMapping(XElement keyMappingEx, string buttonName, string switchName, List<PointPair> targets)
    {
        var pointsToUpdate = ResolveSwitchPoints(keyMappingEx, switchName);
        for (var index = 0; index < Math.Min(targets.Count, pointsToUpdate.Count); index++)
        {
            ApplySwitchPoint(keyMappingEx, pointsToUpdate[index], targets[index], buttonName, switchName);
        }
    }

    /// <summary>
    /// Resolves the elements to update: Point descendants, else DriveKey
    /// descendants, else the matching SwitchOperation elements themselves.
    /// </summary>
    private static List<XElement> ResolveSwitchPoints(XElement keyMappingEx, string switchName)
    {
        var points = keyMappingEx.Descendants("Point").ToList();
        if (points.Count == 0) points = keyMappingEx.Descendants("DriveKey").ToList();
        if (points.Count == 0) points = keyMappingEx.Descendants("SwitchOperation")
            .Where(operation => (string?)operation.Attribute("EnableSwitch") == switchName)
            .ToList();
        return points;
    }

    /// <summary>
    /// Applies one target point, including the scroll-wheel click special case.
    /// </summary>
    private static void ApplySwitchPoint(XElement keyMappingEx, XElement point, PointPair target, string buttonName, string switchName)
    {
        if (buttonName == "Click with Scroll Wheel" && switchName == "Backpage")
        {
            keyMappingEx.SetAttributeValue("Click_X", ShiftCoordinate(target.X, 0.1));
            keyMappingEx.SetAttributeValue("Click_Y", target.Y);
        }

        keyMappingEx.SetAttributeValue("Point_X", target.X);
        keyMappingEx.SetAttributeValue("Point_Y", target.Y);
        if (point.Attribute("Point_X") is not null)
        {
            point.SetAttributeValue("Point_X", target.X);
            point.SetAttributeValue("Point_Y", target.Y);
        }
    }

    private static void UpdateBasicMapping(XElement keyMapping, string switchName, JsonElement points)
    {
        var pointPairs = ToPointPairs(points);
        if (pointPairs.Count == 0) return;
        if (keyMapping.Attribute("Point_X") is not null)
        {
            keyMapping.SetAttributeValue("Point_X", pointPairs[0].X);
            keyMapping.SetAttributeValue("Point_Y", pointPairs[0].Y);
        }

        var operations = keyMapping.Descendants("SwitchOperation")
            .Where(operation => (string?)operation.Attribute("EnableSwitch") == switchName)
            .ToList();
        for (var index = 0; index < Math.Min(pointPairs.Count, operations.Count); index++)
        {
            operations[index].SetAttributeValue("Point_X", pointPairs[index].X);
            operations[index].SetAttributeValue("Point_Y", pointPairs[index].Y);
        }
    }

    private static List<PointPair> ToPointPairs(JsonElement value)
    {
        var points = new List<PointPair>();
        if (value.ValueKind != JsonValueKind.Array) return points;
        if (value.GetArrayLength() == 2 && value.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String))
        {
            var values = value.EnumerateArray().Select(element => element.GetString() ?? "0").ToArray();
            points.Add(new PointPair(values[0], values[1]));
            return points;
        }

        foreach (var pair in value.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
            var values = pair.EnumerateArray().Select(element => element.GetString() ?? "0").ToArray();
            points.Add(new PointPair(values[0], values[1]));
        }
        return points;
    }

    private sealed record PointPair(string X, string Y);

    /// <summary>
    /// Shifts a decimal coordinate by the specified delta using invariant culture formatting.
    /// Returns unparsable input unchanged.
    /// </summary>
    internal static string ShiftCoordinate(string value, double delta)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? (parsed + delta).ToString(CultureInfo.InvariantCulture)
            : value;
    }
}
