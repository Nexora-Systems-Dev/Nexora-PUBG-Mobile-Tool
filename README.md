# Nexora PUBG Mobile Tool

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-161%20passing-2ea44f)](./Nexora.Tests)
[![Build](https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/actions/workflows/build.yml/badge.svg)](https://github.com/mohammad-emad-dev/Nexora-PUBG-Mobile-Tool/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-not%20selected-lightgrey)](#license)

Windows desktop application written in C# and WPF for applying selected GameLoop and PUBG Mobile settings. The project keeps each action visible and user-triggered, so the user can review the intended change before applying it.

The project is a rebuild of the existing tool workflow. The interface is implemented in WPF; the operations remain explicit and user-triggered.

Nexora is a local utility. It does not inject code into PUBG Mobile, read or write game memory, automate gameplay, or modify game executables.

> Nexora is an independent community utility. It is not affiliated with, endorsed by, or sponsored by PUBG Mobile, Tencent, or GameLoop.

## What it does

- **GameLoop workflow:** connect through ADB, inspect the active PUBG Mobile package, and apply supported graphics and display profiles.
- **Hardware-aware tuning:** build recommendations from the detected CPU, memory, GPU, display, power state, and virtualization state.
- **Windows and GPU actions:** apply the selected performance actions with clear result reporting. Results can vary by driver, Windows policy, permissions, and hardware.
- **Network and shortcuts:** manage selected DNS settings and create direct GameLoop shortcuts when the required paths are available.

## Design principles

### Dynamic Path Resolution

Nexora does not assume that GameLoop is installed on the developer's drive. It resolves the installation from the relevant Windows registry entries, running GameLoop processes, and standard installation locations. If a portable or non-standard installation cannot be identified safely, the affected action reports the problem instead of silently targeting an unrelated directory.

### Safe, non-destructive tuning

The tool applies only the settings selected by the user. It does not inject code, automate gameplay, read or write game memory, modify PUBG Mobile executables, or patch the game itself. Some actions touch Windows settings, network adapters, power plans, or GameLoop configuration files; these actions are explicit and their result is reported in the interface.

This design does not constitute a guarantee against account action. Users should follow the current GameLoop and PUBG Mobile rules and use the tool at their own discretion.

## Scope

- Connect to GameLoop through ADB and detect supported PUBG Mobile packages.
- Read and apply graphics quality, frame rate, visual style, and shadow settings.
- Apply hardware-aware GameLoop and Windows performance actions.
- Change DNS settings on active network adapters.
- Apply tested iPad-style display and key-layout profiles.
- Create GameLoop desktop shortcuts.
- Stop GameLoop processes and restore the performance session when requested.
- Check the configured GitHub release endpoint for updates.

## Requirements

- Windows 10 or Windows 11, x64.
- GameLoop 64-bit.
- .NET 8 Desktop Runtime when using a framework-dependent build.
- Administrator permission. The manifest requests elevation because several operations write GameLoop files, registry values, power settings, network adapter settings, GPU profiles, or Windows security exclusions.

The optimizer has paths for Intel, AMD, and NVIDIA hardware and for laptops. Actual results depend on the installed driver, Windows policy, GameLoop version, and hardware capabilities.

Nexora discovers GameLoop from its 32-bit registry entries first, then from running GameLoop processes, and finally from standard Windows installation locations. It does not depend on the developer's drive. Portable or non-standard installations may require a future manual path selector; affected actions report a clear failure instead of silently using an unrelated path.

## Development

Restore and run:

    dotnet restore .\Nexora.slnx
    dotnet run --project .\Nexora.csproj

Build Release:

    dotnet build .\Nexora.slnx --configuration Release
    dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"

The normal test suite does not require GameLoop. Live verification tests require a running, configured emulator and can be run explicitly with:

    dotnet test .\Nexora.slnx --configuration Release --filter "Category=LiveFunctionalVerification"

The GitHub Actions workflow in .github/workflows/build.yml runs the same Release build on Windows for pushes and pull requests targeting main.

The current automated suite contains 161 passing tests. Live GameLoop verification is kept separate because it requires a configured emulator and a running local environment.

## Release package

Create a self-contained Windows x64 executable and a ZIP package:

    .\scripts\package-release.ps1 -Version v1.0.13

The generated files are written to artifacts:

- Nexora-v1.0.13-win-x64.exe — self-contained executable with the .NET runtime.
- Nexora-v1.0.13-win-x64.zip — portable package with release notes.
- Nexora-v1.0.13-win-x64-SHA256SUMS.txt — SHA-256 checksums.

artifacts, bin, and obj are excluded from Git.

## Project layout

    MainWindow.xaml(.cs)       WPF shell, navigation, and page handlers
    Configuration/             Version, paths, options, and operational constants
    Features/                  GameLoop, layout, and network-owned data/codecs
    Services/                  GameLoop, ADB, Windows, registry, update, and layout code
    Shared/                    Process, registry, file, and result primitives
    UI/                        Window behavior and presentation helpers
    Assets/                    Profiles, styles, icons, and bundled helper tools
    scripts/                   Reproducible release packaging scripts
    app.manifest               Administrator-elevation manifest
    Nexora.csproj              .NET 8 WPF project definition

## Operational notes

- Settings are not applied until the related action is selected.
- NVIDIA Profile Inspector or Defender operations can fail because of driver versions, file locks, antivirus policy, or Windows security policy. The result is reported in the UI.
- The update endpoint checks the latest release from `mohammad-emad-dev/Nexora-PUBG-Mobile-Tool` and downloads the ZIP asset.

## License

No open-source license has been selected yet. Until a `LICENSE` file is added, the repository should be treated as source-available for inspection only; do not redistribute or reuse the code as an open-source project.
