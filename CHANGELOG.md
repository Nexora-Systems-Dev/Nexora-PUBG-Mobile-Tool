# Changelog

All notable changes to Nexora PUBG Mobile Tool are documented here.
Version tags match `AppConstants.CurrentVersion` and the GitHub release tags (`vX.Y.Z`).

## [v1.3.0] — 2026-09-24

Covers `origin/main..HEAD` (20 commits): the reliability program, clean-code
Rounds 1–4c plus the complexity watch, the update-trust program F1–F4, and the
dead-code cleanup round. No release-process or version-semantics changes.

### Reliability and UI-token improvement program

- `790d5a0`: operation-bus lock, cancel-as-Skip with preemption, navigation dispatch, full XAML token vocabulary, drift guard tests.

### Clean-code Rounds 1–4c and complexity watch

- `865c6fd` (Round 1): dead usings, comment consolidation, single-resolve factory, expression-bodied startup task.
- `b258252` (Round 2): resolver tier extraction, registry DWORD helper, runner initializer, named constants.
- `22ae7df` (Round 3a): Tuning tool spine, write-set data split, icon path helper, invariant formatting, MarketUnder extraction.
- `a47694c` (Round 3b): Korean-package predicate, connect outcome split, sav locate unification, span search, shadow-file prerequisite.
- `ace78ca` (Round 3c): network operation spine, iPad guard/snapshot homes, sav-batch and shell-probe helpers.
- `13907bb` (Round 3d): plan-axis decomposition, scan-phase helpers, monitor stop spine, stale-check ternary.
- `e7e31c5` (Round 3e): vendor marker home, refresh-rate prerequisite, power capture split, routing outcome, XML mutation pipeline, registry tables.
- `914946a` (Round 3f): staging-tree step, executable-path ladder, checksum/signature prerequisites, handoff prompt split.
- `af2d625` (Round 4a): shell factory extraction, visibility/refresh map pairing, tier selector, close-once split.
- `f9fb71c` (Round 4b): page chrome consolidated into shared App styles, dead keys removed, docs repointed.
- `5cabca0` (Round 4c): shared operation spine for Optimizer and Shortcuts tool paths.
- `4778dd7` (complexity watch): SetIpadResolution write-step extraction, dead usings removed.

### Update-trust program F1–F4

- `fbf3e16`: publisher resolution through options, precise refusal and mismatch messages.
- `eb9c087` (F1): file-bound Authenticode check via WinVerifyTrust plus binding controls.
- `8d31bbb` (F2): exact-CN publisher identity replacing DN substring match.
- `2b93977` (F3): trusted-host validation on every redirect hop of the update fetch.
- `31a2366` (F4): handoff allowlist limited to the verified executable plus release notes.

### Dead-code cleanup round

- `27688e0`: seven unreferenced XAML element names removed.
- `ebaaf3d`: superseded plans archived, tracked release copies dropped, regenerated release output ignored.

## [Unreleased] — Feature-first reorganization (no behavior change)

### Changed
- Pure structural refactor per `docs/archive/PROJECT-LAYOUT.md` / `docs/archive/execution-plan.md`
  (Phases 1–4): every non-UI file moved into its feature folder with
  folder-matching namespaces; the six `MainWindow.xaml` page regions became
  standalone `UserControl`s with ViewModels under
  `Features/<Feature>/Presentation/`; `MainWindow.xaml.cs` shrank from 1,078
  to 250 lines (navigation/lifecycle/chrome shell); DI composition extracted
  to `Bootstrap/ServiceCollectionExtensions.cs`.
- No runtime behavior changed in any refactor commit — each phase landed
  green (build 0 errors / 0 warnings, standard suite passing throughout).
- Standard suite grew from the 419-test baseline to **519 passing** (page
  ViewModel suites, DI registration tests, `ArchitectureGuardTests` source
  guards), with `graphify-out/`, `bin/`, `obj/`, and `artifacts/` untouched
  throughout.

## [v1.1.0] — Emulator Tuning page

### Added
- New **Tuning** workspace page: manual processor cores, memory, screen DPI, and
  emulator-internal toggles (rendering cache, global rendering cache, discrete
  graphics, rendering optimization, V-Sync, ADB debugging, anti-aliasing),
  forced directly into `HKCU\SOFTWARE\Tencent\MobileGamePC` with
  write-then-read-back verification per value.
- Always-visible notice: GameLoop must be closed before applying; Apply is
  locked while emulator processes run.
- **End Task** button on the Tuning page: ends the full GameLoop process tree
  and auto-refreshes the page state.
- `IEmulatorSettingsService` (`Features/GameLoop/EmulatorSettingsService.cs`)
  with `EmulatorTuningCatalog` as the single home for all tuning value names,
  bounds, and the DPI allow-list. Full spec in `docs/archive/EMULATOR-TUNING-PLAN.md`.
- 16 new unit tests (`EmulatorSettingsServiceTests`): load/apply round-trip,
  hardware clamping, DPI rejection, inverted ADB mapping, paired GPU writes,
  write-failure and read-back-mismatch paths, cancellation, running-guard.

### Security
- Tuning writes are explicit Apply-click only, user hive only (`IUserRegistry`),
  server-side clamped to real hardware; unknown DPI rejected before any write.

### Notes
- Render-mode selector (DirectX+/OpenGL+/Auto), root authority, and custom
  phone model are reserved for a follow-up once their exact registry value
  names are confirmed from a live `HKCU\Software\Tencent\MobileGamePC` export.
