# CoreDesk QA and Debug Plan

CoreDesk must be testable by automation and by manual Surface acceptance runs. Automated tests must avoid trapping the user's desktop.

## Modes

- Normal: real Windows integrations enabled.
- Diagnostics: logs, stable test names and screenshot-friendly initial state.
- Mock hardware: fake app list, fake keyboard/touch/network/battery state and no destructive desktop manipulation.
- Safe mode: no taskbar hide, no autostart changes, no hardware auto-switch and default layout.

## Command-Line Options

- `--safe-mode`: force safe defaults and restore taskbar.
- `--reset-config`: move existing config aside and write defaults.
- `--diagnostics`: write detailed logs and expose predictable UI state.
- `--mock-hardware`: use mock hardware/system/app services.
- `--language en|de`: override language for test runs.

## Artifacts

- Logs: `artifacts/logs/<run-id>/coredesk.log`.
- Screenshots: `artifacts/screenshots/<run-id>/<scenario>.png`.
- UI test output: `artifacts/ui-tests/<run-id>/`.
- Build/test output stays outside tracked source files.

## Automated Test Layers

### Unit Tests

- Settings defaults.
- Layout operation behavior.
- JSON backup and broken file recovery.
- Search and fuzzy match.
- Homescreen, dock and folder layout operations.
- Shell mode switching.
- Safe mode decisions.

### Integration Tests

- App discovery from controlled shortcut folders.
- Launcher with harmless protocol/file targets.
- Import/export round trips.
- Taskbar and tray through mock system integration.
- Hardware mode switching through mock monitor events.

### UI Tests

FlaUI UIA3 visual screenshot runs use real Windows app discovery and wallpaper while disabling desktop manipulation:

```text
--diagnostics --safe-mode --language en
```

Do not use `--mock-hardware` for visual acceptance screenshots. Mock mode is only for deterministic service/integration coverage where fake apps, fake status and fake wallpaper are the point of the test.

Required screenshot scenarios:

- Homescreen.
- AppDrawer opened from dock.
- AppDrawer search.
- Settings overlay.
- Control Center.
- Folder overlay.
- Touch/Desktop mode switch.

UI tests must not run against real taskbar hiding. Use `--safe-mode` unless a test explicitly verifies mock system integration.

Current implementation status:

- `CoreDesk.UiTests` builds and is included in the solution.
- The default UI test run is safe and does not launch the desktop app unless `COREDESK_RUN_UI_TESTS=1`.
- The opt-in run launches CoreDesk in diagnostics safe mode and writes screenshot artifacts from the real desktop state.
- Remaining work: stabilize native WinUI window readiness so FlaUI can reliably wait for shell controls before continuing through AppDrawer, Settings and Control Center captures.

## Manual Acceptance

- Start Surface without keyboard: CoreDesk fullscreen.
- Launch app from homescreen.
- Open AppDrawer by gesture/button.
- Drag app to homescreen, folder and dock.
- Attach keyboard: desktop mode and taskbar visible.
- Detach keyboard: touch mode fullscreen.
- Open settings and operate by touch.
- Crash or exit leaves Windows usable.
- Reboot preserves settings and layout.
