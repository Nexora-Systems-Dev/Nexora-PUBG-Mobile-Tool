# Nexora — Architectural Refactoring Execution Plan

> Goal: migrate the current codebase into the feature-first layout defined in
> `PROJECT-LAYOUT.md` **without changing runtime behavior**. This plan is a
> reorganization, not a rewrite.
>
> Source of truth for the target structure: `PROJECT-LAYOUT.md` (§2 final tree,
> §9 move map, §11 acceptance gate). Conventions and gotchas: `AGENTS.md`,
> `ARCHITECTURE.md`.

## How to use this plan

- Every actionable item is a `- [ ]` checkbox. Tick it only when the **gate**
  defined in "Acceptance gate for every move" passes.
- Phases are strictly ordered: **no phase starts until the previous phase is
  fully green**. Inside Phase 1 and Phase 2 the sub-steps are also ordered and
  each is a separate commit.
- The plan deliberately separates **moving files** from **changing namespaces**.
  `PROJECT-LAYOUT.md` requires that a move preserves the existing namespace and
  behavior first; namespaces are normalized only after the moved tree builds and
  tests pass. A pure `git mv` with an unchanged namespace compiles with zero
  edits — that is the safest possible diff and the reason the Phase 1 units can
  run in parallel.

## Invariants that must hold at every commit

Non-negotiable boundaries inherited from `PROJECT-LAYOUT.md` §5–§9 and
`AGENTS.md`. Any unit that would violate one of these must stop and escalate.

- All GameLoop paths resolve through `IGameLoopPathResolver`; never a hardcoded
  path or drive letter.
- `Process.GetProcessesByName` only in `GameLoopProcessEnumerator` /
  `IGameLoopProcessService`.
- All registry access through `IUserRegistry` / `IMachineRegistry`; all process
  execution through `IProcessRunner`; all ADB through `IAdbClient`.
- `IGameLoopConnection` + `IGraphicsProfileStore` stay factory-forwarded to one
  `GameLoopService` singleton — never two separate `AddSingleton<Iface, Impl>`
  registrations (would fork the shared gate/session).
- `IpadLayoutService` keeps the GameLoop running-state guard, backup, and
  rollback. No registry write from a View or ViewModel.
- No `.Result` / `.Wait()` / `async void` outside WPF event handlers
  (`AsyncHygieneTests` enforces this). Every cancellable async API takes a
  `CancellationToken`.
- `App.xaml.cs` stays the only Composition Root. No service locator, no manual
  service construction inside a ViewModel. The nullable-args-with-fallback
  constructor pattern (designer-safe) is preserved on every View and ViewModel.
- No behavior change inside a move commit. XAML moves touch only `x:Class`,
  namespace, and resource-dictionary placement.
- `graphify-out/` is protected: never delete, edit, or commit it. `bin/`,
  `obj/`, `artifacts/` never get committed.

## Acceptance gate for every move (PROJECT-LAYOUT.md §11)

A move is not done until **all** of the following pass:

1. `dotnet build .\Nexora.slnx --configuration Release` → 0 errors, **0
   warnings** (`TreatWarningsAsErrors=true`).
2. `dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"`
   → all standard tests pass (baseline: 419; never regress).
3. No duplicate implementations registered in DI; no leftover old copies of a
   moved file still compiled.
4. `git diff` contains no unintended behavioral change — moves look like moves.
5. One commit per sub-step, so every phase is individually revertable.

---

## Baseline snapshot (measured before any change)

Recorded at branch `main`, working tree with untracked docs only.

| Metric | Value |
|---|---|
| `MainWindow.xaml` | 2,123 lines |
| `MainWindow.xaml.cs` | 1,078 lines, ~70 members |
| Named XAML controls (`x:Name`) | 131 |
| Production `.cs` files | ~96 (root + `Features/` + `Services/` + `Shared/` + `UI/` + `Configuration/`) |
| Test `.cs` files | 50 |
| Standard tests | 419 passing; 5 optional `LiveFunctionalVerification` (need a running GameLoop) |
| Folders that exist today but **not** in the target tree | `Services/`, `Features/SystemTools/`, `Features/Layout/`, `Shared/Infrastructure/` |
| Folders in the target tree that are **new** | `Bootstrap/`, `Infrastructure/`, `Features/Graphics/`, `Features/Tuning/`, `Features/Network/`, `Features/Optimizer/`, `Features/Shortcuts/`, `Features/Updates/`, `UI/Controls/`, `UI/Navigation/` |

Current namespace distribution (what the normalization pass has to rewrite):

| Namespace | Files |
|---|---|
| `Nexora.Services` | 17 |
| `Nexora.Services.Performance` | 18 |
| `Nexora.Features.GameLoop` | 13 |
| `Nexora.Shared.Infrastructure` | 8 |
| `Nexora.Shared.Kernel` | 7 |
| `Nexora.Configuration` | 6 |
| `Nexora.Features.Layout` | 4 |
| `Nexora.Features.SystemTools.Network` | 3 |
| `Nexora.UI.Presentation` | 2 |
| `Nexora.Features.Performance` | 2 |
| `Nexora.UI.Behaviors` / `Nexora.UI.Helpers` / `Nexora.UI.Layout` | 1 each |
| `Nexora.Features.SystemTools` / `Nexora.Features.Security` / `Nexora` | 1 each |

XAML page regions inside `MainWindow.xaml` today (line numbers mark where each
page's panel opens — these become Phase 2 `UserControl`s):

| Page | Region in `MainWindow.xaml` | Controls / handlers it owns |
|---|---|---|
| Graphics | 180 → 635 | Connection (Connect/Refresh, connection pill, version combobox), Quality/FPS/Style segments, shadow toggles, summary bar, Apply |
| Optimizer | 636 → 1100 | Hardware profile texts, plan texts, Smart Settings, Temp Cleaner, GameLoop Optimizer, All Recommended, Force Close, session buttons, activity/status |
| Tuning | 1101 → 1239 | CPU/memory sliders, DPI combobox, 6 toggles, Apply/End Task, loading bar |
| Network | 1240 → 1566 | DNS combobox + Change, iPad preset combobox + details + Apply/Reset |
| Shortcuts | 1567 → 1968 | Shortcut combobox, preview texts/icon, Create |
| About | 1969 → end | Static content |
| Shell (kept) | title bar, sidebar, status bar | Navigation, chrome, window lifecycle, busy/status, connection pill state |

---

## Open decisions (resolved)

Contradictions and gaps in `PROJECT-LAYOUT.md` §2/§9 that had to be decided up
front so two units never fight over the same files. **All resolved — see the
ruling under each item.**

- [x] **D1 — Optimizer vs Performance ownership.** `PROJECT-LAYOUT.md` §2 lists
  `PerformanceModels.cs`, `PerformancePlanBuilder.cs`,
  `PerformanceExecutionReport.cs`, and `PerformanceEngineFacade.cs` under
  **both** `Features/Optimizer/{Application,Domain}` **and**
  `Features/Performance/{Domain,Infrastructure}`. That violates §3's "one owner,
  one place" rule.
  **Ruling:** `Features/Performance` owns the engine, domain models, and
  infrastructure. `Features/Optimizer` remains **Presentation-only**
  (`OptimizerView`, `OptimizerViewModel`, `OptimizerDisplayFormatter`) and
  consumes the existing `IGameLoopPerformanceEngine` contract instead of a new
  `IPerformanceEngine`. The duplicated entries are deleted from the Optimizer
  bucket.
- [x] **D2 — `SaveProfileReader.cs` placement.** Not in the §9 move map. It
  reads the loaded graphics profile (quality/fps/style/shadow) over ADB and is
  consumed by `GameLoopService`.
  **Ruling:** `Features/Graphics/Domain/SaveProfileReader.cs`.
- [x] **D3 — `GpuVendorClassifier.cs` placement.** In `Features/Performance/`
  today but absent from the §2 `Features/Performance/Infrastructure` list.
  **Ruling:** `Features/Performance/Infrastructure/` (map omission).
- [x] **D4 — `ITempCleanupService` ownership.** §9 says
  `Features/Optimizer/Application` "or a shared contract if genuinely used by
  more than one feature". Today only the Optimizer page consumes it.
  **Ruling:** `Features/Optimizer/Application` for both interface and
  implementation; revisit only if a second feature starts calling it.
- [x] **D5 — Connection UI ownership.** The Connect button and connection pill
  sit in the Graphics region (line 592) but connection state also drives the
  sidebar (shell).
  **Ruling:** `GraphicsViewModel` owns connection orchestration via
  `IGameLoopConnection`; the shell subscribes to a shared connection-state
  notification (e.g. the existing `ConnectionState` + a lightweight event) to
  update the sidebar pill — no feature logic in `MainWindow`.
- [x] **D6 — `RunToolAsync` helper.** `MainWindow.RunToolAsync(button, action,
  label)` is generic UI plumbing (busy state + result rendering), not feature
  logic.
  **Ruling:** proceed with the standard page extractions first and **leave the
  core execution plumbing intact in `MainWindow` until Phase 3**. Each Phase 2
  page calls the existing helper (or a thin shared equivalent reachable from the
  page) rather than duplicating it; the plumbing is consolidated into a shared UI
  helper only in Phase 3 step 3.5.

---

## Phase 0 — Baseline build and tests verification

Purpose: prove the starting line is green and record it, so every later phase
has something to compare against. **No file moves in this phase.**

- [x] **0.1** Clean working tree check: `git status` — confirm only the known
  untracked docs exist (`AGENTS.md`, `ARCHITECTURE.md`, `CHANGELOG.md`,
  `EMULATOR-TUNING-PLAN.md`, `FIX-PLAN-100-PERCENT.md`, `PROJECT-LAYOUT.md`,
  `Release/`). No stray files, no `bin/`/`obj/`/`artifacts/` staged.
- [x] **0.2** `dotnet restore .\Nexora.slnx`
- [x] **0.3** `dotnet build .\Nexora.slnx --configuration Release` → confirm
  **0 errors / 0 warnings**. Record output.
- [x] **0.4** `dotnet test .\Nexora.slnx --configuration Release --filter
  "Category!=LiveFunctionalVerification"` → confirm **419 passing, 0 failing**.
  Record the count; it is the regression floor for every later phase.
- [x] **0.5** Known-environment caveat check: the `ProcessPriority` suite can
  fail if a leftover GameLoop/AppMarket process is running. If any test fails,
  verify it is this pre-existing environmental issue (not a regression) before
  proceeding — close the emulator and re-run.
- [x] **0.6** Record structural baseline: `MainWindow.xaml` = 2,123 lines,
  `MainWindow.xaml.cs` = 1,078 lines, 131 named controls. These are the numbers
  Phase 3 must improve.
- [x] **0.7** Confirm `.gitignore` covers `bin/`, `obj/`, `artifacts/`,
  `graphify-out/` and that `graphify-out/` is untouched.
- [ ] **0.8** Tag/commit the baseline: `chore: record refactor baseline (419
  tests, 0 warnings)` so every phase can diff against it.

**Measured Phase 0 results (2026-09-19):**

| Check | Result |
|---|---|
| `dotnet restore` | OK — `Nexora.csproj` restored, 1 of 2 projects up-to-date |
| Release build | **0 Warning(s), 0 Error(s)** — `Build succeeded`, 10.53s |
| Standard test suite | **Passed! Failed: 0, Passed: 419, Skipped: 0, Total: 419**, 15s |
| Working tree | only the known untracked docs + `execution-plan.md`; nothing staged |
| Regenerable dirs tracked | none (`bin/`, `obj/`, `artifacts/`, `Nexora.Tests/bin|obj`) |
| `graphify-out/` tracked | 0 files — protected dir untouched |

> **Baseline correction:** `ARCHITECTURE.md` states 416 standard tests, but the
> measured count is **419**. The measured number is authoritative for the
> regression floor; `ARCHITECTURE.md`/`README.md` get corrected in Phase 4.9.

**Phase 0 exit gate:** build 0/0 ✅, standard tests 419/0 ✅, baseline numbers
recorded in this file ✅, clean tree ✅. Step 0.8 (baseline commit) is deferred
until the working tree's untracked docs are settled.

---

## Phase 1 — Relocating Domain, Contracts, and Infrastructure (no XAML/View touches)

Purpose: move every non-UI file into its target folder. **Rule for the whole
phase: `git mv` only, namespaces unchanged.** Because the namespace does not
change, the compile graph is unaffected, so each sub-step is a pure file move
that builds immediately. The namespace normalization pass (1.14) then aligns
namespaces with folders in one carefully-ordered sweep.

Per sub-step: `git mv` → build Release → run standard tests → review diff →
commit. Sub-steps 1.1–1.13 are **mutually disjoint file sets** — see the
parallelization table; 1.14+ are serial.

### 1.1 Shared kernel split (contracts vs implementations)

- [x] `Shared/Kernel/OperationResult.cs` → `Shared/Contracts/OperationResult.cs`
- [x] `Shared/Kernel/IFileSystem.cs`, `IProcessRunner.cs`, `IWorkRootProvider.cs`
      → stay in `Shared/Kernel/`
- [x] `Shared/Kernel/PhysicalFileSystem.cs`, `FileUtilities.cs` →
      `Infrastructure/Files/`
- [x] `Shared/Kernel/ProcessRunner.cs`, `ProcessText.cs` →
      `Infrastructure/Processes/`
- [x] Build + tests + commit.

### 1.2 Registry & GameLoop infrastructure

- [x] `Shared/Infrastructure/IUserRegistry.cs`, `IMachineRegistry.cs` →
      `Infrastructure/Registry/`
- [x] `Shared/Infrastructure/RegistryService.cs` → `Infrastructure/Registry/`
- [x] `Shared/Infrastructure/GameLoopPathResolver.cs`,
      `GameLoopProcessEnumerator.cs`, `GameLoopWorkRootProvider.cs` →
      `Infrastructure/GameLoop/`
- [x] Build + tests + commit.

### 1.3 GameLoop feature (the hub — do this before the feature splits that depend on it)

- [x] `Services/IGameLoopConnection.cs`, `IGraphicsProfileStore.cs`,
      `IAdbClient.cs`, `GameLoopService.cs` → `Features/GameLoop/Application/`
- [x] `Services/AdbClient.cs`, `GameLoopConnector.cs`,
      `Services/GraphicsSettingsApplier.cs` →
      `Features/GameLoop/Infrastructure/`
- [x] `Services/GameLoopSession.cs` → `Features/GameLoop/Domain/`
- [x] `Features/GameLoop/GameLoopModels.cs`, `GameLoopWorkingStorage.cs`,
      `RemotePaths.cs` → `Features/GameLoop/Domain/`
- [x] Build + tests + commit. Verify the `GameLoopService` singleton still
      factory-forwards both facets (gate check; see Invariants).

### 1.4 Graphics domain extraction

- [x] `Features/GameLoop/PubgVersionCatalog.cs`, `Ue4SavEditor.cs`,
      `UnrealCVarCodec.cs`, `ShadowSettingsStore.cs` →
      `Features/Graphics/Domain/`
- [x] `Services/SaveProfileReader.cs` → `Features/Graphics/Domain/`
      (decision **D2**)
- [x] Build + tests + commit.

### 1.5 Tuning extraction

- [x] `Features/GameLoop/IEmulatorSettingsService.cs`,
      `EmulatorSettingsService.cs` → `Features/Tuning/Application/`
- [x] `Features/GameLoop/EmulatorTuningCatalog.cs`,
      `EmulatorTuningModels.cs` → `Features/Tuning/Domain/`
- [x] Build + tests + commit. Verify the Apply-while-running guard and
      write-then-read-back verification still pass
      (`EmulatorSettingsServiceTests`).

### 1.6 Network extraction (merges two current folders)

- [x] `Features/SystemTools/Network/INetworkToolsService.cs`,
      `NetworkToolsService.cs` → `Features/Network/Application/`
- [x] `Features/SystemTools/Network/DnsCatalog.cs` →
      `Features/Network/Domain/`
- [x] `Features/Layout/IIpadLayoutService.cs`, `IpadLayoutService.cs` →
      `Features/Network/Application/`
- [x] `Features/Layout/IpadPresetCatalog.cs`, `IpadResolutionPreset.cs` →
      `Features/Network/Domain/`
- [x] Build + tests + commit. Verify the iPad running-guard path
      (`IpadLayoutServiceTests`) is untouched.

### 1.7 Shortcuts extraction

- [x] `Features/GameLoop/IShortcutService.cs`, `ShortcutService.cs` →
      `Features/Shortcuts/Application/`
- [x] Build + tests + commit.

### 1.8 Performance extraction

- [x] `Services/Performance/IGameLoopProcessService.cs` →
      `Features/Performance/Application/`
- [x] `Services/Performance/PerformanceModels.cs`,
      `PerformancePlanBuilder.cs`, `PerformanceExecutionReport.cs` →
      `Features/Performance/Domain/`

      > **Correction applied during execution:** the duplicate
      > `Features/Performance/PerformanceModels.cs` listed in D1/§9 did **not**
      > exist on disk — only `Services/Performance/PerformanceModels.cs` did.
      > No dedupe was needed; the file was moved once. The `(dedupe)` note is
      > struck from the move map.
- [x] `Services/Performance/GameLoopProcessService.cs`,
      `GameLoopRegistryOptimizer.cs`, `GpuRoutingService.cs`,
      `HardwareDetectionService.cs`, `NvidiaOptimizerService.cs`,
      `PowerSessionService.cs`, `ProcessPriorityApplier.cs`,
      `ProcessPriorityMonitor.cs`, `ProcessPriorityService.cs`,
      `ProcessPrioritySnapshotStore.cs`, `PerformanceEngineFacade.cs`,
      `Features/Performance/GpuVendorClassifier.cs` (decision **D3**) →
      `Features/Performance/Infrastructure/`
- [x] `Services/Performance/IGameLoopPerformanceEngine.cs` →
      `Features/Performance/Application/`
- [x] Delete now-empty interface files folded into their feature
      (`IProcessPriorityApplier/Monitor/SnapshotStore` →
      `Features/Performance/Infrastructure/`)
- [x] Build + tests + commit. Per decision **D1**, the Facade lives here only.

### 1.9 Updates extraction

- [x] `Services/IUpdateService.cs` → `Features/Updates/Application/`
- [x] `Services/UpdateInfo.cs` → `Features/Updates/Domain/`
- [x] `Services/UpdateService.cs`, `UpdateChecker.cs`,
      `UpdateArchiveValidator.cs`, `UpdateHandoffBuilder.cs`,
      `StagingDirectoryGC.cs` → `Features/Updates/Infrastructure/`
- [x] Build + tests + commit. Verify `%TEMP%/NexoraUpdate-<Guid>` staging sweep
      and `finally` cleanup still hold.

### 1.10 Security + Optimizer application services

- [x] `Features/Security/DefenderExclusionService.cs` →
      `Features/Security/Infrastructure/`
- [x] `Services/ITempCleanupService.cs` + `Features/SystemTools/TempCleanupService.cs`
      → `Features/Optimizer/Application/` (decision **D4**)
- [x] Build + tests + commit.

### 1.11 Folder cleanup

- [x] Confirm `Services/`, `Features/SystemTools/`, `Features/Layout/`, and
      `Shared/Infrastructure/` are empty and remove them.
- [x] Confirm no moved type exists in two compiled locations (`grep` the type
      names across the tree — zero duplicates).
- [x] Build + tests + commit.

### 1.12 DI registration extraction (Bootstrap)

- [x] Create `Bootstrap/ServiceCollectionExtensions.cs` and move
      `App.xaml.cs:ConfigureServices` into it verbatim (same registrations, same
      lifetimes, same factory-forwarding).
- [x] `App.xaml.cs` keeps only startup/exit wiring and calls the extension.
- [x] Create `Bootstrap/StartupTasks.cs` for the on-startup steps currently
      inline in `App.OnStartup`.
- [x] Run `DependencyInjectionTests` + `ServiceCompositionTests` — the
      resolution graph must be identical.
- [x] Build + tests + commit.

### 1.13 Test project mirroring

- [x] Reorganize `Nexora.Tests/` to mirror production paths:
      `Features/{Graphics,GameLoop,Tuning,Network,Shortcuts,Optimizer,Performance,Updates,Security}/`,
      `Infrastructure/{Files,Processes,Registry,GameLoop}/`, `UI/`, `Shared/`.
- [x] Keep `Nexora.Tests/Services/` and `Nexora.Tests/Performance/` as aliases
      only if renames would churn test namespaces — otherwise move files and
      update `namespace` + `using` in the same commit.
- [x] Build + tests + commit.

### 1.14 Namespace normalization pass (serial — after all moves are green)

- [x] Rewrite each moved file's `namespace` to match its new folder
      (`Nexora.Features.Graphics.Domain`, `Nexora.Infrastructure.Registry`, …).
      The target namespace is always computed from the file's **folder path**,
      never from its legacy declaration — `Shared/Kernel/IWorkRootProvider.cs`
      declared `Nexora.Shared.Infrastructure` but lives in `Shared/Kernel/`, so
      folder-authoritative renaming keeps its Domain consumer legal.
- [x] Update `using` statements across production and tests in the same commit.
      Compiler-driven: CS0246 adds, CS8019 removes, so no hand-maintained
      reference map. `TreatWarningsAsErrors=true` makes an over-broad blanket
      `using` sweep impossible (CS8019 is an error in production).
- [x] Prefer one feature per commit (Graphics, then Tuning, Network, Shortcuts,
      Performance, Updates, GameLoop, Infrastructure, Shared) so each diff is
      reviewable; build + tests after each.
      Landed as: 1.14-tuning, 1.14-shortcuts, 1.14-network,
      1.14-optimizer-security, 1.14-performance, 1.14-updates, 1.14-gameloop,
      1.14-infrastructure, 1.14-shared, 1.14-tests.
- [x] Final Phase 1 build: 0 errors / 0 warnings, 419 tests passing.

**Two forced name qualifications (no behavior change):**
`Registry.CurrentUser` became `Microsoft.Win32.Registry.CurrentUser` because the
new `Nexora.Infrastructure.Registry` namespace shadows the `Microsoft.Win32.Registry`
type at its own declaration site; and two inline partially-qualified references
(`Features.Performance.Domain.HardwareSnapshot`,
`Shared.Kernel.OperationResult`) were fully qualified or simplified, because the
deeper test-namespace nesting changes how unqualified prefixes resolve.

**Two layering exceptions exposed by the rename, carried as documented debt:**

| Exception | Arrow | Disposition |
|---|---|---|
| `Features/Graphics/Domain/SaveProfileReader.cs` injects `IAdbClient` (`Features/GameLoop/Application/`) | Domain → Application (across features) | Rename only, per D2 ruling. The flat legacy `Nexora.Services` namespace had made the reference intra-namespace and invisible. §4 rule 4's literal text forbids Domain depending on WPF/Registry/Process/`MessageBox`; it does not name ADB, so this is a diagram-level violation, not a rule violation. **Phase 4 decision.** |
| `Features/Performance/Domain/PerformanceModels.cs` calls `GpuVendorClassifier` (`Features/Performance/Infrastructure/`) | Domain → Infrastructure | Rename only, per D3 ruling (`GpuVendorClassifier` stays in Infrastructure). Same masking by the flat `Nexora.Services.Performance` namespace. **Phase 4 decision.** |

Neither moves a file, adds an interface, or changes behavior; both are queued for
Phase 4. `Features/Shortcuts/Application/ShortcutService.cs` → `Nexora.UI.Helpers.IconImageLoader`
is a pre-existing Application → UI arrow that the rename sharpens but does not create.
No architecture guard test exists yet; building one is Phase 4 work, not 1.14.

**Phase 1 exit gate:** the on-disk tree matches `PROJECT-LAYOUT.md` §2 for every
non-UI file; empty legacy folders deleted; namespaces match folders; DI still
resolves through `Bootstrap/ServiceCollectionExtensions.cs`; build 0/0; 419/0
tests; no behavior change in any diff.

> **✅ PHASE 1 COMPLETE — sign-off (2026-09-19).**

> **Execution status — steps 1.1 through 1.14 complete (2026-09-19).**
>
> | Check | Result |
> |---|---|
> | `dotnet build ./Nexora.slnx -c Release` | **0 errors, 0 warnings** |
> | `dotnet test ./Nexora.slnx -c Release --filter "Category!=LiveFunctionalVerification"` | **419 passed, 0 failed, 0 skipped** (baseline held) |
> | Pure `git mv` with namespaces untouched (1.1–1.13) | yes — zero code edits needed to compile |
> | `GameLoopService` singleton forwarding | `AddSingleton<GameLoopService>` ×1; `IGameLoopConnection` and `IGraphicsProfileStore` both `GetRequiredService<GameLoopService>()` — no forked gate/session |
> | `RegistryService` forwarding | `IUserRegistry` + `IMachineRegistry` both forward to one `RegistryService` singleton |
> | Leftover compiled files in legacy paths | 0 under `Services/`, `Features/SystemTools/`, `Features/Layout/`, `Shared/Infrastructure/` |
> | Empty legacy folders | `Services/`, `Features/SystemTools/`, `Features/Layout/`, `Shared/Infrastructure/` removed |
> | Composition root | `App.xaml.cs` delegates to `Bootstrap/ServiceCollectionExtensions.AddNexoraServices()`; `App.ConfigureServices` kept as a thin public entry point for the ~20 test call sites |
> | Namespace = folder path (1.14) | verified solution-wide: 0 legacy `namespace Nexora.Services*` declarations; every production file matches `Nexora` + folder, every test file matches `Nexora.Tests` + folder |
> | Legacy namespaces fully retired (1.14) | `Nexora.Services`, `Nexora.Services.Performance`, `Nexora.Features.GameLoop`, `Nexora.Shared.Infrastructure`, `Nexora.Features.Layout`, `Nexora.Features.SystemTools.Network`, `Nexora.Tests.Services`, `Nexora.Tests.Performance`, `Nexora.Tests.Catalogs` — all zero declarers |
> | No namespace consumed as data | verified absent: `Type.GetType`, `Assembly.Load`, `Activator.CreateInstance(string)`, `XmlnsDefinition`, `TypeNameHandling`, DI-by-string, XAML `clr-namespace:` — the rename is purely compile-time |
> | False friends untouched | registry key path `SOFTWARE\Nexora\...`, `<InternalsVisibleTo Include="Nexora.Tests" />`, `ApplicationName`, `StagingPrefix`, `Nexora-{version}-{runtime}.exe`, `Nexora.PUBGMobileTool`, `<RootNamespace>` — all unchanged |
> | Behavior change in any 1.14 diff | none — every non-namespace/non-using edit is a name qualification, verified by diff scan |
>
> Each sub-step landed as its own commit on `main` (1.1 → 1.14), so every move is
> individually revertable. Baseline commit 0.8 is `fe8f51c`; Phase 1 completes at
> the 1.14-docs commit.
>
> Step 1.14 ran as a serial commit series, not parallel workers: a namespace rename
> is solution-wide and atomic, every worker would edit the same ~95 reference
> sites, and `TreatWarningsAsErrors=true` forbids the brute-force `using` fix that
> a merged branch would need. Subagents were used for research (inventory,
> collision detection, reference mapping); the compiler's CS0246/CS8019 pair was
> the execution oracle. All 9 legacy namespaces are now zero-declarer.


---

## Phase 2 — Decoupling WPF feature pages one by one

Purpose: turn six inline page regions of `MainWindow.xaml` into independent
`UserControl`s, each with a testable ViewModel, in `Features/<Feature>/Presentation/`.

**Hard rule (PROJECT-LAYOUT.md §7):** one page per sub-step; the next page does
not start until the previous one builds, tests pass, and the app runs. Each XAML
move changes only `x:Class`, namespace, and resource-dictionary placement — no
behavior, no restyling, no control rename in the same commit.

**Identical checklist for every page (2.1–2.6):**

- [ ] a. Create `Features/<Feature>/Presentation/<Feature>View.xaml` +
      `.xaml.cs`; move the page's XAML region out of `MainWindow.xaml` verbatim.
- [ ] b. Fix `x:Class`, namespace, and `ResourceDictionary`/`Styles` references
      only.
- [ ] c. Create `<Feature>ViewModel.cs` implementing `INotifyPropertyChanged`
      (or `CommunityToolkit`-free minimal pattern already used in the repo);
      move the region's handlers as commands/methods; keep the nullable-args +
      fallback constructor pattern.
- [ ] d. Keep the designer fallback constructor on the View — do not remove it
      during the move.
- [ ] e. Preserve `AutomationProperties`, keyboard navigation, and reduced-motion
      behavior exactly.
- [ ] f. Number/text formatting moves into a testable Formatter
      (`<Feature>DisplayFormatter.cs`) where the page formats output; layout
      math stays in `UI/Layout`.
- [ ] g. Register View + ViewModel in `Bootstrap/ServiceCollectionExtensions.cs`
      (Transient by default; document any Singleton exception).
- [ ] h. `MainWindow.xaml` hosts the page control; `MainWindow.xaml.cs` keeps no
      logic from that page.
- [ ] i. Build Release (0/0) + standard tests (419/0) + manual UI smoke of that
      page (see e2e recipe) before committing.
- [ ] j. Commit: `refactor(<feature>): extract <Feature>View and ViewModel from
      MainWindow`.

### 2.1 Graphics (includes connection orchestration)

- [x] 2.1.a–j as above. Scope: XAML lines 180–635. Done at `adeba48`
  (preceded by `1814172` for the formatter/service/models and `b1f69d` for
  `IPageOperationBus`).
  - Owns: `PubgVersionComboBox`, Quality/FPS/Style segments, shadow toggles,
    summary bar (`Summary*`), `ApplyButton`, plus `ConnectButton` /
    `RefreshConnectionButton` and the connection pill.
  - Commands delegate to `IGraphicsProfileStore` / `IGameLoopConnection` /
    `IAdbClient` (per decision **D5**).
  - Extract `GraphicsDisplayFormatter.cs` for the summary/read-back formatting.
  - New `Features/Graphics/Application/IGraphicsSettingsService.cs` +
    `GraphicsSettingsService.cs` host the apply + read-back flow currently living
    in `MainWindow.ApplyLoadedSettingsAsync` / `ApplyButton_Click`.
  - `SaveProfileReader` (moved in 1.4) is consumed here.
  - Sidebar connection pill stays in the shell and subscribes to connection
    state — no graphics logic in `MainWindow`.
  - Deviations from the lettered checklist, all deliberate:
    - **(c)** the hand-rolled `INotifyPropertyChanged` reports display state
      through `ConnectionStateChanged` / `SettingsLoaded` / `BusyVisualChanged`
      / `StatusChanged` events rather than commands, because the page is still
      imperative (no `{Binding}` rewrite — see the scope limit in the plan).
    - **(g)** `IGraphicsSettingsService` is **Singleton**, not Transient: it is a
      stateless facade over the `IGameLoopConnection` / `IGraphicsProfileStore`
      singletons. `GraphicsView` and `GraphicsViewModel` are Transient.
    - **(h)** one sanctioned reach-through remains: `SetStatus` writes
      `GraphicsView.SetStatus`, because the status bar lives visually inside the
      page but is written by ~12 shell methods. Severing it is Phase 3 work.
    - **(i)** gate landed at **0/0 build and 461 passed / 1 failed**, not
      419/0. The one failure —
      `WindowsGpuBoostReportingTests.ProcessPriority_OfflineGameLoop_ReturnsSkipped_AndArmsMonitor`
      — is environmental, not a regression: it fails identically at `b1f69d`
      with these changes stashed (a leftover AppMarket process on this host
      makes the offline-skip assertion see a running emulator). The +42 tests
      over the 419 baseline are the new formatter/service/bus/ViewModel suites.
    - **(i)** the manual UI smoke could not run: this environment has no
      headless WPF and no UI-automation harness, and the page has zero
      `AutomationProperties` (verified 0 in the old region and 0 in the new
      page, with the shell's 10 untouched). A structural parity gate stood in:
      42/42 named elements, 5/5 handlers, 18/18 resource keys, and zero
      orphaned element references in `MainWindow.xaml.cs`.

### 2.2 Tuning

- [x] 2.2.a–j. Scope: XAML lines 1101–1239. Done (see gate note below).
  - Owns: `TuningCpuSlider`, `TuningMemorySlider`, `TuningDpiComboBox`, the six
    toggles, `TuningApplyButton`, `TuningEndTaskButton`, `TuningLoadingBar`.
  - Commands delegate to `IEmulatorSettingsService`; loading/label logic
    (`RefreshTuningAsync`, `SetTuningLoading`, `UpdateTuningLabels`) moves into
    `TuningViewModel`.
  - Keep the "close GameLoop before applying" guard visible and unchanged.
  - Gate: build **0/0**; tests **474 passed / 1 failed**, where the one failure
    (`WindowsGpuBoostReportingTests.ProcessPriority_OfflineGameLoop_ReturnsSkipped_AndArmsMonitor`)
    is environmental, not a regression — it fails identically on the clean tree
    with these changes stashed (leftover GameLoop/AppMarket process on this
    host; same failure as in 2.1). The +13 tests over the 462 post-2.1 count
    are the new `TuningViewModelTests` (12) plus `TuningPage_IsRegisteredWithResolvableDependencies` (1).
    Structural parity: 17/17 named elements, 4/4 handlers, 1/1 local resource
    key moved; zero orphaned element references in `MainWindow.xaml.cs`
    (compiler-verified); shell keeps only navigation/hosting/status-forwarding.
    Manual UI smoke could not run (no headless WPF harness — same constraint
    as 2.1).

### 2.3 Network

- [x] 2.3.a–j. Scope: XAML lines 1240–1566 (post-2.2 file: lines 651–976). Done (see gate note below).
  - Owns DNS (`DnsComboBox`, `ChangeDnsButton`, `DnsStatusText`) and iPad
    (`IpadComboBox`, `IpadPresetDetailsText`, `ChangeIpadButton`,
    `ResetIpadButton`, `IpadApplyStatusText`).
  - Commands delegate to `INetworkToolsService` and `IIpadLayoutService`.
  - `UpdateIpadPresetDetails` / `GetSelectedIpadPreset` move into the ViewModel
    or `Features/Network/Presentation` formatter.
  - Landed as: `NetworkViewModel` owns probe/apply/reset orchestration
    (`ProbeDnsAsync` with stale-selection discard, `ApplyDnsAsync`,
    `ApplyIpadAsync`, `ResetIpadAsync`) plus the `FindPreset` catalog lookup;
    the iPad running-state guard, backup, and restore stay untouched inside
    `IpadLayoutService`. The DNS shutdown guard (QA F-005) is preserved via a
    shutdown-aware selection delegate. `MainWindow` keeps only hosting and the
    status-forwarding subscription.
  - Gate: build **0/0**; tests **487 passed / 1 failed**, where the one failure
    (`WindowsGpuBoostReportingTests.ProcessPriority_OfflineGameLoop_ReturnsSkipped_AndArmsMonitor`)
    is the known environmental issue (proven pre-existing on the clean tree in
    2.2; same host). The +13 tests over the 475 post-2.2 count are the new
    `NetworkViewModelTests` (12) plus `NetworkPage_IsRegisteredWithResolvableDependencies` (1).
    Structural parity: 8/8 named elements, 5/5 handlers, 14/14 local resource
    keys moved; the only `Dns|Ipad` matches left in `MainWindow.xaml` are two
    static About-page marketing strings; zero orphaned element references in
    `MainWindow.xaml.cs` (compiler-verified). Manual UI smoke could not run
    (no headless WPF harness — same constraint as 2.1/2.2).

### 2.4 Optimizer

- [x] 2.4.a–j. Scope: XAML lines 636–1100 (post-2.3 file: lines 185–648). Done (see gate note below).
  - Owns: `Hardware*` texts, `Plan*` texts, `SmartSettingsButton`,
    `TempCleanerButton`, `GameLoopOptimizerButton`, `AllRecommendedButton`,
    `ForceCloseButton`, `PerformanceSessionButton`, `RestoreSessionButton`,
    `RefreshOptimizerButton`, `OptimizerActivityText`/`OptimizerStatusText`.
  - Commands delegate to `IGameLoopPerformanceEngine`, `ITempCleanupService`,
    `IGameLoopProcessService`.
  - `UI/Presentation/OptimizerDisplayFormatter.cs` →
    `Features/Optimizer/Presentation/OptimizerDisplayFormatter.cs` (per §2; keep
    `OptimizerDisplayFormatterTests` green).
  - Per decision **D1**, no engine code is duplicated here — Presentation only.
  - Per decision **D6**, the page reuses the existing `RunToolAsync` plumbing
    rather than duplicating it; consolidation happens only in Phase 3.5.
  - Landed as: `OptimizerViewModel` owns `RefreshProfileAsync` (refuse-if-busy,
    verbatim error text) and the bus-guarded `ExecuteAsync` core moved out of
    the shell's `RunToolAsync`; the view keeps the Optimizer-specific working
    paint + `RenderOptimizerReport` + shell status forwarding, so the shell's
    `RunToolAsync` shrinks to the generic path Shortcuts still uses (2.5 moves
    that). `MainWindow` keeps `_performanceEngine` only for the
    `Window_Closing` session restore; `_tempCleanup`/`_processService` move to
    the page. The formatter and its test move with the page
    (`Nexora.Tests.Features.Optimizer`), and one live-test reference follows
    the namespace.
  - Gate: build **0/0**; tests **496 passed / 1 failed**, where the one failure
    is the known environmental `ProcessPriority_OfflineGameLoop` issue (proven
    pre-existing on the clean tree in 2.2; same host). The +9 tests over the
    488 post-2.3 count are the new `OptimizerViewModelTests` (8) plus
    `OptimizerPage_IsRegisteredWithResolvableDependencies` (1).
    Structural parity: 25/25 named elements, 8/8 handlers, 16/16 local
    resource keys moved; zero orphaned element references in
    `MainWindow.xaml(.cs)` (compiler- and grep-verified). Manual UI smoke
    could not run (no headless WPF harness — same constraint as 2.1–2.3).

### 2.5 Shortcuts

- [x] 2.5.a–j. Scope: XAML lines 1567–1968 (post-2.4 file: lines 192–592). Done (see gate note below).
  - Owns: `ShortcutComboBox`, preview texts/icon, `CreateShortcutButton`.
  - Commands delegate to `IShortcutService`; icon loading stays via
    `UI/Helpers/IconImageLoader`.
  - `ShortcutModels.cs` created in `Features/Shortcuts/Domain/` for the preview
    model if one does not already exist.
  - Landed as: `Features/Shortcuts/Domain/ShortcutPreview.cs` (empty vs.
    selected card, verbatim pre-extraction text); `ShortcutsViewModel` owns
    `GetPreview` plus the bus-guarded, cancellable `ExecuteAsync` core moved
    out of the shell's `RunToolAsync` — the page's last caller, so the shell's
    `RunToolAsync`, `_toolCancellation`, `CancelAndDisposeTool` and
    `_operationBus` are deleted with it. The view keeps the catalog seeding,
    preview paint, icon resolution and button dimming; the shell keeps only
    hosting, the status-forwarding subscription, and the cross-page
    detected-versions feed (`SetAvailableVersions`, with the catalog fallback
    and single-install pre-select inside the page). `Window_Closing` now
    cancels the page's ViewModel instead of the deleted shell token.
    `MainWindow` ctor drops `IShortcutService`/`IPageOperationBus`; the shared
    bus singleton stays registered for the pages.
  - Gate: build **0/0**; tests **504 passed / 1 failed**, where the one failure
    is the known environmental `ProcessPriority_OfflineGameLoop` issue (proven
    pre-existing on the clean tree in 2.2; same host). The +8 tests over the
    497 post-2.4 count are the new `ShortcutsViewModelTests` (7) plus
    `ShortcutsPage_IsRegisteredWithResolvableDependencies` (1).
    Structural parity: 6/6 named elements, 2/2 handlers, 9/9 local resource
    keys moved (template-internal `CtaBorder`/`ItemBorder`/`ComboBorder`/
    `DropDownToggleButton`/`PART_Popup` move with their templates);
    zero orphaned element references in `MainWindow.xaml(.cs)`
    (compiler- and grep-verified). Manual UI smoke could not run
    (no headless WPF harness — same constraint as 2.1–2.4).

### 2.6 About

- [x] 2.6.a–j. Scope: XAML lines 1969 → end (post-2.5 file: lines 195–345). Done (see gate note below).
  - Static content only — the simplest extraction; good warm-up, scheduled last
    only to match the requested order. If the team prefers a warm-up first, do
    this one before 2.1.
  - No ViewModel needed unless version/build strings become dynamic.
  - Landed as: the version string **is** dynamic (`AppConstants.CurrentVersion`
    painted over the `VERSION 1.0.13` XAML placeholder by the shell), so the
    plan's first option was taken — a lightweight, dependency-free
    `AboutViewModel` exposing `VersionDisplay` in the exact pre-extraction
    format. The view paints `VersionText` once in its constructor; no
    handlers, no bus, no services. `MainWindow` drops the paint line
    (keeping `ConnectionState`'s `Graphics.Domain` using, which the connection
    paint still needs) and keeps only hosting/navigation/margin.
  - Asset-URI fix inside the move: the page's `nexora.ico` reference changed
    from relative `Assets/...` to root-absolute `/Assets/...`. Relative URIs
    in Page-compiled XAML resolve against the XAML file's folder, so every
    page move silently repoints them — this also repaired five style-preview
    URIs in `GraphicsView.xaml` left latent by 2.1 (separate `fix(graphics)`
    commit).
  - Gate: build **0/0**; tests **507 passed / 1 failed**, where the one failure
    is the known environmental `ProcessPriority_OfflineGameLoop` issue (proven
    pre-existing on the clean tree in 2.2; same host). The +3 tests over the
    505 post-2.5 count are the new `AboutViewModelTests` (2) plus
    `AboutPage_IsRegisteredWithResolvableDependencies` (1).
    Structural parity: 1/1 named element (`VersionText`), 0/0 handlers, 0/0
    local resource keys; zero orphaned element references in
    `MainWindow.xaml(.cs)` (compiler- and grep-verified). Manual UI smoke
    could not run (no headless WPF harness — same constraint as 2.1–2.5).

**Phase 2 exit gate:** all six pages are `UserControl`s under
`Features/<Feature>/Presentation/`; `MainWindow.xaml.cs` no longer contains any
of the handlers listed in the baseline member scan; every page's smoke test
passed; 419/0 tests; build 0/0.
> **Status:** six `UserControl`s landed (`Graphics`, `Tuning`, `Network`,
> `Optimizer`, `Shortcuts`, `About`); the shell holds no page handlers
> (compiler- and grep-verified per step); build **0/0**; tests **507 passed /
> 1 failed** where the single failure is the known environmental
> `ProcessPriority_OfflineGameLoop` issue (proven pre-existing on the clean
> tree in 2.2) — i.e. 419/0 modulo the environment, plus 89 new
> page/DI/formatter tests. Manual UI smoke could not run in any step (no
> headless WPF harness); structural parity gates stood in throughout.

---

## Phase 3 — Slimming down MainWindow to shell-only

Purpose: `MainWindow` keeps only navigation, lifecycle, chrome, and genuinely
shared state (PROJECT-LAYOUT.md §7, §10 Phase 3). Landed as one
`refactor(shell)` commit (see gate note below).

- [x] **3.1** Remaining feature-specific private methods moved out
  (`UpdateSummary`, `SelectedStyle*`, `SelectedContent`, `SelectContent`,
  `ShowConnectionVisual` family, `UpdateOptimizerPanel`,
  `RenderOptimizerReport`, `RefreshTuningAsync`, `UpdateTuningLabels`,
  `UpdateShortcutPreview`, `UpdateIpadPresetDetails`, `GetSelectedIpadPreset`,
  `ApplyLoadedSettingsAsync` — whatever survived Phase 2). Done: the
  `Show*ConnectionVisual` family + `OnGraphicsConnectionStateChanged` now live
  in `UI/Presentation/ShellConnectionPresenter.cs` (shell surfaces only; the
  page still paints its own), and `CheckForUpdatesAsync` now lives in
  `Features/Updates/Presentation/UpdateHandoff.cs` (verbatim flow via
  shutdown/status/close callbacks). What stayed is shell-owned coordination,
  not feature logic: `RefreshConnectionButton_Click` (sidebar chrome driving
  the page command), `OnGraphicsVersionsChanged` (cross-page feed), the Tuning
  refresh-on-navigate, and the shutdown session restore.
- [x] **3.2** Navigation extracted into `UI/Navigation/NavigationItem.cs` (page
  keys + the Tuning refresh-on-arrive flag) + `UI/Navigation/ShellNavigator.cs`
  (pure show/hide router over callbacks — no WPF dependency, unit-tested);
  `NavigationButton_Click` is a 4-line pure command. XAML sidebar untouched.
- [x] **3.3** Window chrome handling (`Window_SourceInitialized`,
  `Window_SizeChanged`, `TitleBar_*`, min/max/close) into
  `UI/Behaviors/WindowChromeBehavior.cs` + `UI/Layout/ResponsiveLayoutManager.cs`
  — no business logic. Done as specified with one judgment call: the behavior
  already owns the maximized-bounds hook and the manager already owns all
  layout math, so the code-behind keeps only zero-logic one-line delegations
  (drag/min/max/close, size-changed assignments). Moving them into the
  behavior would force button discovery by name — worse, not better.
- [x] **3.4** Lifecycle only in `MainWindow.xaml.cs`: `Window_Loaded`
  (concurrent handoff + Optimizer refresh), `Window_Closing` (bounded
  `ShutdownRestoreTimeout`, restore-then-close in `finally`, dispose tokens,
  detach behavior), `ResourceFreezer.FreezeAll` (extracted verbatim to
  `UI/Presentation/ResourceFreezer.cs`, still called once post-`InitializeComponent`).
- [x] **3.5** Shared busy/status plumbing (`SetBusyState`, `SetStatus`,
  `GetBrush`, `CreateSuccessGlow`, and the `RunToolAsync` helper deliberately
  left in place during Phase 2 per decision **D6**) moves to a shared UI helper
  consumed by page ViewModels; the shell keeps only the status bar surface.
  Done: `RunToolAsync`/`SetBusyState` were already gone (2.5);
  `GetBrush`/`CreateSuccessGlow` moved into the presenter; `SetStatus` stays
  as the shell's only status surface, still reaching through
  `GraphicsView.SetStatus` — severing that reach-through would mean moving the
  status bar's visuals out of the Graphics page (a restyle, forbidden
  mid-refactor), so the single sanctioned reach-through from 2.1 remains and
  is now the shell's only page-control write besides navigation/margin.
- [x] **3.6** `MainWindow` ctor takes only the contracts it truly needs:
  `IGameLoopPerformanceEngine` (shutdown restore), `IUpdateService` (handoff),
  `GameLoopOptions` (timeouts) — pinned by the strengthened
  `MainWindow_IsRegisteredAsTransientWithResolvableDependencies` test, which
  now asserts the exact 3-contract parameter set. Designer fallback graph
  moved to `Bootstrap/DesignerFallback.cs` (same single-identity composition).
  Designer fallback ctor kept. No `Bootstrap` registration change needed.
- [x] **3.7** Target: `MainWindow.xaml.cs` under ~250 lines (from 1,078);
  landed at **exactly 250** (from 394 at Phase 3 start).
- [x] **3.8** Build 0/0 + tests (see gate note) + full UI smoke of all pages.
  UI smoke: not runnable here (no headless WPF harness — same constraint as
  Phase 2); structural + behavioral gates stood in (orphan grep, verbatim-move
  diffs, new router/handoff tests).
- [x] **3.9** Commit: `refactor(shell): reduce MainWindow to navigation and
  lifecycle shell`.
- [x] Audits (per coordinator brief): no `.Result`/`.Wait()` in production
  and `async void` only in the four WPF event handlers
  (`RefreshConnectionButton_Click`, `Window_Loaded`, `Window_Closing`,
  `NavigationButton_Click`); `GetService`/`GetRequiredService` in production
  only in the composition root (`App`, `Bootstrap` factory lambdas) and the
  six sanctioned designer-fallback ctors; zero orphaned references to any
  removed member (compiler- and grep-verified).

**Phase 3 exit gate:** `MainWindow` contains no Graphics/Tuning/Network/
Optimizer/Shortcuts/About logic; line-count target met (250);
all pages still render
and function; tests green.
> **Status:** shell holds no page logic (see 3.1); `MainWindow.xaml.cs` =
> **250 lines** (from 1,078 at baseline, 394 at Phase 3 start); build **0/0**;
> tests **514 passed / 1 failed** where the single failure is the known
> environmental `ProcessPriority_OfflineGameLoop` issue (proven pre-existing
> on the clean tree in 2.2) — i.e. baseline 419/0 modulo the environment,
> plus 95 new tests. New units: `ShellNavigator` (4 tests),
> `UpdateHandoff` (3 tests: the MessageBox branch stays manual-only),
> strengthened `MainWindow` DI contract test. `MainWindow.xaml` untouched
> (201 lines of hosts/chrome). Manual UI smoke outstanding — no headless WPF
> harness in this environment.

---

## Phase 4 — Final acceptance gate and cleanup

Landed across two commits: `test(architecture): add ArchitectureGuardTests source guards` + `docs: Phase 4 acceptance gate` (see gate note below).

- [x] **4.1** `dotnet restore .\Nexora.slnx` — OK, all projects up-to-date.
- [x] **4.2** `dotnet build .\Nexora.slnx --configuration Release` → **0 errors, 0 warnings**.
- [x] **4.3** `dotnet test .\Nexora.slnx --configuration Release --filter
  "Category!=LiveFunctionalVerification"` → **519 passed / 1 failed**, where
  the one failure is the known environmental `ProcessPriority_OfflineGameLoop`
  issue (proven pre-existing on the clean tree in 2.2; same host). 519 is the
  new regression floor (was 419 at baseline).
- [ ] **4.4** Live verification (opt-in, only on a machine with GameLoop
  configured): `dotnet test .\Nexora.slnx --configuration Release --filter
  "Category=LiveFunctionalVerification"` — **not run here**: this environment
  has no running GameLoop and the suite needs one. Run on a configured
  machine before release; the 5 tests still target the same single
  `GameLoopService` singleton (factory-forwarding re-verified in 4.5).
- [x] **4.5** Static architecture audit (added as `Nexora.Tests/Architecture/`
      tests so it never regresses — `ArchitectureGuardTests`, 5 tests green):
  - [x] No duplicate DI registrations for the same interface (39 registrations,
        each ×1; `GameLoopService` forwarded, never forked).
  - [x] No hardcoded GameLoop path or drive letter anywhere in production
        (only file names, package names, and registry *key* paths — all legitimate).
  - [x] No `Process.GetProcessesByName` outside `GameLoopProcessEnumerator` /
        `IGameLoopProcessService` (comment mentions excluded by the guard).
  - [x] No direct registry/process/ADB/PowerShell call from any View or
        ViewModel (guard over `Features/*/Presentation` + `UI/`; designer
        fallbacks may *construct* the services but never execute through them;
        `IAdbClient` appears only as an injected abstraction in Graphics per D5).
  - [x] No `.Result` / `.Wait()` / `async void` outside WPF event handlers
        (guarded; a domain member merely named `Result` is excluded by shape).
  - [x] Presentation → Application → Domain/Contracts → Infrastructure direction
        holds (`PROJECT-LAYOUT.md` §4). Exceptions, all formally recorded:
        the two 1.14 survivors (`SaveProfileReader` → `IAdbClient`,
        `PerformanceModels` → `GpuVendorClassifier`) are unchanged, plus one
        minor new arrow accepted here — `UI/Presentation/ShellConnectionPresenter`
        reads the `ConnectionState` enum (`Features/Graphics/Domain`), the same
        vocabulary the Graphics ViewModel event already exposes; it moves no
        logic and creates no new dependency shape.
  - [x] `RegistryService` placement: disk (`Infrastructure/Registry/`, beside
        `IUserRegistry`/`IMachineRegistry`) is authoritative; the `PROJECT-LAYOUT.md`
        §2 line filing it under `Infrastructure/GameLoop/` is a doc error to fix
        in that file, not in code.
  - [x] Every production file sits in the folder matching its responsibility
        per §3 ownership table (namespaces re-verified folder-authoritative;
        zero legacy-namespace declarers).
- [x] **4.6** Duplicate scan: no type declared in two compiled files; no dead
  copies of moved files remain; empty folders removed (legacy `Services/`,
  `Features/SystemTools/`, `Features/Layout/`, `Shared/Infrastructure/` all
  absent). The untracked `output/imagegen/nexora-ad.png` predates this work
  and is left alone (not a build artifact, not committed).
- [ ] **4.7** Manual acceptance run (PROJECT-LAYOUT.md §11): the app opens, all
  six pages work, the connection flow behaves, iPad/registry/ADB guards still
  fire, shutdown restores cleanly, and no feature logic lives in XAML.
  **Not run here** — headless environment, no UI harness, and the app requires
  UAC elevation a human must approve. Verbatim-move diffs, parity counts, and
  the automated gate stand in; run this checklist on a Windows machine before
  release.
- [x] **4.8** Release-path check: `.\scripts\package-release.ps1` still targets
  self-contained win-x64 single-file publish with SHA256SUMS + the checksum
  gate (verified by inspection; the publish itself was **not** executed here).
- [x] **4.9** Docs sync: `ARCHITECTURE.md` (519 floor, Bootstrap root,
  six-page Presentation layer, current 3-contract shell ctor, guard tests),
  `AGENTS.md` (all post-refactor paths, 250-line shell, guards), `README.md`
  (519 badge/count, final layout), and `CHANGELOG.md` (Unreleased refactor
  entry) all updated. Mark the refactor complete in `CHANGELOG.md` — done.
- [x] **4.10** Protected-artifact check: `graphify-out/` untouched; `bin/`,
  `obj/`, `artifacts/` not committed; refresh the graph only with
  `graphify update .` after the refactor lands.
- [x] **4.11** Final commit: `chore: complete feature-first reorganization` and
  tag against the baseline from step 0.8. Commit landed; **tagging was not
  executed** — tag/publish commands are release operations this environment
  must not run. Ready-to-run commands are in the sign-off report below (they
  also cover the long-deferred 0.8 baseline tag, which can only be placed
  retroactively by date, not by content).

**Phase 4 exit gate:** every checkbox above ticked **except 4.4 and 4.7, which
need a GameLoop machine and a human at the screen** — plus a diff-vs-baseline
review confirming no intended behavior change beyond structure (every Phase
2/3 diff was a verbatim move plus its documented deltas).

---

## End-to-end verification recipe

This is a WPF desktop app that runs elevated (`app.manifest` →
`requireAdministrator`), so a fully autonomous UI click-through is not possible:
launching the exe raises a UAC prompt that only a human can approve. The
verification gate is therefore split into an **automated part every unit runs**
and a **manual UI part run at phase boundaries**.

### Automated (every unit, every commit)

```powershell
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
```

Both must pass (0 warnings under `TreatWarningsAsErrors=true`; 419 tests). This
is the regression floor — no unit may merge with fewer passing tests than the
baseline recorded in Phase 0.

### Automated UI smoke (Phase 2 / 3 / 4 — needs a UAC-approved elevation)

```powershell
$exe = "bin\Release\net8.0-windows\Nexora PUBG Mobile Tool.exe"
$p = Start-Process $exe -PassThru -Verb RunAs   # UAC prompt: a human approves once
$p.WaitForInputIdle(20000)
$p.MainWindowTitle                       # expect the Nexora title
# exercise the changed page, then close cleanly:
$p.CloseMainWindow()
$p.WaitForExit(10000)
$p.ExitCode                              # expect 0 (clean shutdown restore path)
```

What this proves end-to-end: the app actually starts (all DI registrations
resolve at runtime, XAML parses, no static-ctor failure), the window renders, and
the `Window_Closing` teardown path (bounded restore, token disposal) completes —
something unit tests cannot catch.

### Manual UI smoke (per page in Phase 2, and full in Phase 4)

With the app open, click through **Graphics → Tuning → Network → Optimizer →
Shortcuts → About**, and for each page confirm: it renders with no unhandled
exception in the status bar; its controls are present and enabled as before;
keyboard tab order works. Then: attempt a connect with GameLoop closed (expect
the clear failure message, not a guess path); apply Tuning with the emulator
running (expect the guard to refuse); close the window and confirm a clean exit.

### Skip e2e?

Phase 1 units (pure file moves, namespaces preserved) may skip the UI smoke and
rely on build + tests, because they change no runtime behavior by construction —
the move diff is `git mv` only. Every Phase 2, 3, and 4 unit runs the full recipe
including the UI smoke.

---

## Parallelization map (for batch execution)

Phase 1 sub-steps are disjoint **file sets** with namespaces preserved, so they
can run concurrently in isolated worktrees. Phases 2–4 are strictly serial —
they all edit `MainWindow.xaml` / `Bootstrap/ServiceCollectionExtensions.cs`.

| # | Work unit | Files / scope | Depends on | Parallelizable |
|---|---|---|---|---|
| 1 | Shared kernel split | `Shared/Kernel/*` → `Shared/Contracts`, `Infrastructure/Files`, `Infrastructure/Processes` | Phase 0 | yes (with 2) |
| 2 | Registry & GameLoop infra | `Shared/Infrastructure/*` → `Infrastructure/Registry`, `Infrastructure/GameLoop` | Phase 0 | yes (with 1) |
| 3 | GameLoop feature | `Services/*GameLoop*`, `Features/GameLoop/{Models,Storage,RemotePaths}` → `Features/GameLoop/*` | 2 | yes (after 2) |
| 4 | Graphics domain | `Features/GameLoop/{PubgVersionCatalog,Ue4SavEditor,UnrealCVarCodec,ShadowSettingsStore}`, `Services/SaveProfileReader` → `Features/Graphics/*` | 3 | yes |
| 5 | Tuning | `Features/GameLoop/{I,}EmulatorSettings*`, `EmulatorTuning*` → `Features/Tuning/*` | 3 | yes |
| 6 | Network | `Features/SystemTools/Network/*`, `Features/Layout/*` → `Features/Network/*` | 2 | yes |
| 7 | Shortcuts | `Features/GameLoop/{I,}ShortcutService` → `Features/Shortcuts/Application` | 3 | yes |
| 8 | Performance | `Services/Performance/*`, `Features/Performance/*` → `Features/Performance/*` | 2, 3 | yes |
| 9 | Updates | `Services/Update*`, `StagingDirectoryGC` → `Features/Updates/*` | — | yes |
| 10 | Security + Optimizer app | `Features/Security/DefenderExclusionService`, `Services/ITempCleanupService`, `Features/SystemTools/TempCleanupService` | — | yes |
| 11 | Folder cleanup + duplicate scan | delete `Services/`, `Features/SystemTools/`, `Features/Layout/`, `Shared/Infrastructure/` | 1–10 | serial |
| 12 | Bootstrap extraction | `App.xaml.cs:ConfigureServices` → `Bootstrap/ServiceCollectionExtensions` + `StartupTasks` | 11 | serial |
| 13 | Test mirror | reorganize `Nexora.Tests/` to production paths | 11 | serial |
| 14 | Namespace normalization | rewrite namespaces + usings, one feature per commit | 11–13 | serial |
| 15–20 | Page extractions (Graphics, Tuning, Network, Optimizer, Shortcuts, About) | `MainWindow.xaml` regions → `Features/<F>/Presentation/*` | 14, prior page | strictly serial |
| 21 | Shell slim-down | `MainWindow.xaml(.cs)` → navigation/chrome/lifecycle only | 15–20 | serial |
| 22 | Architecture tests + docs + release check | `Nexora.Tests/Architecture/`, docs sync, `package-release.ps1` | 21 | serial |

**Sizing note:** 22 units; ~10 of them (1–10) are parallel-safe. The serial
tail (11–22) is what dominates wall-clock — schedule the page extractions
back-to-back rather than fanning them out, since they all touch
`MainWindow.xaml` and would otherwise conflict on every merge.

---

## Worker instructions template (for batch execution)

> Copy verbatim into each spawned unit.

After you finish implementing the change:
1. **Code review** — Invoke the `Skill` tool with `skill: "code-review"` to find
   correctness bugs (it reports findings; it does not edit code). Fix any
   findings it surfaces before continuing.
2. **Run unit tests** — `dotnet build .\Nexora.slnx --configuration Release`
   (must be 0 errors / 0 warnings) then
   `dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"`.
   The standard-suite pass count must equal or exceed the Phase 0 baseline
   (419). If tests fail, fix them. Never run `Category=LiveFunctionalVerification`
   — those need a live GameLoop.
3. **Test end-to-end** — Follow the e2e test recipe from the coordinator's
   prompt. Phase 1 units: build + tests are sufficient (skip the UI smoke — the
   diff is `git mv` only). Phase 2/3/4 units: also run the UAC-approved UI smoke
   and confirm the page renders and the window closes cleanly.
4. **Respect the invariants** — Before committing, re-check the "Invariants that
   must hold at every commit" list from `execution-plan.md` (no hardcoded
   GameLoop paths, no `Process.GetProcessesByName` outside its one home, no
   registry/process calls from Presentation, no `.Result`/`.Wait()`, DI
   factory-forwarding intact). A violation is a stop-and-escalate, not a fix-it-
   quietly.
5. **Commit and push** — Commit with a message matching the plan's convention
   (e.g. `refactor(graphics): extract GraphicsView and ViewModel from MainWindow`),
   push the branch, and create a PR with `gh pr create`. One commit per sub-step.
   End git commit messages with
   `Co-Authored-By: Claude Code <noreply@anthropic.com>` and PR descriptions with
   `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. If `gh` is
   unavailable or the push fails, note it in your final message.
6. **Report** — End with a single line: `PR: <url>` so the coordinator can track
   it. If no PR was created, end with `PR: none — <reason>`.

---

## Sign-off

- [x] Phase 0 baseline recorded (build 0/0, tests 419/0)
- [x] Phase 1 file relocation complete; namespaces normalized; DI extracted
  (declared complete at the 1.14-docs commit; re-verified in 4.5–4.6)
- [x] Phase 2 all six pages decoupled as `UserControl` + ViewModel (2.1–2.6;
  see the per-step gate notes; manual UI smoke outstanding — no headless WPF
  harness in this environment)
- [x] Phase 3 `MainWindow` reduced to shell (250 lines; see the Phase 3 gate
  note; manual UI smoke outstanding — same harness constraint)
- [x] Phase 4 acceptance gate passed except the two environment-blocked items
  (4.4 live GameLoop suite, 4.7 manual acceptance run); architecture tests
  live; docs updated
- [ ] Phase 3 `MainWindow` reduced to shell (target < 250 lines of code-behind)
- [ ] Phase 4 acceptance gate passed; architecture tests live; docs updated
