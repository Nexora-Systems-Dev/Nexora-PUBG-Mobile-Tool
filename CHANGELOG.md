# Changelog

All notable changes to Nexora PUBG Mobile Tool are documented here.
Version tags match `AppConstants.CurrentVersion` and the GitHub release tags (`vX.Y.Z`).

## [Unreleased] — Feature-first reorganization (no behavior change)

### Changed
- Pure structural refactor per `docs/plans/PROJECT-LAYOUT.md` / `docs/plans/execution-plan.md`
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
  bounds, and the DPI allow-list. Full spec in `docs/plans/EMULATOR-TUNING-PLAN.md`.
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
