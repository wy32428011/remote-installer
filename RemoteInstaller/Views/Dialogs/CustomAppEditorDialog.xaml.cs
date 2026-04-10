using System.Windows;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views.Dialogs;

public partial class CustomAppEditorDialog : Window
{
    public CustomAppEditorDialog(CustomAppEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
