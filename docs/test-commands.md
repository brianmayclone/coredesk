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

UI tests are opt-in because they start the desktop app. They run CoreDesk in diagnostics/mock mode, so real taskbar hiding is disabled.

```powershell
$env:COREDESK_RUN_UI_TESTS='1'
& 'C:\Program Files\dotnet\dotnet.exe' build src\CoreDesk.App\CoreDesk.App.csproj
& 'C:\Program Files\dotnet\dotnet.exe' test tests\CoreDesk.UiTests\CoreDesk.UiTests.csproj
```

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
