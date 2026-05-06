using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double CompactMainContentThreshold = 1420;
        private const double ExpandedTaskPanelColumnWidth = 248;
        private const double CollapsedTaskPanelColumnWidth = 72;
        private bool _isCompactMainContentLayout;

        public MainWindow()
        {
            InitializeComponent();

            // 使用 Locator 获取已注入 AppConfigurationService 的 ViewModel
            DataContext = Locator.Instance;
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            DataContextChanged += MainWindow_DataContextChanged;
            Closed += MainWindow_Closed;

            AttachViewModelEvents(DataContext as MainViewModel);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            UpdateMainContentLayout();

            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.InitializeAfterWindowLoadedAsync();
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMainContentLayout();
        }

        private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || sender is not ListBox listBox)
            {
                return;
            }

            viewModel.UpdateSelectedHostsFromView(listBox.SelectedItems.OfType<HostViewModel>());
        }

        private void TaskListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || sender is not ListBox listBox || listBox.SelectedItem is not TaskViewModel task)
            {
                return;
            }

            viewModel.ShowTaskProgressDialog(task);
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModelEvents(e.OldValue as MainViewModel);
            AttachViewModelEvents(e.NewValue as MainViewModel);
            UpdateMainContentLayout();
        }

        private void MainWindow_Closed(object? sender, System.EventArgs e)
        {
            DetachViewModelEvents(DataContext as MainViewModel);
            DataContextChanged -= MainWindow_DataContextChanged;
            Closed -= MainWindow_Closed;
        }

        private void AttachViewModelEvents(MainViewModel? viewModel)
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void DetachViewModelEvents(MainViewModel? viewModel)
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsTaskPanelCollapsed))
            {
                UpdateMainContentLayout();
            }
        }

        // 根据主窗口宽度在“应用区 + 任务区”左右分栏和上下堆叠之间切换，避免任务面板长期挤压主内容区。
        private void UpdateMainContentLayout()
        {
            var shouldUseCompactLayout = ActualWidth < CompactMainContentThreshold;
            _isCompactMainContentLayout = shouldUseCompactLayout;

            var isTaskPanelCollapsed = DataContext is MainViewModel viewModel && viewModel.IsTaskPanelCollapsed;

            if (shouldUseCompactLayout)
            {
                MainContentPrimaryColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
                MainContentSecondaryColumnDefinition.Width = new GridLength(0);
                MainContentPrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
                MainContentSecondaryRowDefinition.Height = isTaskPanelCollapsed
                    ? GridLength.Auto
                    : new GridLength(0.9, GridUnitType.Star);

                Grid.SetRow(ApplicationTabsPanel, 0);
                Grid.SetColumn(ApplicationTabsPanel, 0);
                Grid.SetColumnSpan(ApplicationTabsPanel, 2);

                Grid.SetRow(TaskPanel, 1);
                Grid.SetColumn(TaskPanel, 0);
                Grid.SetColumnSpan(TaskPanel, 2);
                TaskPanel.Margin = new Thickness(0);

                return;
            }

            MainContentPrimaryColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            MainContentSecondaryColumnDefinition.Width = new GridLength(
                isTaskPanelCollapsed ? CollapsedTaskPanelColumnWidth : ExpandedTaskPanelColumnWidth,
                GridUnitType.Pixel);
            MainContentPrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
            MainContentSecondaryRowDefinition.Height = new GridLength(0);

            Grid.SetRow(ApplicationTabsPanel, 0);
            Grid.SetColumn(ApplicationTabsPanel, 0);
            Grid.SetColumnSpan(ApplicationTabsPanel, 1);

            Grid.SetRow(TaskPanel, 0);
            Grid.SetColumn(TaskPanel, 1);
            Grid.SetColumnSpan(TaskPanel, 1);
            TaskPanel.Margin = new Thickness(10, 0, 0, 0);
        }
    }
}
