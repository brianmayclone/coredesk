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
    private Windows.Foundation.Point? _rootPointerStart;

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

    private void OnAppDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
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
    }

    private async void OnHomeGridDrop(object sender, DragEventArgs e)
    {
        var text = await e.DataView.GetTextAsync();
        const string prefix = "coredesk-app:";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var point = e.GetPosition(HomeGrid);
        var targetIndex = GetHomeTileTargetIndex(point);
        await ViewModel.MoveHomeAppAsync(text[prefix.Length..], targetIndex);
        Bindings.Update();
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
        if (e.PropertyName is nameof(ViewModel.IsControlCenterOpen) or nameof(ViewModel.IsTaskSwitcherOpen) or nameof(ViewModel.IsDrawerOpen) or nameof(ViewModel.IsSettingsOpen))
        {
            App.DockWindow?.ShowDock(homeMode: !ViewModel.IsDesktopMode);
        }

    }

    private async void OnAppRefreshTick(object? sender, object e)
    {
        await ViewModel.RefreshInstalledAppsAsync();
    }
}
