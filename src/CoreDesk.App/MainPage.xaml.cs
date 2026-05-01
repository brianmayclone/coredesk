using Microsoft.UI.Xaml.Controls;
using CoreDesk.Abstractions.Models;
using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CoreDesk_App;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _appRefresh = new();

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

    private async void OnAppRefreshTick(object? sender, object e)
    {
        await ViewModel.RefreshInstalledAppsAsync();
    }
}
