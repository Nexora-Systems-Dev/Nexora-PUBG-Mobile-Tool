# Emulator Tuning — Implementation Plan

> New MainWindow page: manual CPU / RAM / emulator-internal settings, forced through
> `HKCU\Software\Tencent\MobileGamePC` via `IUserRegistry`.
> Standards: clean code, security-first, async hygiene, 0 warnings / 0 errors, full test gate.

## 1. Goal & Non-Goals

- **Goal:** user sets processor, memory, DPI, rendering mode, cache/GPU/VSync/ADB/root/AA
  toggles, and phone model; Nexora writes them to the GameLoop user hive with
  write-then-read-back verification. Least lag, max stability, ideal UX.
- **Non-goals:** automatic hardware-derived plans (existing Smart Settings owns that);
  live ADB/graphics flows; touching `HKLM` or any other hive.

## 2. Control → Registry Map

| # | Control | Type | Registry value | State |
|---|---|---|---|---|
| 1 | Processor (cores) | Slider 1–8, clamped to real cores | `VMCpuCount` DWORD | Known |
| 2 | Memory | Slider, clamped to real RAM | `VMMemorySizeInMB` DWORD | Known |
| 3 | Screen DPI | Slider/dropdown | `VMDPI` DWORD | Known |
| 4 | Rendering cache | Toggle | `LocalShaderCacheEnabled` DWORD | Known |
| 5 | Global rendering cache | Toggle | `ShaderCacheEnabled` DWORD | Known |
| 6 | Discrete graphics | Toggle | `GraphicsCardEnabled` + `SetGraphicsCard` DWORD | Known |
| 7 | Rendering optimization | Toggle | `RenderOptimizeEnabled` DWORD | Known |
| 8 | V-Sync | Toggle | `VSyncEnabled` DWORD | Known |
| 9 | ADB debugging | Toggle (inverted: ON → `AdbDisable=0`) | `AdbDisable` DWORD | Known |
| 10 | Anti-aliasing | Toggle/dropdown | `FxaaQuality` DWORD | Likely — verify levels |
| 11 | Rendering mode (DirectX+/OpenGL+/Auto) | Segmented 3-way | ? engine-mode DWORD | **OPEN — need name + values** |
| 12 | Root authority | Toggle | ? | **OPEN — need name + values** |
| 13 | Custom phone model | Text fields (brand/model) | ? strings (REG_SZ, not DWORD) | **OPEN — need names + write path** |

> Build order: ship 1–10 first; 11–13 slot into the same catalog/service the moment
> the names arrive. No rework — the catalog is data-driven.

## 3. Architecture (follows AGENTS.md / ARCHITECTURE.md)

```text
MainWindow page (XAML only + thin handlers)
  → IEmulatorSettingsService (new, singleton)
      → IUserRegistry (existing RegistryService → HKCU\...\MobileGamePC)
      → IGameLoopProcessService (existing running-guard)
      → Hardware snapshot (existing detection, for clamping CPU/RAM)
EmulatorTuningCatalog (new, Features/GameLoop): single home for names, ranges, defaults
EmulatorTuningOptions (new, Configuration): strongly-typed options, nullable-ctor fallback
```

- **No new facade.** Service is focused, constructor-injected, registered in
  `App.xaml.cs:ConfigureServices` as singleton; `MainWindow` takes the interface
  (nullable + manual fallback keeps the designer-safe pattern).
- **DWORD path:** `IUserRegistry.SetUserDword/GetUserDword` (existing seam).
- **String path (phone model):** `GetCurrentUserString/SetCurrentUserString` (existing
  seam, same one GPU routing uses). If the model values prove to be DWORDs, they
  collapse into the DWORD path with zero structural change.

## 4. UX Specification (locked decisions)

- **Notice banner**, always visible at top of page:
  *"GameLoop must be closed before applying settings."*
- **End Task button**: ends the entire GameLoop process tree via existing
  `IGameLoopProcessService.KillGameLoopProcesses` (same engine as Force Close),
  then auto-refreshes page state (notice clears, Apply unlocks).
- **Apply prompt** under the Apply button: *"To apply settings, click the button."*
- **Running guard:** values load anytime (reads are free); **Apply is disabled**
  while any emulator process is alive — same guard as the iPad view, surfaced as
  the notice instead of a surprise error.
- **Layout:** grouped cards — Performance (processor, memory), Graphics (render mode,
  DPI, caches, GPU, optimization, V-Sync, AA), System (ADB, root), Device (phone model).
  Reuses `SegmentButtonStyle`, card styles, and design tokens in `App.xaml`.
- **Apply report:** per-setting Applied/Skipped/Failed lines through the existing
  `PerformanceExecutionReport → OperationResult` pipeline, shown in the status area.
  Emulator restart note: settings take effect at emulator startup.

## 5. Security

1. **Explicit user-triggered writes only.** No background/apply-on-load writes; the
   elevated process writes registry solely from the Apply click (admin rule).
2. **Fail-closed validation:** every value clamped server-side before write
   (CPU 1–8 ∧ ≤ physical cores; RAM within physical memory; DPI/AA to allow-lists).
   Out-of-range input is rejected with a message, never written.
3. **Write-then-read-back verify** per value (pattern from `ApplySmartSettings:56`):
   write DWORD → read → compare → report. Mismatch = Failed, not silent.
4. **No new hives, no HKLM.** Service accepts only the fixed `MobileGamePC` user hive
   through `IUserRegistry`; arbitrary key paths are not exposable from the UI.
5. **Trust parity:** any future path-like input (none planned) must pass the same
   exact-segment trust bar as Defender/GPU checks.
6. **Inverted semantics documented in one place:** `AdbDisable` (0 = debugging on)
   mapped in the catalog, never inline in UI code.

## 6. Performance & Hygiene

- All service methods `async` with `CancellationToken` propagation; no `.Result`/`.Wait()`.
- Registry I/O is fast-local; still off the UI thread via `RunToolAsync`-style helper;
  Apply honors cancellation between values (no new writes start after cancel).
- Load on page-open only (no polling); refresh after End Task and after Apply.
- Shutdown path untouched (`Window_Closing` restore already covers power/priority;
  tuning values are intentional permanents, stated in the UI).

## 7. Testing Plan

| Area | Tests |
|---|---|
| Catalog | names/ranges unique; inverted `AdbDisable` mapping; clamp bounds |
| Service (fake `IUserRegistry`) | load defaults when missing; apply+verify round-trip; clamp CPU/RAM to hardware; write-mismatch → Failed; pre-cancelled CT throws |
| Guard | Apply blocked with fake running process; allowed when none; End Task refresh path |
| UI seams | null-guards, DI registration (singleton service, transient view unaffected) |
| Regression | full `Category!=LiveFunctionalVerification` suite green |

## 8. Docs

- `ARCHITECTURE.md`: new subsection under feature models + test-count bump.
- This file stays as the build record; `FIX-PLAN-100-PERCENT.md` untouched.

## 9. Build Order (steps)

1. `EmulatorTuningCatalog` + `EmulatorTuningOptions` (controls 1–10).
2. `IEmulatorSettingsService` + impl (load/clamp/apply/verify) + DI registration.
3. Tests for 1–2 (fake registry + fake process service).
4. XAML page: cards, notice, End Task, Apply + prompt, status report wiring.
5. `MainWindow` wiring (nullable ctor + fallback), nav entry.
6. Full gate: `build Release` 0W/0E + `test Category!=LiveFunctionalVerification` all green.
7. Docs bump.
8. Slot in controls 11–13 when names arrive (catalog rows + tests + XAML rows only).

## 10. Acceptance Criteria

- [ ] All 10 known controls load current values, clamp invalid input, apply with
      per-setting verified report; Apply locked while GameLoop runs with notice shown.
- [ ] End Task kills the tree and unlocks Apply without restart of Nexora.
- [ ] Build 0 warnings / 0 errors; full standard suite passes; live tests still excluded.
- [ ] No hardcoded paths/values outside the catalog; no `.Result`/`.Wait()`; no new facade.

## 11. Open Inputs (needed to finish 11–13)

- Rendering-mode value name + the 3 DWORDs for DirectX+ / OpenGL+ / Auto.
- Root-authority value name + on/off values.
- Phone-model value names (+ string vs DWORD) — paste a regedit export of
  `HKCU\Software\Tencent\MobileGamePC` and all three close out at once.
