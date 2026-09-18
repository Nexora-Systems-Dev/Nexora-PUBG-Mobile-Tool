# Nexora — 100% Readiness Fix Plan (Step by Step)

> Source: read-only verification audit (build PASS 0W/0E, tests 370/370 PASS, 5 live excluded).
> Verdict: **NOT READY for new feature** until Phase 1 (Must-Fix Gate) is done.
> How to use: work top → bottom. Check each box only after `build + test` for that step passes.

Baseline commands (Windows, exact):
```powershell
dotnet restore .\Nexora.slnx
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
```

---

## Phase 1 — Must-Fix Gate (blocking new feature)

### Step 1 — Update single-factor trust [SUPPLY-CHAIN / HIGH]
- [ ] Fix `Services/UpdateArchiveValidator.cs:78-102` `VerifyExecutableIntegrity`
- Problem: returns `true` on hash match at `:92` without calling `IsAuthenticodeSigned`. Attacker compromising release-notes hash ships matching unsigned binary.
- Fix:
  1. Require **hash AND signature** when `expectedSha256` present: hash check → then `IsAuthenticodeSigned(exe, updates)` → fail with distinct errors (`hash mismatch` vs `untrusted signature`).
  2. Keep hash-only path as `false` (do not fall through to `true`).
  3. Keep existing guards: `ExtractEntriesSafely:39-58` zip-slip, `FindExpectedExecutable:13-37` allowlist, `IsAuthenticodeSigned:148-192` publisher pin + chain.
- Tests to add in `Nexora.Tests/Services/UpdateArchiveValidatorTests.cs` + `UpdateIntegrityTests.cs`:
  - `hash_match_unsigned_returns_false`
  - `hash_match_signed_returns_true`
  - `hash_mismatch_returns_false_with_hash_error`
- Verify: `dotnet test --filter "FullyQualifiedName~UpdateArchiveValidator|FullyQualifiedName~UpdateIntegrity"`
- Done when: unsigned binary with correct hash is rejected; error messages distinguish hash vs signature.

### Step 2 — Performance permanent writes + broken shutdown restore [SAFETY / HIGH]
- [ ] Fix `Services/Performance/PerformanceEngineFacade.cs:66-132` + `Services/Performance/ProcessPriorityService.cs:61-65`
- Problem: registry/GPU/NVIDIA/Defender writes never snapshotted; `RestorePerformanceSessionAsync:127-132` wraps sync `Restore()` in `Task.Run`; `ProcessPriorityService.Restore()` fire-and-forgets `_monitor.Stop()` instead of awaiting `StopMonitorAsync` (`ProcessPriorityMonitor.cs:74-104`, 2s bound in `GameLoopOptions.cs:75`). Crash/kill between Apply and Close leaves High power plan + High priority + permanent registry.
- Fix:
  1. Facade → `await _processPriority.RestoreAsync(ct)` + `_powerSession.Restore()` on same path; pass `ct` from `MainWindow.xaml.cs:345` (currently missing).
  2. Add snapshot/restore for `GameLoopRegistryOptimizer.cs:35-204`, `GpuRoutingService.cs:21-76`, `NvidiaOptimizerService.cs:36-63` — OR explicitly mark them `permanent-with-consent` in UI text if restore is not possible.
  3. `ProcessPriorityService.Restore()` → await `StopMonitorAsync` with bound; no fire-and-forget.
- Tests to add:
  - `PowerSessionRestoreTests` — assert `powercfg /setactive <orig>` called on restore.
  - `ShutdownRestorationTests` — assert content restored, not just shape/timing.
  - Cancel during `OptimizeAllAsync` leaves no orphan monitor tick.
- Verify: `dotnet test --filter "FullyQualifiedName~ShutdownRestoration|FullyQualifiedName~ProcessPriority|FullyQualifiedName~Performance"`
- Done when: kill/cancel mid-apply → power + priority restored; registry/GPU either restored or UI says permanent.

### Step 3 — Process-enumeration split brain [CORRECTNESS / HIGH]
- [ ] Fix `Shared/Infrastructure/GameLoopPathResolver.cs:67,118` vs `Services/Performance/GameLoopProcessService.cs:86`
- Problem: violates `AGENTS.md` one-home rule. Name lists differ (`EmulatorOptions.Emulator.RunningCheckProcessNames` vs `ProcessImageNames`). Resolver can resolve a path killer won't match or vice versa.
- Fix:
  1. Create shared `GameLoopProcessEnumerator` (single `GetProcessesByName` loop, single name list).
  2. Both `GameLoopPathResolver` and `GameLoopProcessService` inject/use it; delete direct `Process.GetProcessesByName` calls.
  3. Unify `RunningCheckProcessNames` + `ProcessImageNames` in `Configuration/EmulatorOptions.cs:22-25` (add missing `AppMarket`, `aow_exe` per spec).
- Tests to add: static pinning test — resolver and killer enumerate same set; no direct `GetProcessesByName` outside enumerator (grep in test).
- Verify: `dotnet test --filter "FullyQualifiedName~GameLoopPathResolver|FullyQualifiedName~ServiceComposition"`
- Done when: one enumeration home; `grep GetProcessesByName` hits one prod file only.

### Step 4 — Cancel doesn't stop in-flight ADB [UX HANG / MED]
- [ ] Fix `Services/AdbClient.cs:70-73` `Shell()`, `Services/AdbClient.cs:36` `Run`, `Services/GraphicsSettingsApplier.cs:138,157,197`
- Problem: sync `Run` (20s `AdbCommandTimeout`) + `Shell(string)` without CT. Cancel mid-`Run` waits out timeout; `TransferWithRetryAsync:85-108` + `WaitForBootAsync:111+` check CT only between calls.
- Fix:
  1. Add `Shell(string command, CancellationToken ct)` overload (keep old as `ct = default` wrapper).
  2. Make `Run` cancellable or kill in-flight child on cancel (runner kill path); `GraphicsSettingsApplier` passes CT through.
  3. `TrySelectDevice(ct)` already CT-aware — reuse pattern.
- Tests to add in `AdbCancellationTests.cs`: cancel mid-Shell returns <2s, no 20s hang.
- Verify: `dotnet test --filter "FullyQualifiedName~AdbCancellation|FullyQualifiedName~AsyncHygiene"`
- Done when: Cancel button stops ADB in <2s.

### Step 5 — Docs truth [TRUTH / LOW but blocking]
- [ ] Fix `ARCHITECTURE.md:9,209` + `AGENTS.md` test count `246 → 370`
- [ ] Fix `ARCHITECTURE.md:130` `OnClosed → Window_Closing` (code is `MainWindow.xaml.cs:400-434` defer-close + 5s CTS `:418` + `Close()` in finally `:431-433`)
- [ ] Soften `AGENTS.md:44` "enforced by AsyncHygieneTests" → behavioral CT tests, not source scanner (`AsyncHygieneTests:112-133`)
- [ ] Refresh `graphify-out/` (stale since `21d61dd2`, HEAD `36a1df2` + deleted `IWindowsToolsService`/`WindowsToolsService`/`IGameLoopService` still graphed as god-node)
- Verify: `dotnet test` count matches doc; `git log --oneline -5` matches graph base.
- Done when: docs == code; no stale facade in graph.

**Gate check:** Steps 1-5 all checked → run full `build + test Category!=LiveFunctionalVerification` → 0W/0E, all green → allowed to plan new feature.

---

## Phase 2 — Hardening GAPs (can ride alongside next feature)

### Step 6 — No custom-install-dir override [MED]
- Files: `Shared/Infrastructure/GameLoopPathResolver.cs:27-103`, `Configuration/EmulatorOptions.cs`
- Problem: stale registry + stopped `D:\`/`E:\` install = loud but unrecoverable "not found".
- Fix: manual-path setting (validated, persisted) as tier-0 before registry; validate `Directory.Exists` + `IsGameLoopPath`; UI textbox in Settings page.
- Test: `D:\Games\TxGameAssistant` with empty registry resolves via override.
- Verify: `ServiceCompositionTests:41-52` + new override test.

### Step 7 — GetAppMarketPath unvalidated + GetRoot tier-3 missing [MED]
- Files: `Shared/Infrastructure/GameLoopPathResolver.cs:54-103,159-170`
- Problem: `GetAppMarketPath` derives from UI branch with no existence check; `GetRoot` has registry→process only, `GetUiPath:108-153` has full 3-tier.
- Fix: add `Directory.Exists` guard to `GetAppMarketPath`; add ProgramFiles tier to `GetRoot` (mirror `GetUiPath:142-150`).
- Test: derived non-existent AppMarket → null (loud fail in `ShortcutService.cs:44-54`, not silent wrong path).
- Verify: `GameLoopPathResolverTests:17-40`.

### Step 8 — Silent PATH fallback for ADB [MED-LOW]
- Files: `Services/AdbClient.cs:238-255` bare `adb.exe`
- Problem: falls back to silent PATH instead of loud "ADB not found".
- Fix: explicit error `ADB not found — repair GameLoop or set custom path` OR document intentional fallback in code comment + user message. Pick one, remove ambiguity.
- Test: `AdbPathResolutionTests:41-80` pins chosen behavior.

### Step 9 — GPU routing without trust check + Disconnect gate race [MED-LOW]
- Files: `Services/Performance/GpuRoutingService.cs:85-99` accepts any existing dir; `Services/GameLoopService.cs:64` `Disconnect` outside `SemaphoreSlim`, `_isBusy` plain bool (`MainWindow.xaml.cs:42`)
- Fix: `GpuRoutingService` → `IsTrustedGameLoopPath` fail-closed (match `DefenderExclusionService.cs:40-49,77-91` bar); `GameLoopService.Disconnect` → `WaitAsync(0)` fail-fast or `Interlocked` guard.
- Tests: untrusted dir rejected; concurrent Disconnect + gated op does not tear session.
- Verify: `DefenderExclusionTrustTests:17-88` + `GameLoopSessionConcurrencyTests`.

### Step 10 — Corrupt/offline/cancel paths unpinned [LOW, breadth]
- Files: `Features/GameLoop/Ue4SavEditor.cs:42-60`, `Features/GameLoop/UnrealCVarCodec.cs:38-41`, `Services/SaveProfileReader.cs:59,64`, `Services/UpdateService.cs`, `Features/SystemTools/Network/NetworkToolsService.cs`
- Fix + tests:
  1. Corrupt `Active.sav` (truncated/zero-length) → safe-fail pinned test.
  2. Corrupt `UserCustom.ini` (odd/non-hex) → pinned; replace `SaveProfileReader:59,64` hardcoded shadow hex + `EndsWith("48")` with `DecodeCVar`.
  3. Offline update/DNS, cancel mid-download, no-admin registry denial, multi-emulator (`:5555`) selector or explicit "first device" notice.
  4. Remove test-only local maps `LiveGameLoopVerificationTests.cs:200-211` drift risk → import `PubgVersionCatalog`.
- Verify: new `UpdateOfflineCancellationTests`, `RegistryElevationTests`, corrupt-format tests.

---

## Final Readiness Checklist
- [ ] Phase 1 Steps 1-5 checked
- [ ] `dotnet build Release` → 0 warnings, 0 errors
- [ ] `dotnet test Category!=LiveFunctionalVerification` → all pass (update count in docs)
- [ ] 5 live tests still present, still excluded by default
- [ ] `grep GetProcessesByName` → one prod home only
- [ ] `grep "\.Result\|\.Wait()"` → no prod hits (only `OperationResult.Result`)
- [ ] No hardcoded `C:\Program Files` in prod (docs/test fixtures only)
- [ ] Then: **READY FOR NEW FEATURE = YES**

_Last audit: 370/370 standard PASS, 5 live present/excluded. Re-run gate check after each Phase 1 step._
