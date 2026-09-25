<div align="center">
  <img src="Assets/Icons/nexora.png" alt="Nexora logo" width="128" />
  <h1>Nexora PUBG Mobile Tool</h1>
  <p>Explicit, review-before-apply tuning for GameLoop and PUBG Mobile — graphics, emulator, network, Windows, and GPU, from one Windows desktop app.</p>
  <p>
    <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" /></a>
    <a href="./Nexora.Tests"><img src="https://img.shields.io/badge/tests-534-2ea44f" alt="534 tests" /></a>
    <a href="https://github.com/Nexora-Systems-Dev/Nexora-PUBG-Mobile-Tool/actions/workflows/build.yml"><img src="https://github.com/Nexora-Systems-Dev/Nexora-PUBG-Mobile-Tool/actions/workflows/build.yml/badge.svg" alt="Build status" /></a>
    <a href="#license"><img src="https://img.shields.io/badge/license-not%20selected-lightgrey" alt="License not selected" /></a>
  </p>
  <p><em>Nexora is an independent community utility. It is not affiliated with, endorsed by, or sponsored by PUBG Mobile, Tencent, or GameLoop.</em></p>
</div>

## Features

Nexora connects to a running GameLoop instance over ADB and applies exactly the settings you pick — nothing in the background, nothing bundled. Every action shows what it is about to change and reports what actually happened.

- **Graphics** — read and apply quality, frame rate, visual style, and shadow presets per PUBG Mobile package, with iPad-style display and key-layout profiles.
- **Tuning** — manual emulator controls (CPU cores, memory, DPI, render caches, discrete GPU, V-Sync, ADB, anti-aliasing), clamped to your real hardware and verified by read-back.
- **Network** — DNS management on active adapters, plus connection and package inspection.
- **Optimizer** — hardware-aware Windows and GPU actions (power plans, GameLoop priority, NVIDIA profiles) with per-action results.
- **Shortcuts** — desktop shortcuts that launch GameLoop packages through the resolved install path.
- **About** — application version, environment facts, and the safety statement.

## What it never does

No code injection. No game-memory reads or writes. No gameplay automation. No patches to PUBG Mobile executables. Some actions do touch Windows settings, power plans, network adapters, or GameLoop config files — those are always explicit, and the UI reports the outcome. Following GameLoop and PUBG Mobile rules remains your call.

## Requirements

Windows 10/11 x64 · GameLoop 64-bit · Administrator rights (the manifest requests elevation because several actions write GameLoop files, registry values, power settings, or adapter configuration).

## Quick start

Run from source:

```powershell
dotnet restore .\Nexora.slnx
dotnet run --project .\Nexora.csproj
```

## Build and test

Build and test a Release:

```powershell
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
```

The standard suite (534 tests) needs no emulator. The live GameLoop tests are opt-in:

```powershell
dotnet test .\Nexora.slnx --configuration Release --filter "Category=LiveFunctionalVerification"
```

## Release process

Package the self-contained release with [`scripts/package-release.ps1`](./scripts/package-release.ps1):

```powershell
.\scripts\package-release.ps1 -Version v1.3.0
```

This produces `Nexora-v1.3.0-win-x64.exe`, `Nexora-v1.3.0-win-x64.zip`, and a `SHA256SUMS.txt` under `artifacts/`. Releases are unsigned (no Authenticode certificate configured), so paste the `SHA-256 (...)` line the script prints into the GitHub release body — the in-app updater reads the checksum from there.

## Project layout

- `MainWindow.xaml(.cs)` is a 250-line shell: navigation, lifecycle, chrome. Each page is a `UserControl` + ViewModel under `Features/<Feature>/Presentation/`.
- One folder per feature (`Graphics`, `Tuning`, `Network`, `Optimizer`, `Shortcuts`, `About`, `GameLoop`, `Performance`, `Updates`, `Security`), each with `Application` / `Domain` / `Infrastructure` layers.
- Composition lives in `Bootstrap/`; GameLoop paths are resolved from registry, running processes, and standard locations — never hardcoded, never assumed to be on a particular drive.
- Full structure, conventions, and the refactor record: [`ARCHITECTURE.md`](./ARCHITECTURE.md) · [`docs/archive/PROJECT-LAYOUT.md`](./docs/archive/PROJECT-LAYOUT.md) · [`CHANGELOG.md`](./CHANGELOG.md)

## Operational notes

- Settings are not applied until you select the action.
- NVIDIA Profile Inspector, Defender, and power-plan operations can fail on driver versions, file locks, or Windows policy. Failures are reported, not hidden.
- **Defender exclusions are cleaned up on close.** When an optimizer action excludes the GameLoop directory from Windows Defender, Nexora removes that exclusion again when you close the window or restore the performance session — and only the exclusion it added itself; an exclusion you or another tool already had is left untouched. If a close is interrupted before that runs, remove it by hand from an elevated PowerShell (list first, then remove your path):

  ```powershell
  Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
  Remove-MpPreference -ExclusionPath 'C:\Program Files\TxGameAssistant'
  ```
- Updates apply in place: a helper waits for the app to exit, overwrites the executable, relaunches it, and cleans up staging.
- `artifacts/`, `bin/`, and `obj/` are build outputs and stay out of Git.

## License

No open-source license has been selected yet. Until a `LICENSE` file is added, treat this repository as source-available for inspection only — do not redistribute or reuse the code as an open-source project.
