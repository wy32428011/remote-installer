using System.Windows;
using System.Windows.Controls;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views;

/// <summary>
/// 添加/编辑主机对话框
/// </summary>
public partial class AddHostDialog : Window
{
    public AddHostViewModel ViewModel => (AddHostViewModel)DataContext;

    public AddHostDialog(AddHostViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 绑定密码框
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 绑定 PasswordBox 到 ViewModel 的 Password 属性
        var passwordBox = FindName("PasswordBox") as PasswordBox;
        if (passwordBox != null)
        {
            passwordBox.PasswordChanged += OnPasswordChanged;
            
            // 如果是编辑模式，设置初始密码值
            if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.Password))
            {
                passwordBox.Password = ViewModel.Password;
            }
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && ViewModel != null)
        {
            ViewModel.Password = pb.Password;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        // 对话框关闭时由调用方处理结果
    }
}
