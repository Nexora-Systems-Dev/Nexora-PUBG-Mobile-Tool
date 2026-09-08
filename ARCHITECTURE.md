# Nexora PUBG Mobile Tool — Architecture

WPF desktop companion (`net8.0-windows`, `WinExe`, nullable + implicit usings
enabled) for managing the GameLoop emulator's PUBG Mobile installation:
graphics/shadow tuning, iPad-layout resolution patching, DNS switching,
process-priority tuning, temp cleaning, and self-updates. Runs elevated
(`app.manifest` requires administrator) because it touches emulator registry
keys, GPU settings, and system temp locations.

Solution: `Nexora.slnx` → app (`Nexora.csproj`) + tests
(`Nexora.Tests/Nexora.Tests.csproj`, xUnit + FluentAssertions, 106 tests,
`InternalsVisibleTo("Nexora.Tests")` for internal seams).

## High-Level Architecture

```text
┌─ UI Layer ─────────────────────────────┐
│ MainWindow.xaml(.cs)  App.xaml(.cs)    │  view bindings only;
│ UI/Behaviors/WindowChromeBehavior.cs   │  detachable HWND hook
└───────────────┬────────────────────────┘
                │  calls, CancellationToken-async APIs
┌─ Features ─────────────────────────────┤
│ GameLoop ADB bridge (Services/):       │
│   AdbClient, GameLoopService           │
│ Windows tools facade (Services/):      │
│   WindowsToolsService → NetworkTools / │
│   Shortcut / TempCleanup services      │
│ Update pipeline (Services/):           │
│   UpdateService                        │
│ Layout patching: Services/             │
│   IpadLayoutService + Features/Layout/ │
│   IpadPresetCatalog                    │
│ SystemTools network data: Features/    │
│   SystemTools/Network/DnsCatalog       │
│ Performance (Services/Performance/):   │
│   priority / GPU / power /             │
│   hardware-detection engine            │
└───────────────┬────────────────────────┘
                │  reads only
┌─ Shared & Data ────────────────────────┤
│ Shared/Kernel: ProcessRunner,          │
│   ProcessResult, ProcessText           │
│ Shared/Infrastructure: RegistryService │
│ Configuration/AppConstants.cs (single  │
│   source of truth)                     │
│ Models/SettingsModels.cs               │
│   (OperationResult envelope, settings) │
│ Assets/ (icons, styles, layout map)    │
└────────────────────────────────────────┘
```

Target (ECC feature-first, in progress): `Features/GameLoop`,
`Features/Layout`, `Features/Updates`, `Features/SystemTools`,
`Features/Performance` + `Shared/Kernel`, `Shared/Infrastructure`,
`Shared/Configuration` + `UI/`. Phase 1 (done): leaf files moved
without behavior change — `DnsCatalog` →
`Features/SystemTools/Network` (`Nexora.Features.SystemTools.Network`),
`IpadPresetCatalog` → `Features/Layout`
(`Nexora.Features.Layout`), `WindowChromeBehavior` → `UI/Behaviors`
(`Nexora.UI.Behaviors`), `ProcessRunner`/`ProcessText` →
`Shared/Kernel` (`Nexora.Shared.Kernel`), `RegistryService` →
`Shared/Infrastructure` (`Nexora.Shared.Infrastructure`).
Namespaces follow folders; consumers gained explicit
`using Nexora.Shared.Kernel/Infrastructure` imports.

Cross-cutting rules (ECC-governed): async/await with `CancellationToken`
through public APIs — never `.Result`/`.Wait()`; fail fast with clear
messages; best-effort cleanup that never alters user-visible results.

## Component Responsibilities

| Component | Responsibility |
|---|---|
| `Services/UpdateService.cs` | Checks GitHub releases (`CheckAsync`), downloads the release `.zip` into a unique `%TEMP%/NexoraUpdate-<Guid>/` staging tree, validates the archive, extracts safely, and launches the installer elevated. Exposes internal `TryDeleteDirectory` (best-effort recursive sweep) and `PurgeStaleStagingDirectories` (reclaims orphan trees older than `StaleStagingMaxAge`). |
| `Services/NetworkToolsService.cs` | DNS switching (`ChangeDns`, gated by `IPAddress.TryParse`), latency probing (`PingDns`, null-on-failure so unresolvable hosts never throw). |
| `Services/ShortcutService.cs` | Desktop/Start-menu shortcut creation for the tool. |
| `Services/TempCleanupService.cs` | Temp/Prefetch/ShaderCache cleaning. Per-file/per-directory exception isolation with a skipped-location report; reuses the updater's hardened sweep so one locked file no longer spares a whole subtree. |
| `Services/Performance/ProcessPriorityService.cs` | GameLoop process-priority monitor: `StartMonitor`, non-blocking `StopMonitor`, bounded `StopMonitorAsync` (2 s cap), snapshot `RestoreAsync`, and dead/recycled-PID pruning each tick. |
| `Configuration/AppConstants.cs` | Single source of truth: app identity/version (`v1.0.9`), update endpoints and staging names, all timeout budgets, ADB details (`emulator-5554`, `127.0.0.1:5555`), emulator process/registry names, asset file names, tools, and `Validation` (Android package-name pattern). |
| `Services/AdbClient.cs` | Low-level ADB bridge: connect, shell, push/pull with retry, boot wait, installed-package queries. All cancellable; validates inputs before spawning `adb`. |
| `Services/GameLoopService.cs` | PUBG version catalog, connect/load-version flow, shadow + graphics application, Korean Full-HD pass. Rejects malformed package names before any device or filesystem touch. |
| `Services/WindowsToolsService.cs` | Thin facade (`IGameLoopPerformanceEngine`) delegating to the network/shortcut/cleanup services. |
| `Services/IpadLayoutService.cs` | Resolution/layout XML patching driven by `Assets/ipad_layout_map.json`; coordinate math strictly under `InvariantCulture`. |
| `Shared/Kernel/ProcessRunner.cs` | Bounded process spawning (default 30 s cap). |
| `Shared/Infrastructure/RegistryService.cs` | Typed access to the `AppMarket`/`UI` registry branches (install path, flags). |
| `Services/Performance/` (rest) | `HardwareDetectionService`, `GpuRoutingService`, `PowerSessionService`, `PerformancePlanBuilder`, `PerformanceExecutionReport` — detection, routing, and planning behind `IGameLoopPerformanceEngine`. |
| `Features/SystemTools/Network/`, `Features/Layout/`, `Models/` | `DnsCatalog` / `IpadPresetCatalog` static data (feature-owned); `OperationResult` success/fail envelope and settings models. |
| `MainWindow.xaml(.cs)` | View bindings + tool dispatch with per-action `CancellationTokenSource`; disposes connection CTS and window hook on close. |

## Safety & Security Protocols

- **Zip Slip protection** — every archive entry resolves through
  `GetSafeExtractionPath`; any destination escaping the extraction root
  aborts the update with an error.
- **ADB package validation** — `AppConstants.Validation`
  (`^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`) rejects traversal, shell
  metacharacters, and malformed names in `GameLoopService.LoadVersionAsync`
  (fail result) and `AdbClient.FindInstalledPackages` (`ArgumentException`)
  before any shell or filesystem use.
- **Temp directory isolation** — per-attempt GUID staging (no predictable
  `%TEMP%/NexoraUpdate` path to pre-plant); full-tree sweep in `finally` on
  every branch (success, error, cancellation); 24 h stale-orphan purge on the
  next attempt; locked installer files deferred, never force-deleted.
- **Executable allow-list** — only the expected versioned or portable
  `Nexora-*.exe` entry is launched, over HTTPS only.
- **DNS input gating** — both servers must `IPAddress.TryParse` before any
  PowerShell is built.
- **Invariant culture handling** — coordinate parse/format and user-facing
  numeric messages use `CultureInfo.InvariantCulture`, so comma-decimal
  locales can neither throw nor corrupt values.
- **Cleanup exception safety** — all deletion paths are catch-all
  best-effort with explicit comments; failures are skipped/reported, never
  thrown, and never change the operation result.
- **No secrets in tree** — no API keys, tokens, or connection strings in
  source or `appsettings`; elevated actions go through explicit UAC (`runas`).

## Verification

- `dotnet build Nexora.slnx` → 0 warnings, 0 errors.
- `dotnet test Nexora.slnx` → 106/106 passed.
- CI (`.github/workflows/ci.yml`, `windows-latest`): restore → Release
  build → Release test on push/PR to `main`/`master`. (`build.yml` remains
  as the app-only Release build.)
