using System.Text.Json;
using System.Xml.Linq;
using Nexora.Models;

namespace Nexora.Services;

public sealed class IpadLayoutService
{
    private readonly RegistryService _registry;
    private readonly string _mapPath;

    public IpadLayoutService(RegistryService registry)
    {
        _registry = registry;
        _mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ipad_layout_map.json");
    }

    public OperationResult Apply(int width, int height)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var keymapFolder = Path.Combine(appData, "AndroidTbox");
        var originalPath = Path.Combine(keymapFolder, "TVM_100.xml");
        var backupPath = originalPath + ".mkbackup";
        if (!File.Exists(originalPath))
        {
            return OperationResult.Fail("GameLoop keymap file was not found.");
        }

        try
        {
            if (!File.Exists(backupPath)) File.Copy(originalPath, backupPath);
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

            return OperationResult.Ok($"Resolution set to {width} x {height}.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not set iPad resolution: {ex.Message}");
        }
    }

    public OperationResult Reset()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var originalPath = Path.Combine(appData, "AndroidTbox", "TVM_100.xml");
        var backupPath = originalPath + ".mkbackup";
        if (!File.Exists(backupPath)) return OperationResult.Fail("No saved iPad resolution was found.");

        try
        {
            File.Copy(backupPath, originalPath, overwrite: true);
            File.Delete(backupPath);
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
        if (!File.Exists(_mapPath)) throw new FileNotFoundException("The iPad layout map is missing.", _mapPath);
        var map = JsonDocument.Parse(File.ReadAllText(_mapPath)).RootElement;
        var source = File.ReadAllText(originalPath);
        var root = XElement.Parse($"<root>{source}</root>", LoadOptions.PreserveWhitespace);
        var supportedPackages = GameLoopService.PubgVersions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in root.Descendants("Item").Where(item => supportedPackages.Contains((string?)item.Attribute("ApkName") ?? "")))
        {
            foreach (var query in map.EnumerateObject())
            {
                var values = query.Value;
                var mode = item.Descendants("KeyMapMode").FirstOrDefault(element => (string?)element.Attribute("Name") == query.Name);
                if (mode is null) continue;

                foreach (var button in values.EnumerateObject())
                {
                    var keyMappingEx = mode.Descendants("KeyMappingEx").FirstOrDefault(element => (string?)element.Attribute("ItemName") == button.Name);
                    var keyMapping = mode.Descendants("KeyMapping").FirstOrDefault(element => (string?)element.Attribute("ItemName") == button.Name);
                    foreach (var switchEntry in button.Value.EnumerateObject())
                    {
                        if (keyMappingEx is not null)
                        {
                            UpdateExtendedMapping(keyMappingEx, button.Name, switchEntry.Name, switchEntry.Value);
                        }
                        else if (keyMapping is not null)
                        {
                            UpdateBasicMapping(keyMapping, switchEntry.Name, switchEntry.Value);
                        }
                    }
                }
            }
        }

        var updated = string.Concat(root.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)));
        File.WriteAllText(originalPath, updated);
    }

    private static void UpdateExtendedMapping(XElement keyMappingEx, string buttonName, string switchName, JsonElement points)
    {
        var pointMode = keyMappingEx.Descendants("SwitchOperation").Any(operation => operation.Attribute("EnablePositionSwitch") is not null);
        if (pointMode)
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
            return;
        }

        var switchOperations = keyMappingEx.Descendants("SwitchOperation")
            .Where(operation => (string?)operation.Attribute("EnableSwitch") == switchName)
            .ToList();
        var targets = ToPointPairs(points);
        var pointsToUpdate = keyMappingEx.Descendants("Point").ToList();
        if (pointsToUpdate.Count == 0) pointsToUpdate = keyMappingEx.Descendants("DriveKey").ToList();
        if (pointsToUpdate.Count == 0) pointsToUpdate = switchOperations;

        for (var index = 0; index < Math.Min(targets.Count, pointsToUpdate.Count); index++)
        {
            var target = targets[index];
            if (buttonName == "Click with Scroll Wheel" && switchName == "Backpage")
            {
                keyMappingEx.SetAttributeValue("Click_X", (double.Parse(target.X) + 0.1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                keyMappingEx.SetAttributeValue("Click_Y", target.Y);
            }

            keyMappingEx.SetAttributeValue("Point_X", target.X);
            keyMappingEx.SetAttributeValue("Point_Y", target.Y);
            if (pointsToUpdate[index].Attribute("Point_X") is not null)
            {
                pointsToUpdate[index].SetAttributeValue("Point_X", target.X);
                pointsToUpdate[index].SetAttributeValue("Point_Y", target.Y);
            }
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
}
