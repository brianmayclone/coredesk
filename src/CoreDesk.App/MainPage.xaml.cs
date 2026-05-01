using Microsoft.UI.Xaml.Controls;
using CoreDesk.Abstractions.Models;
using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;

namespace CoreDesk_App;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _appRefresh = new();
    private readonly DispatcherTimer _dockAutoHide = new();
    private bool _isBottomGestureActive;
    private double _bottomGestureStartY;

    public ShellViewModel ViewModel { get; } = App.Services.CreateShellViewModel();

    public MainPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;

        _clock.Interval = TimeSpan.FromSeconds(15);
        _clock.Tick += (_, _) => ViewModel.Tick();
        _clock.Start();

        _appRefresh.Interval = TimeSpan.FromSeconds(30);
        _appRefresh.Tick += OnAppRefreshTick;
        _appRefresh.Start();

        _dockAutoHide.Interval = TimeSpan.FromSeconds(4);
        _dockAutoHide.Tick += (_, _) =>
        {
            _dockAutoHide.Stop();
            ViewModel.HideDockCommand.Execute(null);
            CollapseDesktopOverlayIfIdle();
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
        ApplyWallpaper();
        Bindings.Update();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateViewport(e.NewSize.Width, e.NewSize.Height);
        Bindings.Update();
    }

    private void ApplyWallpaper()
    {
        var wallpaper = ViewModel.WallpaperPath;
        if (string.IsNullOrWhiteSpace(wallpaper) || !File.Exists(wallpaper))
        {
            WallpaperImage.Source = null;
            return;
        }

        try
        {
            WallpaperImage.Source = new BitmapImage(new Uri(wallpaper));
        }
        catch
        {
            WallpaperImage.Source = null;
        }
    }

    private async void OnAppItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeTileViewModel { App: { } homeApp })
        {
            await ViewModel.LaunchAppCommand.ExecuteAsync(homeApp);
            return;
        }

        if (e.ClickedItem is HomeTileViewModel { Folder: { } folder })
        {
            ViewModel.OpenFolderCommand.Execute(folder);
            return;
        }

        if (e.ClickedItem is AppEntry app)
        {
            await ViewModel.LaunchAppCommand.ExecuteAsync(app);
        }
    }

    private async void OnDockAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DockItemViewModel item })
        {
            await ViewModel.OpenDockItemCommand.ExecuteAsync(item);
            return;
        }

        if (sender is Button { Tag: AppEntry app })
        {
            await ViewModel.LaunchAppCommand.ExecuteAsync(app);
        }
    }

    private void OnFolderTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FolderTileViewModel folder })
        {
            ViewModel.OpenFolderCommand.Execute(folder);
        }
    }

    public void OpenSettings()
    {
        ViewModel.OpenSettingsCommand.Execute(null);
    }

    public void OpenDrawer()
    {
        ViewModel.OpenDrawerCommand.Execute(null);
    }

    public void OpenControlCenter()
    {
        ViewModel.OpenControlCenterCommand.Execute(null);
    }

    public void OpenTaskSwitcher()
    {
        ViewModel.OpenTaskSwitcherCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsControlCenterOpen) or nameof(ViewModel.IsTaskSwitcherOpen) or nameof(ViewModel.IsDrawerOpen) or nameof(ViewModel.IsSettingsOpen))
        {
            if (ViewModel.IsControlCenterOpen || ViewModel.IsTaskSwitcherOpen || ViewModel.IsDrawerOpen || ViewModel.IsSettingsOpen)
            {
                App.DockWindow?.HideDock();
            }
            else
            {
                App.DockWindow?.ShowDock();
            }
        }

        if (e.PropertyName == nameof(ViewModel.CurrentMode))
        {
            if (ViewModel.IsDesktopMode)
            {
                ScheduleDockAutoHide();
            }
            else
            {
                _dockAutoHide.Stop();
            }
        }
    }

    private void OnBottomGesturePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsDesktopMode)
        {
            return;
        }

        App.DockWindow?.ShowDock();
        ViewModel.ShowDockCommand.Execute(null);
        ScheduleDockAutoHide();
    }

    private void OnBottomGesturePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsDesktopMode)
        {
            return;
        }

        _isBottomGestureActive = true;
        _bottomGestureStartY = e.GetCurrentPoint(Root).Position.Y;
        BottomGestureZone.CapturePointer(e.Pointer);
        App.DockWindow?.ShowDock();
        ViewModel.ShowDockCommand.Execute(null);
        _dockAutoHide.Stop();
    }

    private void OnBottomGesturePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isBottomGestureActive || !ViewModel.IsDesktopMode)
        {
            return;
        }

        var currentY = e.GetCurrentPoint(Root).Position.Y;
        if (_bottomGestureStartY - currentY > Root.ActualHeight * 0.16)
        {
            ViewModel.ShowDockCommand.Execute(null);
        }
    }

    private void OnBottomGesturePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        FinishBottomGesture(e);
    }

    private void OnBottomGesturePointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isBottomGestureActive = false;
        BottomGestureZone.ReleasePointerCapture(e.Pointer);
        ScheduleDockAutoHide();
    }

    private void FinishBottomGesture(PointerRoutedEventArgs e)
    {
        if (!_isBottomGestureActive)
        {
            return;
        }

        _isBottomGestureActive = false;
        BottomGestureZone.ReleasePointerCapture(e.Pointer);
        var currentY = e.GetCurrentPoint(Root).Position.Y;
        var crossedMidpoint = currentY < Root.ActualHeight * 0.52;
        if (crossedMidpoint)
        {
            App.DockWindow?.HideDock();
            ViewModel.OpenTaskSwitcherCommand.Execute(null);
            return;
        }

        ViewModel.ShowDockCommand.Execute(null);
        ScheduleDockAutoHide();
    }

    private void ScheduleDockAutoHide()
    {
        if (!ViewModel.IsDesktopMode)
        {
            return;
        }

        _dockAutoHide.Stop();
        _dockAutoHide.Start();
    }

    private void CollapseDesktopOverlayIfIdle()
    {
        if (!ViewModel.IsDesktopMode || ViewModel.IsControlCenterOpen || ViewModel.IsTaskSwitcherOpen || ViewModel.IsDrawerOpen || ViewModel.IsSettingsOpen)
        {
            return;
        }

        App.DockWindow?.ShowDock();
    }

    private async void OnAppRefreshTick(object? sender, object e)
    {
        await ViewModel.RefreshInstalledAppsAsync();
    }
}
