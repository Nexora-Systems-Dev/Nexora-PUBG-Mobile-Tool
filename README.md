# Nexora PUBG Mobile Tool

Windows desktop application written in C# and WPF for applying selected GameLoop and PUBG Mobile settings.

The project is a rebuild of the existing tool workflow. The interface is implemented in WPF; the operations remain explicit and user-triggered.

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

## Development

Restore and run:

    dotnet restore .\Nexora.csproj
    dotnet run --project .\Nexora.csproj

Build Release:

    dotnet build .\Nexora.csproj --configuration Release

The GitHub Actions workflow in .github/workflows/build.yml runs the same Release build on Windows for pushes and pull requests targeting main.

## Release package

Create a self-contained Windows x64 executable and a ZIP package:

    .\scripts\package-release.ps1 -Version v1.0.9

The generated files are written to artifacts:

- Nexora-v1.0.9-win-x64.exe — self-contained executable with the .NET runtime.
- Nexora-v1.0.9-win-x64.zip — portable package with release notes.
- Nexora-v1.0.9-win-x64-SHA256SUMS.txt — SHA-256 checksums.

artifacts, bin, and obj are excluded from Git.

## Project layout

    MainWindow.xaml(.cs)       WPF shell, navigation, and page handlers
    Models/                    Shared result and settings models
    Services/                  GameLoop, ADB, Windows, registry, update, and layout code
    Assets/                    Profiles, styles, icons, and bundled helper tools
    scripts/                   Reproducible release packaging scripts
    app.manifest               Administrator-elevation manifest
    Nexora.csproj              .NET 8 WPF project definition

## Operational notes

- Settings are not applied until the related action is selected.
- NVIDIA Profile Inspector or Defender operations can fail because of driver versions, file locks, antivirus policy, or Windows security policy. The result is reported in the UI.
- The update endpoint checks the latest release from `mohammad-emad-dev/Nexora-PUBG-Mobile-Tool` and downloads the ZIP asset.
- No open-source license has been selected yet. Add a LICENSE file before granting reuse rights.
