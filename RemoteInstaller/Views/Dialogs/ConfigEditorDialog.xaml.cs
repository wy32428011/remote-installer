using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteInstaller.ViewModels;
using RemoteInstaller.ViewModels.Shared.ConfigEditing;

namespace RemoteInstaller.Views.Dialogs;

/// <summary>
/// ConfigEditorDialog.xaml 的交互逻辑
/// </summary>
public partial class ConfigEditorDialog : Window
{
    public ConfigEditorDialog(ConfigEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.CloseAction = Close;

        // 快捷键支持
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (viewModel.SaveCommand.CanExecute(null))
                {
                    viewModel.SaveCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (viewModel.CancelCommand.CanExecute(null))
                {
                    viewModel.CancelCommand.Execute(null);
                    e.Handled = true;
                }
            }
        };
    }

    private void YamlConfigTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ConfigEditorViewModel vm)
        {
            vm.SelectedYamlNode = e.NewValue as YamlTreeNode;
        }
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindScrollableParent(source, e.Delta) ?? FindScrollableChild(sender as DependencyObject, e.Delta);
        if (scrollViewer is null)
        {
            return;
        }

        var lines = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
        for (var i = 0; i < lines; i++)
        {
            if (e.Delta > 0)
            {
                scrollViewer.LineUp();
            }
            else
            {
                scrollViewer.LineDown();
            }
        }

        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableParent(DependencyObject? current, int delta)
    {
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer && CanScroll(scrollViewer, delta))
            {
                return scrollViewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static ScrollViewer? FindScrollableChild(DependencyObject? current, int delta)
    {
        if (current is null || !CanInspectChildren(current))
        {
            return null;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is ScrollViewer scrollViewer && CanScroll(scrollViewer, delta))
            {
                return scrollViewer;
            }

            var descendant = FindScrollableChild(child, delta);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int delta)
    {
        return delta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return CanInspectChildren(current)
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
    }

    private static bool CanInspectChildren(DependencyObject current)
    {
        return current is Visual || current is System.Windows.Media.Media3D.Visual3D;
    }
}
