using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace CoreDesk_Dock;

public sealed partial class DockView : UserControl
{
    public DockView()
    {
        InitializeComponent();
    }

    public ShellViewModel? ViewModel { get; private set; }

    public void SetViewModel(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        Root.DataContext = viewModel;
    }

    private void OnDockItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DockItemViewModel item } && ViewModel?.OpenDockItemCommand.CanExecute(item) == true)
        {
            ViewModel.OpenDockItemCommand.Execute(item);
        }
    }

    private void OnHomeIndicatorClick(object sender, RoutedEventArgs e)
    {
        App.Window.RaiseDock();
    }

    private void OnDockButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.72);
        control.RenderTransform = new ScaleTransform { ScaleX = 1.12, ScaleY = 1.12 };
    }

    private void OnDockButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control control)
        {
            control.RenderTransform = null;
        }
    }
}
