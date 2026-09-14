# Graph Report - Nexora PUBG Mobile Tool c#  (2026-09-14)

## Corpus Check
- 99 files · ~60,169 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1233 nodes · 2754 edges · 84 communities (64 shown, 18 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 357 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `0ca0c7cc`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .GameLoopProcessService_IsGameLoopPath_MatchesExpectedPaths
- Nexora.Shared.Kernel
- PerformanceExecutionReport
- AsyncHygieneTests
- HardwareDetectionService
- .BuildServiceProvider_ResolvesAllInterfacesSuccessfully
- UpdateInfo
- Window
- ProcessPriorityService
- Ue4SavEditor
- WindowChromeBehavior
- Application
- DnsCatalog
- RoutedEventArgs
- TempCleanupService
- GameLoopServiceLogicTests
- MainWindow
- .SettingRadioButton_Checked
- ProcessRunner
- What You Must Do When Invoked
- .Fail
- .Format
- UpdateReplacementTests
- AppConstants
- Border
- Nexora PUBG Mobile Tool — Architecture Specification
- OperationResult
- .StyleButton_Checked
- AppConstantsTests
- .TryDeleteDirectory
- Nexora.Tests.csproj
- UpdateService
- Grid
- RegistryService
- Nexora PUBG Mobile Tool
- GameLoopProcessService
- Ellipse
- .Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive
- .OptimizeForNvidia
- SidebarColumn
- IRegistryService
- KoreanResolutionPanel
- PART_Popup
- ShortcutIcon
- graphify reference: extra exports and benchmark
- .SendAsync
- graphify reference: query, path, explain
- .Build
- graphify reference: add a URL and watch a folder
- graphify reference: commit hook and native CLAUDE.md integration
- graphify reference: incremental update and cluster-only
- .Create
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- rules/graphify.md
- extraction-spec.md
- workflows/graphify.md
- IGameLoopPerformanceEngine
- .ConfigureServices
- UpdateIntegrityTests
- AdbCancellationTests
- .UpdateShadowFile
- .RefreshOptimizerProfileAsync
- .ConnectAsync
- .RunToolAsync
- GpuVendor
- GameLoopWorkingStorage
- .OptimizeGameLoopRegistry
- GameLoopService
- IGameLoopService
- IpadLayoutOptions
- IpadLayoutService
- GameLoopRegistryOptimizer
- .UpdateShortcutPreview
- .DnsComboBox_SelectionChanged
- .IsTrustedDownloadUrl_AcceptsTrustedGitHubHosts
- .ChangeIpadButton_Click
- .Ok
- .Format
- .SelectContent
- OperationCanceledException
- Skipped

## God Nodes (most connected - your core abstractions)
1. `Window` - 118 edges
2. `OperationResult` - 85 edges
3. `MainWindow` - 59 edges
4. `Nexora.Shared.Kernel` - 50 edges
5. `WindowsToolsService` - 48 edges
6. `ProcessRunner` - 48 edges
7. `GameLoopService` - 41 edges
8. `TextBlock` - 36 edges
9. `Nexora.Configuration` - 35 edges
10. `Nexora.Services` - 31 edges

## Surprising Connections (you probably didn't know these)
- `IpadLayoutService` --references--> `IpadLayoutOptions`  [EXTRACTED]
  Services/IpadLayoutService.cs → Configuration/IpadLayoutOptions.cs
- `GameLoopService` --references--> `GameLoopWorkingStorage`  [EXTRACTED]
  Services/GameLoopService.cs → Features/GameLoop/GameLoopWorkingStorage.cs
- `MainWindow` --inherits--> `Window`  [EXTRACTED]
  MainWindow.xaml.cs → MainWindow.xaml
- `MainWindow` --references--> `IGameLoopService`  [EXTRACTED]
  MainWindow.xaml.cs → Services/IGameLoopService.cs
- `MainWindow` --references--> `IWindowsToolsService`  [EXTRACTED]
  MainWindow.xaml.cs → Services/IWindowsToolsService.cs

## Import Cycles
- None detected.

## Communities (84 total, 18 thin omitted)

### Community 1 - "Nexora.Shared.Kernel"
Cohesion: 0.08
Nodes (15): Nexora.Configuration, Nexora.Features.GameLoop, Nexora.Tests.Configuration, Nexora.Services, Nexora.UI.Presentation, Nexora, Nexora.Tests.Performance, Nexora.Shared.Kernel (+7 more)

### Community 2 - "PerformanceExecutionReport"
Cohesion: 0.17
Nodes (10): IReadOnlyList, PerformanceExecutionReport, AppliedCount, FailedCount, Failures, SkippedCount, Steps, Succeeded (+2 more)

### Community 3 - "AsyncHygieneTests"
Cohesion: 0.30
Nodes (6): Fact, OperationCanceledException, Task, AsyncHygieneTests, CancellationToken, Task

### Community 4 - "HardwareDetectionService"
Cohesion: 0.23
Nodes (8): DevMode, DllImport, HardwareSnapshot, JsonElement, DevMode, HardwareDetectionService, SystemPowerStatus, SystemPowerStatus

### Community 5 - ".BuildServiceProvider_ResolvesAllInterfacesSuccessfully"
Cohesion: 0.05
Nodes (31): ArgumentException, IAdbClient, Fact, InlineData, Task, Theory, BoundaryValidationTests, CancellationToken (+23 more)

### Community 6 - "UpdateInfo"
Cohesion: 0.27
Nodes (5): Fact, Task, TemporaryFileHygieneTests, TimeSpan, UpdateInfo

### Community 7 - "Window"
Cohesion: 0.09
Nodes (41): ActualHeight, ActualWidth, Foreground, ArchitectureText, ConnectionDetail, ConnectionTitle, DnsStatusText, HardwareCpuText (+33 more)

### Community 8 - "ProcessPriorityService"
Cohesion: 0.12
Nodes (17): AccessDenied, AlreadyHigh, Candidates, Changed, Fact, Task, TimeSpan, ProcessPriorityShutdownTests (+9 more)

### Community 9 - "Ue4SavEditor"
Cohesion: 0.25
Nodes (5): Ue4SavEditor, RawBuffer, ArgumentNullException, Fact, Ue4SavEditorTests

### Community 10 - "WindowChromeBehavior"
Cohesion: 0.11
Nodes (19): Nexora.UI.Behaviors, EventArgs, HwndSource, HwndSourceHook, IntPtr, MarshalAs, MinMaxInfo, MonitorInfo (+11 more)

### Community 11 - "Application"
Cohesion: 0.15
Nodes (20): ActiveRail, Application, ButtonBorder, CardBorder, CloseBorder, ComboBorder, CompactBorder, ControlBorder (+12 more)

### Community 12 - "DnsCatalog"
Cohesion: 0.09
Nodes (20): Nexora.Tests.Catalogs, Nexora.Features.Layout, Nexora.Features.SystemTools.Network, IReadOnlyList, IpadPresetCatalog, Presets, IpadResolutionPreset, Details (+12 more)

### Community 13 - "RoutedEventArgs"
Cohesion: 0.12
Nodes (13): ChangeDnsButton, CloseWindowButton, ConnectButton, CreateShortcutButton, ForceCloseButton, MaximizeWindowButton, MinimizeWindowButton, RefreshConnectionButton (+5 more)

### Community 14 - "TempCleanupService"
Cohesion: 0.16
Nodes (11): IReadOnlyList, TempCleanupOptions, ShaderCacheFolderName, TargetDirectories, ICollection, ITempCleanupService, Fact, OptionsTests (+3 more)

### Community 15 - "GameLoopServiceLogicTests"
Cohesion: 0.22
Nodes (5): Fact, InlineData, Theory, GameLoopServiceLogicTests, Type

### Community 16 - "MainWindow"
Cohesion: 0.18
Nodes (9): CancelEventArgs, ApplyButton, CancellationToken, CancellationTokenSource, IDisposable, IUpdateService, PubgVersion, Task (+1 more)

### Community 17 - ".SettingRadioButton_Checked"
Cohesion: 0.22
Nodes (16): BalancedButton, ExtremeButton, Fps120Button, Fps90Button, HdButton, HdrButton, HighButton, LowButton (+8 more)

### Community 18 - "ProcessRunner"
Cohesion: 0.29
Nodes (5): Fact, WindowsToolsExtractionTests, Fact, WindowsToolsServiceTests, ProcessRunner

### Community 19 - "What You Must Do When Invoked"
Cohesion: 0.08
Nodes (24): For /graphify add and --watch, For /graphify query, For the commit hook and native CLAUDE.md integration, For --update and --cluster-only, /graphify, Honesty Rules, Interpreter guard for subcommands, Part A - Structural extraction for code files (+16 more)

### Community 20 - ".Fail"
Cohesion: 0.17
Nodes (9): Guid, IEnumerable, TimeSpan, HardwareSnapshot, PowerSessionService, IEnumerable, TimeSpan, ProcessResult (+1 more)

### Community 21 - ".Format"
Cohesion: 0.09
Nodes (17): Nexora.Tests.UI, Nexora.Features.Performance, Nexora.UI.Layout, HardwareSnapshot, Architecture, HasDedicatedGpu, OptimizerPlan, Fact (+9 more)

### Community 22 - "UpdateReplacementTests"
Cohesion: 0.14
Nodes (11): HttpClient, Fact, InlineData, Task, Theory, MockHttpClient, MockProcessRunner, LastArguments (+3 more)

### Community 23 - "AppConstants"
Cohesion: 0.17
Nodes (11): TimeSpan, Adb, AppConstants, Assets, Emulator, Registry, Timeouts, Tools (+3 more)

### Community 24 - "Border"
Cohesion: 0.17
Nodes (12): ButtonBorder, CardBorder, ComboBorder, CtaBorder, DangerBorder, HoverOverlay, ItemBorder, SegmentBorder (+4 more)

### Community 25 - "Nexora PUBG Mobile Tool — Architecture Specification"
Cohesion: 0.10
Nodes (20): 1.1 Composition Root (`App.xaml.cs`), 1.2 Constructor Injection & Fallback Design, 1. High-Level Architecture & Design Patterns, 2.1 Implementation Across Subsystems, 2. Dynamic Path Resolution Strategy, 3.1 Async Standards & Cancellation Propagation, 3.2 Shutdown Safety & Resource Hygiene, 3.3 Strongly Typed Options (+12 more)

### Community 26 - "OperationResult"
Cohesion: 0.06
Nodes (28): Fact, Task, ShutdownRestorationTests, IIpadLayoutService, CancellationToken, Task, ITempCleanupService, CancellationToken (+20 more)

### Community 27 - ".StyleButton_Checked"
Cohesion: 0.29
Nodes (9): IsDropDownOpen, ClassicButton, ColorfulButton, DropDownToggleButton, KoreanFullHdButton, MovieButton, RealisticButton, SoftButton (+1 more)

### Community 29 - ".TryDeleteDirectory"
Cohesion: 0.36
Nodes (3): Fact, FileUtilitiesTests, FileUtilities

### Community 30 - "Nexora.Tests.csproj"
Cohesion: 0.18
Nodes (9): net8.0-windows, Microsoft.NET.Sdk, net8.0-windows, Microsoft.NET.Sdk, FluentAssertions (6.12.0), Microsoft.Extensions.DependencyInjection (8.0.0), Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2) (+1 more)

### Community 31 - "UpdateService"
Cohesion: 0.23
Nodes (8): HashSet, IUpdateService, CancellationToken, HttpClient, Task, UpdateService, ZipArchive, ZipArchiveEntry

### Community 32 - "Grid"
Cohesion: 0.25
Nodes (8): AboutView, GraphicsView, MainLayoutGrid, NetworkView, OptimizerView, ShortcutsView, TitleBarGrid, Grid

### Community 33 - "RegistryService"
Cohesion: 0.18
Nodes (4): Fact, RegistryServiceExtensionsTests, ShortcutService, RegistryService

### Community 34 - "Nexora PUBG Mobile Tool"
Cohesion: 0.15
Nodes (12): Design principles, Development, Dynamic Path Resolution, License, Nexora PUBG Mobile Tool, Operational notes, Project layout, Release package (+4 more)

### Community 35 - "GameLoopProcessService"
Cohesion: 0.19
Nodes (8): Fact, InlineData, Theory, DefenderExclusionTrustTests, RegistryService, DefenderExclusionService, RegistryService, GameLoopProcessService

### Community 36 - "Ellipse"
Cohesion: 0.50
Nodes (4): ConnectionDot, SidebarConnectionDot, TopConnectionDot, Ellipse

### Community 37 - ".Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive"
Cohesion: 0.32
Nodes (8): Dictionary, ITestOutputHelper, Fact, IAdbClient, INetworkToolsService, Task, LiveGameLoopVerificationTests, Trait

### Community 38 - ".OptimizeForNvidia"
Cohesion: 0.24
Nodes (6): ArgumentNullException, Fact, NvidiaOptimizerServiceTests, List, RegistryService, NvidiaOptimizerService

### Community 39 - "SidebarColumn"
Cohesion: 0.67
Nodes (3): SidebarColumn, TitleBrandColumn, ColumnDefinition

### Community 46 - "graphify reference: extra exports and benchmark"
Cohesion: 0.22
Nodes (8): graphify reference: extra exports and benchmark, Step 6b - Wiki (only if --wiki flag), Step 7 - Neo4j export (only if --neo4j or --neo4j-push flag), Step 7a - FalkorDB export (only if --falkordb or --falkordb-push flag), Step 7b - SVG export (only if --svg flag), Step 7c - GraphML export (only if --graphml flag), Step 7d - MCP server (only if --mcp flag), Step 8 - Token reduction benchmark (only if total_words > 5000)

### Community 47 - ".SendAsync"
Cohesion: 0.33
Nodes (5): HttpMessageHandler, HttpRequestMessage, HttpResponseMessage, CancellationToken, MockHttpMessageHandler

### Community 48 - "graphify reference: query, path, explain"
Cohesion: 0.33
Nodes (5): For /graphify explain, For /graphify path, graphify reference: query, path, explain, Step 0 — Constrained query expansion (REQUIRED before traversal), Step 1 — Traversal

### Community 49 - ".Build"
Cohesion: 0.23
Nodes (7): Fact, InlineData, Theory, PerformancePlanBuilderTests, HardwareSnapshot, OptimizerPlan, PerformancePlanBuilder

### Community 50 - "graphify reference: add a URL and watch a folder"
Cohesion: 0.50
Nodes (3): For /graphify add, For --watch, graphify reference: add a URL and watch a folder

### Community 51 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 52 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 59 - "IGameLoopPerformanceEngine"
Cohesion: 0.17
Nodes (7): PerformanceSessionButton, RestoreSessionButton, CancellationToken, HardwareSnapshot, OptimizerPlan, Task, IGameLoopPerformanceEngine

### Community 60 - ".ConfigureServices"
Cohesion: 0.12
Nodes (15): IAdbClient, IIpadLayoutService, INetworkToolsService, ITempCleanupService, IUpdateService, NetworkToolsService, RegistryService, App (+7 more)

### Community 61 - "UpdateIntegrityTests"
Cohesion: 0.26
Nodes (3): Fact, Task, UpdateIntegrityTests

### Community 62 - "AdbCancellationTests"
Cohesion: 0.42
Nodes (5): CancellationToken, Fact, Task, AdbCancellationTests, OperationCanceledException

### Community 63 - ".UpdateShadowFile"
Cohesion: 0.22
Nodes (4): IReadOnlyDictionary, UnrealCVarCodec, InlineData, Theory

### Community 64 - ".RefreshOptimizerProfileAsync"
Cohesion: 0.18
Nodes (4): AllRecommendedButton, SmartSettingsButton, HardwareSnapshot, OptimizerPlan

### Community 65 - ".ConnectAsync"
Cohesion: 0.18
Nodes (8): ConnectionResult, PubgVersion, CancellationToken, IEnumerable, IReadOnlyList, Task, IAdbClient, DeviceSerial

### Community 66 - ".RunToolAsync"
Cohesion: 0.20
Nodes (5): Button, Func, GameLoopOptimizerButton, TempCleanerButton, TextBlock

### Community 67 - "GpuVendor"
Cohesion: 0.25
Nodes (7): InlineData, Theory, GpuVendor, Amd, Intel, Nvidia, Unknown

### Community 68 - "GameLoopWorkingStorage"
Cohesion: 0.20
Nodes (8): GameLoopWorkingStorage, AssetRoot, ConnectionProbePath, KoreanResolutionAssetPath, PendingSavPath, PreviousSavPath, ShadowSettingsPath, WorkRoot

### Community 69 - ".OptimizeGameLoopRegistry"
Cohesion: 0.31
Nodes (3): IEnumerable, RegistryService, GpuRoutingService

### Community 70 - "GameLoopService"
Cohesion: 0.17
Nodes (9): CancellationToken, GraphicsSelection, IAdbClient, IReadOnlyDictionary, Task, GameLoopService, CurrentPackage, IsConnected (+1 more)

### Community 71 - "IGameLoopService"
Cohesion: 0.13
Nodes (8): CancellationToken, ConnectionResult, GraphicsSelection, Task, IGameLoopService, CurrentPackage, IsConnected, IsGameLoopConnected

### Community 72 - "IpadLayoutOptions"
Cohesion: 0.24
Nodes (7): IpadLayoutOptions, BackupExtension, KeymapDirectory, KeymapFileName, LayoutMapPath, Fact, IpadLayoutServiceTests

### Community 73 - "IpadLayoutService"
Cohesion: 0.33
Nodes (7): IIpadLayoutService, PointPair, JsonElement, List, IpadLayoutService, PointPair, XElement

### Community 74 - "GameLoopRegistryOptimizer"
Cohesion: 0.39
Nodes (4): HardwareSnapshot, OptimizerPlan, RegistryService, GameLoopRegistryOptimizer

### Community 75 - ".UpdateShortcutPreview"
Cohesion: 0.33
Nodes (3): BitmapImage, Nexora.UI.Helpers, IconImageLoader

### Community 76 - ".DnsComboBox_SelectionChanged"
Cohesion: 0.29
Nodes (5): DnsComboBox, IpadComboBox, PubgVersionComboBox, ShortcutComboBox, ComboBox

### Community 78 - ".ChangeIpadButton_Click"
Cohesion: 0.29
Nodes (3): IpadResolutionPreset, ChangeIpadButton, SelectionChangedEventArgs

## Knowledge Gaps
- **147 isolated node(s):** `graphify`, `Usage`, `What graphify is for`, `Step 0 - GitHub repos and multi-path merge (only if a URL or several paths)`, `Step 1 - Ensure graphify is installed` (+142 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 298 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **18 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `.RefreshOptimizerProfileAsync`, `Nexora.Shared.Kernel`, `.RunToolAsync`, `Window`, `.StyleButton_Checked`, `IGameLoopService`, `WindowChromeBehavior`, `.UpdateShortcutPreview`, `.DnsComboBox_SelectionChanged`, `RoutedEventArgs`, `.ChangeIpadButton_Click`, `.SelectContent`, `.SettingRadioButton_Checked`, `.Format`, `OperationResult`, `IGameLoopPerformanceEngine`, `.ConfigureServices`?**
  _High betweenness centrality (0.260) - this node is a cross-community bridge._
- **Why does `Window` connect `Window` to `Grid`, `.RefreshOptimizerProfileAsync`, `.RunToolAsync`, `Ellipse`, `SidebarColumn`, `KoreanResolutionPanel`, `PART_Popup`, `IGameLoopPerformanceEngine`, `.DnsComboBox_SelectionChanged`, `RoutedEventArgs`, `.ChangeIpadButton_Click`, `ShortcutIcon`, `MainWindow`, `.SettingRadioButton_Checked`, `WindowChromeBehavior`, `.Format`, `Border`, `.StyleButton_Checked`?**
  _High betweenness centrality (0.151) - this node is a cross-community bridge._
- **Why does `OperationResult` connect `OperationResult` to `PerformanceExecutionReport`, `.BuildServiceProvider_ResolvesAllInterfacesSuccessfully`, `ProcessPriorityService`, `TempCleanupService`, `GameLoopServiceLogicTests`, `.Fail`, `UpdateService`, `GameLoopProcessService`, `.OptimizeForNvidia`, `IRegistryService`, `.Create`, `IGameLoopPerformanceEngine`, `.UpdateShadowFile`, `.RefreshOptimizerProfileAsync`, `.RunToolAsync`, `.OptimizeGameLoopRegistry`, `GameLoopService`, `IGameLoopService`, `GameLoopRegistryOptimizer`, `.Ok`, `.Format`?**
  _High betweenness centrality (0.116) - this node is a cross-community bridge._
- **Are the 8 inferred relationships involving `WindowsToolsService` (e.g. with `.RestorePerformanceSessionAsync_CanBeSynchronouslyAwaited_WithTimeout()` and `.RestorePerformanceSessionAsync_CompletesPromptly()`) actually correct?**
  _`WindowsToolsService` has 8 INFERRED edges - model-reasoned connections that need verification._
- **What connects `graphify`, `Usage`, `What graphify is for` to the rest of the system?**
  _147 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Nexora.Shared.Kernel` be split into smaller, more focused modules?**
  _Cohesion score 0.08281573498964803 - nodes in this community are weakly interconnected._
- **Should `.BuildServiceProvider_ResolvesAllInterfacesSuccessfully` be split into smaller, more focused modules?**
  _Cohesion score 0.050078247261345854 - nodes in this community are weakly interconnected._