using System.Windows;
using System.Windows.Controls;
using RemoteInstaller.Models;
using RemoteInstaller.ViewModels;
using RemoteInstaller.ViewModels.Shared.ConfigEditing;

namespace RemoteInstaller.Views.Dialogs;

public partial class SupportDeployDialog : Window
{
    public SupportDeployDialog(SupportDeployViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = () => Close();
        Loaded += async (s, e) => await viewModel.InitializeAsync();
    }

    private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            if (dirItem.IsDirectory && !dirItem.IsLoaded)
            {
                if (DataContext is SupportDeployViewModel vm)
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
        if (DataContext is SupportDeployViewModel vm && e.NewValue is DirectoryItem selected)
        {
            vm.SelectedDirectoryItem = selected;
        }
    }

    private void ConfigFileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SupportDeployViewModel vm && e.NewValue is DirectoryItem selected)
        {
            vm.SelectedConfigFileItem = selected;
        }
    }

    private void YamlConfigTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SupportDeployViewModel vm)
        {
            vm.SelectedYamlNode = e.NewValue as YamlTreeNode;
        }
    }

    private async void ConfigFileTreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            if (dirItem.IsDirectory && !dirItem.IsLoaded)
            {
                if (DataContext is SupportDeployViewModel vm)
                {
                    await vm.LoadChildDirectoriesCommand.ExecuteAsync(dirItem);
                }
            }
            e.Handled = true;
        }
    }

    private void LogTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SupportDeployViewModel vm && e.NewValue is DirectoryItem selected)
        {
            vm.SelectedLogDirectoryItem = selected;
        }
    }

    private async void LogTreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            if (dirItem.IsDirectory && !dirItem.IsLoaded)
            {
                if (DataContext is SupportDeployViewModel vm)
                {
                    await vm.LoadChildDirectoriesCommand.ExecuteAsync(dirItem);
                }
            }
            e.Handled = true;
        }
    }
}
