using CoreDesk.Abstractions.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace CoreDesk_App;

public enum AppIconContextMenuAction
{
    Open,
    Info,
    RemoveFromHome,
    Delete
}

public sealed class AppIconContextMenuActionRequestedEventArgs(
    AppEntry app,
    AppIconContextMenuAction action) : EventArgs
{
    public AppEntry App { get; } = app;

    public AppIconContextMenuAction Action { get; } = action;
}

public sealed partial class AppIconContextMenu : UserControl
{
    private const double BaseMenuWidth = 302;
    private const double BaseMenuHeight = 330;

    private AppEntry? _app;

    public AppIconContextMenu()
    {
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public event EventHandler<AppIconContextMenuActionRequestedEventArgs>? ActionRequested;

    public event EventHandler? Closed;

    public string DisplayName { get; private set; } = string.Empty;

    public string? IconPath { get; private set; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void ShowFor(FrameworkElement anchor, AppEntry app, double uiScale)
    {
        _app = app;
        DisplayName = app.DisplayName;
        IconPath = app.IconPath;
        Bindings.Update();

        Visibility = Visibility.Visible;
        Opacity = 0;

        var scale = Math.Clamp(uiScale, 0.94, 1.28);
        MenuScaleTransform.ScaleX = scale;
        MenuScaleTransform.ScaleY = scale;

        var anchorTopLeft = anchor.TransformToVisual(this).TransformPoint(new Point(0, 0));
        var anchorWidth = Math.Max(anchor.ActualWidth, 108);
        var anchorHeight = Math.Max(anchor.ActualHeight, 108);
        var menuWidth = BaseMenuWidth * scale;
        var menuHeight = BaseMenuHeight * scale;
        var parent = Parent as FrameworkElement;
        var availableWidth = Math.Max(1, parent?.ActualWidth ?? ActualWidth);
        var availableHeight = Math.Max(1, parent?.ActualHeight ?? ActualHeight);
        var gap = Math.Round(12 * scale);

        var left = anchorTopLeft.X + anchorWidth + gap;
        if (left + menuWidth > availableWidth - gap)
        {
            left = anchorTopLeft.X - menuWidth - gap;
        }

        if (left < gap)
        {
            left = Math.Clamp(anchorTopLeft.X + (anchorWidth / 2) - (menuWidth / 2), gap, Math.Max(gap, availableWidth - menuWidth - gap));
        }

        var top = Math.Clamp(anchorTopLeft.Y - (18 * scale), gap, Math.Max(gap, availableHeight - menuHeight - gap));
        Canvas.SetLeft(MenuRoot, left);
        Canvas.SetTop(MenuRoot, top);

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(130))
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    public void Hide()
    {
        var wasOpen = IsOpen;
        Visibility = Visibility.Collapsed;
        _app = null;
        if (wasOpen)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Hide();
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        Request(AppIconContextMenuAction.Open);
    }

    private void OnInfoClicked(object sender, RoutedEventArgs e)
    {
        Request(AppIconContextMenuAction.Info);
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        Request(AppIconContextMenuAction.RemoveFromHome);
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        Request(AppIconContextMenuAction.Delete);
    }

    private void Request(AppIconContextMenuAction action)
    {
        if (_app is not { } app)
        {
            Hide();
            return;
        }

        Hide();
        ActionRequested?.Invoke(this, new AppIconContextMenuActionRequestedEventArgs(app, action));
    }
}
