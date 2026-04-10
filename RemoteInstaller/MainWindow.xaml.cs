using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 使用 Locator 获取已注入 AppConfigurationService 的 ViewModel
            DataContext = Locator.Instance;
        }

        private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || sender is not ListBox listBox)
            {
                return;
            }

            viewModel.UpdateSelectedHostsFromView(listBox.SelectedItems.OfType<HostViewModel>());
        }
    }
}
