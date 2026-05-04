# CoreDesk Test Commands

Use these commands from the repository root.

## Build

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build CoreDesk.sln
```

## Unit and Integration Tests

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests\CoreDesk.Tests\CoreDesk.Tests.csproj
```

## UI Tests With Screenshots

UI tests are opt-in because they start the desktop app. Visual screenshot runs use diagnostics safe mode so real wallpaper and icons are visible while taskbar hiding and hardware auto-switching stay disabled.

```powershell
$env:COREDESK_RUN_UI_TESTS='1'
$env:COREDESK_APP_ARGS='--diagnostics --safe-mode --language en'
& 'C:\Program Files\dotnet\dotnet.exe' build src\CoreDesk.App\CoreDesk.App.csproj -p:Platform=x64
& 'C:\Program Files\dotnet\dotnet.exe' test tests\CoreDesk.UiTests\CoreDesk.UiTests.csproj
```

Use `--mock-hardware` only for deterministic mock-service checks, not for visual acceptance screenshots.

## Session Shell Replacement Test

This mode is closer to Cairo-style shell replacement testing without permanently changing `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`. It stops Explorer shell surfaces for the current run and restarts Explorer when CoreDesk exits.

```powershell
dotnet run --project src\CoreDesk.App\CoreDesk.App.csproj -p:Platform=x64 -- --diagnostics --replace-explorer-for-session --language en
```

Keep a terminal open before running this mode so you have a recovery path if the UI crashes.

Screenshots are written to:

```text
artifacts/screenshots/<run-id>/
```

## Portable Self-Contained Builds

These builds include the .NET runtime for the selected architecture.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish src\CoreDesk.App\CoreDesk.App.csproj /p:PublishProfile=portable-x64
& 'C:\Program Files\dotnet\dotnet.exe' publish src\CoreDesk.App\CoreDesk.App.csproj /p:PublishProfile=portable-x86
& 'C:\Program Files\dotnet\dotnet.exe' publish src\CoreDesk.App\CoreDesk.App.csproj /p:PublishProfile=portable-arm64
```
