using System.Windows;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views
{
    /// <summary>
    /// SettingsDialog.xaml 的交互逻辑
    /// 系统设置对话框窗口
    /// </summary>
    public partial class SettingsDialog : Window
    {
        /// <summary>
        /// 获取 ViewModel 实例
        /// </summary>
        public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="viewModel">设置 ViewModel</param>
        public SettingsDialog(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 订阅主题变更事件
            if (viewModel is SettingsViewModel sv)
            {
                sv.ThemeChanged += OnThemeChanged;
            }
        }

        /// <summary>
        /// 主题变更事件处理
        /// </summary>
        private void OnThemeChanged(Models.ThemeType newTheme)
        {
            // 主题切换逻辑由主窗口处理
            // 这里只触发事件通知
        }

        /// <summary>
        /// 窗口关闭时的清理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 取消事件订阅
            if (DataContext is SettingsViewModel sv)
            {
                sv.ThemeChanged -= OnThemeChanged;
            }
        }
    }
}
