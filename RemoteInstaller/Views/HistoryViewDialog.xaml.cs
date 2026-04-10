using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.Views;

/// <summary>
/// 历史记录查看对话框
/// </summary>
public partial class HistoryViewDialog : Window
{
    private readonly DatabaseService _databaseService;
    private readonly ObservableCollection<InstallHistory> _historyItems;

    public HistoryViewDialog(DatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _historyItems = new ObservableCollection<InstallHistory>();
        GridHistory.ItemsSource = _historyItems;

        Loaded += HistoryViewDialog_Loaded;
    }

    private async void HistoryViewDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.Run(() =>
        {
            // 加载服务器列表
            Dispatcher.Invoke(() => LoadServers());

            // 加载应用列表
            Dispatcher.Invoke(() => LoadApplications());

            // 加载历史记录
            Dispatcher.Invoke(() => LoadHistory());
        });
    }

    /// <summary>
    /// 加载服务器列表
    /// </summary>
    private void LoadServers()
    {
        CboServer.Items.Clear();
        CboServer.Items.Add(new ComboBoxItem { Content = "所有服务器", Tag = "-1" });

        var hosts = _databaseService.GetAllHosts();
        foreach (var host in hosts)
        {
            CboServer.Items.Add(new ComboBoxItem { Content = host.Name, Tag = host.Id });
        }
    }

    /// <summary>
    /// 加载应用列表
    /// </summary>
    private void LoadApplications()
    {
        CboApplication.Items.Clear();
        CboApplication.Items.Add(new ComboBoxItem { Content = "所有应用", Tag = "-1" });

        // 从历史记录中提取唯一的应用名称
        var applications = _databaseService.GetInstallHistory().Select(h => h.ApplicationName).Distinct();
        foreach (var app in applications)
        {
            CboApplication.Items.Add(new ComboBoxItem { Content = app, Tag = app });
        }
    }

    /// <summary>
    /// 加载历史记录
    /// </summary>
    private void LoadHistory()
    {
        _historyItems.Clear();

        try
        {
            var histories = _databaseService.GetInstallHistory();
            foreach (var history in histories)
            {
                _historyItems.Add(history);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载历史记录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 重置过滤条件
    /// </summary>
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        CboServer.SelectedIndex = 0;
        CboApplication.SelectedIndex = 0;
        CboStatus.SelectedIndex = 0;
        CboOperation.SelectedIndex = 0;
        DtpFrom.SelectedDate = null;
        DtpTo.SelectedDate = null;

        LoadHistory();
    }

    /// <summary>
    /// 筛选历史记录
    /// </summary>
    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterHistory();
    }

    /// <summary>
    /// 应用过滤条件
    /// </summary>
    private void FilterHistory()
    {
        var allHistories = _databaseService.GetInstallHistory();

        var filtered = allHistories.AsEnumerable();

        // 服务器过滤
        var serverItem = (CboServer.SelectedItem as ComboBoxItem);
        if (serverItem?.Tag?.ToString() != "-1")
        {
            var serverId = serverItem?.Tag?.ToString();
            filtered = filtered.Where(h => h.HostId == serverId);
        }

        // 应用过滤
        var appItem = (CboApplication.SelectedItem as ComboBoxItem);
        if (appItem?.Tag?.ToString() != "-1")
        {
            var appName = appItem?.Tag?.ToString();
            filtered = filtered.Where(h => h.ApplicationName == appName);
        }

        // 状态过滤
        var statusItem = (CboStatus.SelectedItem as ComboBoxItem);
        if (statusItem?.Tag?.ToString() != "-1")
        {
            if (int.TryParse(statusItem?.Tag?.ToString(), out var status))
            {
                filtered = filtered.Where(h => (int)h.Status == status);
            }
        }

        // 操作类型过滤
        var opItem = (CboOperation.SelectedItem as ComboBoxItem);
        if (opItem?.Tag?.ToString() != "-1")
        {
            if (int.TryParse(opItem?.Tag?.ToString(), out var opType))
            {
                filtered = filtered.Where(h => (int)h.OperationType == opType);
            }
        }

        // 日期范围过滤
        if (DtpFrom.SelectedDate.HasValue)
        {
            var fromDate = DtpFrom.SelectedDate.Value.Date;
            filtered = filtered.Where(h => h.StartTime >= fromDate);
        }

        if (DtpTo.SelectedDate.HasValue)
        {
            var toDate = DtpTo.SelectedDate.Value.Date.AddDays(1).AddTicks(-1);
            filtered = filtered.Where(h => h.StartTime <= toDate);
        }

        // 更新列表
        _historyItems.Clear();
        foreach (var history in filtered)
        {
            _historyItems.Add(history);
        }
    }

    /// <summary>
    /// 查看详情
    /// </summary>
    private void BtnViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not InstallHistory history)
        {
            MessageBox.Show("请选择一条历史记录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var details = $"服务器：{history.HostName}\n" +
                     $"应用：{history.ApplicationName}\n" +
                     $"版本：{history.ApplicationVersion}\n" +
                     $"操作：{history.OperationTypeDisplayText}\n" +
                     $"状态：{history.StatusDisplayText}\n" +
                     $"开始时间：{history.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                     $"结束时间：{history.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "进行中"}\n" +
                     $"耗时：{history.DurationDisplayText}\n" +
                     $"\n错误信息:\n{history.ErrorMessage ?? "无"}";

        MessageBox.Show(details, "详细信息", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 导出 CSV
    /// </summary>
    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_historyItems.Count == 0)
        {
            MessageBox.Show("没有可导出的数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"安装历史_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);

            // 写入表头
            writer.WriteLine("服务器，应用，版本，操作，状态，开始时间，结束时间，耗时，错误信息");

            // 写入数据
            foreach (var history in _historyItems)
            {
                var endTime = history.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                var errorMessage = (history.ErrorMessage ?? "").Replace("\"", "\"\"");

                writer.WriteLine($"\"{history.HostName}\",\"{history.ApplicationName}\",\"{history.ApplicationVersion}\",\"{history.OperationTypeDisplayText}\",\"{history.StatusDisplayText}\",\"{history.StartTime:yyyy-MM-dd HH:mm:ss}\",\"{endTime}\",\"{history.DurationDisplayText}\",\"{errorMessage}\"");
            }

            MessageBox.Show($"导出成功：{dialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
