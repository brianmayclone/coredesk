using CoreDesk.Application.ViewModels;
using CoreDesk.LiquidGlass;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Numerics;

namespace CoreDesk_App;

public sealed partial class DockView : UserControl
{
    public DockView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyNativeGlassOptions();
    }

    public void SetViewModel(ShellViewModel viewModel)
    {
        Root.DataContext = viewModel;
        ApplyNativeGlassOptions();
    }

    private void ApplyNativeGlassOptions()
    {
        LiquidDockSurface.Options = LiquidGlassOptions.Default with
        {
            SurfaceWidth = Math.Max(1, LiquidDockSurface.ActualWidth),
            SurfaceHeight = 92,
            BlurAmount = 30f,
            Saturation = 1.42f,
            Contrast = 1.14f,
            Brightness = 0.04f,
            TintOpacity = 0.20f,
            TintColor = new Vector4(0.96f, 0.98f, 1f, 1f),
            EdgeLightOpacity = 0.49f,
            CornerRadius = 38f,
            RefractionStrength = 0.014f,
            InnerShadowOpacity = 0.30f,
            OuterShadowOpacity = 0.24f,
            ShadowBlurRadius = 34f,
            ShadowOffsetY = 12f,
            SpecularOpacity = 0.24f
        };
    }

    private async void OnDockItemClick(object sender, RoutedEventArgs e)
    {
        if (Root.DataContext is ShellViewModel viewModel && sender is Button { Tag: DockItemViewModel item })
        {
            await viewModel.OpenDockItemCommand.ExecuteAsync(item);
        }
    }

    private void OnDrawerClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openDrawer: true);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openSettings: true);
    }

    private void OnControlCenterClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openControlCenter: true);
    }

    private void OnTaskSwitcherClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openTaskSwitcher: true);
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell();
    }

    private void OnDockButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.CenterPoint = new System.Numerics.Vector3((float)(button.ActualWidth / 2), (float)(button.ActualHeight / 2), 0);
            button.Scale = new System.Numerics.Vector3(1.14f, 1.14f, 1);
            button.Translation = new System.Numerics.Vector3(0, -7, 0);
        }
    }

    private void OnDockButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Scale = System.Numerics.Vector3.One;
            button.Translation = System.Numerics.Vector3.Zero;
        }
    }
}
