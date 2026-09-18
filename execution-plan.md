# Architectural Refactoring — Execution Plan

Source: architectural audit (Codex CLI, read-only, verified against `AGENTS.md`
+ all `.agent/*.md` files).

## How this file works

- Tasks are ordered by **actual operational risk**, not by the order they
  appeared in the original audit report.
- **A task is only removed from this file after it is fully done** — see
  "Definition of Done" below. Do not delete a task, or mark it done, based
  on "this should work" — that is explicitly forbidden by
  `.agent/stop-and-ask.md` §4.
- If a task turns out to be blocked, ambiguous, or riskier than described
  once you're inside the code, **stop and ask before proceeding** — do not
  silently reinterpret the task or skip it. Per `.agent/stop-and-ask.md` §2,
  if the same fix fails twice, stop immediately and report back instead of
  trying more variations.
- Work top to bottom. Do not start a P-tier before every task in the
  previous P-tier is removed from this file, unless explicitly told
  otherwise — later tiers assume earlier ones are already fixed.

## Definition of Done (applies to every task below)

A task is done, and may be deleted from this file, only when **all** of the
following are true:

1. `dotnet build .\Nexora.slnx --configuration Release` succeeds with
   **0 warnings, 0 errors** (`TreatWarningsAsErrors=true` is already
   enforced — do not weaken it to pass).
2. `dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"`
   passes in full.
3. The specific architectural smell described in the task is actually gone
   — re-read the affected file(s) and confirm, don't assume the edit did
   what was intended.
4. No new violation of `.agent/*.md` rules was introduced by the fix itself
   (e.g. don't fix a DI violation by introducing a new hardcoded fallback).
5. If the task involved a file move, all references/usings/namespaces at
   the old and new location were checked — a move that compiles is not
   automatically a move that's semantically correct.

If any of these can't be satisfied, the task stays in this file and the
blocker is reported instead — never silently downgrade the task to "mostly
done."

---

## P0 — Restore DI integrity / remove hardcoded fallback

**Status: COMPLETE.** All five tasks verified against the Definition of Done
and removed. One documented deviation in P0.4: the resolver carries a 5th
member, `GetAppMarketPath()`, beyond the four listed — it was required to
make `AdbClient.FindAdbPath` and `ShortcutService` thin callers without
changing resolution semantics. P0.3 also dropped the newly-dead
`runner`/`registry` params it orphaned (keeping them would have failed the
build under `TreatWarningsAsErrors` via CS0414).

---

## P1 — Thin out MainWindow.xaml.cs (810 → under ~200 lines)

**Reorder approved by user (2026-09-14):** P1.4 + P1.3 + P1.5 (mechanical,
independently verifiable shrinks) execute before P1.2 (structural
ViewModel extraction), because P1.4 absorbs the same methods P1.2 would
otherwise move twice, and real ViewModels need a smaller, clearer surface.
P1.2 is rescoped pending that outcome (see its note below).

**Why second**: Once DI is trustworthy, extracting logic out of the
code-behind and into injectable ViewModels is safe. Doing this before P0
would mean building new structure on top of the same broken DI graph.

- [ ] **P1.1** — ❌ DECLINED by user decision (2026-09-14, supersedes plan).
      `AGENTS.md` explicitly requires keeping the nullable-ctor + manual
      fallback pattern for XAML-designer safety, and the fallbacks only
      execute for the designer or direct `new MainWindow()` — production
      always resolves via DI. Removing them would break the designer with
      no production benefit. Left unchanged; continuing with P1.2.
- [ ] **P1.2** — ✅ RESOLVED without 6-ViewModel split (user decision 2026-09-14).
      After P1.3–P1.5 the code-behind holds only event wiring, connection
      visuals, and delegation to tested services/formatters — compliant with
      `.agents/clean-code.md` §1. A binding-less ViewModel split would add
      indirection without decoupling (over-fragmentation per
      `.agents/code-mastery.md` §3). Full MVVM would require a XAML rebind
      not verifiable in this environment. P1 tier complete.
- [ ] **P1.3** — ✅ DONE (verified: `ShortcutService.GetIcon()` owns icon resolution with package validation; exposed via facade; MainWindow simplified; build 0/0, 246/246 tests).
- [ ] **P1.4** — ✅ DONE (verified: single RunToolAsync overload, single SetConnectionVisual; build 0/0, 241/241 tests).
- [ ] **P1.5** — ✅ DONE (verified: async defer-close with _closeRequested guard + bounded CTS(ShutdownRestoreTimeout); zero .Wait()/.Result in prod; shutdown test rewritten to mirror new sequence; build 0/0, 246/246 tests).

---

## P2 — Split the WindowsToolsService facade / fix interface segregation

**Why third**: This depends on P0 (clean DI) being in place, and is safer
to do once P1 has already reduced how much of `MainWindow` depends on the
current facade shape.

- [ ] **P2.1** — ✅ DONE (verified: `SystemToolsFacade` + `PerformanceEngineFacade`
      own disjoint members; former optimization-step methods privatized in the
      engine facade; `OptimizeAll` Temp step preserved via facade-owned
      stateless `TempCleanupService`; old class deleted; build 0/0, 246/246
      tests — one transient parallel flake in an untouched staging test,
      green on re-run, documented below).
- [ ] **P2.2** — ✅ DONE (verified: `IWindowsToolsService` no longer extends
      `IGameLoopPerformanceEngine`; DI registers the two facades directly;
      MainWindow takes both interfaces with the kept fallback pattern).

---

## P3 — Reunite features (file moves + catalog extraction)

**Why fourth**: Lower operational risk (organizational, not logic-changing)
but mechanically easy to get subtly wrong (missed reference, wrong
namespace) — safer once the higher-risk logic changes above are stable and
verifiable by tests.

- [ ] **P3.1** — ✅ DONE (verified: `git mv` renames tracked; namespaces `Nexora.Features.Layout`; all usings updated; build 0/0, 246/246 tests).
- [ ] **P3.2** — ✅ DONE (verified: `git mv` renames tracked; namespace `Nexora.Features.SystemTools.Network`; usings updated; build 0/0, 246/246 tests).
- [ ] **P3.3** — ✅ DONE (verified: moved to `Features/GameLoop/` — existing folder fits the GameLoop-bounded shortcut logic; build 0/0, 246/246 tests).
- [ ] **P3.4** — ✅ DONE (verified: impl moved, interface intentionally left in `Services` per plan — note the resulting Features→Services using; build 0/0, 246/246 tests).
- [ ] **P3.5** — ✅ DONE (verified: new `Features/Security/` folder; namespace updated; all static+construction references re-imported; build 0/0, 246/246 tests).
- [ ] **P3.6** — ✅ DONE (verified: `Features/GameLoop/PubgVersionCatalog.cs` owns all four maps + forward/reverse resolvers; all 5 consumers rerouted; Services→Features lateral coupling gone; build 0/0, 246/246 tests).
- [ ] **P3.7** — ✅ DONE (verified: 5 wrappers deleted with the Test Seams region; tests call `Ue4SavEditor`/`UnrealCVarCodec` directly; genuine `ReadProperty`/`ChangeProperty` retained; build 0/0, 246/246 tests).

## P3 tier COMPLETE. Remaining: P4 (IO abstraction) + P5 (large-file splits) + Deferred decisions.

---

## P4 — Push IO out of domain logic

**Why fifth**: This is what actually makes P3's reorganized features and
P0-P2's cleaned-up services testable without touching the filesystem —
doing it earlier would mean re-doing it after files move in P3.

- [ ] **P4.1** — ✅ DONE (verified: `Shared/Kernel/IFileSystem.cs` + `PhysicalFileSystem.cs`; `GameLoopService`/`IpadLayoutService`/`GameLoopWorkingStorage` take required `IFileSystem`, zero direct `File.`/`Directory.` calls left in all three; `SystemToolsFacade` passes it through; `IFileSystem` + `GameLoopWorkingStorage` registered in `App.xaml.cs`; all construction sites updated incl. 4 missed by grep but caught by compiler; new `FileSystemAbstractionTests` with in-memory fake proves the seam; build 0/0, 249/249 tests. Notes: interface has 9 members, not 6 — call sites also needed `ReadAllText`/`WriteAllText`/`CreateDirectory`; `GameLoopWorkingStorage` needed an explicit DI registration because MS.DI does not auto-construct unregistered concrete types).
- [ ] **P4.2** — ✅ DONE (verified: chose `IWorkRootProvider` over moving the file — storage is GameLoop-domain state, moving it would push feature knowledge into shared infra; new `Shared/Infrastructure/IWorkRootProvider.cs` + `GameLoopWorkRootProvider.cs` own the `AppContext`/`Environment` calls; `GameLoopWorkingStorage` takes required `(IFileSystem, IWorkRootProvider)`, zero `AppContext`/`Environment`/`File.`/`Directory.` left in it; registered in DI; `MainWindow` fallback + 3 test sites updated — build caught the `MainWindow` one that grep missed; `FileSystemAbstractionTests` gained a stub-roots seeding test + provider composition test; build 0/0, 250/250 tests).
- [ ] **P4.3** — ✅ DONE (verified: new `Features/GameLoop/ShadowSettingsStore.cs` (instance, required `IFileSystem`) owns Exists/ReadAllLines/WriteAllLines with identical messages; `UpdateShadowFile` deleted from the codec, which is now pure — zero `File.`/`Directory.` and no remaining references; `IFileSystem` gained `ReadAllLines`/`WriteAllLines` (`WriteAllLines` default UTF-8-no-BOM is byte-identical to the old explicit encoding); `GameLoopService` composes the store from its injected FS as a readonly field — no ctor cascade since the store is domain logic over an already-injected seam, not a new seam; new `ShadowSettingsStoreTests` (3 tests: missing file, no CVar, enable/disable round-trip); build 0/0, 253/253 tests).
- [ ] **P4.4** — ✅ DONE (verified: pure code motion, no logic rewrites — `Services/UpdateInfo.cs` (record), `UpdateChecker.cs` (CheckAsync verbatim, composed from orchestrator's HttpClient), `UpdateArchiveValidator.cs` (zip-slip guard + SHA256 + Authenticode, 7 members verbatim; 3 private→internal for orchestrator access), `UpdateHandoffBuilder.cs` (PS template + args verbatim), `StagingDirectoryGC.cs` (purge + delete verbatim); orchestrator 575→203 lines keeping only CheckAsync delegation, DownloadAndLaunchAsync sequence, IsTrustedDownloadUrl/hosts, GetCurrentExecutablePath, ValidateTargetPath; `App.xaml.cs` purge call + 30 test qualifiers updated (compiler found a missing using + all sites); a stray doc comment left by the chunk deletes was caught on re-read and removed; build 0/0, 253/253 — the byte-exact security tests (script fragments, sentinel hashes, purge counts, failure messages) all pass against the moved code).
Post-P4.4 gap closure (pre-existing gap, validator logic untouched): new `Nexora.Tests/Services/UpdateArchiveValidatorTests.cs` (12 tests) exercises the zip-slip guard against real hostile archives — relative traversal (`../`, `..\`, nested) and absolute paths (`C:/`, `C:\`, `/absolute/`) via `GetSafeExtractionPath` (null) and `ExtractEntriesSafely` (throws `InvalidOperationException`, fail-closed, nothing outside staging; per-test unique parents so parallel-safe). Suite now 265/265.

---

## P5 — Shrink remaining large files

**Why last**: Lowest operational urgency — these are maintainability
improvements, not active risks, and are easiest to do correctly once
everything above has already isolated their dependencies.

- [ ] **P5.1** — ✅ DONE (verified: `Services/GameLoopService.cs` 441→44 lines, pure facade over a shared `GameLoopSession.cs` (21 lines: sav buffer, package, connected flag, `IsConnected`, `Reset()`); `GameLoopConnector.cs` (135: Connect/LoadVersion/IsGameLoopRunning verbatim), `SaveProfileReader.cs` (64: Quality/FrameRate/Style/Shadow + ReadProperty verbatim), `GraphicsSettingsApplier.cs` (203: ApplyGraphics/Deploy/KR flow + all remote-folder/account helpers + ChangeProperty verbatim, nullable-locals only addition for the new property-based state); facade ctor `(registry, adb, storage, fileSystem)` unchanged — collaborators composed internally over injected seams, so zero churn in DI/`MainWindow`/other tests; dead `PrepareWorkingFiles()` wrapper deleted; `GameLoopServiceLogicTests` retargeted to reader/applier over an in-memory session (reflection kept only for genuinely-private helpers); `IsGameLoopRunning` callers (connect + 2 tests) updated; build 0/0, 265/265 tests. Note: plan said "540 lines" — file was 441 by the time P5.1 started.)
- [ ] **P5.2** — ✅ DONE (verified: `Services/Performance/ProcessPriorityService.cs` 414→72 lines, parameterless public facade composing the three over one shared store — zero churn in `PerformanceEngineFacade`/tests; `ProcessPrioritySnapshotStore.cs` (149: dict+lock, Count/TryAdd/TakeAll/Prune/AddSnapshotForTesting + IsSnapshotStale, all verbatim); `ProcessPriorityApplier.cs` (139: scan + restore loop verbatim, locking the store's SyncRoot so whole-scan exclusion matches the original single `_sync`); `ProcessPriorityMonitor.cs` (94: CTS/task lifecycle verbatim on its own lock over disjoint monitor-only state, loop calls store+applier without holding it, await stays outside the lock); shared path/process helpers defined exactly once (`TryGetExecutablePath`/`PathsEqual` internal on the store); each static helper exists in exactly one file; `PruneDeadSnapshots` delegation added to the facade after the compiler caught 3 test call sites my grep had missed; build 0/0, 265/265 tests incl. shutdown/prune/priority suites.)
- [ ] **P5.3** — ✅ DONE (verified: `Configuration/AppConstants.cs` 174→36 lines, keeps `ApplicationName`/`CurrentVersion`/`Tools`/`Validation` only; new `GameLoopOptions.cs` (Adb+Registry+Timeouts nested groups), `EmulatorOptions.cs` (Emulator+Assets nested groups), `UpdateOptions.cs` (flat Update group, `ReleasesUrl` computed from `Repository` so the derivation survives); all ~130 call sites across 28 prod files migrated to the nullable-ctor `?? new()` pattern + DI singletons; 3 const-locked signatures reshaped without behavior change — `RegistryService.GetLocalString` `branch=null`+coalesce (interface-typed callers still get `""` exactly as before), `UpdateArchiveValidator.IsAuthenticodeSigned` `expectedPublisher=null`+coalesce, `StagingDirectoryGC.PurgeStaleStagingDirectories` + `ValidateTargetPath` + `IsGameLoopRunning`/`IsTrustedGameLoopPath`/`FindExpectedExecutable`/`VerifyExecutableIntegrity` gained optional options params; `MainWindow` gained a 5th optional `GameLoopOptions?` param under the kept fallback pattern; `AppConstantsTests` rewritten to the remaining surface, option-value tests moved/extended in `OptionsTests` (+10 tests incl. `ReleasesUrl`-follows-`Repository` derivation); `ARCHITECTURE.md` options sections updated; build 0/0, 272/272 tests.)

---

## Deferred / needs a decision before scheduling

These were flagged in the audit but don't have a clear single-owner fix yet
— surface them rather than guessing an approach:

- **Stale branding**: ✅ DONE (decided behavior implemented:
  `RegistryService` keeps `LegacyAppSettingsPath =
  @"SOFTWARE\MK Apps\MK PUBG Mobile Tool"` for permanent read fallback and
  writes only to new `AppSettingsPath =
  $"SOFTWARE\\Nexora\\{AppConstants.ApplicationName}"` (vendor key mirrors
  the legacy `MK Apps` shape; product tracks `ApplicationName`);
  `GetAppSettingDword` tries current-first via a shared `TryGet...` helper
  (same sequential-attempt shape as `GameLoopPathResolver.GetAppMarketPath`),
  current key wins on conflict; `Set` touches the current key only while
  `Delete` clears both keys as a deliberate Reset exception (user decision:
  Reset must leave no trace, otherwise a legacy-only value would resurface
  via the fallback); sole prod caller
  `IpadLayoutService` inherits the behavior with no changes, `IRegistryService`
  unchanged; new `RegistryAppSettingsBrandingTests` (5 tests, real-HKCU +
  GUID names + key cleanup per the existing registry-test pattern) covers
  current-wins, legacy fallback, null-when-absent, write-only-current, and
  legacy-untouched; build 0/0, 277/277 tests.)
- **Naming inconsistencies**: ✅ DONE (all four items implemented per
  user decisions: (1) `IpadLayoutService.Apply/Reset` renamed to
  `SetIpadResolution/ResetIpadResolution` matching the facade, call sites +
  tests updated; (2) dead `KillGameLoopProcessesAsync` deleted from both
  interfaces + implementation + facade + its cancellation test (sole prod
  caller already wraps sync in `Task.Run`); (3) `IsGameLoopConnected`
  renamed to `IsAdbConnected` (`IsConnected` untouched), 5 files;
  (4) unified `.nexora-backup` suffix for local + remote with permanent
  new-then-old read fallback — local `IpadLayoutOptions.BackupExtension`
  + `LegacyBackupExtension`/`.mkbackup` + `GetLegacyBackupFilePath`,
  `ResetIpadResolution` restores legacy without deleting it and
  `SetIpadResolution` only snapshots when neither backup exists; remote
  `GraphicsSettingsApplier` consts + bilingual backup/restore (new wins
  when both exist, no auto-migration); `NexoraUpdate-` deliberately left
  alone. Docs updated: `AGENTS.md`, `ARCHITECTURE.md` (options table,
  5.4 guard + rollback, plus drive-by fix of stale
  `ProcessManagementService` → `GameLoopProcessService`), oracle
  glossary + patterns, memory pattern. New tests: 2 local fallback +
  5 remote fallback (`RemoteBackupFallbackTests` with scripted fake ADB);
  build 0/0, full suite green.)
- **Concurrency hardening for GameLoop state**: ✅ DONE (confirmed scope
  implemented: single non-reentrant `SemaphoreSlim(1,1)` in
  `GameLoopService` around exactly `ConnectAsync`/`LoadVersionAsync`/
  `ApplyGraphicsAsync`, acquired with the caller's token and released in
  `finally`; collaborators untouched so the connector-internal Connect →
  LoadVersion flow stays below the gate and can never re-acquire —
  verified by re-read, nesting impossible by construction. Readers stay
  outside (transient stale display only); `Disconnect` stays outside per
  user decision, with the residual programmatic risk documented in the
  gate comment. Analysis found the UI already serializes everything via
  `_isBusy`, so user-visible behavior is unchanged — the gate enforces the
  same invariant below the UI. New `GameLoopSessionConcurrencyTests`
  (TCS-gated fake ADB, isolated temp work roots) proves LoadVersion ×
  LoadVersion self-consistency plus Apply × LoadVersion no-contamination;
  build 0/0, 280/280 tests.)
