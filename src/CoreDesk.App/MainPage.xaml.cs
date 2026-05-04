using Microsoft.UI.Xaml.Controls;
using CoreDesk.Abstractions.Models;
using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;

namespace CoreDesk_App;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _appRefresh = new();
    private readonly DispatcherTimer _dragPageSwitch = new();
    private Windows.Foundation.Point? _rootPointerStart;
    private int _pendingDragPageDirection;
    private bool _isDraggingHomeItem;
    private bool _createdPageDuringCurrentDrag;

    public ShellViewModel ViewModel { get; } = App.Services.CreateShellViewModel();

    public MainPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnRootPointerReleased), true);
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnRootPointerWheelChanged), true);

        _clock.Interval = TimeSpan.FromSeconds(15);
        _clock.Tick += (_, _) => ViewModel.Tick();
        _clock.Start();

        _appRefresh.Interval = TimeSpan.FromSeconds(30);
        _appRefresh.Tick += OnAppRefreshTick;
        _appRefresh.Start();

        _dragPageSwitch.Interval = TimeSpan.FromMilliseconds(650);
        _dragPageSwitch.Tick += OnDragPageSwitchTick;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
        ApplyWallpaper();
        Bindings.Update();
        App.NotifyHomeExperienceReady(homeMode: true);
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

    private void OnAppDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.OfType<HomeTileViewModel>().FirstOrDefault(tile => tile.Folder is not null) is { Folder: { } folderTile })
        {
            e.Data.RequestedOperation = DataPackageOperation.Move;
            e.Data.Properties.Title = folderTile.Name;
            e.Data.SetText($"coredesk-folder:{folderTile.Folder.Id}");
            _isDraggingHomeItem = true;
            return;
        }

        var app = e.Items.OfType<AppEntry>().FirstOrDefault()
            ?? e.Items.OfType<HomeTileViewModel>().FirstOrDefault(tile => tile.App is not null)?.App;
        if (app is null)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.Properties.Title = app.DisplayName;
        e.Data.SetText($"coredesk-app:{app.Id}");
        _isDraggingHomeItem = true;
    }

    private void OnHomeTileDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not HomeTileViewModel tile)
        {
            args.Cancel = true;
            return;
        }

        if (tile.App is { } app)
        {
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.Properties.Title = app.DisplayName;
            args.Data.SetText($"coredesk-app:{app.Id}");
            TrySetDragBitmap(args, app.IconPath);
            _isDraggingHomeItem = true;
            return;
        }

        if (tile.Folder is { } folder)
        {
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.Properties.Title = folder.Name;
            args.Data.SetText($"coredesk-folder:{folder.Folder.Id}");
            _isDraggingHomeItem = true;
            return;
        }

        args.Cancel = true;
    }

    private void OnDrawerAppDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppEntry app)
        {
            args.Cancel = true;
            return;
        }

        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.Properties.Title = app.DisplayName;
        args.Data.SetText($"coredesk-app:{app.Id}");
        TrySetDragBitmap(args, app.IconPath);
        _isDraggingHomeItem = true;
    }

    private void OnWidgetDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not HomeWidgetViewModel widget)
        {
            args.Cancel = true;
            return;
        }

        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.Properties.Title = widget.Title;
        args.Data.SetText($"coredesk-widget:{widget.Widget.Id}");
    }

    private void OnHomeGridDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        UpdateDragPageSwitch(e.GetPosition(HomeGrid));
    }

    private async void OnHomeGridDrop(object sender, DragEventArgs e)
    {
        var text = await e.DataView.GetTextAsync();
        StopDragPageSwitch(resetDragState: true);

        var point = e.GetPosition(HomeGrid);
        var targetIndex = GetHomeTileTargetIndex(point);

        const string appPrefix = "coredesk-app:";
        if (text.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await ViewModel.MoveHomeAppAsync(text[appPrefix.Length..], targetIndex);
            Bindings.Update();
            return;
        }

        const string folderPrefix = "coredesk-folder:";
        if (text.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await ViewModel.MoveHomeFolderAsync(text[folderPrefix.Length..], targetIndex);
            Bindings.Update();
        }
    }

    private void OnWidgetDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
    }

    private async void OnWidgetDrop(object sender, DragEventArgs e)
    {
        var text = await e.DataView.GetTextAsync();
        const string prefix = "coredesk-widget:";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var point = e.GetPosition(WidgetsStrip);
        var targetIndex = Math.Clamp((int)Math.Round(point.X / 398d), 0, ViewModel.Widgets.Count);
        await ViewModel.MoveWidgetAsync(text[prefix.Length..], targetIndex);
        Bindings.Update();
    }

    private void UpdateDragPageSwitch(Windows.Foundation.Point point)
    {
        if (!_isDraggingHomeItem || HomeGrid.ActualWidth <= 0)
        {
            StopDragPageSwitch();
            return;
        }

        var edgeWidth = Math.Max(96, HomeGrid.ActualWidth * 0.1);
        var direction = point.X <= edgeWidth
            ? -1
            : point.X >= HomeGrid.ActualWidth - edgeWidth
                ? 1
                : 0;

        if (direction == 0)
        {
            StopDragPageSwitch();
            return;
        }

        _pendingDragPageDirection = direction;
        if (!_dragPageSwitch.IsEnabled)
        {
            _dragPageSwitch.Start();
        }
    }

    private void OnDragPageSwitchTick(object? sender, object e)
    {
        if (!_isDraggingHomeItem || _pendingDragPageDirection == 0)
        {
            StopDragPageSwitch();
            return;
        }

        if (ViewModel.MoveToAdjacentPageForDrag(_pendingDragPageDirection, allowCreatePage: !_createdPageDuringCurrentDrag))
        {
            _createdPageDuringCurrentDrag = true;
        }

        Bindings.Update();
    }

    private void StopDragPageSwitch(bool resetDragState = false)
    {
        _pendingDragPageDirection = 0;
        if (resetDragState)
        {
            _isDraggingHomeItem = false;
            _createdPageDuringCurrentDrag = false;
        }

        _dragPageSwitch.Stop();
    }

    private static void TrySetDragBitmap(DragStartingEventArgs args, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return;
        }

        try
        {
            args.DragUI.SetContentFromBitmapImage(new BitmapImage(new Uri(iconPath)));
        }
        catch
        {
            // Drag visuals are best-effort; the data payload still drives the move.
        }
    }

    private static int GetHomeTileTargetIndex(Windows.Foundation.Point point)
    {
        const double tileWidth = 150;
        const double tileHeight = 174;
        var column = Math.Max(0, (int)Math.Floor(point.X / tileWidth));
        var row = Math.Clamp((int)Math.Floor(point.Y / tileHeight), 0, 7);
        return (column * 8) + row;
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _rootPointerStart = e.GetCurrentPoint(Root).Position;
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rootPointerStart is null)
        {
            return;
        }

        var end = e.GetCurrentPoint(Root).Position;
        var deltaX = end.X - _rootPointerStart.Value.X;
        var deltaY = end.Y - _rootPointerStart.Value.Y;
        var start = _rootPointerStart.Value;
        _rootPointerStart = null;

        if (Math.Abs(deltaX) < 90 || Math.Abs(deltaX) < Math.Abs(deltaY) * 1.3)
        {
            return;
        }

        if (deltaX < 0)
        {
            ViewModel.NextPageCommand.Execute(null);
        }
        else
        {
            ViewModel.PreviousPageCommand.Execute(null);
        }

        Bindings.Update();
    }
    private void OnControlCenterBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        ViewModel.CloseControlCenterCommand.Execute(null);
        Bindings.Update();
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ViewModel.SetVolumePercent((int)Math.Round(e.NewValue));
        Bindings.Update();
    }

    private void OnBrightnessSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ViewModel.SetBrightnessPercent((int)Math.Round(e.NewValue));
        Bindings.Update();
    }

    private void OnRootPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        if (delta < 0)
        {
            ViewModel.NextPageCommand.Execute(null);
        }
        else if (delta > 0)
        {
            ViewModel.PreviousPageCommand.Execute(null);
        }

        Bindings.Update();
    }

    private void OnTilePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.Opacity = 0.76;
        element.RenderTransform = new ScaleTransform
        {
            ScaleX = 0.94,
            ScaleY = 0.94
        };
    }

    private void OnTilePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.Opacity = 1;
        element.RenderTransform = new ScaleTransform
        {
            ScaleX = 1,
            ScaleY = 1
        };
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

    public void ShowHome()
    {
        ViewModel.CloseTaskSwitcherCommand.Execute(null);
        ViewModel.CloseControlCenterCommand.Execute(null);
        ViewModel.CloseDrawerCommand.Execute(null);
        ViewModel.CloseSettingsCommand.Execute(null);
        App.Services.ShellMode.EnterTouchMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsControlCenterOpen) or nameof(ViewModel.IsTaskSwitcherOpen) or nameof(ViewModel.IsDrawerOpen) or nameof(ViewModel.IsSettingsOpen) or nameof(ViewModel.IsFolderOpen))
        {
            var isHomescreenOnly = !ViewModel.IsControlCenterOpen
                && !ViewModel.IsTaskSwitcherOpen
                && !ViewModel.IsDrawerOpen
                && !ViewModel.IsSettingsOpen
                && !ViewModel.IsFolderOpen;
            App.ShowStatusAndReserveWorkArea(homeMode: isHomescreenOnly);
            App.ShowDockWhenReady(homeMode: isHomescreenOnly);
        }

    }

    private async void OnAppRefreshTick(object? sender, object e)
    {
        await ViewModel.RefreshInstalledAppsAsync();
    }
}
