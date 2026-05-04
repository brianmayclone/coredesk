using CoreDesk.Abstractions.Services;
using CoreDesk.Application.ViewModels;
using System.Diagnostics;
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
    private readonly System.Windows.Forms.Timer _foregroundTimer = new();
    private readonly Dictionary<string, Image> _iconCache = [];
    private readonly List<(Rectangle Bounds, DockItemViewModel Item)> _hitTargets = [];
    private bool _raised = true;

    public NativeDockForm(ShellViewModel viewModel, IDiagnosticsService diagnostics)
    {
        _viewModel = viewModel;
        _diagnostics = diagnostics;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0.96;

        InitializeDock();
        ConfigureWindow();

        _refreshTimer.Interval = 2500;
        _refreshTimer.Tick += (_, _) => RefreshDock();
        _refreshTimer.Start();

        _foregroundTimer.Interval = 500;
        _foregroundTimer.Tick += (_, _) => KeepOverlayState();
        _foregroundTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
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
        EnableAcrylicBlur(Handle);
        ApplyRoundedRegion();
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
    }

    private void RefreshDock()
    {
        try
        {
            _viewModel.Tick();
            PositionDock();
            Invalidate();
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, "Native dock refresh failed.");
        }
    }

    private void KeepOverlayState()
    {
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
        if (IsForegroundLargeNonDockWindow())
        {
            if (_raised)
            {
                _raised = false;
                Top = Screen.PrimaryScreen!.Bounds.Bottom - 12;
            }

            return;
        }

        if (!_raised)
        {
            _raised = true;
            PositionDock();
        }
    }

    private void PositionDock()
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        var itemCount = Math.Max(1, _viewModel.PinnedDockItems.Count)
            + _viewModel.RunningDockItems.Count
            + 1;
        var desiredWidth = Math.Clamp(42 + (itemCount * 64) + Math.Max(0, itemCount - 1) * 10, 560, Math.Min(1320, screen.Width - 220));
        var desiredHeight = 108;
        Bounds = new Rectangle(
            screen.Left + ((screen.Width - desiredWidth) / 2),
            screen.Bottom - desiredHeight - 30,
            desiredWidth,
            desiredHeight);
        ApplyRoundedRegion();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _raised = true;
        PositionDock();
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
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

        var surface = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(surface, 42);
        using var shadow = new SolidBrush(Color.FromArgb(56, 0, 0, 0));
        using var glass = new LinearGradientBrush(surface, Color.FromArgb(206, 255, 255, 255), Color.FromArgb(88, 122, 190, 230), 16f);
        using var stroke = new Pen(Color.FromArgb(180, 255, 255, 255), 1.3f);

        graphics.FillPath(shadow, RoundedRect(new Rectangle(0, 5, Width - 1, Height - 1), 42));
        graphics.FillPath(glass, path);
        graphics.DrawPath(stroke, path);
        using (var highlight = new Pen(Color.FromArgb(210, 255, 255, 255), 1f))
        {
            graphics.DrawLine(highlight, 36, 12, Width - 36, 12);
        }

        var x = 22;
        var y = 18;
        DrawItems(graphics, _viewModel.PinnedDockItems, ref x, y, pinned: true);
        using (var dividerPen = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
        {
            graphics.DrawLine(dividerPen, x + 5, 24, x + 5, Height - 30);
        }

        x += 20;
        DrawItems(graphics, _viewModel.RunningDockItems, ref x, y, pinned: false);

        using var homeBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
        graphics.FillRoundedRectangle(homeBrush, new Rectangle((Width - 66) / 2, Height - 14, 66, 4), 2);
    }

    private void DrawItems(Graphics graphics, IEnumerable<DockItemViewModel> items, ref int x, int y, bool pinned)
    {
        foreach (var item in items.Take(pinned ? 10 : 7))
        {
            var bounds = new Rectangle(x, y, 60, 70);
            _hitTargets.Add((bounds, item));
            DrawIcon(graphics, item, new Rectangle(x + 3, y, 54, 54));
            if (item.IsRunning || !pinned)
            {
                using var indicator = new SolidBrush(Color.FromArgb(255, 10, 132, 255));
                graphics.FillRoundedRectangle(indicator, new Rectangle(x + 21, y + 62, 18, 3), 2);
            }

            x += 74;
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

    private void ApplyRoundedRegion()
    {
        Region?.Dispose();
        Region = new Region(RoundedRect(new Rectangle(0, 0, Width, Height), 42));
    }

    private bool IsForegroundLargeNonDockWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        var screen = Screen.PrimaryScreen!.Bounds;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return width >= screen.Width * 0.72 && height >= screen.Height * 0.72;
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

    private static void EnableAcrylicBlur(IntPtr handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0x20,
            GradientColor = unchecked((int)0x88FFFFFF)
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
            _foregroundTimer.Dispose();
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
    private const uint SWP_SHOWWINDOW = 0x0040;
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
