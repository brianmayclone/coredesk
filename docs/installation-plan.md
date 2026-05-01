# CoreDesk Installation Plan

CoreDesk must not assume developer runtimes are already installed on the target tablet.

## Runtime Dependencies

- Development target: .NET 9 and Windows App SDK.
- Framework-dependent installer must include or install the matching .NET 9 Desktop Runtime for the chosen architecture.
- Current .NET runtime observed during development: `.NET 9.0 Desktop Runtime v9.0.15`.
- Architecture matters:
  - x64 installer needs x64 .NET Desktop Runtime.
  - x86 installer needs x86 .NET Desktop Runtime.
  - ARM64 installer needs ARM64 .NET Desktop Runtime.
- The development app is currently built x64 by default for automation stability.

## Preferred Installer Strategy

- V1 installer should use a self-contained publish where practical, so end users do not manually install .NET.
- Windows App SDK is configured self-contained for unpackaged dev/test runs.
- If using framework-dependent publishing later, the installer must bootstrap:
  - .NET 9 Desktop Runtime.
  - Windows App SDK runtime if not self-contained.
  - VC/runtime dependencies required by Windows App SDK tooling.

## Build Outputs

- Developer/debug build: unpackaged x64 WinUI app.
- Portable diagnostic build: self-contained folder, no autostart by default.
- Portable profiles exist for `portable-x64`, `portable-x86` and `portable-arm64`.
- Production installer: classic Windows installer first.
- Optional later: MSIX package.

## Installer Options

- Autostart with Windows.
- Desktop shortcut.
- Tray icon enabled.
- Start in Touch Mode.
- Start minimized/Desktop Mode.
- Surface default configuration.
- Safe Mode shortcut.
