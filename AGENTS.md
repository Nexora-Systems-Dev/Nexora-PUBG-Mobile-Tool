# AGENTS.md — Nexora PUBG Mobile Tool

.NET 8 WPF desktop app (Windows-only, `net8.0-windows`, `WinExe`). Solution `Nexora.slnx` = `Nexora.csproj` (app) + `Nexora.Tests/` (xUnit + FluentAssertions). Full spec in `ARCHITECTURE.md`.

## Build / test (Windows, exact commands)

```powershell
dotnet restore .\Nexora.slnx
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
# Single test:
dotnet test .\Nexora.slnx --configuration Release --filter "FullyQualifiedName~<TestClassOrMethod>"
# Live emulator tests (NEVER run by default — need running GameLoop):
dotnet test .\Nexora.slnx --configuration Release --filter "Category=LiveFunctionalVerification"
```

- `TreatWarningsAsErrors=true` — build must be 0 warnings, 0 errors.
- CI (`ci.yml`) = restore → build Release → test with `Category!=LiveFunctionalVerification`. `build.yml` builds only `Nexora.csproj`.
- Release package: `.\scripts\package-release.ps1 -Version v1.0.15` → self-contained win-x64 exe+zip+SHA256SUMS in `artifacts/`.

## Never touch / never commit

- `graphify-out/` — protected analysis output. Never delete, edit, or include in cleanup.
- Regenerable, gitignored: `bin/`, `obj/`, `artifacts/`, `Nexora.Tests/bin/`, `Nexora.Tests/obj/`. Don't commit them.

## Architecture that isn't obvious

- DI root is `App.xaml.cs:ConfigureServices` — services are singletons, `MainWindow` is transient. `MainWindow` ctor args are nullable with manual fallbacks so the XAML designer doesn't crash; keep that pattern.
- `SystemToolsFacade`/`IWindowsToolsService` were dissolved (P2.8b): `MainWindow` takes five focused contracts (`ITempCleanupService`, `INetworkToolsService`, `IGameLoopProcessService`, `IShortcutService`, `IIpadLayoutService`). `PerformanceEngineFacade` (registered as `IGameLoopPerformanceEngine`) remains a separate singleton. Don't reintroduce a coordinating Windows-tools facade without updating all registrations.
- `IGameLoopService` was split (P2.8c) into `IGameLoopConnection` (session/connect: `CurrentPackage`/`IsAdbConnected`/`IsConnected`/`Disconnect`/`ConnectAsync`/`LoadVersionAsync`) and `IGraphicsProfileStore` (reads/apply), both implemented by one `GameLoopService` via factory-forward (`AddSingleton<GameLoopService>()` + per-facet forwards — never dual `AddSingleton<Iface,Impl>`, which would fork the shared gate/session). `MainWindow` takes both. New code needing only one side must depend on that facet, not the concrete class.
- GameLoop paths are **never hardcoded**. The pipeline lives in `Shared/Infrastructure/GameLoopPathResolver.cs` behind `IGameLoopPathResolver` (`GetRootFromRegistry`/`GetRoot`/`GetUiPath`/`GetAppMarketPath`/`IsGameLoopPath`). Resolution order: validated `CustomInstallRoot` override (`NEXORA_GAMELOOP_ROOT`) → `HKLM\SOFTWARE\WOW6432Node\Tencent\MobileGamePC` registry → running emulator process directories → `ProgramFiles` traversal. `GetAppMarketPath` returns only existing directories. New code touching GameLoop/ADB/shortcuts must call the resolver and fail loudly, never silently use a wrong drive.
- Process discovery has one home: `Services/Performance/IGameLoopProcessService.cs` (`GameLoopProcessService`) for PIDs/paths, backed by the single `Process.GetProcessesByName` home `Shared/Infrastructure/GameLoopProcessEnumerator.cs`. New code must inject the service or use the enumerator, never call `Process.GetProcessesByName` directly.
- PUBG package/version maps have one home: `Features/GameLoop/PubgVersionCatalog.cs` (`PubgVersions` + Quality/FrameRate/Style maps + `TryResolveGraphicsValues`). Don't reintroduce per-file static maps.
- App runs elevated (`app.manifest` = `requireAdministrator`) because it writes GameLoop files, registry, power plans, DNS, Defender exclusions. All such actions must stay explicit and user-triggered.

## Binary-format gotchas

- `Features/GameLoop/Ue4SavEditor.cs`: edit `Active.sav` **in place** — overwrite only the value byte, never change stream length or GameLoop corrupts the save.
- `Features/GameLoop/UnrealCVarCodec.cs`: `UserCustom.ini` CVars use XOR key `0x79` + uppercase hex under `+CVars=`. Use `EncodeCVar`/`DecodeCVar` with invariant-culture hex parsing.
- iPad layout (`Features/Layout/IpadLayoutService.cs`): reject `SetIpadResolution` while GameLoop is running (it locks `TVM_100.xml` and overwrites registry on exit). When closed, write `TVM_100.xml.nexora-backup` first; `ResetIpadResolution()` restores it, falling back to a legacy `TVM_100.xml.mkbackup` left by older versions (never auto-deleted).

## Code conventions

- Async everywhere with `CancellationToken` propagation; never `.Result` / `.Wait()` (covered by `AsyncHygieneTests` behavioral CT tests).
- `UpdateService` stages in `%TEMP%/NexoraUpdate-<Guid>`, sweeps in `finally`, purges >24h staleness on startup.
- UI: thin `MainWindow.xaml.cs`; formatting in `UI/Presentation/OptimizerDisplayFormatter` (invariant culture), layout math in `UI/Layout/ResponsiveLayoutManager`, tokens in `App.xaml`.
