using CoreDesk.Abstractions.Services;
using CoreDesk.Application.ViewModels;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreDesk_Dock;

public sealed class NativeDockForm : Form
{
    private readonly ShellViewModel _viewModel;
    private readonly IDiagnosticsService _diagnostics;
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly Dictionary<string, Image> _iconCache = [];
    private readonly List<(Rectangle Bounds, DockItemViewModel Item)> _hitTargets = [];

    public NativeDockForm(ShellViewModel viewModel, IDiagnosticsService diagnostics)
    {
        _viewModel = viewModel;
        _diagnostics = diagnostics;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 24);
        ShowIcon = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        InitializeDock();
        ConfigureWindow();

        _refreshTimer.Interval = 1200;
        _refreshTimer.Tick += (_, _) => RefreshDock();
        _refreshTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    private void InitializeDock()
    {
        _viewModel.InitializeAsync().GetAwaiter().GetResult();
        RefreshDock();
    }

    private void ConfigureWindow()
    {
        PositionDock();
        ConfigureDwmBackdrop(Handle);
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
        Invalidate();
    }

    private void RefreshDock()
    {
        try
        {
            _viewModel.Tick();
            PositionDock();
            NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
            Invalidate();
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, "Native dock refresh failed.");
        }
    }

    private void PositionDock()
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        var itemCount = Math.Clamp(_viewModel.PinnedDockItems.Count, 1, 10)
            + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 5);
        var desiredWidth = Math.Clamp(28 + (itemCount * 48) + (Math.Max(0, itemCount - 1) * 12), 340, Math.Min(820, screen.Width - 240));
        var desiredHeight = 92;
        Bounds = new Rectangle(
            screen.Left + ((screen.Width - desiredWidth) / 2),
            screen.Bottom - desiredHeight - 24,
            desiredWidth,
            desiredHeight);
        Region?.Dispose();
        Region = new Region(RoundedRect(new Rectangle(0, 0, Width, Height), 22));
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        foreach (var target in _hitTargets)
        {
            if (!target.Bounds.Contains(e.Location))
            {
                continue;
            }

            if (_viewModel.OpenDockItemCommand.CanExecute(target.Item))
            {
                _viewModel.OpenDockItemCommand.Execute(target.Item);
            }

            return;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        _hitTargets.Clear();

        var dockSurface = new Rectangle(7, 9, Width - 14, 70);
        using var path = RoundedRect(dockSurface, 18);
        DrawSoftShadow(graphics, dockSurface);

        using var glass = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(188, 88, 26, 45),
            Color.FromArgb(168, 55, 15, 32),
            90f);
        using var sheen = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(78, 255, 255, 255),
            Color.FromArgb(12, 255, 255, 255),
            90f);
        using var stroke = new Pen(Color.FromArgb(82, 255, 255, 255), 1f);

        graphics.FillPath(glass, path);
        graphics.FillPath(sheen, path);
        graphics.DrawPath(stroke, path);
        using (var highlight = new Pen(Color.FromArgb(96, 255, 255, 255), 1f))
        {
            graphics.DrawLine(highlight, dockSurface.Left + 18, dockSurface.Top + 1, dockSurface.Right - 18, dockSurface.Top + 1);
        }

        var itemCount = Math.Clamp(_viewModel.PinnedDockItems.Count, 1, 10)
            + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 5);
        var contentWidth = (itemCount * 48) + (Math.Max(0, itemCount - 1) * 12);
        var x = (Width - contentWidth) / 2;
        var y = dockSurface.Top + 10;
        DrawItems(graphics, _viewModel.PinnedDockItems, ref x, y, pinned: true);

        if (_viewModel.RunningDockItems.Count > 0)
        {
            x += 12;
        }

        DrawItems(graphics, _viewModel.RunningDockItems, ref x, y, pinned: false);
    }

    private void DrawItems(Graphics graphics, IEnumerable<DockItemViewModel> items, ref int x, int y, bool pinned)
    {
        foreach (var item in items.Take(pinned ? 10 : 5))
        {
            var bounds = new Rectangle(x, y, 48, 54);
            _hitTargets.Add((bounds, item));
            DrawIcon(graphics, item, new Rectangle(x + 2, y, 44, 44));
            if (item.IsRunning || !pinned)
            {
                using var indicator = new SolidBrush(Color.FromArgb(255, 10, 132, 255));
                graphics.FillRoundedRectangle(indicator, new Rectangle(x + 20, y + 49, 8, 3), 2);
            }

            x += 60;
        }
    }

    private void DrawIcon(Graphics graphics, DockItemViewModel item, Rectangle bounds)
    {
        var image = LoadIcon(item.IconPath);
        if (image is not null)
        {
            graphics.DrawImage(image, bounds);
            return;
        }

        using var fallbackBrush = new SolidBrush(Color.FromArgb(235, 246, 248, 250));
        using var fallbackPen = new Pen(Color.FromArgb(120, 20, 30, 42), 1f);
        graphics.FillEllipse(fallbackBrush, bounds);
        graphics.DrawEllipse(fallbackPen, bounds);
    }

    private Image? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var image = Image.FromFile(path);
            _iconCache[path] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSoftShadow(Graphics graphics, Rectangle surface)
    {
        for (var index = 0; index < 5; index++)
        {
            var alpha = 34 - (index * 6);
            var shadowRect = Rectangle.Inflate(surface, index * 2, index);
            shadowRect.Offset(0, 5 + index);
            using var shadow = new SolidBrush(Color.FromArgb(Math.Max(6, alpha), 0, 0, 0));
            using var shadowPath = RoundedRect(shadowRect, 18 + index);
            graphics.FillPath(shadow, shadowPath);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ConfigureDwmBackdrop(IntPtr handle)
    {
        var useHostBackdrop = 1;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_USE_HOSTBACKDROPBRUSH, ref useHostBackdrop, sizeof(int));

        var backdropType = DWMSBT_TRANSIENTWINDOW;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

        var cornerPreference = DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        var borderColor = DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        EnableAcrylicAccent(handle);
    }

    private static void EnableAcrylicAccent(IntPtr handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0x20,
            GradientColor = unchecked((int)0x66FFFFFF)
        };

        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = accentSize
            };
            _ = NativeMethods.SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
