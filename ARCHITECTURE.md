# Nexora PUBG Mobile Tool — Architecture Specification

Enterprise desktop companion (`net8.0-windows`, `WinExe`, nullable reference types, implicit usings) engineered for the GameLoop Android emulator and PUBG Mobile. Provides deep graphics/FPS tuning, UE4 SaveGame binary manipulation, obfuscated CVar codecs, iPad-view resolution/keymap patching, low-latency DNS routing, hardware-adaptive performance optimization, and self-updating lifecycle management.

Runs with elevated administrator privileges (`app.manifest` execution level `requireAdministrator`) to interface with emulator registry hives, GPU routing configurations, Windows power schemes, and system-level temporary caching subsystems.

Solution layout: `Nexora.slnx`
- **Application:** `Nexora.csproj`
- **Automated Test Suite:** `Nexora.Tests/Nexora.Tests.csproj` (519 standard tests passing; five optional Live Emulator Verification tests require a configured running GameLoop instance).

---

## 1. High-Level Architecture & Design Patterns

Nexora is structured around modern Enterprise Coding Conventions (ECC), utilizing a feature-first domain organization, an explicit Dependency Injection (DI) Composition Root, constructor injection, and interface segregation.

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                               Presentation Layer                            │
│  MainWindow.xaml / MainWindow.xaml.cs (250-line shell: navigation via       │
│    UI/Navigation/ShellNavigator, connection paint via                       │
│    UI/Presentation/ShellConnectionPresenter, update handoff via             │
│    Features/Updates/Presentation/UpdateHandoff)                             │
│  Features/*/Presentation: Graphics, Tuning, Network, Optimizer, Shortcuts,  │
│    About — each a UserControl + ViewModel owning its flow                   │
│  UI/Presentation (PageOperationBus, ActivityReportFormatter,                │
│    ResourceFreezer)                                                         │
│  UI/Navigation (NavigationItem, ShellNavigator)                             │
│  UI/Layout (ResponsiveLayoutManager)                                        │
│  UI/Behaviors (WindowChromeBehavior)                                        │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │ (Constructor Injection / Interfaces)
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                            Application Services                             │
│  IGameLoopConnection         IAdbClient             IUpdateService          │
│  IShortcutService            INetworkToolsService   ITempCleanupService     │
│  IGraphicsProfileStore                                                       │
│  IIpadLayoutService          IGameLoopProcessService IGameLoopPathResolver  │
│  IGameLoopPerformanceEngine (Facade)                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                         Performance & Engine Domain                         │
│  IHardwareDetectionService   IPerformancePlanBuilder                        │
│  IGameLoopRegistryOptimizer  IGpuRoutingService                             │
│  IPowerSessionService        IProcessPriorityService                        │
│  PerformanceEngineFacade                                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                           Domain & Feature Models                           │
│  Features/Graphics/Domain: Ue4SavEditor, UnrealCVarCodec, PubgVersionCatalog, │
│    SaveProfileReader, ShadowSettingsStore                                   │
│  Features/Shortcuts: ShortcutService (Application), ShortcutPreview (Domain)│
│  Features/Network: NetworkToolsService, DnsCatalog, IpadLayoutService,      │
│    IpadPresetCatalog                                                        │
│  Features/Optimizer/Application: TempCleanupService                         │
│  Features/Optimizer/Presentation: OptimizerDisplayFormatter                 │
│  Features/Security: DefenderExclusionService                                │
│  Configuration: TempCleanupOptions, IpadLayoutOptions, GameLoopOptions,     │
│    EmulatorOptions, UpdateOptions, AppConstants (identity only)              │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                        │ (Reads & Executes)
┌──────────────────────────────────────▼──────────────────────────────────────┐
│                        Shared Kernel & Infrastructure                       │
│  Shared/Contracts: OperationResult                                          │
│  Shared/Kernel: IFileSystem, IProcessRunner, IWorkRootProvider              │
│  Infrastructure/Files: PhysicalFileSystem, FileUtilities                    │
│  Infrastructure/Processes: ProcessRunner, ProcessText                       │
│  Infrastructure/Registry: RegistryService (IUserRegistry + IMachineRegistry)│
│  Infrastructure/GameLoop: GameLoopPathResolver (IGameLoopPathResolver),     │
│    GameLoopProcessEnumerator (the single Process.GetProcessesByName home),  │
│    GameLoopWorkRootProvider                                                 │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.1 Composition Root (`Bootstrap/ServiceCollectionExtensions`)
The application bootstraps its dependencies inside `App.OnStartup` via `Bootstrap/ServiceCollectionExtensions.AddNexoraServices` (`App.ConfigureServices` stays as a thin entry point for tests) using `Microsoft.Extensions.DependencyInjection`.

- **Singleton Services:** Heavyweight and state-coordinating components (`IProcessRunner`, `IUserRegistry` + `IMachineRegistry` both forwarded to one `RegistryService`, `IGameLoopProcessService`, `IGameLoopPathResolver`, `IAdbClient`, `IGameLoopConnection` + `IGraphicsProfileStore` (both forwarded to one `GameLoopService`), `IUpdateService`, `IEmulatorSettingsService`, `ITempCleanupService`, `IIpadLayoutService`, `INetworkToolsService`, `IShortcutService` (factory-built with the emulator asset root), `IGraphicsSettingsService`, `IGameLoopPerformanceEngine` (implemented by `PerformanceEngineFacade`), `IPageOperationBus` (the window-wide one-operation guard shared by the shell and every page ViewModel)).
- **Options Registration:** Strongly typed configuration records (`TempCleanupOptions`, `IpadLayoutOptions`, `GameLoopOptions`, `EmulatorOptions`, `UpdateOptions`) registered as singletons for dynamic customization. Services take them via nullable ctor params defaulting to `new()` (designer-safe fallback pattern; the shell's fallback graph lives in `Bootstrap/DesignerFallback.cs`); `AppConstants` retains only identity/version/validation.
- **No Windows-tools facade:** the former `SystemToolsFacade` (`IWindowsToolsService`) was split and deleted, and the shell finished shedding feature services in Phase 3 — `MainWindow` takes only three contracts (`IGameLoopPerformanceEngine` for the shutdown restore, `IUpdateService` for the update handoff, `GameLoopOptions` for timeouts). Every page owns its services via its ViewModel. `PerformanceEngineFacade` implements `IGameLoopPerformanceEngine` (performance optimization); don't reintroduce a coordinating facade without updating all registrations.
- **Transient Views:** `MainWindow` and all six page Views + ViewModels are registered as transient, resolved through the service provider, and displayed upon startup.
- **Controlled Teardown:** `App.OnExit` explicitly disposes the `ServiceProvider`, triggering graceful shutdown across active services.

### 1.2 Constructor Injection & Fallback Design
`MainWindow` strictly consumes dependencies via its primary constructor:
```csharp
public MainWindow(
    IGameLoopPerformanceEngine? performanceEngine = null,
    IUpdateService? updates = null,
    GameLoopOptions? gameLoopOptions = null)
```
Every View and ViewModel follows the same nullable-args-with-fallback shape: when invoked without parameters (such as in XAML designer tooling or isolated test harnesses), the container-resolved instance is used when available and sensible fallback instances are constructed otherwise, maintaining maximum testability without breaking XAML runtime constraints.

---

## 2. Dynamic Path Resolution Strategy

To guarantee flawless operation across non-standard, custom, or multi-drive environments (e.g., `D:\Games\TxGameAssistant`, `E:\GameLoop`, etc.), Nexora rejects hardcoded paths (e.g., `C:\Program Files\...`). It implements a robust, three-tier discovery pipeline:

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Dynamic Discovery Pipeline                        │
├─────────────────────────────────────────────────────────────────────────────┤
│ 1. Windows Registry (Primary)                                               │
│    HKLM\SOFTWARE\WOW6432Node\Tencent\MobileGamePC\UI                         │
│    HKLM\SOFTWARE\WOW6432Node\Tencent\MobileGamePC\AppMarket                 │
│    -> Reads "InstallPath" to identify UI and AppMarket root directories     │
├─────────────────────────────────────────────────────────────────────────────┤
│ 2. Running Process Inspection (Active Fallback)                             │
│    Enumerates EmulatorOptions.Emulator.RunningCheckProcessNames:             │
│    ["AndroidEmulatorEn", "AndroidEmulator", "AppMarket", "aow_exe", ...]    │
│    -> Inspects Process.MainModule.FileName                                  │
│    -> Resolves Directory.GetParent(...) to derive emulator base root        │
│    -> Hardened with try/catch to gracefully handle protected processes     │
├─────────────────────────────────────────────────────────────────────────────┤
│ 3. Active ProgramFiles Traversal (Last-Resort Heuristic)                    │
│    Checks Environment.SpecialFolder.ProgramFiles and ProgramFilesX86        │
│    -> Traverses TxGameAssistant/UI and TxGameAssistant/AppMarket            │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Implementation Across Subsystems
- **`GameLoopProcessService.GetGameLoopRoot()`**: Queries the `UI` registry hive; if unpopulated, inspects active emulator processes to discover the live root path; validates directory existence before returning.
- **`AdbClient.FindAdbPath()`**: Probes the bundled `Assets/adb.exe`, registry-discovered `InstallPath`, active emulator process parent directories, and `ProgramFiles` candidates.
- **`ShortcutService.CreateShortcut()`**: Resolves `AppMarket.exe` dynamically via `RegistryService` (falling back from `UI` to `AppMarket` branches), ensuring shortcut creation succeeds regardless of the installation drive.

---

## 3. Asynchronous Hygiene & Options Pattern

### 3.1 Async Standards & Cancellation Propagation
Nexora adheres strictly to non-blocking asynchronous programming:
- **No Blocking Sync Calls:** Public APIs never call `.Result` or `.Wait()`. All I/O, process executions, network probes, and ADB interactions utilize `async Task` or `async Task<T>`.
- **Deep Cancellation Token Propagation:** Every asynchronous method accepts a `CancellationToken` (e.g., `AdbClient.WaitForBootAsync`, `NetworkToolsService.PingDnsAsync`, `UpdateService.CheckAsync`).
- **Pre-Flight Cancellation Checks:** Long-running loops check `cancellationToken.ThrowIfCancellationRequested()` before spawning external processes or invoking sockets.
- **Controlled Timeouts:** Operations employ bounded `CancellationTokenSource` instances (e.g., 60-second live verification timeouts, 30-second process execution caps).

### 3.2 Shutdown Safety & Resource Hygiene
- **`ProcessPriorityService.StopMonitorAsync()`**: Guarantees a bounded monitor loop shutdown within 2 seconds without deadlocking the UI thread.
- **Window Teardown:** `MainWindow.Window_Closing` defers close under a bounded 5s `ShutdownRestoreTimeout`, restores power + priority via `RestorePerformanceSessionAsync`, then calls `Close()` in `finally`; it disposes connection/tool cancellation tokens and detaches `WindowChromeBehavior`.
- **Temporary Staging Isolation:** `UpdateService` creates per-attempt GUID staging trees (`%TEMP%/NexoraUpdate-<Guid>`) swept in a guaranteed `finally` block and purges stale directories older than 24 hours.

### 3.3 Strongly Typed Options
System behaviors are decoupled into strongly typed, injectable options classes located in `Nexora.Configuration`:

| Options Class | Configurable Properties | Default Values |
|---|---|---|
| `TempCleanupOptions` | `TargetDirectories`<br>`ShaderCacheFolderName` | User `%TEMP%`, `%WINDIR%\Temp`, `%WINDIR%\Prefetch`<br>`"ShaderCache"` |
| `IpadLayoutOptions` | `LayoutMapPath`<br>`KeymapDirectory`<br>`KeymapFileName`<br>`BackupExtension`<br>`LegacyBackupExtension` | `Assets/ipad_layout_map.json`<br>`%APPDATA%\AndroidTbox`<br>`"TVM_100.xml"`<br>`".nexora-backup"`<br>`".mkbackup"` (legacy read fallback, never written or deleted) |

---

## 4. Cyber-Minimalist Design System & UI Restructure

Nexora features an **Electric Cyan Cyber-Minimalist** visual design language, characterized by high-contrast dark slate surfaces, subtle cyan accents, and strict presentation separation.

### 4.1 Design System Tokens & Color Palette
Defined globally in `App.xaml`:

| Token | Value | Semantic Usage |
|---|---|---|
| `PageBackground` | `#0B1118` | Root application and viewport canvas |
| `SidebarBackground` | `#0A1017` | Navigation sidebar background |
| `PanelBackground` | `#111B24` | Primary card and tile containers |
| `PanelAltBackground` | `#17232D` | Secondary inset panels and headers |
| `BorderBrush` | `#2D3F4D` | Subtle borders for cards and inputs |
| `Accent` | `#00D2FF` | Electric Cyan accent for active states, indicators, and primary CTAs |
| `AccentSoft` | `#102534` | Soft cyan tint for selected toggle backgrounds |
| `TextPrimary` | `#F2EEE7` | Off-white primary text |
| `TextSecondary` | `#9DA8B2` | Slate gray secondary text and labels |
| `Success` | `#10B981` | Status pill connected state |
| `Danger` | `#EF4444` | Status pill disconnected / error states |
| `AccentGlowEffect` | `BlurRadius=14, Opacity=0.55` | DropShadow glow for active cyan highlights |

### 4.2 Reusable Controls & Components
- **Segmented Radio Buttons (`GraphicsSegmentButtonStyle`):** Modern segmented pill selectors for graphics options (Quality, FPS, Style) featuring cyan border highlight on hover and `AccentSoft` fill when checked.
- **Navigation Rail (`NavButtonStyle`):** Sidebar items featuring a vertical glowing 3.5px Electric Cyan rail indicator on the active page.
- **Metric & Style Cards (`GraphicsStyleCardButton`):** Toggable tiles for visual styles (Classic, Colorful, Realistic, Soft, Movie).
- **Dark ComboBox (`InputComboBoxStyle`):** Custom-styled WPF dropdown with rounded borders, hover glows, and scrollable dark popup lists.

### 4.3 Presentation & Layout Separation
To ensure clean code behind in `MainWindow.xaml.cs` (a 250-line navigation/lifecycle/chrome shell), layout and formatting responsibilities are decoupled:
- **`OptimizerDisplayFormatter` (`Features/Optimizer/Presentation`):** Pure functional static transformer. Takes raw hardware telemetry (`HardwareSnapshot`) and optimizer calculations (`OptimizerPlan`), formatting them into a display model (`OptimizerDisplayModel`) using invariant culture rules.
- **`ResponsiveLayoutManager` (`UI/Layout`):** Dynamically computes sidebar widths and page margins based on window width across defined breakpoints:
  - `TightBreakpoint` (< 1200px): Sidebar `228px`, Margins `24,20,24,18`
  - `CompactBreakpoint` (1200px - 1320px): Sidebar `240px`, Margins `32,22,32,20`
  - `StandardBreakpoint` (> 1320px): Sidebar `260px`, Margins `48,26,48,24`

---

## 5. Live Verification & Reliability Engine

Nexora contains a dedicated, non-destructive live verification suite ([`LiveGameLoopVerificationTests.cs`](./Nexora.Tests/Features/GameLoop/LiveGameLoopVerificationTests.cs)) executed directly against the running GameLoop emulator.

### 5.1 UE4 SaveGame Binary Parser (`Ue4SavEditor`)
- **Binary Header Matching:** Scans arbitrary `Active.sav` byte streams for Unreal Engine 4 `IntProperty` signature markers: `\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0`.
- **In-Place Modification:** Modifies target single-byte property values (FPS level, Battle Render Quality, Lobby Render Quality) in-place without altering stream length, preserving binary checksum integrity and preventing GameLoop SaveGame corruption.

### 5.2 Obfuscated CVar Codec (`UnrealCVarCodec`)
- **PUBG Mobile XOR-79 Obfuscation:** PUBG Mobile encrypts configuration console variables in `UserCustom.ini` under the `+CVars=` prefix using a static XOR cipher key (`0x79`).
- **Bidirectional Transformation:** `EncodeCVar` and `DecodeCVar` convert plain-text CVar definitions (e.g., `r.UserShadowSwitch=1`) to and from uppercase hex-encoded strings with culture-invariant hex parsing.
- **Shadow Presets:** `TryApplyShadowPreset` preserves file indentation while toggling dynamic object shadows, distance scaling, and cascade resolution.

### 5.3 Non-Destructive Registry & Power Session Management
- **`GameLoopRegistryOptimizer`:** Safely applies CPU core counts, memory size (`VMMemorySizeInMB`), DirectX/OpenGL renderers, and ADB flags in `HKCU\SOFTWARE\Tencent\MobileGamePC`. All live verification tests snapshot initial DWORD states and restore them in guaranteed `finally` blocks.
- **`EmulatorSettingsService` (manual Tuning page):** User-chosen counterpart to Smart Settings — processor, memory, DPI, render/global caches, discrete GPU (paired `GraphicsCardEnabled` + `SetGraphicsCard`), render optimization, V-Sync, ADB (inverted `AdbDisable` mapped in one place), and anti-aliasing. All ten value names live in `EmulatorTuningCatalog`; every write is clamped to real hardware and verified by read-back; Apply refuses while GameLoop runs (`"Close GameLoop before applying emulator tuning..."`). Full spec in `docs/archive/EMULATOR-TUNING-PLAN.md`.
- **`PowerSessionService`:** Activates Windows high-performance power plan GUIDs during gameplay sessions and safely restores the host machine's original active power plan upon session teardown.

### 5.4 iPad View Execution Guard
- **Concurrency Protection:** GameLoop maintains exclusive file locks on keymap XML files (`TVM_100.xml`) while running and overwrites registry resolution keys upon emulator exit.
- **Safety Guard:** `IpadLayoutService.SetIpadResolution` verifies active emulator processes via `GameLoopProcessService.FindGameLoopProcesses()`. If active, it safely rejects the operation:
  > *"Close GameLoop before applying iPad View (it locks keymap files and will overwrite your settings on exit), then apply it again."*
- **Atomic Backup & Rollback:** When applied with GameLoop closed, `IpadLayoutService` generates `TVM_100.xml.nexora-backup` prior to modifying button layout coordinates. Calling `.ResetIpadResolution()` restores the original XML file, deletes the backup, and rolls back registry resolutions; a legacy `TVM_100.xml.mkbackup` left by older versions still restores (and is never auto-deleted).

---

## 6. Verification & Test Suite Summary

- **Build Quality:** `dotnet build Nexora.slnx` → 0 Errors, 0 Warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- **Automated Test Suite:** `dotnet test Nexora.slnx --filter "Category!=LiveFunctionalVerification"` → **519 standard tests passing** (419 baseline at refactor start, plus page ViewModel suites, formatter/service suites, DI registration tests, and the `ArchitectureGuardTests` source guards below; the one recurring failure, `ProcessPriority_OfflineGameLoop_ReturnsSkipped_AndArmsMonitor`, is environmental — it fails identically on the clean tree when a leftover GameLoop/AppMarket process is running).
  - **Live GameLoop Verification:** five opt-in tests cover connection and diagnostics, graphics settings and SavEditor, Performance Center and hardware telemetry, DNS latency and iPad layout guards, and dynamic path resolution. Run them with `--filter "Category=LiveFunctionalVerification"` on a configured GameLoop machine.
  - **Security & Integrity Tests:** Authenticode signature validation, publisher pinning, GitHub domain verification, dual-format SHA-256 extraction, and untrusted update rejection.
  - **Defender Exclusion Trust Tests:** Registry-only path resolution, TxGameAssistant directory segment validation, and rejection of system directories and prefix spoofing.
  - **Shutdown Teardown Tests:** Bounded synchronous wait for session restoration, safe exception handling, and avoidance of process hangs or UI deadlocks.
  - **Async Hygiene Tests:** Cancellation token adherence, non-blocking asynchronous execution, and process priority monitor shutdown.
  - **Boundary Validation Tests:** Malformed Android package rejection, culture-invariant coordinate shifting, and invalid IP address handling.
  - **Service-Level Seam Tests:** Dedicated unit tests for `NvidiaOptimizerService`, `IpadLayoutService`, `EmulatorSettingsService` (manual Tuning page: load/apply/verify, hardware clamping, running-guard), `FileUtilities`, and `CatalogTests`.
  - **Dependency Injection Tests:** Composition Root resolution, transient view lifetimes, and mock service substitutability.
  - **Architecture Guard Tests:** Executable Phase 4 acceptance checks over the production sources — unique DI registrations, single-home process discovery, no blocking waits / `async void` outside WPF handlers, drive-letter-free sources, and Presentation purity (no direct registry/process/ADB/PowerShell calls from views or ViewModels).
