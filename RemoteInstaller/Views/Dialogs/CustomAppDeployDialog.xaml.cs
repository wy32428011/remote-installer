using System.Windows;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views.Dialogs;

public partial class CustomAppDeployDialog : Window
{
    public CustomAppDeployDialog(CustomAppDeployViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;

        Loaded += (_, _) => LocalSourceTextBox.Focus();
    }
}
