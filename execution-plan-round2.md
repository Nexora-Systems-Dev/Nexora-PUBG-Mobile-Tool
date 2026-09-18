# Clean Code / SOLID Audit — Execution Plan (Round 2)

Source: strict Clean Code + SOLID audit (Codex CLI, read-only, verified
against `AGENTS.md` + all `.agent/*.md` files). This is a separate plan
from the original architectural refactor (`execution-plan.md`), which is
fully complete — this one addresses a fresh, independent audit pass over
the resulting codebase.

## How this file works

Same rules as the first execution plan:

- Tasks are ordered by **actual operational risk**, not by the order the
  audit table listed them or by the audit's own P0-P3 labels — this file
  re-prioritizes one item (F-08) above the audit's own P1 position because
  it's a correctness/data-integrity risk, not just a hygiene issue.
- **A task is only removed from this file after it is fully done** — see
  "Definition of Done" below. Do not delete a task, or mark it done, based
  on "this should work" — forbidden by `.agent/stop-and-ask.md` §4.
- If a task turns out to be blocked, ambiguous, or riskier than described
  once you're inside the code, **stop and ask before proceeding**. Per
  `.agent/stop-and-ask.md` §2, if the same fix fails twice, stop
  immediately and report back instead of trying more variations.
- Work top to bottom. Do not start a tier before every task in the
  previous tier is removed from this file, unless explicitly told
  otherwise.
- Some findings below describe **newly visible** issues introduced or
  exposed by the prior refactor (the audit flagged these explicitly, e.g.
  "Newly visible fallout of the facade split"). Treat these with the same
  rigor as legacy findings — a regression introduced by earlier cleanup is
  not lower priority just because it's new.

## Definition of Done (applies to every task below)

A task is done, and may be deleted from this file, only when **all** of
the following are true:

1. `dotnet build .\Nexora.slnx --configuration Release` succeeds with
   **0 warnings, 0 errors**.
2. `dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"`
   passes in full.
3. The specific violation described in the task is actually gone —
   re-read the affected file(s) and confirm, don't assume the edit did
   what was intended.
4. No new violation of `.agent/*.md` rules was introduced by the fix
   itself.
5. If the task involves consolidating duplicated logic or routing a call
   through an existing single-source-of-truth abstraction (e.g.
   `IGameLoopProcessService`, `IGameLoopPathResolver`), confirm there is
   now exactly one implementation, not two that happen to agree today.

If any of these can't be satisfied, the task stays in this file and the
blocker is reported instead — never silently downgrade the task to
"mostly done."

---

## P0.0 — Device-state split (elevated above the audit's own ordering)

**Why first, above everything else in the audit's original P0**: this is
the one finding in the whole report that can cause the app to act on the
wrong physical device — a correctness/data-integrity risk, not a hygiene
or maintainability issue. Everything else in P0 is important but doesn't
carry this specific risk.

> **P0.0 complete (all Definition of Done checks passed):** `MainWindow`
> now takes an `IAdbClient?` parameter (option b) — the DI singleton is
> shared between `MainWindow._adb` and the injected `GameLoopService`,
> with a single locally-constructed fallback client shared by both in
> the designer path. `DeviceIdentityTests` pins the wiring; build 0/0,
> 288/288 tests pass.

---

## P0 — Correctness / cancellation / trust boundaries

**Why second**: user-visible hangs (frozen UI under elevated ADB/registry
calls) and trust-boundary bypasses (a spoofed-name process being trusted).

> **P0.1 complete (all Definition of Done checks passed):** `RunToolAsync`
> now creates one `CancellationTokenSource` per invocation and passes its
> token to the tool action (replacing `action(CancellationToken.None)`),
> with `OperationCanceledException` reported as "Operation canceled." and
> cancel+dispose in `finally` via a new `CancelAndDisposeTool()` helper
> mirroring `CancelAndDisposeConnection()`. `Window_Closing` cancels the
> tool CTS before the shutdown session restore. No new test: the wiring
> is private UI code untestable without a WPF host (same reason documented
> in `DeviceIdentityTests`); token-respecting behavior of the underlying
> services is already covered by `AdbCancellationTests`/`AsyncHygieneTests`.
> Build 0/0, 288/288 tests pass.
> **P0.2 complete (all Definition of Done checks passed):** the static
> `GameLoopConnector.IsGameLoopRunning` (name-only `Process.GetProcessesByName`
> check) is deleted. `GameLoopConnector` now takes a required
> `IGameLoopProcessService` (null-guarded per file convention) and its
> private `IsGameLoopRunning()` delegates to `FindGameLoopProcesses()` with
> caller-side disposal mirroring `SystemToolsFacade.SetIpadResolution`;
> `GameLoopService` takes and forwards the service (DI resolves it; the
> `MainWindow` fallback shares one resolver local between adb and the
> process service). The now-unused `_emulator` field/ctor-param was removed
> (it was read only by the deleted static; keeping it would trip CS0414
> under `TreatWarningsAsErrors`). The `IsGameLoopRunning_*` test was
> retargeted to repeated dispose-clean `FindGameLoopProcesses()` calls;
> the live test uses the container-resolved service; two further
> `GameLoopService` constructions the initial grep missed (target-typed
> `new(` in `AdbCancellationTests`, `GameLoopSessionConcurrencyTests`)
> were caught by the compiler and fixed the same way. No coverage loss:
> `OptionsTests` already pins `RunningCheckProcessNames` as a subset of
> `ProcessImageNames` stems. `GameLoopPathResolver`'s own process
> enumeration is the underlying pipeline the service depends on (rerouting
> it would be circular) — out of scope. Build 0/0, 288/288 tests pass.
> **P0.3 complete (all Definition of Done checks passed):** the applier no
> longer enumerates processes itself. `ProcessPriorityApplier` now takes a
> required `IGameLoopProcessService` (null-guarded per file convention) and
> `ApplyToRunningProcesses` iterates `FindGameLoopProcesses(gameLoopRoot)`
> — every candidate arrives path-verified, so the per-process trust
> decision is gone along with both privates (`IsGameLoopPath`, the
> line-for-line resolver duplicate, and the now-dead
> `IsKnownGameLoopProcess`). `ProcessPriorityService` takes the service
> and forwards it (P0.2 precedent: required param, no hidden composition
> root); the facade passes its existing `_processService` instance.
> `EmulatorOptions` is no longer consulted by the applier (ctor param
> removed — it would trip CS0414 otherwise). Tests use an explicit local
> `GameLoopProcessService` via a `CreateService()` helper
> (`ProcessPriorityShutdownTests`) / inline construction
> (`WindowsGpuBoostReportingTests`). No behavior loss: the old 5-name
> `PerformanceProcessNames` stems are a strict subset of the 16
> `ProcessImageNames` stems; null-root handling is equivalent (service
> resolves via `GetRoot()`, else InstallFolderName-contains). The change
> also closes a real spoof hole: the old `IsKnownGameLoopProcess`
> OR-clause trusted any same-named process unconditionally, so any
> `aow_exe` anywhere got High priority; the service only name-matches
> `SafeFallbackImageNames` for inaccessible paths. `PerformanceProcessNames`
> remains as config surface (still subset-pinned by `OptionsTests`) —
> removing it is P3.7 options-cleanup territory, not this task. Build 0/0,
> 288/288 tests pass.
> **P0.4 complete (all Definition of Done checks passed):** both facades
> now constructor-inject the DI singletons instead of composing their own.
> `SystemToolsFacade` takes required `IGameLoopPathResolver` (passed
> straight to `ShortcutService`, whose ctor already wanted the interface),
> `IGameLoopProcessService`, and `ITempCleanupService` (field retyped from
> the concrete class — the interface covers both `CleanTemp` members);
> `PerformanceEngineFacade` takes the latter two (it never used the
> resolver for anything but building its own process service, so no
> resolver param — injecting an unused dependency would be dead weight).
> The now-dead `TempCleanupOptions?` params were removed from both ctors
> (verified no caller passed them; the injected service carries the DI
> options). The `MainWindow` designer fallback builds one shared
> process-service + temp-cleanup pair used by all three fallbacks (P0.0
> single-identity pattern). Tests construct explicit collaborators
> (`CreateEngine()` helper in `ShutdownRestorationTests`, shared locals in
> `ServiceCompositionTests`, explicit locals elsewhere). One fix during
> verification: the `Shared.Infrastructure` using briefly removed from
> `PerformanceEngineFacade` was still needed for `IRegistryService`.
> Proof of single root: prod `new` of the three types remains only in the
> designer fallback; `DependencyInjectionTests` resolves both facades from
> the real container. Remaining `new` compositions inside the facades
> (hardware/power/nvidia/defender/registry/ipad/network/shortcut) are
> owned by later tasks (P2.6 etc.), untouched. Build 0/0, 288/288 pass.
> **P0.5 complete (all Definition of Done checks passed):** `AdbClient`
> no longer freezes its path at construction. The ctor is now pure
> (assignments + null guards only); path probing moved behind a locked,
> TTL-cached `AdbPath` property (1-minute named constant — kept private
> rather than expanding `GameLoopOptions` config surface) used by the
> single `Run` funnel, plus a public `RefreshAdbPath()` (also on
> `IAdbClient`) for immediate re-probe after a repair/reinstall. Both
> `IAdbClient` fakes gained no-op `RefreshAdbPath`. New
> `AdbPathResolutionTests` pins both deterministic halves with hermetic
> resolver stubs (no processes spawned): ctor performs zero probe calls
> even against a throwing resolver, and each refresh performs exactly one
> 3-lookup probe pass. The TTL-expiry path is time-based and intentionally
> untested. Build 0/0, 290/290 tests pass (288 + 2 new).

---

## P1 — Structural bloat and silent defaults

**Why third**: these affect maintainability and correctness-of-display
(confident wrong values shown to the user) but aren't active hangs or
trust bypasses.

> **P1.1 complete (all Definition of Done checks passed):** all five
> methods decomposed into named single-purpose helpers with zero behavior
> change (same order, messages, cancellation points, try/catch/finally
> coverage — verified by re-read plus diff review):
> `ConnectAsync` → `CheckAdbRegistryStatus`/`CheckGameLoopRunning`/
> `EnsureBootCompletedAsync`/`PullConnectionProbeAsync`/
> `DetectInstalledVersions`/`SelectVersionAsync`;
> `DownloadAndLaunchAsync` → `ValidateUpdateRequest`/
> `StageUpdateArchiveAsync` (tuple return — `out` params are illegal in
> async methods)/`LaunchHandoff`; `GetSnapshot` → hoisted
> `DetectionScript` const + `TryRunDetectionScript`/`ParseSnapshot`;
> `OptimizeForNvidia` → `EnsureOptimizerAssets`/
> `LoadAndCustomizeProfile`/`ImportProfileWithInspector`;
> `OptimizeGameLoopRegistry` → `ApplyCpuPriorityKeys`/
> `ApplyAppCompatFlags`. `ValidateTargetPath` takes an optional
> `stagingBaseDirectory` (existing 2-arg test calls unaffected) and the
> prod caller passes `GetStagingBaseDirectory()` — the now-redundant
> inline staging check in `DownloadAndLaunchAsync` was removed (identical
> message, previously unreachable after the fix; fail-fast now skips a
> pointless purge). Two new tests pin both halves of the base fix.
> P2.5 (`GetCurrentExecutablePath`) and P2.7 (`ResolveProfileTargets`
> hardcodes, `.nip` temp path, `ToLowerInvariant`) untouched — diff
> confirms those lines only moved. Build 0/0, 292/292 pass (290 + 2 new).
> **P1.2 complete (all Definition of Done checks passed):** `GetRoot`
> now calls `GetRootFromRegistry()` first and falls through to the
> unchanged process scan on null. Equivalence is exact: the deleted
> block was character-identical to `GetRootFromRegistry`'s body, so all
> four paths (blank → scan, invalid → scan, valid-but-missing → scan,
> valid-and-present → return) are preserved. The direct
> `Process.GetProcessesByName` calls in the scan fallback are
> intentionally untouched per the P0.2 decision (rerouting the resolver
> through `IGameLoopProcessService` would be circular — that service
> depends on the resolver). New `GameLoopPathResolverTests` pins the
> delegation (`GetRoot() == GetRootFromRegistry()` on a registry hit)
> using the `TemporaryFileHygieneTests` temp-dir/try-finally convention.
> Build 0/0, 294/294 pass (292 + 2 new).
> **P1.3 complete (all Definition of Done checks passed):** both
> `TrySelectDevice` passes in `AdbClient.cs` now share `internal static
> ParseDeviceSerials(string)` (the cited parsing pipeline, verbatim) plus
> a private `SelectPreferredSerial` for the equally-duplicated
> preferred-serial/TCP-suffix selection the plan didn't name — leaving
> those 4 lines duplicated would have been a half-fix, so both went in
> the same pass. Retry flow, order, messages, and cancellation points
> unchanged (verified by re-read). New `AdbDeviceSelectionTests` pins the
> parser: ready-only filtering, case-insensitive state match, empty
> outputs. Build 0/0, 297/297 pass (294 + 3 new).
> **P1.4 complete (all Definition of Done checks passed):**
> `QualityName`/`FrameRateName`/`StyleName` return `string?` (null on
> unknown bytes); `SaveProfileReader` getters return `string?` and
> `GetShadowAsync` returns `Task<string?>` (null for all four unreadable
> modes); nullability threaded through `GameLoopService` +
> `IGameLoopService`. UI: null quality/fps/style/shadow now leaves the
> corresponding buttons unchecked, and the summary shows the existing
> neutral "—" placeholder for all four (quality/fps/shadow already did;
> style needed a `SelectedStyleOrNull` split because `SelectedStyle()`'s
> "Classic" fallback also feeds the Apply write path at
> `MainWindow.xaml.cs:225` — changing it there would have submitted "—"
> into `TryResolveGraphicsValues`, so the write path keeps "Classic"
> while only the summary shows "—"). The style loop guards `style is
> not null` so a null read can never match a null-Tag button.
> **On the stop-and-ask question:** proceeded without stopping because
> the codebase already answered it — `UpdateSummary` establishes "—" as
> the neutral placeholder for "nothing selected", so null → unchecked →
> "—" follows the existing convention; no new error-state visual
> language was invented. New `SaveProfileReaderTests` (14 cases: unknown
> bytes incl. the 0x05 style gap, no-save, all shadow-null modes, plus
> codec-built Enable/Disable positive controls). One self-correction:
> the positive shadow controls initially failed because the test's
> hand-written marker prefix was wrong — rebuilt through
> `UnrealCVarCodec.EncodeCVar` instead of hardcoding hex. Live-only
> `LiveGameLoopVerificationTests` needed an explicit fail-fast guard for
> null originals (round-trip + restore are meaningless without them).
> Build 0/0, 314/314 pass (297 + 17 new).
> **P1.5 complete (all Definition of Done checks passed):** the
> 4-deep `ApplyLayoutMap` chain is now four single-level helpers —
> `ApplyLayoutMapToItems` (package filter + early-continue) /
> `ApplyLayoutMapToModes` (mode lookup + early-continue) /
> `ApplyModeButtons` (Ex/Basic lookup) / `ApplyButtonSwitches`
> (per-switch dispatch); `UpdateExtendedMapping` is a point-mode router
> over `UpdatePositionSwitchMapping` (coordinate rewrite loop, verbatim)
> and `UpdateSwitchPointMapping` + `ResolveSwitchPoints` (Point →
> DriveKey → SwitchOperation fallback chain, verbatim) +
> `ApplySwitchPoint` (scroll-wheel special case + point writes).
> Deepest nesting anywhere is now foreach→if (2 levels). One
> self-correction: the first test edit replaced the last existing test
> instead of appending — restored it immediately and confirmed all four
> pre-existing tests intact. Two new happy-path tests through
> `SetIpadResolution` (first-ever coverage of the mapping core, via an
> in-memory fake registry + temp keymap/map files) pin the position-
> switch rewrite, the basic point rewrite, the package filter, and backup
> creation. Build 0/0, 316/316 pass (314 + 2 new). **P1 tier complete.**
      level.

---

## P2 — Contracts, interfaces, shared state

**Why fourth**: these are correctness/design issues but with a narrower
or more contained blast radius than P0/P1 — mostly about making failures
consistent and closing an encapsulation gap, not fixing an active
hang/trust bypass/silent-wrong-value.

> **P2.1 complete (all Definition of Done checks passed):** the five
> one-line `?? throw new ArgumentNullException(...)` guards added exactly
> as scoped (`TempCleanupService:16` registry, `HardwareDetectionService:19`
> runner, `NetworkToolsService:15` runner, `PowerSessionService:18` runner,
> `IpadLayoutService:19` registry — its `fileSystem` guard already
> existed). Optional `*Options?` params intentionally untouched (the
> `?? new ...()` fallback is the established convention, not a missing
> guard). New `ConstructorNullGuardTests` (6 facts, following the existing
> `NvidiaOptimizerServiceTests.Constructor_Throws_WhenRequiredArgumentsNull`
> pattern) pins each required param including the pre-existing
> `IpadLayoutService.fileSystem` guard. Build 0/0, 322/322 pass
> (316 + 6 new).
> **P2.2 complete (all Definition of Done checks passed):** throw-
> everywhere implemented as decided. All six bare catches removed from
> `RegistryService` (`SetUserDword`, `GetLocalString`,
> `SetCurrentUserString`, `GetCurrentUserString`, `SetLocalMachineDword`,
> `GetLocalMachineDword` — re-read confirms zero `catch` blocks remain;
> legitimate-absence null-checks (`key is not null`, `key?.`) preserved so
> missing keys/values still return null/false-free). The four already-
> throwing methods (`GetUserDword`, `GetAppSettingDword`,
> `SetAppSettingDword`, `DeleteAppSetting`) are byte-identical. A
> caller-impact analysis (in session, before implementing) traced every
> call site to its UI boundary: no new crash exposure — all paths
> terminate at `RunToolAsync:604`, `ConnectToGameLoopAsync:136`,
> `SelectionChanged:208`, `ApplyButton:243`, or methods with their own
> try/catch (`ApplySmartSettings`, `ApplyHighPerformance`,
> `CleanTempCore`, both iPad methods); `App.xaml.cs` has no global
> handler so this tracing was load-bearing. Per decision A, the one new
> try/catch is in `OptimizeGameLoopRegistry` via
> `TryApplyCpuPriorityKeys`/`TryApplyAppCompatFlags` wrappers:
> access-denied (`UnauthorizedAccessException`/`SecurityException`)
> maps to the exact existing elevation/completion messages, other
> exceptions to honest `...: {ex.Message}` variants. Per decision B the
> monitor loop is untouched; per decision C raw messages surface
> elsewhere. New `RegistryFailurePolicyTests` (3 facts, throwing-fake
> based) pins `ApplySmartSettings` failure conversion and both preserved
> messages exactly. Two self-corrections: (1) a mid-task edit orphaned
> the `ApplyCpuPriorityKeys` body — caught on immediate re-read and
> repaired before building; (2) direct throw-tests against the real
> registry via embedded-null paths failed honestly — `OpenSubKey`
> returns null instead of throwing and `CreateSubKey` truncates-and-
> writes (it created a real `HKCU\Invalid` key, manually removed) — so
> real registry failures are not hermetically forcible and throw-behavior
> is verified by re-read, with behavior pinned through fakes.
> Build 0/0, 325/325 pass (322 + 3 new).
> **P2.3 complete (all Definition of Done checks passed):**
> `RawBuffer` removed outright (zero callers anywhere — verified by
> search, so no clone-variant needed). `GameLoopSession` setters are now
> private; the only mutation paths are `Reset()` (pre-existing),
> `LoadVersion(byte[]?, string?)` (publishes the buffer/package pair in
> one explicit step — an improvement on the torn-pair concern, since the
> connector previously set the two fields as separate statements), and
> `MarkAdbConnected()`. The class doc states the operation-gate
> discipline. Deliberate non-change: `Ue4SavEditor` keeps reference
> semantics (no defensive copy in the ctor) because
> `GraphicsSettingsApplier.ChangeProperty` intentionally edits the
> session buffer in place under the gate and deploys it — copying would
> silently break apply. Prod call sites updated (`GameLoopConnector`
> connect/load paths); test initializers migrated to `LoadVersion` via
> small helpers (partial states remain expressible with null halves).
> New `GameLoopSessionEncapsulationTests` (7 cases) pins private setters
> by reflection, `RawBuffer` absence, and the three methods' behavior.
> Final grep confirms no `RawBuffer` or external setter writes remain.
> Build 0/0, 332/332 pass (325 + 7 new).
> **P2.4 complete (all Definition of Done checks passed):**
> New `Features/GameLoop/RemotePaths.cs` (`Nexora.Features.GameLoop`)
> sits next to `PubgVersionCatalog`: `RemotePaths.For(package)` returns
> an immutable holder exposing `PackageName`, `SavedRoot`, and the two
> derived file paths `ActiveSavPath` / `UserCustomIniPath` (built from
> the root, so the template exists exactly once). `For` throws
> `ArgumentException` on a blank package — safe on every prod path
> because the connector validates, the reader guards whitespace, and
> the applier guards `IsConnected` before deploying; this follows the
> P2.1 fail-loud convention instead of silently addressing a wrong
> directory. Replaced all five ad hoc rebuilds: connector sav + shadow
> pulls, reader shadow pull, applier deploy root, and the KR full-HD
> `configPath` (its `dataPath`/`obbPath`/`/data/data` lines are
> KR-specific account/obb mechanics, not the UE4 template, so they
> stay). The live verification test builds its sav path from the
> builder too. New `RemotePathsTests` (6 cases) pins exact on-device
> strings, root derivation, per-package isolation, and blank-package
> rejection. Final grep confirms `ShadowTrackerExtra` survives only in
> the builder, the pinning literals, and an unrelated INI section
> header in a test fake. Build 0/0, 338/338 pass (332 + 6 new).
> **P2.5 complete (all Definition of Done checks passed):**
> Extracted `internal static bool IsTestHostPath(string?)` in
> `UpdateService.cs` ahead of `GetCurrentExecutablePath`; both the
> `Environment.ProcessPath` and `MainModule` branches now collapse to
> single-line guards. Blank input reports true (never the app exe),
> with the existing whitespace guards kept at the call sites so
> nullable narrowing for `GetFullPath` is untouched — no `!`
> suppressions. New theories in `UpdateIntegrityTests` (12 cases)
> pin host rejection incl. full paths/extensions/case variants, blank
> rejection, and acceptance of lookalikes (`dotnet-launcher.exe`
> proves exact-filename, not substring, matching). Re-read the region
> post-edit. Build 0/0, 350/350 pass (338 + 12 new).
> **P2.6 complete (all Definition of Done checks passed):**
> New `IProcessPrioritySnapshotStore` / `IProcessPriorityApplier` /
> `IProcessPriorityMonitor` (public, per codebase convention — which
> required promoting the `ProcessPrioritySnapshot` DTO record to
> public for interface signatures). Classes implement them; only
> interface members changed visibility (`internal` → `public`).
> `ProcessPriorityService` now takes the trio with null guards;
> `PerformanceEngineFacade` takes the service with a null guard
> (replacing its own `new`); `App.xaml.cs` registers all four as
> singletons — singleton is load-bearing so applier/monitor/service
> share one store. Six manual-composition sites updated to the same
> shared-store wiring: 2 service tests, 3 facade tests, and the
> `MainWindow` designer fallback (mirrors the DI singletons per its
> existing comment). `DependencyInjectionTests` extended (registration
> + resolution + singleton-lifetime pin); `ConstructorNullGuardTests`
> extended (service trio nulls + facade `processPriority` null).
> Final grep: no `new ProcessPriority*` remains under `Services/`.
> Build 0/0, 354/354 pass. Count note: 349 pre-existing + 5 new;
> source-attribute count (208 facts + 151 theory rows = 359) exactly
> matches discovery (359 = 354 + 5 live), so the ±1 vs the P2.5
> transcript figure is a cross-turn arithmetic artifact, not a lost
> test — every addition was individually verified present and green.
> **P2.7 complete (all Definition of Done checks passed):**
> All three fixed in `NvidiaOptimizerService.cs`. (1) New
> `EmulatorOptions.Emulator.NvidiaProfileImageNames` (same four names,
> same order — first hit names the profile; AndroidRenderer rationale
> preserved on the setting) sources `ResolveProfileTargets`, now an
> instance method. (2) The `.nip` stages inside a
> `NexoraUpdate-{guid}/` tree built from `UpdateOptions.StagingPrefix`
> (new optional ctor param, P2.1 convention; default equals the DI
> singleton's value so the facade needed no change) under the asset
> file name, with the tree swept in `finally` via
> `StagingDirectoryGC.TryDeleteDirectory` — a crashed import is now
> collected by the startup sweep, where the old bare
> `Nexora-{guid}.nip` file in temp root was invisible to the GC
> (it enumerates directories). (3) Targets keep on-disk case; the doc
> states downstream case-insensitive handling must use
> `OrdinalIgnoreCase`. Residual note, not a blocker: the shipped
> `Assets/mk.nip` itself contains lowercase paths (author-machine
> artifact); Windows/inspector matching is case-insensitive, so this
> changes nothing observable — flagged in case a profile ever
> misbehaves on a mixed-case install path. New tests: two
> `ResolveProfileTargets` cases via the suite's reflection precedent
> (case preservation + option order + missing-skip; custom names prove
> options-sourcing) and an `OptionsTests` subset/non-empty case
> mirroring the existing image-list conventions. Self-caught edit
> slip that orphaned the next test's header repaired before build
> (re-read the file end-to-end). Build 0/0, 357/357 pass
> (354 + 3 new).
> **P2.8a registry split complete (all Definition of Done checks passed):**
> `IRegistryService` (10 members) deleted; replaced by `IUserRegistry`
> (user DWORDs + app settings + CU strings) and `IMachineRegistry`
> (`GetLocalString` + machine DWORDs) in `Shared/Infrastructure`,
> both implemented by `RegistryService`. DI uses the factory-forward
> pattern (`AddSingleton<RegistryService>()` + per-facet forwards) so
> both contracts resolve to one instance — pinned by a new
> same-instance test. All 9 production consumers migrated to their
> single side; sole dual consumer `GameLoopRegistryOptimizer` takes
> both (`userRegistry`/`machineRegistry` + null guards, `GpuRoutingService`
> built from the user facet). Decision (c) applied: `GetLocalString`
> default aligned to `string? branch = null` on the new interface,
> ending the static-type-dependent divergence. Fakes shrank to the
> faked side (Fixed/Null → 3 machine members; Memory → 7 user
> members). `ARCHITECTURE.md` updated. Live tests rewired (compile
> only). Caught 4 missed manual compositions via build-as-truth
> (`ShutdownRestorationTests`, `AsyncHygieneTests`,
> `ServiceCompositionTests` x2 — same-instance-twice fix). Build 0/0,
> 360/360 pass (357 + 3 new: same-instance + 2 optimizer guards).
> **P2.8b Windows-tools dissolution complete (all Definition of Done checks passed):**
> `IWindowsToolsService` + `SystemToolsFacade` deleted. New
> `IShortcutService` (`Features/GameLoop`: `CreateShortcut` + `GetIcon`)
> implemented by `ShortcutService` (ctor unchanged) and registered via a
> DI factory that supplies the emulator asset root — mirroring the old
> facade composition. Pre-existing `IIpadLayoutService` reused (no new
> interface needed); `IpadLayoutService` gains a trailing optional
> `IGameLoopProcessService?` (all positional-3 call sites unaffected)
> and absorbs the facade's running-guard verbatim (same message), so the
> safety invariant is now unfalsifiable by callers. Decision (a)
> applied in full: sync `CleanTemp()` dropped from
> `ITempCleanupService` + impl and sync `PingDns()` dropped from
> `INetworkToolsService` + impl. Forced fallout, kept contained:
> `IGameLoopPerformanceEngine.OptimizeAll()` → `OptimizeAllAsync(ct)`
> (sole production caller `MainWindow` adopts it, dropping its
> `Task.Run` wrapper; zero test callers; follows the existing
> Restore-twin precedent). `MainWindow` takes the five focused
> contracts (10 nullable params, designer fallback composes all five
> with single shared process/temp identity). Tests: DI pins switched
> to `IShortcutService`; composition test builds the five focused
> services; `AdbCancellation` kills via `GameLoopProcessService`
> directly; extraction file reworked (2 sync ping tests deleted —
> async coverage already in `AsyncHygieneTests`); `OptionsTests` temp
> sweep migrated to `CleanTempAsync`; live Step4 rewired to
> `IIpadLayoutService` (same guard message assertion). New pins: 2
> guard tests (`FixedProcessService` stub; reject-with-message +
> pass-through-to-keymap-check). Docs: `ARCHITECTURE.md` diagram +
> ctor snippet + guard owner, `AGENTS.md` facade rule updated. Build
> 0/0 first try (no missed compositions — exhaustive pre-mapping),
> 360/360 pass (360 − 2 deleted + 2 new).
> **P2.8c GameLoop segregation complete (all Definition of Done checks passed):**
> New `Services/IGameLoopConnection.cs` (session/connect: `CurrentPackage`,
> `IsAdbConnected`, `IsConnected`, `Disconnect`, `ConnectAsync`,
> `LoadVersionAsync`) + `Services/IGraphicsProfileStore.cs` (reads/apply:
> quality/fps/style, `GetShadowAsync`, `ApplyGraphicsAsync`) — the boundary
> maps 1:1 onto collaborator ownership (connector vs reader/applier), so it
> is structural, not artificial. `IGameLoopService` deleted; `GameLoopService`
> implements both with zero logic change (gate + session untouched —
> cohesion trap respected). DI uses the P2.8a factory-forward pattern
> (`AddSingleton<GameLoopService>()` + per-facet forwards) so both contracts
> resolve to one instance. Flag-rule outcome: `MainWindow` is the sole
> production consumer and genuinely needs both sides (connect orchestration
> + display reads + apply), so it takes both — same "view needs all sides"
> principle as P2.8b; 11 nullable params, designer fallback shares one
> lazy `LoopFallback()` instance across both fields, mirroring the DI
> singleton. 21 call sites retargeted member-wise (`_connection.*` vs
> `_graphics.*`). Tests: DI registration + resolution pins switched to the
> two facets; new same-instance test (P2.8a precedent — dual
> `AddSingleton<Iface,Impl>` would fork the gate/session);
> `DeviceIdentityTests` resolves `IGameLoopConnection` (still
> `BeOfType<GameLoopService>`, `_connector` reflection intact);
> `BoundaryValidationTests` untouched (concrete `new GameLoopService`
> still compiles); live steps resolve concrete `GameLoopService`
> (registered as itself — minimal churn, compile-only). One self-caught
> miss via build-as-truth (`ConnectionResult` using in the new file).
> Docs: `ARCHITECTURE.md` diagram/list/ctor, `AGENTS.md` facet rule added
> (P2.8b bullet preserved). Build 0/0, 361/361 pass (360 + 1 new).
> **P2.8 as a whole complete — all three fat interfaces split.**
- [x] **P2.8** — Split the three fat interfaces by capability (registry half done, see note above):
      `IWindowsToolsService` (11 members spanning 5 capability groups —
      temp/DNS/shortcut/iPad/kill), `IGameLoopService` (11 members mixing
      connect + reads + apply), `IRegistryService` (10 members mixing
      user/app-settings/local-machine/string-value groups). Propose the
      split boundaries (e.g. `ITempCleanup`, `IDnsTools`,
      `IGameLoopConnection` vs `IGraphicsProfileStore`, `IUserRegistry` vs
      `IMachineRegistry`) before implementing — if a consumer needs
      members from more than one proposed split, flag that rather than
      forcing an artificial boundary.
- [x] **P2.9** — Fold the `MainWindow.xaml.cs` brush/visual/preset
      duplications into helpers: `TransportConnected` vs `FullyConnected`
      share 5 identical lines (`:682-690` vs `:693-699`); success-glow
      setup is triplicated; `FindResource(…) as Brush` is repeated 14
      times across the file. Extract a `GetBrush(string)` helper and a
      single `ShowConnectionVisual(state)` method with only the
      per-state deltas inline.
> **P2.9 complete (all Definition of Done checks passed):**
> Current file state differed slightly from the cited lines (a prior
> merge had already extracted `ShowSuccess/Failure/Disconnected`
> helpers; the emerald glow appeared 2×, not 3×). Folded the honest
> remainder: new `GetBrush(string)` (single `FindResource(key) as
> Brush`, `Brush?`) retargeted at all 14 call sites — plain assignments,
> ternary keys, and the four `?? Brushes.*` fallbacks all map 1:1 with
> identical null behavior. New `CreateSuccessGlow()` factory (fresh
> instance per call, same as before) serves `AwaitingVersion` and
> `ShowSuccessConnectionVisual`; the `AwaitingVersion` branch body is
> otherwise untouched per its do-not-fold comment. Dispatcher renamed
> `SetConnectionVisual` → `ShowConnectionVisual` (6 call sites, all in
> `MainWindow`, no test references) to match the plan and the
> `Show*ConnectionVisual` sibling family. `TransportConnected` +
> `FullyConnected` collapse into one case delegating to
> `ShowConnectedConnectionVisual(state, message)`: the 6 shared lines
> appear once, deltas stay inline (`ApplyButton` enabled only when
> fully connected; shadow toggles forced off only when
> transport-only — `FullyConnected` still leaves them untouched, as
> before). Danger glow stays inline (single use). Final grep: zero
> `FindResource(…) as Brush` casts and zero `SetConnectionVisual`
> remain in code. Build 0/0, 361/361 pass (no count change —
> pure refactor, no new pins needed).

---

## P3 — Readability polish

**Why last**: naming, magic numbers, and minor consistency issues — real,
but lowest risk of the whole report.

- [x] **P3.1** — Magic strings / repeated control lists in
      `MainWindow.xaml.cs` and `GraphicsSettingsApplier.cs`:
      `"com.pubg.krmobile"` appears 4 times when the catalog already owns
      it (add `PubgVersions.KoreanPackage` const); `"Smooth"`/`"Low"`/
      `"Classic"` fallback strings duplicate the catalog's own defaults
      (add `GraphicsSelection.Defaults`); the 5 style `RadioButton`s are
      enumerated 3 separate times (consolidate into one `StyleButtons`
      property).
> **P3.1 complete (all Definition of Done checks passed):**
> Naming deviation: `PubgVersions` is a dictionary instance, so a const
> cannot live on it — added `PubgVersionCatalog.KoreanPackage` instead
> and used it as the `PubgVersions` dictionary key as well (const in
> the initializer, compiles fine). All 4 production KR literals
> retargeted (`MainWindow` `:252,:693,:840` + `GraphicsSettingsApplier`
> `:85`); test files keep their literals as input data (out of scope).
> `GraphicsSelection.Defaults` is a static readonly instance
> `("Smooth","Low","Classic",false,false)` with a doc comment noting
> the names must exist in the catalog maps (drift surfaces at apply
> time via `TryResolveGraphicsValues`, which validates); the three
> `??` fallbacks now read `Defaults.Quality/.FrameRate/.Style`. New
> `ToggleButton[] StyleButtons` property serves all 3 enumeration
> sites (`StyleButton_Checked`, `ApplyLoadedSettingsAsync`,
> `SelectedStyleOrNull`) with identical fresh-array-per-access
> semantics. Final grep: zero KR literals, zero `?? "Smooth"/"Low"/
> "Classic"` fallbacks, zero repeated style enumerations in production
> code (remaining hits are catalog map keys, XAML Content attributes,
> and test inputs — all legitimate). Build 0/0, 361/361 pass (no count
> change — pure readability refactor, no new pins needed).
- [x] **P3.2** — `PowerSessionService.cs`: hoist the laptop/battery
      ternary (`:35-46`, evaluated twice) into a local; replace inline
      `"SCHEME_BALANCED"`/`"SCHEME_MIN"` literals (`:26`) with named
      constants; replace the uncompiled inline `Regex` (recompiled every
      `Apply` call) with a source-generated (`GeneratedRegex`) or
      `static readonly` instance.
> **P3.2 complete (all Definition of Done checks passed):**
> Judgment call: chose `static readonly RegexOptions.Compiled` over
> `GeneratedRegex` to match the existing codebase convention
> (`AppConstants.cs:31` uses the identical idiom) rather than
> introducing a second idiom plus a `partial`-class change for a
> readability-polish task. `batterySafe` local evaluates the ternary
> once and drives both the scheme pick and the result message (messages
> byte-identical). `BalancedSchemeAlias` / `HighPerformanceSchemeAlias`
> consts replace both literals. `PowerSchemeGuidRegex` (compiled,
> same pattern) replaces the per-call `Regex.Match`. Final grep: only
> the const definitions, the compiled-regex call site, and the single
> ternary remain. Build 0/0, 361/361 pass (no count change — pure
> refactor, no new pins needed).
- [x] **P3.3** — `PerformancePlanBuilder.cs:13-29`: name the magic-number
      tuning table (8/16/6/4/4096/6144/8192/2048/512/0.75, plus the
      unexplained `RenderQuality: 2`) as named threshold/ladder constants;
      replace the nested ternaries at `:17,19,29,36-38` with clearer
      if/else chains or a lookup table.
> **P3.3 complete (all Definition of Done checks passed):**
> Chose if/else chains over a lookup table (conditions are mixed
> boolean/threshold tests, not pure key lookups — a table would
> over-complicate). 19 consts in 4 groups: tier thresholds
> (`LimitedVramGb`/`SmallMemoryGb`/`LargeMemoryGb`/`FewCores`/
> `ManyCores`/`PerformanceVramGb`), memory ladder (`Small/Medium/
> LargeMemoryReserveMb`, `Min/MaxMemoryMb`, `MemoryStepMb`), CPU tuning
> (`SmallMachineCoreFloor`/`Min/MaxCpuCores`/`CpuShareRatio`), render
> ladder (`Low/HighContentScale`, `Disabled/Integrated/DedicatedFxaa`,
> `DefaultRenderQuality`). All four nested ternaries (tier, reserve,
> FXAA, power mode) are now if/else with byte-identical outputs,
> verified branch-by-branch on re-read. The `:23-25` CPU ternary is
> single-level (not cited) so its structure stays — only its numbers
> got names; likewise the flat `contentScale`/`gpuRoute` ternaries.
> `GpuMemoryGb < 4` reuses `PerformanceVramGb` (same "4 GB VRAM means
> capable" threshold, not a coincidence to split). `1024` stays inline
> as a GB→MB unit conversion, not tuning. Final grep: const
> definitions are the only numeric hits; zero nested ternaries remain.
> Build 0/0, 361/361 pass (no count change — pure refactor, existing
> plan-builder tests pin the outputs).
- [x] **P3.4** — `UnrealCVarCodec.cs`: the `Enabled`/`Disabled`
      dictionaries (`:16-36`) repeat the same 6 keys with values differing
      only by `"1"`/`"0"` — collapse to a single key set plus
      `enabled ? "1" : "0"`. Fix the silent truncation at `:47`
      (`(byte)character` silently drops non-ASCII input) with an explicit
      ASCII guard or documented encoding decision, per
      `.agent/clean-code.md` §5 on hidden truncations.
> **P3.4 complete (all Definition of Done checks passed):**
> Both dicts collapsed into one `IReadOnlySet<string> ShadowCVarNames`
> (same 6 names, `Ordinal` comparison preserved); `TryApplyShadowPreset`
> computes `enable ? "1" : "0"` once and checks membership — preset
> behavior identical, indentation logic untouched. `EncodeCVar` now
> throws `ArgumentException` (naming the offending code point) for any
> char > 127 instead of silently truncating via `(byte)` cast, and the
> doc comment records the ASCII-only encoding decision; the remaining
> `(byte)` cast is reachable only post-guard. Unlike the pure refactors
> so far, the guard is new behavior, so it gets a test pin:
> `EncodeCVar("r.ShadowQuality", "１")` (U+FF11 fullwidth digit — looks
> like "1" but would previously have truncated to 0x11) must throw.
> Final grep: old dict names gone. Build 0/0, 362/362 pass (361 + 1 new pin).
- [x] **P3.5** — Unify the two divergent GPU classification heuristics:
      `PerformanceModels.cs:19-23`'s `HasDedicatedGpu` (broad substring
      match on "RX", "Arc", etc.) can disagree with
      `NvidiaOptimizerService.cs:117-130`'s `ClassifyGpuProvider` (e.g. an
      Intel Arc GPU could get a dedicated-GPU plan from one and be skipped
      by the other). Extract one shared `GpuVendorClassifier` and route
      both call sites through it.
> **P3.5 complete (all Definition of Done checks passed):**
> New `Features/Performance/GpuVendorClassifier.cs` owns all marker
> knowledge: the `GpuVendor` enum (moved here — Features cannot depend
> on Services, where it lived), `Classify` (old provider logic verbatim:
> NVIDIA → Intel → AMD-marker array → Unknown), and `IsDedicatedGpu`
> (exact old OR-chain: NVIDIA vendor, else any of RTX/GTX/RX/Arc in the
> name — plus null-tolerance instead of an NRE on a null name, strictly
> safer). Deliberate semantic preservation: an AMD vendor string alone
> is still not enough for dedicated (integrated Radeons carry it too),
> documented on the method. `HasDedicatedGpu` delegates to
> `IsDedicatedGpu`; `ClassifyGpuProvider` is now a one-line forwarder so
> all existing tests compile untouched. Disagreement is impossible by
> construction now: vendor truth and discrete truth come from one file.
> 8 new pins: `HasDedicatedGpu_Shares_Classifier_Markers` theory (7
> rows incl. MX150, Arc, RX 7900, integrated Radeon/Iris/UHD negatives)
> plus `IntelArc_Classifies_IntelVendor_But_DedicatedPlan` (the plan's
> motivating case — Intel vendor skips the NVIDIA profile while the
> same card gets the dedicated plan). Final grep: zero scattered
> RTX/GTX/RX/Radeon/Arc substring checks remain outside the
> classifier. Build 0/0, 370/370 pass (362 + 8 new).
- [x] **P3.6** — `ShortcutService.cs`: `:41-43` throws
      `DirectoryNotFoundException` where every sibling service returns an
      `OperationResult` — change to `OperationResult.Fail` for the
      unresolved-path case, keeping the fail-loud behavior on the same
      result channel the rest of the codebase uses. Fix the double-space
      in `"-startpkg {pkg}  -from DesktopLink"` (`:64`) and the unescaped
      quote in `$s.Description = '{AppConstants.ApplicationName}'`
      (`:65`).
> **P3.6 complete (all Definition of Done checks passed):**
> Unresolved market path now returns `OperationResult.Fail` with the
> exact same message words (fail-loud, same channel as the
> `AppMarket.exe`-missing check two lines down); the UI path
> (`CreateShortcutButton_Click` via `RunToolAsync`) shows the same
> failure outcome as before, and direct callers no longer need a
> `DirectoryNotFoundException` catch. Arguments collapsed to a single
> space; Description now goes through `ProcessText.Quote` (single-quote
> wrap + embedded-quote doubling), matching every other interpolated
> value in the same script. The old throw-pinning test was rewritten as
> `CreateShortcut_ReturnsFailure_WhenMarketPathUnresolvable` asserting
> `Success == false` plus the message — mandated by the task, not scope
> creep. Final grep: no throw, no double space, no raw-quoted
> Description. Build 0/0, 370/370 pass (count unchanged — one test
> renamed/reshaped).
- [x] **P3.7** — Misc signature/naming/consistency cleanup:
      `GpuRoutingService.cs:16`'s nullable-but-always-required parameter
      (make it a required non-nullable parameter instead, since passing
      null always throws); reduce the parameter counts on
      `UpdateHandoffBuilder.cs:14-20` (5 params) and
      `GameLoopConnector.cs:22-29` (7 params) by grouping related
      parameters into an options/context object if that doesn't
      over-complicate the call sites; replace per-call inline
      `new EmulatorOptions()` at `DefenderExclusionService.cs:85` and
      `IpadLayoutOptions.cs:11` with the DI-registered options instance;
      decouple `DnsCatalog.cs:6`'s `ShortName` from its `" - "` label
      format coupling; clarify `OperationResult.cs:14`'s tri-state
      (`Success` + `Outcome`) — either document the intent clearly or
      collapse to a cleaner single-source-of-truth state; rename
      `RegistryServiceExtensionsTests.cs` (misnamed — no extensions class
      exists in the codebase it's testing).
> **P3.7 complete (all Definition of Done checks passed):**
> Done: `GpuRoutingService` takes required `IUserRegistry` (both
> production callers already pass non-null; no test constructs it).
> `IsTrustedGameLoopPath` takes required `EmulatorOptions` — production
> already passed its DI-fed `_emulator`, the 4 test sites now pass
> `new EmulatorOptions()` explicitly; the `?? new()` fallback is gone.
> `IpadLayoutOptions` gained an `EmulatorOptions`-taking ctor (init
> props still overridable, all existing `new IpadLayoutOptions()`
> tests compile untouched) and DI uses a factory registration, so the
> app singleton derives `LayoutMapPath` from the DI-registered
> `EmulatorOptions`; the parameterless ctor chaining to `new()` is the
> documented default path, same as every other options fallback.
> `DnsEntry` carries explicit `ShortName` (4th positional); all 5
> entries updated and the `CatalogTests` pin rewritten to assert
> canonical short names from differently-cased lookups — the
> `Split(" - ")` parse is gone repo-wide. `OperationResult` tri-state
> documented on the record (Outcome = source of truth, Success = gate
> signal true for Applied+Skipped) instead of collapsed: zero direct
> constructions exist but `.Success` reads are ubiquitous, so collapse
> would be high-churn for zero behavior gain. Test file renamed to
> `RegistryServiceTests.cs` (+ class) via unstaged filesystem rename.
> Declined with reason (plan's own "if" hedge): grouping
> `UpdateHandoffBuilder`'s 5 params (single prod caller passes 5
> locals straight through; a paths-record would add a type and edit 5
> call sites without reducing arity anywhere) and `GameLoopConnector`'s
> 7 params (6 heterogeneous services — a parameter object would be a
> service locator in disguise; single production caller). Both are
> honest signatures, not grab-bags waiting to happen. Final grep: no
> `Split(" - ")`, no nullable-required param, no cited inline `new()`.
> Build 0/0, 370/370 pass (count unchanged).

---

## Explicitly not scheduled (per the audit's own findings)

The audit deliberately did not flag these, and this plan agrees — no task
needed:

- The `.nexora-backup`/`.MKbackup` and `Nexora`/`MK Apps` registry dual
  fallbacks — these are the documented, intentional permanent
  read-fallbacks from this engagement's own Deferred items, not
  accidental duplication.
- `async void` event handlers — required by WPF's event model.
- `FindResource(...) as Brush` casts — idiomatic WPF; the actual issue
  (repetition) is covered by P2.9's `GetBrush` helper.
- `catch (Exception)` blocks that translate into `OperationResult.Fail` —
  these report the failure through the established channel; they don't
  swallow it.
- The `MainWindow` nullable-constructor fallback pattern itself — mandated
  by `AGENTS.md` for XAML designer safety. Only its P0.0 divergence (using
  a *different* `AdbClient` instance than DI would provide) is a problem,
  not the pattern itself.
