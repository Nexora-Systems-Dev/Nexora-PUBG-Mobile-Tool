namespace Nexora.Features.Tuning.Domain;

/// <summary>
/// User-chosen emulator tuning values. Booleans are UI truth (<c>AdbEnabled</c>
/// means debugging on); the inverted <c>AdbDisable</c> DWORD mapping lives in
/// <see cref="EmulatorSettingsService"/> alone.
/// </summary>
public sealed record EmulatorTuningSelection(
    int CpuCores,
    int MemoryMb,
    int Dpi,
    bool RenderCacheEnabled,
    bool GlobalCacheEnabled,
    bool DiscreteGpuEnabled,
    bool RenderOptimizeEnabled,
    bool VSyncEnabled,
    bool AdbEnabled,
    bool AntiAliasingEnabled);

/// <summary>
/// Tuning page state: current (or defaulted) selection plus the GameLoop
/// liveness that gates Apply.
/// </summary>
public sealed record EmulatorTuningState(
    EmulatorTuningSelection Selection,
    bool IsGameLoopRunning,
    IReadOnlyList<string> RunningProcessNames);
