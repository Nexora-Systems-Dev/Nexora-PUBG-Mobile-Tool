# Graph Report - Nexora PUBG Mobile Tool c#  (2026-09-14)

## Corpus Check
- 97 files · ~58,828 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1140 nodes · 2684 edges · 80 communities (62 shown, 15 thin omitted)
- Extraction: 85% EXTRACTED · 15% INFERRED · 0% AMBIGUOUS · INFERRED: 396 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `d3bcb82c`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- GameLoopProcessService
- Nexora.Shared.Kernel
- GameLoopService
- AsyncHygieneTests
- HardwareSnapshot
- AdbClient
- TemporaryFileHygieneTests
- Window
- ProcessPriorityService
- Ue4SavEditor
- WindowChromeBehavior
- Application
- DnsCatalog
- Button
- TempCleanupOptions
- GameLoopServiceLogicTests
- MainWindow
- .SettingRadioButton_Checked
- ProcessRunner
- What You Must Do When Invoked
- .Fail
- .Window_SizeChanged
- UpdateReplacementTests
- AppConstants
- Border
- Nexora PUBG Mobile Tool — Architecture Specification
- OperationResult
- .StyleButton_Checked
- AppConstantsTests
- .TryDeleteDirectory
- Nexora.Tests
- UpdateService
- Grid
- RegistryService
- Nexora PUBG Mobile Tool
- .AddDefenderExclusion
- Ellipse
- .Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive
- NvidiaOptimizerService
- SidebarColumn
- .Apply
- KoreanResolutionPanel
- PART_Popup
- ShortcutIcon
- graphify reference: extra exports and benchmark
- UpdateInfo
- graphify reference: query, path, explain
- NetworkToolsService
- graphify reference: add a URL and watch a folder
- graphify reference: commit hook and native CLAUDE.md integration
- graphify reference: incremental update and cluster-only
- Nexora
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- rules/graphify.md
- extraction-spec.md
- workflows/graphify.md
- IGameLoopPerformanceEngine
- .ConfigureServices
- UpdateIntegrityTests
- BoundaryValidationTests
- .UpdateShadowFile
- RoutedEventArgs
- .ConnectAsync
- .RunToolAsync
- .BuildServiceProvider_ResolvesAllInterfacesSuccessfully
- GameLoopWorkingStorage
- IRegistryService
- .ApplyKoreanFullHdAsync
- IGameLoopService
- IpadLayoutOptions
- IpadLayoutService
- .TryLoadIcon
- ComboBox
- .IsTrustedDownloadUrl_AcceptsTrustedGitHubHosts
- ConnectionResult
- .MaximizeButton_Click

## God Nodes (most connected - your core abstractions)
1. `Window` - 117 edges
2. `OperationResult` - 78 edges
3. `MainWindow` - 58 edges
4. `RegistryService` - 51 edges
5. `WindowsToolsService` - 48 edges
6. `Nexora.Shared.Kernel` - 48 edges
7. `ProcessRunner` - 47 edges
8. `GameLoopService` - 41 edges
9. `Nexora.Configuration` - 35 edges
10. `TextBlock` - 35 edges

## Surprising Connections (you probably didn't know these)
- `IpadLayoutService` --references--> `IpadLayoutOptions`  [EXTRACTED]
  Services/IpadLayoutService.cs → Configuration/IpadLayoutOptions.cs
- `TempCleanupService` --references--> `TempCleanupOptions`  [EXTRACTED]
  Services/TempCleanupService.cs → Configuration/TempCleanupOptions.cs
- `GameLoopService` --references--> `GameLoopWorkingStorage`  [EXTRACTED]
  Services/GameLoopService.cs → Features/GameLoop/GameLoopWorkingStorage.cs
- `MainWindow` --inherits--> `Window`  [EXTRACTED]
  MainWindow.xaml.cs → MainWindow.xaml
- `MainWindow` --references--> `IGameLoopService`  [EXTRACTED]
  MainWindow.xaml.cs → Services/IGameLoopService.cs

## Import Cycles
- None detected.

## Communities (80 total, 15 thin omitted)

### Community 0 - "GameLoopProcessService"
Cohesion: 0.18
Nodes (7): Fact, InlineData, Theory, WindowsToolsServiceTests, List, Process, GameLoopProcessService

### Community 1 - "Nexora.Shared.Kernel"
Cohesion: 0.09
Nodes (13): Nexora.Configuration, Nexora.Features.GameLoop, Nexora.Tests.Configuration, Nexora.Services, Nexora.UI.Presentation, Nexora, Nexora.Tests.Performance, Nexora.Features.Performance (+5 more)

### Community 2 - "GameLoopService"
Cohesion: 0.19
Nodes (5): IReadOnlyDictionary, GameLoopService, CurrentPackage, IsConnected, IsGameLoopConnected

### Community 3 - "AsyncHygieneTests"
Cohesion: 0.42
Nodes (4): Fact, OperationCanceledException, Task, AsyncHygieneTests

### Community 4 - "HardwareSnapshot"
Cohesion: 0.09
Nodes (22): DevMode, HardwareSnapshot, Architecture, HasDedicatedGpu, OptimizerPlan, Fact, InlineData, Theory (+14 more)

### Community 5 - "AdbClient"
Cohesion: 0.08
Nodes (23): ArgumentException, CancellationToken, Fact, OperationCanceledException, Task, AdbCancellationTests, IEnumerable, TimeSpan (+15 more)

### Community 6 - "TemporaryFileHygieneTests"
Cohesion: 0.29
Nodes (4): Fact, Task, TemporaryFileHygieneTests, TimeSpan

### Community 7 - "Window"
Cohesion: 0.09
Nodes (40): ActualHeight, Foreground, ArchitectureText, ConnectionDetail, ConnectionTitle, DnsStatusText, HardwareCpuText, HardwareDisplayText (+32 more)

### Community 8 - "ProcessPriorityService"
Cohesion: 0.12
Nodes (17): AlreadyHigh, Candidates, Changed, Fact, Task, TimeSpan, ProcessPriorityShutdownTests, ProcessPriorityClass (+9 more)

### Community 9 - "Ue4SavEditor"
Cohesion: 0.21
Nodes (5): Ue4SavEditor, RawBuffer, ArgumentNullException, Fact, Ue4SavEditorTests

### Community 10 - "WindowChromeBehavior"
Cohesion: 0.11
Nodes (19): Nexora.UI.Behaviors, EventArgs, HwndSource, HwndSourceHook, IntPtr, MarshalAs, MinMaxInfo, MonitorInfo (+11 more)

### Community 11 - "Application"
Cohesion: 0.10
Nodes (26): ActiveRail, Application, ButtonBorder, CardBorder, CloseBorder, ComboBorder, CompactBorder, ControlBorder (+18 more)

### Community 12 - "DnsCatalog"
Cohesion: 0.09
Nodes (19): Nexora.Tests.Catalogs, Nexora.Features.Layout, IReadOnlyList, IpadPresetCatalog, Presets, IpadResolutionPreset, Details, DisplayName (+11 more)

### Community 13 - "Button"
Cohesion: 0.20
Nodes (7): ChangeIpadButton, CloseWindowButton, ConnectButton, MinimizeWindowButton, RefreshConnectionButton, ResetIpadButton, Button

### Community 14 - "TempCleanupOptions"
Cohesion: 0.26
Nodes (6): IReadOnlyList, TempCleanupOptions, ShaderCacheFolderName, TargetDirectories, Fact, OptionsTests

### Community 15 - "GameLoopServiceLogicTests"
Cohesion: 0.22
Nodes (5): Fact, InlineData, Theory, GameLoopServiceLogicTests, Type

### Community 16 - "MainWindow"
Cohesion: 0.13
Nodes (10): CancelEventArgs, ApplyButton, CancellationToken, CancellationTokenSource, IDisposable, IEnumerable, Task, MainWindow (+2 more)

### Community 17 - ".SettingRadioButton_Checked"
Cohesion: 0.22
Nodes (16): BalancedButton, ExtremeButton, Fps120Button, Fps90Button, HdButton, HdrButton, HighButton, LowButton (+8 more)

### Community 18 - "ProcessRunner"
Cohesion: 0.43
Nodes (4): Fact, WindowsToolsExtractionTests, ShortcutService, ProcessRunner

### Community 19 - "What You Must Do When Invoked"
Cohesion: 0.08
Nodes (24): For /graphify add and --watch, For /graphify query, For the commit hook and native CLAUDE.md integration, For --update and --cluster-only, /graphify, Honesty Rules, Interpreter guard for subcommands, Part A - Structural extraction for code files (+16 more)

### Community 21 - ".Window_SizeChanged"
Cohesion: 0.19
Nodes (8): Nexora.Tests.UI, Nexora.UI.Layout, InlineData, Theory, ResponsiveLayoutManagerTests, SizeChangedEventArgs, Thickness, ResponsiveLayoutManager

### Community 22 - "UpdateReplacementTests"
Cohesion: 0.24
Nodes (4): Fact, InlineData, Theory, UpdateReplacementTests

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
Nodes (25): Guid, Fact, PerformanceExecutionReportTests, Fact, Task, ShutdownRestorationTests, CancellationToken, Task (+17 more)

### Community 27 - ".StyleButton_Checked"
Cohesion: 0.29
Nodes (9): ClassicButton, ColorfulButton, DropDownToggleButton, KoreanFullHdButton, MovieButton, RealisticButton, SoftButton, IsDropDownOpen (+1 more)

### Community 29 - ".TryDeleteDirectory"
Cohesion: 0.36
Nodes (3): Fact, FileUtilitiesTests, FileUtilities

### Community 30 - "Nexora.Tests"
Cohesion: 0.29
Nodes (7): Nexora.Tests, net8.0-windows, Microsoft.NET.Sdk, FluentAssertions (6.12.0), Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2)

### Community 31 - "UpdateService"
Cohesion: 0.23
Nodes (8): HashSet, HttpRequestMessage, CancellationToken, HttpClient, Task, UpdateService, ZipArchive, ZipArchiveEntry

### Community 32 - "Grid"
Cohesion: 0.25
Nodes (8): AboutView, GraphicsView, MainLayoutGrid, NetworkView, OptimizerView, ShortcutsView, TitleBarGrid, Grid

### Community 33 - "RegistryService"
Cohesion: 0.23
Nodes (6): Fact, RegistryServiceExtensionsTests, GameLoopRegistryOptimizer, IEnumerable, GpuRoutingService, RegistryService

### Community 34 - "Nexora PUBG Mobile Tool"
Cohesion: 0.15
Nodes (12): Design principles, Development, Dynamic Path Resolution, License, Nexora PUBG Mobile Tool, Operational notes, Project layout, Release package (+4 more)

### Community 35 - ".AddDefenderExclusion"
Cohesion: 0.22
Nodes (6): Fact, InlineData, Theory, DefenderExclusionTrustTests, DefenderExclusionService, ProcessText

### Community 36 - "Ellipse"
Cohesion: 0.50
Nodes (4): ConnectionDot, SidebarConnectionDot, TopConnectionDot, Ellipse

### Community 37 - ".Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive"
Cohesion: 0.42
Nodes (6): Dictionary, ITestOutputHelper, Fact, Task, LiveGameLoopVerificationTests, Trait

### Community 38 - "NvidiaOptimizerService"
Cohesion: 0.42
Nodes (4): ArgumentNullException, Fact, NvidiaOptimizerServiceTests, NvidiaOptimizerService

### Community 39 - "SidebarColumn"
Cohesion: 0.67
Nodes (3): SidebarColumn, TitleBrandColumn, ColumnDefinition

### Community 46 - "graphify reference: extra exports and benchmark"
Cohesion: 0.22
Nodes (8): graphify reference: extra exports and benchmark, Step 6b - Wiki (only if --wiki flag), Step 7 - Neo4j export (only if --neo4j or --neo4j-push flag), Step 7a - FalkorDB export (only if --falkordb or --falkordb-push flag), Step 7b - SVG export (only if --svg flag), Step 7c - GraphML export (only if --graphml flag), Step 7d - MCP server (only if --mcp flag), Step 8 - Token reduction benchmark (only if total_words > 5000)

### Community 47 - "UpdateInfo"
Cohesion: 0.17
Nodes (10): HttpClient, HttpMessageHandler, HttpResponseMessage, CancellationToken, Task, MockHttpClient, MockHttpMessageHandler, CancellationToken (+2 more)

### Community 48 - "graphify reference: query, path, explain"
Cohesion: 0.33
Nodes (5): For /graphify explain, For /graphify path, graphify reference: query, path, explain, Step 0 — Constrained query expansion (REQUIRED before traversal), Step 1 — Traversal

### Community 49 - "NetworkToolsService"
Cohesion: 0.17
Nodes (6): CancellationToken, Task, INetworkToolsService, CancellationToken, Task, NetworkToolsService

### Community 50 - "graphify reference: add a URL and watch a folder"
Cohesion: 0.50
Nodes (3): For /graphify add, For --watch, graphify reference: add a URL and watch a folder

### Community 51 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 52 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 53 - "Nexora"
Cohesion: 0.50
Nodes (4): net8.0-windows, Nexora, Microsoft.NET.Sdk, Microsoft.Extensions.DependencyInjection (8.0.0)

### Community 59 - "IGameLoopPerformanceEngine"
Cohesion: 0.15
Nodes (6): GameLoopOptimizerButton, PerformanceSessionButton, RestoreSessionButton, CancellationToken, Task, IGameLoopPerformanceEngine

### Community 60 - ".ConfigureServices"
Cohesion: 0.21
Nodes (8): ICollection, IServiceCollection, CancellationToken, Task, ITempCleanupService, CancellationToken, Task, TempCleanupService

### Community 61 - "UpdateIntegrityTests"
Cohesion: 0.26
Nodes (3): Fact, Task, UpdateIntegrityTests

### Community 62 - "BoundaryValidationTests"
Cohesion: 0.26
Nodes (5): Fact, InlineData, Task, Theory, BoundaryValidationTests

### Community 63 - ".UpdateShadowFile"
Cohesion: 0.22
Nodes (4): IReadOnlyDictionary, UnrealCVarCodec, InlineData, Theory

### Community 64 - "RoutedEventArgs"
Cohesion: 0.19
Nodes (4): AllRecommendedButton, RefreshOptimizerButton, SmartSettingsButton, RoutedEventArgs

### Community 65 - ".ConnectAsync"
Cohesion: 0.26
Nodes (7): Task, CancellationToken, IEnumerable, IReadOnlyList, Task, IAdbClient, DeviceSerial

### Community 66 - ".RunToolAsync"
Cohesion: 0.17
Nodes (7): Button, Func, ChangeDnsButton, CreateShortcutButton, ForceCloseButton, TempCleanerButton, TextBlock

### Community 67 - ".BuildServiceProvider_ResolvesAllInterfacesSuccessfully"
Cohesion: 0.29
Nodes (6): CancellationToken, Fact, Task, DependencyInjectionTests, StubUpdateService, IUpdateService

### Community 68 - "GameLoopWorkingStorage"
Cohesion: 0.20
Nodes (8): GameLoopWorkingStorage, AssetRoot, ConnectionProbePath, KoreanResolutionAssetPath, PendingSavPath, PreviousSavPath, ShadowSettingsPath, WorkRoot

### Community 71 - "IGameLoopService"
Cohesion: 0.25
Nodes (6): CancellationToken, Task, IGameLoopService, CurrentPackage, IsConnected, IsGameLoopConnected

### Community 72 - "IpadLayoutOptions"
Cohesion: 0.24
Nodes (7): IpadLayoutOptions, BackupExtension, KeymapDirectory, KeymapFileName, LayoutMapPath, Fact, IpadLayoutServiceTests

### Community 73 - "IpadLayoutService"
Cohesion: 0.38
Nodes (6): PointPair, JsonElement, List, IpadLayoutService, PointPair, XElement

### Community 75 - ".TryLoadIcon"
Cohesion: 0.40
Nodes (3): BitmapImage, Nexora.UI.Helpers, IconImageLoader

### Community 76 - "ComboBox"
Cohesion: 0.40
Nodes (5): DnsComboBox, IpadComboBox, PubgVersionComboBox, ShortcutComboBox, ComboBox

### Community 78 - "ConnectionResult"
Cohesion: 0.67
Nodes (3): IReadOnlyList, ConnectionResult, PubgVersion

## Knowledge Gaps
- **135 isolated node(s):** `ToggleButton`, `IsDropDownOpen`, `Popup`, `ActualWidth`, `Services` (+130 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 244 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **15 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `RoutedEventArgs`, `Nexora.Shared.Kernel`, `.RunToolAsync`, `.BuildServiceProvider_ResolvesAllInterfacesSuccessfully`, `HardwareSnapshot`, `Window`, `.StyleButton_Checked`, `IGameLoopService`, `WindowChromeBehavior`, `Application`, `Button`, `.MaximizeButton_Click`, `.SettingRadioButton_Checked`, `.Window_SizeChanged`, `OperationResult`, `IGameLoopPerformanceEngine`, `.ConfigureServices`?**
  _High betweenness centrality (0.260) - this node is a cross-community bridge._
- **Why does `Window` connect `Window` to `Grid`, `RoutedEventArgs`, `.RunToolAsync`, `Ellipse`, `SidebarColumn`, `IGameLoopPerformanceEngine`, `KoreanResolutionPanel`, `PART_Popup`, `ComboBox`, `Button`, `ShortcutIcon`, `.MaximizeButton_Click`, `MainWindow`, `.SettingRadioButton_Checked`, `WindowChromeBehavior`, `.Window_SizeChanged`, `Border`, `.StyleButton_Checked`?**
  _High betweenness centrality (0.178) - this node is a cross-community bridge._
- **Why does `OperationResult` connect `OperationResult` to `ProcessPriorityService`, `GameLoopServiceLogicTests`, `.Fail`, `UpdateService`, `RegistryService`, `.AddDefenderExclusion`, `NvidiaOptimizerService`, `.Apply`, `UpdateInfo`, `NetworkToolsService`, `IGameLoopPerformanceEngine`, `.ConfigureServices`, `.UpdateShadowFile`, `RoutedEventArgs`, `.ConnectAsync`, `.RunToolAsync`, `.BuildServiceProvider_ResolvesAllInterfacesSuccessfully`, `.ApplyKoreanFullHdAsync`, `IGameLoopService`, `.ApplySmartSettings`?**
  _High betweenness centrality (0.115) - this node is a cross-community bridge._
- **Are the 30 inferred relationships involving `RegistryService` (e.g. with `.TempCleanupService_WithCustomOptions_SweepsConfiguredDirectory()` and `.RestorePerformanceSessionAsync_CanBeSynchronouslyAwaited_WithTimeout()`) actually correct?**
  _`RegistryService` has 30 INFERRED edges - model-reasoned connections that need verification._
- **Are the 8 inferred relationships involving `WindowsToolsService` (e.g. with `.RestorePerformanceSessionAsync_CanBeSynchronouslyAwaited_WithTimeout()` and `.RestorePerformanceSessionAsync_CompletesPromptly()`) actually correct?**
  _`WindowsToolsService` has 8 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ToggleButton`, `IsDropDownOpen`, `Popup` to the rest of the system?**
  _135 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Nexora.Shared.Kernel` be split into smaller, more focused modules?**
  _Cohesion score 0.08955223880597014 - nodes in this community are weakly interconnected._