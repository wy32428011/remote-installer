using System.Windows;
using System.Windows.Input;
using RemoteInstaller.ViewModels;

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
            vm.SelectedYamlNode = e.NewValue as ConfigEditorViewModel.YamlTreeNode;
        }
    }
}