using System.Windows;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views
{
    /// <summary>
    /// 安装配置对话框
    /// </summary>
    public partial class InstallConfigDialog : Window
    {
        public InstallConfigDialog()
        {
            InitializeComponent();
        }

        public InstallConfigViewModel ViewModel => (InstallConfigViewModel)DataContext!;
    }
}
