using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CoreDesk_App;

public sealed partial class DockView : UserControl
{
    public DockView()
    {
        InitializeComponent();
    }

    public void SetViewModel(ShellViewModel viewModel)
    {
        Root.DataContext = viewModel;
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
