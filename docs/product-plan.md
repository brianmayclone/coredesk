# CoreDesk Product Plan

CoreDesk is a Windows 11 touch shell overlay for Surface-class tablets and 2-in-1 devices. It gives Windows a mobile-style touch surface without replacing Explorer, blocking Windows recovery paths, or taking ownership of security-critical OS behavior.

## Product Goal

CoreDesk runs as the primary fullscreen touch UI when a device is used like a tablet. It recedes automatically when keyboard, docking, external monitor, or manual desktop intent is detected. The user can always return to normal Windows use.

The product character is: a Surface without keyboard feels like a tablet; the same device with keyboard feels like a normal Windows laptop.

## Target Platform

- Windows 11 22H2 or newer.
- Microsoft Surface devices, Windows tablets, convertibles and 2-in-1 devices.
- Touchscreen recommended.
- Runtime stack: C#, .NET 9, WinUI 3, Windows App SDK.
- Persistence: JSON for V1, SQLite only if later data volume requires it.

## Product Layers

### Shell Host

- Fullscreen homescreen.
- Safe desktop fallback.
- Taskbar hide/show in touch mode.
- Tray icon at all times.
- Emergency exit with `Esc`.
- Safe mode with destructive integrations disabled.
- Startup diagnostics and logs.

### Operating Modes

- Touch Mode: fullscreen shell, taskbar hidden if enabled, gestures active, touch-sized UI.
- Desktop Mode: shell hidden or backgrounded, taskbar restored, tray and hotkey available.
- Manual switching: dock button, settings button, tray command, `Ctrl+Alt+T`, optional three-finger gesture.
- Automatic switching: keyboard, Type Cover, Bluetooth keyboard, mouse, docking station, external monitor and slate posture signals when available.

### Homescreen

- Wallpaper, app icon grid, dock, status area, page indicator and optional widgets.
- First-run layout seeds common Windows apps on the homescreen and a Utilities folder for tools.
- Multiple pages with horizontal swipe.
- Responsive portrait and landscape grid.
- Default landscape: about 8 columns by 4 rows.
- Default portrait: about 5 columns by 6 rows.
- Touch targets at least 48px, preferably 64px+.
- App icons show symbol, name, optional badge and long-press context menu.

### Dock

- Bottom by default, configurable left/right.
- Translucent, rounded, visually separated from wallpaper.
- Recommended max 6-10 items.
- Contains favorite apps and system actions: Browser, File Explorer, Settings, AppDrawer, Desktop, optional Search.
- Optional status items: time, battery, Wi-Fi, volume, keyboard, notification indicator.

### AppDrawer

- Fullscreen touch view with search.
- Shows Win32 shortcuts, Store/UWP apps, PWAs, URLs, files and custom shortcuts.
- Installed apps are re-indexed periodically while CoreDesk is running so newly installed programs appear without restarting the shell.
- Supports alphabetical grid/list, categories, recent apps and frequent apps.
- Supports launch, add to homescreen, add to dock, details, hide and uninstall when available.
- Search supports names, partial terms and fuzzy matching.

### Folders

- Created by dragging app onto app, context action or multi-select.
- Displayed as an icon tile with mini app previews, folder name and optional count.
- Open as large centered rounded overlay.
- Supports app reorder, remove, rename, delete, color and optional folder icon.

### Drag and Drop

- Long press enters edit mode.
- Icons can move across grid positions, pages, folders and dock.
- Edge hover switches pages.
- Valid and invalid drop targets are visually distinct.
- Required acceptance path: AppDrawer to homescreen, then into folder, then into dock using touch only.

### Status and Control Center

- Status placement: top bar, dock-integrated or off.
- Status contents: time, optional date, battery, charging, Wi-Fi, volume, Bluetooth, keyboard, mode and optional user profile.
- Control Center opens by top-right swipe, status tap or dock button.
- Control Center actions: brightness, volume, Wi-Fi, Bluetooth, airplane mode when available, rotation lock, night mode, screenshot, touch keyboard, desktop, lock shell and restart shell.

### Settings

Fullscreen tablet-style settings app with these sections:

- General: enable/disable CoreDesk, autostart, default mode, language, theme.
- Appearance: wallpaper, accent color, icon size, grid size, dock position, transparency, status position, reduced animations.
- Behavior: keyboard/mouse/monitor switching, startup behavior, return after app closes, desktop access.
- Apps: visible/hidden apps, categories, defaults, AppDrawer sorting, dock apps, reset homescreen.
- Gestures: AppDrawer, Control Center, Desktop, Back, Multitasking and enable/disable.
- System: hardware status, input devices, Windows version, logs, diagnostics, restart shell, import/export.

Current V1 implementation binds core General/Appearance behavior to persisted settings: language, theme, dock position, autostart, taskbar hiding, keyboard auto-switch and reduced animations.

### Gestures

- Left/right swipe: homescreen page.
- Bottom-up swipe: AppDrawer.
- Top-right/down swipe: Control Center.
- Left-edge swipe: back or recents.
- Long press: edit mode.
- Optional pinch: page overview.
- Optional three-finger: desktop.
- Optional four-finger: task switch.
- All gestures configurable and disableable.

### App Launch and Window Behavior

- Launch UWP, Microsoft Store, Win32, PWA/Web apps, system tools, custom shortcuts, URLs, files and folders.
- Per-app behavior: normal, maximized, best-effort fullscreen, keep shell behind, minimize shell, keep launcher available, return after app closes.
- Avoid always-on-top behavior that traps users.

### Multitasking

- Recent launched apps.
- App switching through dock.
- Desktop action.
- Windows Task View action.
- Running apps list where technically possible.
- Later: custom recents, split-screen suggestions, floating switcher.

### Touch Keyboard

- Search and settings inputs open the Windows touch keyboard when appropriate.
- Input fields avoid being covered.
- Physical keyboard presence does not permanently disable touch keyboard flows.

### Design

- Modern, calm, high-quality tablet visual style.
- Large icons, rounded corners, soft shadows, Mica/Acrylic/blur where available.
- Light, dark, system theme and custom accent color.
- Smooth but restrained animations, reduce-motion setting.
- No classic desktop-window feel in primary UI.

### Persistence

- Persist settings, homescreen layout, dock, folders, app visibility, theme, gestures and hardware behavior.
- JSON with schema version, backups and broken-file recovery.
- Import/export for settings and layouts.

### Safety and Stability

- CoreDesk must never make Windows unusable.
- Explorer is not replaced or blocked.
- `Ctrl+Alt+Del` remains untouched.
- Crash path restores taskbar.
- Tray exit, `Esc`, safe mode, reset config and logs are required.
- Watchdog/recovery path planned before production packaging.

### Performance

- Target startup under 2 seconds after login.
- Aim for 60 FPS shell interactions where hardware supports it.
- Low idle CPU.
- RAM target under 300 MB.
- Cache app list and icons.

### Accessibility

- Large touch targets.
- Scalable text.
- High contrast.
- UI Automation names for important controls.
- Reduced animations.
- Clear focus states.
- Keyboard operation where practical.

### Installation and Administration

- Classic Windows installer first.
- MSIX optional.
- Portable build for diagnostics/development.
- Installer must include or bootstrap required runtime dependencies, especially the matching .NET 9 Desktop Runtime architecture.
- Autostart, desktop shortcut, tray icon, start touch/minimized and Surface defaults.
- Enterprise preparation: central JSON config, locked settings, kiosk-like mode, app whitelist/blacklist, layout import/export and group policy-ready structure.

## Release Shape

### V1 Required

- Fullscreen homescreen, app grid, dock, AppDrawer, search, launch apps.
- Move icons, create folders, fullscreen settings.
- Automatic keyboard detection and touch/desktop switching.
- Status for time, battery and Wi-Fi.
- JSON configuration, autostart and emergency exit.
- Automated unit tests, integration tests and UI screenshots for core views.

### V1.5

- Store app discovery refinement.
- Control Center complete actions.
- Rich gestures and page overview.
- Recents view and running-app hints.
- Import/export UI.
- Better icon extraction and caching.

### Post-V1

- Widgets, usage stats, profiles, enterprise kiosk mode, cloud sync, theme store, icon packs, gesture customizer, Windows Search integration, voice input and pen-specific actions.
