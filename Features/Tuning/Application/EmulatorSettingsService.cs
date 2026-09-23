using Nexora.Features.Performance.Application;
using Nexora.Features.Performance.Domain;
using Nexora.Features.Performance.Infrastructure;
using Nexora.Shared.Kernel;
using Nexora.Features.Tuning.Domain;
using Nexora.Infrastructure.Registry;
using Nexora.Infrastructure.Processes;
using Nexora.Shared.Contracts;

namespace Nexora.Features.Tuning.Application;

/// <summary>
/// Manual counterpart to hardware-derived Smart Settings: reads the ten tuning
/// controls from the GameLoop user hive and forces user-chosen values back with
/// write-then-read-back verification. Refuses to write while GameLoop runs, so
/// the emulator cannot overwrite the settings on exit.
/// </summary>
public sealed class EmulatorSettingsService : IEmulatorSettingsService
{
    private readonly IUserRegistry _registry;
    private readonly IGameLoopProcessService? _processService;
    private readonly Func<CancellationToken, Task<HardwareSnapshot>> _hardwareSnapshot;
    private HardwareSnapshot? _cachedHardware;

    public EmulatorSettingsService(
        IUserRegistry registry,
        IGameLoopProcessService? processService = null,
        Func<CancellationToken, Task<HardwareSnapshot>>? hardwareSnapshot = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _processService = processService;
        _hardwareSnapshot = hardwareSnapshot ?? (ct => new HardwareDetectionService(new ProcessRunner()).GetSnapshotAsync(ct));
    }

    public async Task<EmulatorTuningState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hardware = await GetHardwareSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var running = FindRunningProcesses();
        try
        {
            var selection = new EmulatorTuningSelection(
                CpuCores: ClampCpuCores(ReadOrDefault(EmulatorTuningCatalog.CpuCoresName, EmulatorTuningCatalog.DefaultCpuCores), hardware),
                MemoryMb: ClampMemoryMb(ReadOrDefault(EmulatorTuningCatalog.MemoryMbName, EmulatorTuningCatalog.DefaultMemoryMb), hardware),
                Dpi: ReadDpiOrDefault(),
                RenderCacheEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.RenderCacheName, true),
                GlobalCacheEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.GlobalCacheName, true),
                DiscreteGpuEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.DiscreteGpuName, true),
                RenderOptimizeEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.RenderOptimizeName, true),
                VSyncEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.VSyncName, false),
                AdbEnabled: !ReadFlagOrDefault(EmulatorTuningCatalog.AdbDisableName, false),
                AntiAliasingEnabled: ReadFlagOrDefault(EmulatorTuningCatalog.AntiAliasingName, true));
            return new EmulatorTuningState(
                selection,
                running.Count > 0,
                running.Select(GetDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        finally
        {
            DisposeProcesses(running);
        }
    }

    public async Task<OperationResult> ApplyAsync(
        EmulatorTuningSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();

        var running = FindRunningProcesses();
        try
        {
            if (running.Count > 0)
            {
                var names = string.Join(", ", running.Select(GetDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                return OperationResult.Fail($"Close GameLoop before applying emulator tuning ({names}), then apply it again.");
            }
        }
        finally
        {
            DisposeProcesses(running);
        }

        if (!EmulatorTuningCatalog.DpiOptions.Contains(selection.Dpi))
        {
            return OperationResult.Fail($"Unsupported screen DPI '{selection.Dpi}'. Choose one of: {string.Join(", ", EmulatorTuningCatalog.DpiOptions)}.");
        }

        var hardware = await GetHardwareSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var writes = BuildWrites(selection, hardware);

        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.SetUserDword(write.Key, write.Value) || _registry.GetUserDword(write.Key) != write.Value)
            {
                return OperationResult.Fail($"Could not verify GameLoop setting: {write.Key}.");
            }
        }

        return OperationResult.Ok($"Applied {writes.Count} emulator settings. Restart GameLoop to take effect.");
    }

    /// <summary>
    /// The DWORD write set for a selection, in write order, already clamped to
    /// the detected hardware. The paired <c>SetGraphicsCard</c> mirror and the
    /// inverted <c>AdbDisable</c> mapping live here and nowhere else.
    /// </summary>
    private static List<KeyValuePair<string, int>> BuildWrites(EmulatorTuningSelection selection, HardwareSnapshot hardware) => new()
    {
        new(EmulatorTuningCatalog.CpuCoresName, ClampCpuCores(selection.CpuCores, hardware)),
        new(EmulatorTuningCatalog.MemoryMbName, ClampMemoryMb(selection.MemoryMb, hardware)),
        new(EmulatorTuningCatalog.DpiName, selection.Dpi),
        new(EmulatorTuningCatalog.RenderCacheName, ToDword(selection.RenderCacheEnabled)),
        new(EmulatorTuningCatalog.GlobalCacheName, ToDword(selection.GlobalCacheEnabled)),
        new(EmulatorTuningCatalog.DiscreteGpuName, ToDword(selection.DiscreteGpuEnabled)),
        new(EmulatorTuningCatalog.DiscreteGpuPairedName, ToDword(selection.DiscreteGpuEnabled)),
        new(EmulatorTuningCatalog.RenderOptimizeName, ToDword(selection.RenderOptimizeEnabled)),
        new(EmulatorTuningCatalog.VSyncName, ToDword(selection.VSyncEnabled)),
        // Inverted vendor semantics: AdbDisable=0 means debugging is ON.
        new(EmulatorTuningCatalog.AdbDisableName, selection.AdbEnabled ? 0 : 1),
        new(EmulatorTuningCatalog.AntiAliasingName, ToDword(selection.AntiAliasingEnabled)),
    };

    /// <summary>
    /// Hardware does not change at runtime, so the first scan is cached for the
    /// session. The scan itself (powershell + CIM) runs off-thread inside
    /// <c>GetSnapshotAsync</c>; awaiting it here keeps the UI thread responsive
    /// while Tuning loads (QA F-002).
    /// </summary>
    private async Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_cachedHardware is not null) return _cachedHardware;
        var hardware = await _hardwareSnapshot(cancellationToken).ConfigureAwait(false);
        _cachedHardware = hardware;
        return hardware;
    }

    private List<System.Diagnostics.Process> FindRunningProcesses() =>
        _processService?.FindGameLoopProcesses() ?? new List<System.Diagnostics.Process>();

    private static void DisposeProcesses(List<System.Diagnostics.Process> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Names a running process, tolerating the race where it exits between
    /// enumeration and naming.
    /// </summary>
    private static string GetDisplayName(System.Diagnostics.Process process)
    {
        try
        {
            return $"{process.ProcessName}.exe";
        }
        catch (InvalidOperationException)
        {
            return "GameLoop process";
        }
    }

    private int ReadOrDefault(string name, int fallback) => _registry.GetUserDword(name) ?? fallback;

    private bool ReadFlagOrDefault(string name, bool fallback) =>
        (_registry.GetUserDword(name) ?? (fallback ? 1 : 0)) != 0;

    private int ReadDpiOrDefault()
    {
        var current = _registry.GetUserDword(EmulatorTuningCatalog.DpiName);
        return current is not null && EmulatorTuningCatalog.DpiOptions.Contains(current.Value)
            ? current.Value
            : EmulatorTuningCatalog.DefaultDpi;
    }

    private static int ClampCpuCores(int requested, HardwareSnapshot hardware) =>
        Math.Clamp(requested, EmulatorTuningCatalog.MinCpuCores, Math.Min(EmulatorTuningCatalog.MaxCpuCores, Math.Max(1, hardware.LogicalCores)));

    private static int ClampMemoryMb(int requested, HardwareSnapshot hardware) =>
        Math.Clamp(requested, EmulatorTuningCatalog.MinMemoryMb, Math.Max(EmulatorTuningCatalog.MinMemoryMb, hardware.TotalMemoryGb * 1024));

    private static int ToDword(bool enabled) => enabled ? 1 : 0;
}
