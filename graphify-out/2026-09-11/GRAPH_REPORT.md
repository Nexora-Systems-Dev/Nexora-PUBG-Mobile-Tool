# Graph Report - Nexora PUBG Mobile Tool c#  (2026-09-11)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 942 nodes · 2340 edges · 46 communities (34 shown, 10 thin omitted)
- Extraction: 85% EXTRACTED · 15% INFERRED · 0% AMBIGUOUS · INFERRED: 340 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `d3bcb82c`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- OperationResult
- Nexora.Shared.Kernel
- GameLoopService
- ProcessRunner
- HardwareSnapshot
- AdbClient
- UpdateService
- Window
- ProcessPriorityService
- Ue4SavEditor
- WindowChromeBehavior
- Application
- DnsCatalog
- RoutedEventArgs
- .Apply
- GameLoopServiceLogicTests
- MainWindow
- .SettingRadioButton_Checked
- .RunToolAsync
- RegistryService
- .DnsComboBox_SelectionChanged
- .Window_SizeChanged
- .Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive
- AppConstants
- Border
- AdbCancellationTests
- IRegistryService
- ToggleButton
- AppConstantsTests
- .TryDeleteDirectory
- Nexora.Tests
- IpadLayoutService
- Grid
- GameLoopRegistryOptimizer
- ConnectionResult
- .ChangeIpadButton_Click
- Ellipse
- .MaximizeButton_Click
- .Window_Closing
- SidebarColumn
- .SelectContent
- KoreanResolutionPanel
- PART_Popup
- ShortcutIcon

## God Nodes (most connected - your core abstractions)
1. `Window` - 117 edges
2. `OperationResult` - 78 edges
3. `MainWindow` - 58 edges
4. `WindowsToolsService` - 46 edges
5. `Nexora.Shared.Kernel` - 43 edges
6. `RegistryService` - 42 edges
7. `GameLoopService` - 41 edges
8. `ProcessRunner` - 37 edges
9. `TextBlock` - 35 edges
10. `ProcessPriorityService` - 29 edges

## Surprising Connections (you probably didn't know these)
- `Window` --references--> `ActualWidth`  [EXTRACTED]
  MainWindow.xaml → App.xaml
- `MainWindow` --references--> `IWindowsToolsService`  [EXTRACTED]
  MainWindow.xaml.cs → Services/IWindowsToolsService.cs
- `DefenderExclusionService` --references--> `RegistryService`  [EXTRACTED]
  Services/Performance/DefenderExclusionService.cs → Shared/Infrastructure/RegistryService.cs
- `DefenderExclusionService` --references--> `ProcessRunner`  [EXTRACTED]
  Services/Performance/DefenderExclusionService.cs → Shared/Kernel/ProcessRunner.cs
- `GameLoopProcessService` --references--> `RegistryService`  [EXTRACTED]
  Services/Performance/GameLoopProcessService.cs → Shared/Infrastructure/RegistryService.cs

## Import Cycles
- None detected.

## Communities (46 total, 10 thin omitted)

### Community 0 - "OperationResult"
Cohesion: 0.06
Nodes (26): Fact, InlineData, Theory, WindowsToolsServiceTests, CancellationToken, Task, IWindowsToolsService, DefenderExclusionService (+18 more)

### Community 1 - "Nexora.Shared.Kernel"
Cohesion: 0.08
Nodes (15): Nexora.Configuration, Nexora.Features.GameLoop, Nexora.Tests.Configuration, Nexora.Services, Nexora.UI.Presentation, Nexora.Tests.Catalogs, Nexora, Nexora.Tests.Performance (+7 more)

### Community 2 - "GameLoopService"
Cohesion: 0.06
Nodes (24): GraphicsSelection, GameLoopWorkingStorage, AssetRoot, ConnectionProbePath, KoreanResolutionAssetPath, PendingSavPath, PreviousSavPath, ShadowSettingsPath (+16 more)

### Community 3 - "ProcessRunner"
Cohesion: 0.07
Nodes (28): IReadOnlyList, TempCleanupOptions, ShaderCacheFolderName, TargetDirectories, ICollection, IServiceCollection, Fact, OperationCanceledException (+20 more)

### Community 4 - "HardwareSnapshot"
Cohesion: 0.07
Nodes (24): DevMode, HardwareSnapshot, Architecture, HasDedicatedGpu, OptimizerPlan, Fact, InlineData, Theory (+16 more)

### Community 5 - "AdbClient"
Cohesion: 0.08
Nodes (24): ArgumentException, Fact, InlineData, Task, Theory, BoundaryValidationTests, CancellationToken, IEnumerable (+16 more)

### Community 6 - "UpdateService"
Cohesion: 0.10
Nodes (19): HttpClient, CancellationToken, Fact, Task, DependencyInjectionTests, StubUpdateService, Fact, Task (+11 more)

### Community 7 - "Window"
Cohesion: 0.09
Nodes (39): ActualHeight, Foreground, ArchitectureText, ConnectionDetail, ConnectionTitle, DnsStatusText, HardwareCpuText, HardwareDisplayText (+31 more)

### Community 8 - "ProcessPriorityService"
Cohesion: 0.12
Nodes (17): AlreadyHigh, Candidates, Changed, Fact, Task, TimeSpan, ProcessPriorityShutdownTests, ProcessPriorityClass (+9 more)

### Community 9 - "Ue4SavEditor"
Cohesion: 0.12
Nodes (9): ArgumentNullException, Ue4SavEditor, RawBuffer, IReadOnlyDictionary, UnrealCVarCodec, Fact, InlineData, Theory (+1 more)

### Community 10 - "WindowChromeBehavior"
Cohesion: 0.11
Nodes (19): Nexora.UI.Behaviors, EventArgs, HwndSource, HwndSourceHook, IntPtr, MarshalAs, MinMaxInfo, MonitorInfo (+11 more)

### Community 11 - "Application"
Cohesion: 0.11
Nodes (23): ActiveRail, Application, ButtonBorder, CardBorder, CloseBorder, ComboBorder, CompactBorder, ControlBorder (+15 more)

### Community 12 - "DnsCatalog"
Cohesion: 0.11
Nodes (17): IReadOnlyList, IpadPresetCatalog, Presets, IpadResolutionPreset, Details, DisplayName, IReadOnlyDictionary, IReadOnlyList (+9 more)

### Community 13 - "RoutedEventArgs"
Cohesion: 0.11
Nodes (14): ApplyButton, ChangeDnsButton, CloseWindowButton, ConnectButton, CreateShortcutButton, ForceCloseButton, MinimizeWindowButton, RefreshConnectionButton (+6 more)

### Community 14 - ".Apply"
Cohesion: 0.16
Nodes (8): IpadLayoutOptions, BackupExtension, KeymapDirectory, KeymapFileName, LayoutMapPath, Fact, OptionsTests, IIpadLayoutService

### Community 15 - "GameLoopServiceLogicTests"
Cohesion: 0.23
Nodes (5): Fact, InlineData, Theory, GameLoopServiceLogicTests, Type

### Community 16 - "MainWindow"
Cohesion: 0.26
Nodes (5): CancellationToken, CancellationTokenSource, IDisposable, Task, MainWindow

### Community 17 - ".SettingRadioButton_Checked"
Cohesion: 0.22
Nodes (16): BalancedButton, ExtremeButton, Fps120Button, Fps90Button, HdButton, HdrButton, HighButton, LowButton (+8 more)

### Community 18 - ".RunToolAsync"
Cohesion: 0.12
Nodes (7): Button, Func, AllRecommendedButton, GameLoopOptimizerButton, PerformanceSessionButton, SmartSettingsButton, TextBlock

### Community 19 - "RegistryService"
Cohesion: 0.22
Nodes (5): Fact, RegistryServiceExtensionsTests, IEnumerable, GpuRoutingService, RegistryService

### Community 20 - ".DnsComboBox_SelectionChanged"
Cohesion: 0.15
Nodes (9): BitmapImage, Nexora.UI.Helpers, DnsComboBox, IpadComboBox, PubgVersionComboBox, ShortcutComboBox, SelectionChangedEventArgs, IconImageLoader (+1 more)

### Community 21 - ".Window_SizeChanged"
Cohesion: 0.19
Nodes (8): Nexora.Tests.UI, Nexora.UI.Layout, InlineData, Theory, ResponsiveLayoutManagerTests, SizeChangedEventArgs, Thickness, ResponsiveLayoutManager

### Community 22 - ".Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive"
Cohesion: 0.27
Nodes (8): Dictionary, Guid, ITestOutputHelper, Fact, Task, LiveGameLoopVerificationTests, PowerSessionService, Trait

### Community 23 - "AppConstants"
Cohesion: 0.17
Nodes (11): TimeSpan, Adb, AppConstants, Assets, Emulator, Registry, Timeouts, Tools (+3 more)

### Community 24 - "Border"
Cohesion: 0.17
Nodes (12): ButtonBorder, CardBorder, ComboBorder, CtaBorder, DangerBorder, HoverOverlay, ItemBorder, SegmentBorder (+4 more)

### Community 25 - "AdbCancellationTests"
Cohesion: 0.42
Nodes (5): CancellationToken, Fact, OperationCanceledException, Task, AdbCancellationTests

### Community 27 - "ToggleButton"
Cohesion: 0.27
Nodes (10): DropDownToggleButton, IsDropDownOpen, ToggleButton, ClassicButton, ColorfulButton, DropDownToggleButton, KoreanFullHdButton, MovieButton (+2 more)

### Community 29 - ".TryDeleteDirectory"
Cohesion: 0.36
Nodes (3): Fact, FileUtilitiesTests, FileUtilities

### Community 30 - "Nexora.Tests"
Cohesion: 0.22
Nodes (9): net8.0-windows, Nexora, Microsoft.NET.Sdk, Nexora.Tests, FluentAssertions (6.12.0), Microsoft.Extensions.DependencyInjection (8.0.0), Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2) (+1 more)

### Community 31 - "IpadLayoutService"
Cohesion: 0.38
Nodes (6): PointPair, JsonElement, List, IpadLayoutService, PointPair, XElement

### Community 32 - "Grid"
Cohesion: 0.25
Nodes (8): AboutView, GraphicsView, MainLayoutGrid, NetworkView, OptimizerView, ShortcutsView, TitleBarGrid, Grid

### Community 34 - "ConnectionResult"
Cohesion: 0.67
Nodes (3): IReadOnlyList, ConnectionResult, PubgVersion

### Community 36 - "Ellipse"
Cohesion: 0.50
Nodes (4): ConnectionDot, SidebarConnectionDot, TopConnectionDot, Ellipse

### Community 39 - "SidebarColumn"
Cohesion: 0.67
Nodes (3): SidebarColumn, TitleBrandColumn, ColumnDefinition

## Knowledge Gaps
- **56 isolated node(s):** `Rect32`, `Adb`, `Assets`, `Emulator`, `Registry` (+51 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 141 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `OperationResult`, `Nexora.Shared.Kernel`, `GameLoopService`, `ProcessRunner`, `HardwareSnapshot`, `UpdateService`, `Window`, `WindowChromeBehavior`, `Application`, `RoutedEventArgs`, `.SettingRadioButton_Checked`, `.RunToolAsync`, `.DnsComboBox_SelectionChanged`, `.Window_SizeChanged`, `ToggleButton`, `.ChangeIpadButton_Click`, `.MaximizeButton_Click`, `.Window_Closing`, `.SelectContent`?**
  _High betweenness centrality (0.319) - this node is a cross-community bridge._
- **Why does `Window` connect `Window` to `UpdateService`, `WindowChromeBehavior`, `Application`, `RoutedEventArgs`, `MainWindow`, `.SettingRadioButton_Checked`, `.RunToolAsync`, `.DnsComboBox_SelectionChanged`, `.Window_SizeChanged`, `Border`, `ToggleButton`, `Grid`, `.ChangeIpadButton_Click`, `Ellipse`, `.MaximizeButton_Click`, `.Window_Closing`, `SidebarColumn`, `KoreanResolutionPanel`, `PART_Popup`, `ShortcutIcon`?**
  _High betweenness centrality (0.217) - this node is a cross-community bridge._
- **Why does `OperationResult` connect `OperationResult` to `GameLoopRegistryOptimizer`, `GameLoopService`, `ProcessRunner`, `HardwareSnapshot`, `UpdateService`, `.Window_Closing`, `ProcessPriorityService`, `.Apply`, `GameLoopServiceLogicTests`, `.RunToolAsync`, `RegistryService`, `.Step3_PerformanceCenter_LiveHardwarePlanAndRegistryOptimization_NonDestructive`?**
  _High betweenness centrality (0.129) - this node is a cross-community bridge._
- **Are the 6 inferred relationships involving `WindowsToolsService` (e.g. with `.KillGameLoopProcesses_Throws_ForCanceledToken()` and `.KillGameLoopProcessesAsync_Throws_ForCanceledToken()`) actually correct?**
  _`WindowsToolsService` has 6 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Rect32`, `Adb`, `Assets` to the rest of the system?**
  _56 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `OperationResult` be split into smaller, more focused modules?**
  _Cohesion score 0.05642080517190714 - nodes in this community are weakly interconnected._
- **Should `Nexora.Shared.Kernel` be split into smaller, more focused modules?**
  _Cohesion score 0.08322026232473993 - nodes in this community are weakly interconnected._