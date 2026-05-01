# CoreDesk MVP Plan

This file tracks the V1 minimum required product. The complete product plan is in `docs/product-plan.md`; implementation sequencing is in `docs/implementation-roadmap.md`; testing and screenshots are in `docs/qa-debug-plan.md`; requirement status is in `docs/requirements-traceability.md`.

## Phase 1 - Foundation

- WinUI 3 .NET 9 solution.
- Fullscreen shell host.
- Tray icon.
- Taskbar hide/show abstraction.
- JSON settings and layout persistence.
- English/German localization service.
- Emergency exit and taskbar restore.

## Phase 2 - App Surface

- Discover Start Menu shortcuts.
- Add Store app discovery.
- Cache app metadata and icons.
- Launch Win32, Store apps, URLs and system actions.
- AppDrawer with search and categories.

## Phase 3 - Touch Layout

- Homescreen pages.
- Responsive app grid.
- Dock with configurable position.
- Touch drag and drop.
- Persistent icon positions.
- Folder creation by dropping one app onto another.

## Phase 4 - Modes and Hardware

- Detect keyboard, mouse, touch and monitor changes.
- Auto switch touch/desktop mode.
- Manual mode switch via dock, hotkey and tray.
- Show mode-change notification.
- Respect user setting for auto switching.

## Phase 5 - Settings and Administration

- Fullscreen settings UI.
- Autostart toggle.
- Import/export configuration.
- Safe mode.
- Log viewer.
- Initial admin policy hooks for locked settings.

## Acceptance Checklist

- Surface without keyboard starts into fullscreen CoreDesk.
- App icon launches the app.
- Swipe/button opens AppDrawer.
- App can be moved to homescreen, folder and dock by touch.
- Keyboard attach sends CoreDesk to desktop mode.
- Keyboard detach restores CoreDesk.
- Settings are touch usable.
- Crash or `Esc` leaves Windows usable.
- Restart keeps settings and layout.
