namespace CoreDesk.Abstractions.Models;

public sealed class CoreDeskSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string Language { get; set; } = "en";

    public ShellTheme Theme { get; set; } = ShellTheme.System;

    public DockPosition DockPosition { get; set; } = DockPosition.Bottom;

    public bool HideTaskbarInTouchMode { get; set; } = true;

    public bool AutoSwitchOnKeyboard { get; set; } = true;

    public bool AutoStartWithWindows { get; set; }

    public int IconSize { get; set; } = 72;

    public int DockMaxItems { get; set; } = 8;

    public bool DiagnosticsEnabled { get; set; }

    public GridSettings Grid { get; set; } = new();

    public GestureSettings Gestures { get; set; } = new();

    public HardwareSwitchSettings HardwareSwitching { get; set; } = new();

    public AccessibilitySettings Accessibility { get; set; } = new();

    public AdminPolicySettings AdminPolicies { get; set; } = new();
}

public sealed class GridSettings
{
    public int LandscapeColumns { get; set; } = 8;

    public int LandscapeRows { get; set; } = 4;

    public int PortraitColumns { get; set; } = 5;

    public int PortraitRows { get; set; } = 6;

    public int Gap { get; set; } = 18;
}

public sealed class GestureSettings
{
    public bool Enabled { get; set; } = true;

    public bool AppDrawerSwipe { get; set; } = true;

    public bool ControlCenterSwipe { get; set; } = true;

    public bool DesktopGesture { get; set; } = true;

    public bool BackGesture { get; set; } = true;

    public bool MultitaskingGesture { get; set; } = true;
}

public sealed class HardwareSwitchSettings
{
    public bool SwitchOnKeyboard { get; set; } = true;

    public bool SwitchOnMouse { get; set; }

    public bool SwitchOnExternalMonitor { get; set; }

    public bool ShowModeChangeNotification { get; set; } = true;
}

public sealed class AccessibilitySettings
{
    public bool ReduceAnimations { get; set; }

    public bool HighContrast { get; set; }

    public double TextScale { get; set; } = 1.0;
}

public sealed class AdminPolicySettings
{
    public bool KioskMode { get; set; }

    public List<string> LockedSettings { get; set; } = [];

    public List<string> AppWhitelist { get; set; } = [];

    public List<string> AppBlacklist { get; set; } = [];
}
