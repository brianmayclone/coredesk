# CoreDesk Architecture

CoreDesk is a WinUI 3 touch shell for Windows 11 tablets. It can run as a safe overlay above Explorer, and it has an opt-in session shell replacement test mode that stops Explorer for the current run without changing the user's Winlogon shell registry value.

## Projects

- `CoreDesk.App`: WinUI 3 host, shell UI, fullscreen window, XAML resources.
- `CoreDesk.Abstractions`: domain models and service contracts.
- `CoreDesk.Application`: shell state, commands, localization, mode orchestration.
- `CoreDesk.Windows`: Windows-specific app discovery, launching, hardware, taskbar/tray, and shell replacement integration.
- `CoreDesk.Persistence`: JSON configuration and layout storage with backups.
- `CoreDesk.Tests`: unit tests for non-UI behavior.

## MVP Decisions

- UI stack: WinUI 3 with Windows App SDK.
- Runtime: .NET 9.
- Languages: English and German from day one.
- Persisted storage: JSON first, SQLite optional later.
- Taskbar: actively hidden in overlay mode and restored in desktop/recovery paths.
- Session shell replacement: `--replace-explorer-for-session` stops Explorer shell surfaces for the current run, signals shell readiness when available, and restarts Explorer when CoreDesk exits.
- Tray: required MVP surface, with richer commands to follow.
- Safety: `Esc` restores the taskbar and closes the overlay.

## Vertical Slices

1. Shell host and recovery.
2. App discovery and app launching.
3. Homescreen, dock and app drawer.
4. Drag and drop layout editing.
5. Folder manager.
6. Settings and persisted preferences.
7. Hardware driven touch/desktop mode switching.
8. Installer, autostart and diagnostics.
