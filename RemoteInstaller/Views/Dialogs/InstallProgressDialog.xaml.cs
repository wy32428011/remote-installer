using System.Windows;
using System.Windows.Controls;

namespace RemoteInstaller.Views.Dialogs
{
    /// <summary>
    /// 安装进度监控窗口
    /// </summary>
    public partial class InstallProgressDialog : Window
    {
        private const double CompactLayoutThreshold = 800;
        private bool _isCompactLayout;

        public InstallProgressDialog()
        {
            InitializeComponent();
            Loaded += InstallProgressDialog_Loaded;
            SizeChanged += InstallProgressDialog_SizeChanged;
        }

        private void InstallProgressDialog_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void InstallProgressDialog_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        // 根据窗口宽度在左右摘要和上下摘要之间切换，避免阶段区域在窄窗口中被压成细长列。
        private void UpdateResponsiveLayout()
        {
            var shouldUseCompactLayout = ActualWidth < CompactLayoutThreshold;
            if (shouldUseCompactLayout == _isCompactLayout)
            {
                return;
            }

            _isCompactLayout = shouldUseCompactLayout;

            if (shouldUseCompactLayout)
            {
                SummaryPrimaryColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
                SummarySecondaryColumnDefinition.Width = new GridLength(0);
                SummaryPrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
                SummarySecondaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(OverallProgressPanel, 0);
                Grid.SetColumn(OverallProgressPanel, 0);
                Grid.SetColumnSpan(OverallProgressPanel, 2);
                OverallProgressPanel.Margin = new Thickness(0, 0, 0, 12);

                Grid.SetRow(StageProgressPanel, 1);
                Grid.SetColumn(StageProgressPanel, 0);
                Grid.SetColumnSpan(StageProgressPanel, 2);
                StageProgressPanel.Margin = new Thickness(0);

                return;
            }

            SummaryPrimaryColumnDefinition.Width = new GridLength(1.2, GridUnitType.Star);
            SummarySecondaryColumnDefinition.Width = new GridLength(0.8, GridUnitType.Star);
            SummaryPrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
            SummarySecondaryRowDefinition.Height = new GridLength(0);

            Grid.SetRow(OverallProgressPanel, 0);
            Grid.SetColumn(OverallProgressPanel, 0);
            Grid.SetColumnSpan(OverallProgressPanel, 1);
            OverallProgressPanel.Margin = new Thickness(0, 0, 12, 0);

            Grid.SetRow(StageProgressPanel, 0);
            Grid.SetColumn(StageProgressPanel, 1);
            Grid.SetColumnSpan(StageProgressPanel, 1);
            StageProgressPanel.Margin = new Thickness(0);
        }
    }
}
