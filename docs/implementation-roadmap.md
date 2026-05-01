# CoreDesk Implementation Roadmap

This roadmap is the working sequence for implementing the full TouchShell requirements. Each milestone must leave the repository buildable and testable.

## Milestone 1 - Planning, Diagnostics and Harness

- Add complete product, roadmap, QA and traceability documentation.
- Add command-line options: `--safe-mode`, `--reset-config`, `--diagnostics`, `--mock-hardware`, `--language en|de`.
- Add mock services for app discovery, hardware and system integration.
- Add UI test project using FlaUI UIA3.
- Save screenshots and logs under `artifacts/`.

## Milestone 2 - Foundation Hardening

- Replace static service construction with a small composition root.
- Keep taskbar and tray behind system integration interfaces.
- Restore taskbar on exit, crash, safe mode and desktop mode.
- Add tray commands: open, touch mode, desktop mode, settings, safe mode, exit.
- Add `Ctrl+Alt+T` mode toggle.
- Add autostart, power, network and status service abstractions.
- Add structured diagnostics log file.

## Milestone 3 - Settings and Persistence

- Expand settings and layout models to match product plan.
- Add schema versioning and migration hooks.
- Add import/export APIs.
- Add testable layout operations for homescreen placement, dock placement and folder membership.
- Add settings view models for all settings sections.
- Add tests for defaults, migrations, broken config recovery and round trips.

## Milestone 4 - App Index

- Index Start Menu shortcuts.
- Add Store/UWP app discovery.
- Add URL, file, folder and custom shortcut support.
- Extract and cache icons.
- Add app categories, hidden apps and frequent/recent metadata.
- Add fuzzy search tests.

## Milestone 5 - Shell UI

- Build responsive homescreen grid with pages.
- Build dock with bottom/left/right positions.
- Build AppDrawer with search, categories, recent and frequent groups.
- Build status area and Control Center.
- Move visible strings to localization resources.
- Add accessibility names and keyboard focus states.

## Milestone 6 - Layout Editing

- Add edit mode entered by long press.
- Add drag and drop between homescreen pages.
- Add AppDrawer to homescreen drag.
- Add folder creation and folder overlay.
- Add dock drop/reorder/remove.
- Persist every layout mutation.

## Milestone 7 - Hardware and System Mode

- Improve detection for keyboards, Type Cover, Bluetooth keyboards, mouse, monitor count, touch, battery and network.
- Add user-configurable auto-switch rules.
- Add mode change toast.
- Add screen keyboard handling.
- Add desktop, task view and app switch actions.

## Milestone 8 - Installer and Administration

- Add portable publish profile.
- Add classic installer project or script.
- Bundle or bootstrap the correct .NET 9 Desktop Runtime per architecture.
- Prefer self-contained publishing for user-facing installers where package size is acceptable.
- Add autostart configuration.
- Add central JSON policy loading.
- Add setting locks, app whitelist and blacklist.
- Add Surface default profile.

## Milestone 9 - Performance and Accessibility Verification

- Measure startup time, idle CPU and memory.
- Verify touch target sizes.
- Verify high contrast and reduced motion.
- Verify UI Automation tree for core flows.
- Capture final acceptance screenshots.
