using System.Windows;
using System.Windows.Controls;
using RemoteInstaller.Models;
using RemoteInstaller.ViewModels;
using RemoteInstaller.ViewModels.Shared.ConfigEditing;

namespace RemoteInstaller.Views.Dialogs;

public partial class GenericAppDeployDialog : Window
{
    public GenericAppDeployDialog(GenericAppDeployViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = () => Close();
    }

    private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            if (dirItem.IsDirectory && !dirItem.IsLoaded)
            {
                if (DataContext is GenericAppDeployViewModel vm)
                {
                    await vm.LoadChildDirectoriesCommand.ExecuteAsync(dirItem);
                }
            }
            e.Handled = true;
        }
    }

    private async void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            e.Handled = true;
        }
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GenericAppDeployViewModel vm && e.NewValue is DirectoryItem selected)
        {
            vm.SelectedDirectoryItem = selected;
        }
    }

    private void YamlConfigTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GenericAppDeployViewModel vm)
        {
            vm.SelectedYamlNode = e.NewValue as YamlTreeNode;
        }
    }
}
