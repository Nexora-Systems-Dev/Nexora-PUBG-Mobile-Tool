using System.Runtime.InteropServices;
using System.Text.Json;
using Nexora.Configuration;
using Nexora.Features.Performance;
using Nexora.Shared.Kernel;

namespace Nexora.Services.Performance;

/// <summary>
/// Reads read-only hardware and power information for optimization planning and diagnostics.
/// </summary>
public sealed class HardwareDetectionService
{
    private readonly IProcessRunner _runner;
    private readonly GameLoopOptions _gameLoop;

    public HardwareDetectionService(IProcessRunner runner, GameLoopOptions? gameLoop = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _gameLoop = gameLoop ?? new GameLoopOptions();
    }

    public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetSnapshot();
        }, cancellationToken);
    }

    public HardwareSnapshot GetSnapshot()
    {
        var json = TryRunDetectionScript();
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateFallbackSnapshot();
        }

        try
        {
            using var document = JsonDocument.Parse(json.Trim());
            return ParseSnapshot(document.RootElement);
        }
        catch (JsonException)
        {
            return CreateFallbackSnapshot();
        }
    }

    /// <summary>
    /// Runs the hardware detection script and returns its raw JSON output,
    /// or null when detection fails.
    /// </summary>
    private string? TryRunDetectionScript()
    {
        var result = _runner.RunPowerShell(DetectionScript, _gameLoop.Timeouts.HardwareDetectionTimeout);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        return result.StandardOutput;
    }

    /// <summary>
    /// Builds a snapshot from parsed detection output, clamping values into valid ranges.
    /// </summary>
    private static HardwareSnapshot ParseSnapshot(JsonElement root)
    {
        return new HardwareSnapshot(
            ReadJsonString(root, "CpuVendor", "Unknown CPU vendor"),
            ReadJsonString(root, "CpuName", "Unknown CPU"),
            Math.Max(1, ReadJsonInt(root, "PhysicalCores", Math.Max(1, Environment.ProcessorCount / 2))),
            Math.Max(1, ReadJsonInt(root, "LogicalCores", Environment.ProcessorCount)),
            Math.Max(1, ReadJsonInt(root, "TotalMemoryGb", 8)),
            ReadJsonString(root, "GpuVendor", "Unknown GPU vendor"),
            ReadJsonString(root, "GpuName", "Unknown GPU"),
            Math.Max(0, ReadJsonInt(root, "GpuMemoryGb", 0)),
            Math.Max(60, ReadJsonInt(root, "RefreshRateHz", 0) > 0
                ? ReadJsonInt(root, "RefreshRateHz", 60)
                : GetDisplayRefreshRate()),
            ReadJsonBool(root, "IsLaptop"),
            IsOnAcPower(),
            ReadJsonBool(root, "VirtualizationEnabled"),
            ReadJsonBool(root, "HypervisorDetected"));
    }

    private const string DetectionScript = @"
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$gpu = Get-CimInstance Win32_VideoController | Sort-Object AdapterRAM -Descending | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$laptop = @(Get-CimInstance Win32_Battery).Count -gt 0
[pscustomobject]@{
    CpuVendor = [string]$cpu.Manufacturer
    CpuName = [string]$cpu.Name
    PhysicalCores = [int]$cpu.NumberOfCores
    LogicalCores = [int]$cpu.NumberOfLogicalProcessors
    TotalMemoryGb = [int][math]::Round($computer.TotalPhysicalMemory / 1GB)
    GpuVendor = [string]$gpu.AdapterCompatibility
    GpuName = [string]$gpu.Name
    GpuMemoryGb = [int][math]::Round(([double]$gpu.AdapterRAM / 1GB))
    RefreshRateHz = [int]$gpu.CurrentRefreshRate
    IsLaptop = $laptop
    VirtualizationEnabled = [bool]$cpu.VirtualizationFirmwareEnabled
    HypervisorDetected = [bool]$computer.HypervisorPresent
} | ConvertTo-Json -Compress
";

    private HardwareSnapshot CreateFallbackSnapshot()
    {
        var physicalCores = Math.Max(1, Environment.ProcessorCount / 2);
        return new HardwareSnapshot(
            "Unknown CPU vendor",
            "Unknown CPU",
            physicalCores,
            Math.Max(1, Environment.ProcessorCount),
            8,
            "Unknown GPU vendor",
            "Unknown GPU",
            0,
            60,
            false,
            IsOnAcPower(),
            false,
            false);
    }

    private static string ReadJsonString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int ReadJsonInt(JsonElement root, string propertyName, int fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static bool ReadJsonBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
            (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }

    private static bool IsOnAcPower()
    {
        try
        {
            return GetSystemPowerStatus(out var status) && status.ACLineStatus != 0;
        }
        catch
        {
            return true;
        }
    }

    private static int GetDisplayRefreshRate()
    {
        try
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            return EnumDisplaySettings(null, -1, ref mode) && mode.dmDisplayFrequency > 0
                ? mode.dmDisplayFrequency
                : 60;
        }
        catch
        {
            return 60;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
