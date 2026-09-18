# Nexora PUBG Mobile Tool — QA / Performance Audit Report

**Date (UTC):** 2026-09-18 · **Auditor:** senior QA + WPF performance pass, read-only (no source file was created, edited, or deleted; no commits; no release commands — this report file is the sole artifact, written at explicit user request)
**Claim discipline:** every item is tagged **[OBSERVED]** (measured/logged) or **[HYPOTHESIS]** (inferred, needs confirmation). Paths carry exact `file:line`.

---

## 1. Environment & method

| Item | Detail |
|---|---|
| OS / build | Windows 11 Pro, `10.0.26200` (2009) |
| CPU / RAM | 12th-gen i7-1255U (10 cores), 16 GB |
| GPU / display | NVIDIA MX550 + Intel Iris Xe; single screen **1920×1080, 100% scaling** (`AppliedDPI=96`, no `LogPixels` override) |
| Privilege | Shell already elevated (High Mandatory Level) — no UAC friction encountered |
| .NET SDK / git | `10.0.400`; HEAD `36a1df2` (working tree has unrelated uncommitted moves — left untouched) |
| Binary used | **Fresh Release build** `bin\Release\net8.0-windows\Nexora PUBG Mobile Tool.exe` (`dotnet build .\Nexora.slnx -c Release` → **0 warnings, 0 errors**). The shipped `artifacts\Nexora-v1.1.0-win-x64.exe` (built 8:02 PM) was deliberately NOT used after the event log showed it still contains the startup crash (see F-003) |
| Tests | `dotnet test --filter Category!=LiveFunctionalVerification` → **416/416 passed, 17 s** |
| GameLoop | Registry present (`HKLM\…\Tencent\MobileGamePC`, `HKCU\…\MobileGamePC`). **No emulator processes at session start; GameLoop auto-started itself mid-session at 8:20:53 PM** (AppMarket → `AndroidEmulatorEn` → ~40 `aow_exe`). All kill/apply actions after that point were deliberately skipped per safety rules |
| Driving method | UI Automation (verify state) + synthetic mouse clicks at measured rects (navigate). Clicks only ever switched views / moved sliders / selected combo items / CONNECT / window X. **Never clicked:** Tuning APPLY, END TASK, FORCE CLOSE, Graphics APPLY, DNS apply/reset, iPad APPLY, shortcut CREATE, any Optimizer apply action |
| Event log | Baseline read before launch; delta read after each run/close |

**Side effects of this session:** none. Emulator left running untouched (45 `aow_exe`/`AndroidEmulatorEn`/`AppMarket` processes + its own `adb.exe` pid 21456 all intact at session end); no registry writes (the only connect-time write, `Services/GameLoopConnector.cs:90`, fires only when `AdbDisable==1`; value observed `0`); slider drags were local-UI-only (APPLY never clicked).

---

## 2. Findings table

| # | Sev | Page | Repro steps | Evidence | Suspected root cause |
|---|---|---|---|---|---|
| F-001 | **Critical** | All (shutdown) | Close the app via X (or any `Close()` path) | **[OBSERVED]** 2/2 closes this session crashed: `Application Error 1000` + `.NET Runtime 1026`, `InvalidOperationException: Cannot set Visibility… while a Window is closing`, stack `Window.VerifyNotClosing → InternalClose → Nexora.MainWindow.Window_Closing … MainWindow.xaml.cs:line 438`. Same signature twice in pre-session baseline (8:03, 8:10 PM). PDB path in log confirms current-source build | `Window_Closing` (`MainWindow.xaml.cs:406-440`) `await`s the 5 s restore, then calls `Close()` in `finally` (`:438`). **[HYPOTHESIS, high confidence]** The deferred `Close()` races an in-flight close (double-X, or shutdown-initiated close): second `Closing` hits the `_closeRequested` guard and returns *without* `e.Cancel=true`, so the window is already closing when the stale continuation's `Close()` runs → `VerifyNotClosing` throws. Unhandled (async void) → process terminated. Note `FIX-PLAN-100-PERCENT.md:70` already tracks this item |
| F-002 | **Critical** (perceived as "lag") | Tuning | Click Tuning nav (3rd sidebar entry) | **[OBSERVED]** Two timed opens: **6068 ms and 5284 ms** from click to sliders interactive (UIA provider itself blocked — classic frozen-UI-thread signature). No crash, page then fully functional | `EmulatorSettingsService.LoadAsync` (`Features/GameLoop/EmulatorSettingsService.cs:30-35`) is **fake-async**: `await Task.CompletedTask` (no yield), then `_hardwareSnapshot()` runs **synchronously on the UI thread**. Default snapshot (`:27`) = `HardwareDetectionService.GetSnapshot()` → spawns `powershell.exe` + 4× `Get-CimInstance` (`Services/Performance/HardwareDetectionService.cs:57,89-108`), bounded only by `HardwareDetectionTimeout=20 s` (`Configuration/GameLoopOptions.cs:61`). Same sync snapshot in `ApplyAsync` (`EmulatorSettingsService.cs:88`) → APPLY will freeze likewise (code-evident, click untested). The 15 s CTS in `RefreshTuningAsync` (`MainWindow.xaml.cs:552`) cannot pre-empt the sync block |
| F-003 | **Critical** (for artifact users) | Startup (stale artifact only) | Launch `artifacts\Nexora-v1.1.0-win-x64.exe` | **[OBSERVED]** Baseline log: 2× `NullReferenceException at Nexora.MainWindow.UpdateTuningLabels ← TuningCpuSlider_ValueChanged ← … ← InitializeComponent` (7:58 PM). **[OBSERVED]** Current source HAS the guard (`MainWindow.xaml.cs:599-604`) and the fresh build launched clean 3× with zero startup exceptions | XAML `Value="4"` on `TuningCpuSlider` (`MainWindow.xaml:1164`) fires `ValueChanged` during `InitializeComponent` while `TuningCpuText` is still null. Fixed in source, but the **published artifact predates the fix** → anyone running v1.1.0 gets an instant startup crash. Fix = rebuild/republish, not code |
| F-004 | Major | Tuning (sibling class of F-003) | Static + runtime check of every init-time event | **[OBSERVED]** Swept all XAML-wired `Checked=/ValueChanged=/SelectionChanged=`: only the two Tuning sliders carry XAML `Value=` + handler (`MainWindow.xaml:1164,1169`); both now funnel into the guarded `UpdateTuningLabels`. Nav `IsChecked="True"` (`:127`) uses `Click`, not `Checked` → silent during init. Segment/style radios get `IsChecked` in ctor post-init (`MainWindow.xaml.cs:120-122`). CheckBox `IsChecked="True"` (`MainWindow.xaml:1179-1206`) have no handlers. Combos guarded (`DnsComboBox_SelectionChanged :474`, `UpdateIpadPresetDetails :521`, `UpdateShortcutPreview :646-656`, `PubgVersionComboBox_SelectionChanged :215` — all null/shape-checked). **No unguarded sibling found** | — (closed, no action) |
| F-005 | Minor | Graphics/Network | Select DNS provider in combo | **[OBSERVED]** Selecting Cloudflare → `Cloudflare DNS • Ping: 4ms • Ready to apply` — handler correct, ping-only, no system change. Code note: continuation uses sync `Dispatcher.Invoke` (`MainWindow.xaml.cs:483`) although already on the UI thread after `await` (redundant, not dead-locking; shutdown-throw is caught) | Cleanup: replace with direct assignment + `Dispatcher.HasShutdownStarted` check (pattern already used at `:479`) |
| F-006 | Minor | All (startup path) | Code read + cold-start timing (handle 2.1 s, interactive 3.3 s — healthy) | **[OBSERVED]** `Window_Loaded` (`MainWindow.xaml.cs:288-318`) awaits `UpdateService.CheckAsync()` (GitHub HTTPS, `HttpTimeout=12 s`, `Configuration/UpdateOptions.cs:26`) **sequentially before** `RefreshOptimizerProfileAsync()`. **[HYPOTHESIS]** On offline/slow networks the Optimizer panel stays empty up to ~12 s+ even though the two tasks are independent. Not measured offline (network was up) | Run both concurrently; never gate panel fill on update check |
| F-007 | Minor | All (a11y/testability) | UIA enumeration of nav | **[OBSERVED]** All six nav `RadioButton`s expose empty `AutomationId/Name` (icon `Path` + `TextBlock`, `MainWindow.xaml:127-174`); UIA children of the window intermittently report placeholder rects until the window is foregrounded/restored | Add `AutomationProperties.Name` per nav entry ("Graphics", "Optimizer", …). Zero runtime risk |
| F-008 | Minor | Tuning/Network | Intermittent probe failure | **[OBSERVED]** One scripted Tuning click in a double-navigation sequence produced no page switch (app healthy on Graphics, responsive). All single deliberate clicks (10+) landed. No log entries | **[HYPOTHESIS]** Missed synthetic click (focus/timing), not an app defect — but it shows nav gives no feedback while `RefreshTuningAsync` runs (`_isBusy` isn't set, no spinner). Consider a loading indicator + `AutomationProperties` (links F-002, F-007) |

**Static-sweep negatives (checked, clean):** no `.Result`/`.Wait()`/`GetAwaiter().GetResult()` in prod code (only `OperationResult.Result` false positives); **zero timers/polling loops** (`DispatcherTimer`/`Timer` — none); zero `TODO/FIXME` in prod; zero hardcoded `C:\`/`Program Files`/emulator paths in `Features/`, `Services/`, `Shared/` (all GameLoop paths via `IGameLoopPathResolver`); DI registrations in `App.xaml.cs:44-82` cover all 12 `MainWindow` ctor params (launch success = proof); all registry writes HKCU-scoped with write-then-read-back verify (`EmulatorSettingsService.cs:108`), running-emulator guards (Tuning apply, iPad `IpadLayoutService.cs`), and conditional ADB auto-enable (`GameLoopConnector.cs:88-91`); `ResponsiveLayoutManager` is pure math (no thrash — `SizeChanged` only sets two `GridLength`s + margins, `MainWindow.xaml.cs:696-709`); `UpdateSummary` (`:953-964`) is O(controls) reads; no storyboards/animations (0 matches); 416/416 tests green.

---

## 3. Performance analysis (ranked by user impact)

1. **Tuning page open freezes UI ~5–6 s — the reported "heaviness." [OBSERVED]** Evidence §F-002. Offenders, all on the UI thread: `powershell.exe` spawn + `Win32_Processor/VideoController/ComputerSystem/Battery` CIM (≈5 s measured, 20 s worst-case cap). No amount of layout tuning fixes this; it's a threading bug. **Fix (high impact, low effort):** in `EmulatorSettingsService`, use `GetSnapshotAsync` (already `Task.Run`-backed, `HardwareDetectionService.cs:23-30`) or inject a cached per-session snapshot (hardware doesn't change at runtime); add a loading state; make the 15 s CTS actually pre-emptive. Same fix covers `ApplyAsync`.
2. **Startup sequencing risk (12 s panel stall when offline). [HYPOTHESIS]** Evidence §F-006. **Fix (medium/low):** `await Task.WhenAll(updateCheck, optimizerRefresh)`; render panel independently.
3. **28 `DropShadowEffect` definitions + 9 live `Effect=` usages, large per-page radial glows/ellipses (`MainWindow.xaml`, `App.xaml`). [HYPOTHESIS]** Each forces software rasterization of its subtree; with 6 fully-built page grids (≈106 live UIA elements, 2114-line XAML) this is the classic WPF resize/scroll drag source. Honestly it did **not** manifest in testing (resize/maximize/scroll all smooth on Iris Xe), so rank is contributory, not causal. **Fix (low/low):** freeze shared effects (`Freeze`), cut duplicate glows, keep only visible-page accents.
4. **Sync icon decode on UI thread in ctor path** (`UpdateShortcutPreview :663 → ShortcutService.GetIcon :28-36 → IconImageLoader.TryLoadIcon :15-35`, `File.Exists` + `BitmapImage.EndInit`, no freeze). Trivial cost for local `.ico`, but it's startup-path I/O. **Fix (low/trivial):** `BitmapCacheOption.OnLoad` + `Freeze()` already half-there — add `Freeze()`, or lazy-load on first Shortcuts visit.
5. **Micro:** `StyleButtons` allocates a new array per access (`MainWindow.xaml.cs:320`); redundant `Dispatcher.Invoke` (`:483`). Negligible; fix opportunistically.

**Startup breakdown [OBSERVED]:** window handle 2.1 s, 11 buttons interactive 3.3 s, ~200–227 MB WS, ~41 threads — startup itself is **not** the problem. No timers, no redundant refresh loops, no virtualization issues (no long lists exist) found.

---

## 4. Tuning visibility verdict

**Verdict: no defect — the page is fully reachable at every tested size; do not "fix" the layout.**

* The mission's suspect, `ClipToBounds="True"` on `TuningView` (`MainWindow.xaml:1101`), is **innocent**: it only clips the decorative bleed ellipses (`:1116-1117`). The content `ScrollViewer` (`:1141`, `VerticalScrollBarVisibility="Auto"`) sits in `Row 1` (`*`) below the fixed header and scrolls normally — **there is no missing ScrollViewer in current source**.
* **[OBSERVED]** 1440×900 native: 2 sliders (live registry values CPU=8, MEM=8192), 7/7 checkboxes, DPI combo (10 options incl. defaults), END TASK + APPLY — all `IsOffscreen=False`; slider drags update labels live (`2 cores`, `2048 MB`).
* **[OBSERVED]** 1180×700 minimum: scrollbar present (17 px track), scrollable pane `v%=0, vview=93.7%` (≈6% below fold, reachable by scroll), every control `IsOffscreen=False`.
* **[OBSERVED]** Maximized 1920×1032: 106/106 elements on-screen, 0 offscreen.
* Running-emulator state is handled correctly: notice flips to danger text, APPLY disables, END TASK stays enabled (`RefreshTuningAsync`, `MainWindow.xaml.cs:580-596`) — observed live after GameLoop auto-started.
* Correct layout (already the case): header fixed, two-column cards + APPLY card inside one scrolling region, `MinWidth/MinHeight` (1180×700) bounding the squeeze. The historical "not all visible" report most plausibly predates the `ScrollViewer` or was observed on the crashing v1.1.0 artifact (which never got past startup). Optional polish only: nothing required.

---

## 5. UI MAINTENANCE VERDICT

**The UI needs targeted fixes, not maintenance/refactoring/redevelopment.** The visual tree is modest, code-behind is thin with formatting/layout properly separated (`OptimizerDisplayFormatter`, `ResponsiveLayoutManager`, `App.xaml` tokens), all six pages navigate and degrade gracefully (offline ADB, absent emulator, running emulator all show correct states), and the full suite is green. Stated priority order:

1. **P0 — Fix fatal shutdown crash** (F-001): guard the deferred `Close()` (`MainWindow.xaml.cs:436-439`) — e.g. `try/catch (InvalidOperationException)` + `Dispatcher.HasShutdownStarted/Finished` check, or `BeginInvoke`, plus a `_closed` flag for the double-X race. Reproducible 4/4 in logs.
2. **P1 — Unfreeze Tuning open/apply** (F-002): async snapshot (use existing `GetSnapshotAsync`) or session-cached snapshot + loading indicator.
3. **P2 — Republish the release artifact** (F-003): current `artifacts\…v1.1.0…exe` crashes on launch; rebuild from fixed source.
4. **P3 — Startup concurrency** (F-006): don't gate Optimizer panel on the 12 s update check.
5. **P4 — Hygiene batch, one pass**: `AutomationProperties.Name` on nav (F-007), drop redundant `Dispatcher.Invoke` (`:483`), `Freeze()` icon + trim duplicate `DropShadowEffect`s, loading feedback on Tuning nav (F-008).

---

## 6. Untested items + how to test them

(All deliberately skipped: each writes system state, kills live processes, or needs missing preconditions. The emulator was running from 8:20:53 PM — retest kills only with it stopped.)

| # | Item | Why skipped | How to test |
|---|---|---|---|
| U-1 | Tuning APPLY (11 registry DWORDs) | Writes `HKCU\SOFTWARE\Tencent\MobileGamePC` | Stop emulator, snapshot the 11 `EmulatorTuningCatalog` values, move sliders, APPLY, diff registry, restore snapshot |
| U-2 | Tuning END TASK / Optimizer FORCE CLOSE | Kills live emulator (45 procs during session) | With emulator running *intentionally*: END TASK → assert `FindGameLoopProcesses` empty + Tuning notice flips to apply-ready |
| U-3 | Graphics APPLY, shadow toggles, Korean FHD | Writes GameLoop profile files/`Active.sav` | Connected session (as achieved: PUBG Global loaded) → snapshot `Active.sav` + profile, APPLY one quality step, byte-diff (length must not change per `Ue4SavEditor` rule), restore |
| U-4 | Optimizer: SMART OPTIMIZE, WINDOWS & GPU BOOST, APPLY ALL, START/RESTORE SESSION, CLEAN CACHE | Power plan, process priorities, temp deletion, Defender exclusions | Record `powercfg /getactivescheme` + priorities first; run each; RESTORE after; assert restoration (mirrors `ShutdownRestorationTests`) |
| U-5 | DNS apply/reset, iPad APPLY, shortcut CREATE | Adapter/DNS, `TVM_100.xml`+registry, Desktop `.lnk` | DNS: apply → `ipconfig /all`, then RESET; iPad: emulator closed, backup asserted, apply one preset, RESET restores; shortcut: create → assert `.lnk` target, delete it |
| U-6 | Update download/relaunch path | Launches new elevated process, closes app | Point `UpdateOptions` at a local stub feed (or wait for a real release), accept update, assert handoff + clean close (also re-exercises F-001's fix) |
| U-7 | `Category=LiveFunctionalVerification` tests | Explicitly forbidden (needs live emulator interaction) | `dotnet test --filter Category=LiveFunctionalVerification` on a dedicated machine with emulator under test control |
| U-8 | DPI >100%, text scaling, multi-monitor | Environment fixed at 96 DPI single screen | Set 125/150% scaling, repeat §4 measurements; drag across monitors with different DPIs; assert 0 fully-offscreen controls |
| U-9 | CONNECT failure-message prominence | One attempt showed unchanged status (likely missed click) | Click CONNECT with emulator stopped → assert status bar shows the failure reason (compare pre-fix "Connection failed" behavior) |

---

## 7. Top-5 fix list

1. **`MainWindow.xaml.cs:436-439` — stop the fatal shutdown crash.** Guard/replace deferred `Close()` (shutdown-state check + `InvalidOperationException` catch). Evidence: 4 crash events, 2 reproduced this session.
2. **`Features/GameLoop/EmulatorSettingsService.cs:30-35,88` — move hardware snapshot off the UI thread** (`GetSnapshotAsync` or session cache) + loading indicator. Evidence: 5.3/6.1 s measured freezes.
3. **Republish `artifacts\Nexora-v1.1.0-win-x64.exe`** — current artifact NRE-crashes on startup; source already fixed (`MainWindow.xaml.cs:599-604`).
4. **`MainWindow.xaml.cs:288-318` — load update check and Optimizer profile concurrently;** never gate the panel on a 12 s network call.
5. **A11y/testability pass** — `AutomationProperties.Name` on the six nav buttons (`MainWindow.xaml:127-174`); opportunistically `Freeze()` icons/effects and drop the redundant `Dispatcher.Invoke` (`:483`).

*Note: `.axme-code/` storage was reported uninitialized at session start, so no memories/decisions were persisted; combined with the strictly read-only mandate, this report file (written at explicit user request) is the sole deliverable and no project source files were touched.*
