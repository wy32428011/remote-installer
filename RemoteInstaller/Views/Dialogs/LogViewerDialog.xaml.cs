using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using RemoteInstaller.Services;

namespace RemoteInstaller.Views.Dialogs;

/// <summary>
/// 日志查看器对话框
/// </summary>
public partial class LogViewerDialog : Window
{
    private readonly FileLoggerService _fileLogger;
    private readonly string _logDirectory;

    public LogViewerDialog()
    {
        InitializeComponent();

        _fileLogger = new FileLoggerService();
        _logDirectory = _fileLogger.GetLogDirectory();

        LogPathText.Text = $"日志目录: {_logDirectory}";

        LoadLogs();
    }

    private void LoadLogs()
    {
        try
        {
            var logs = _fileLogger.ReadRecentLogs(2000);
            var sb = new StringBuilder();
            foreach (var log in logs)
            {
                sb.AppendLine(log);
            }
            LogTextBox.Text = sb.ToString();

            // 滚动到末尾
            LogTextBox.ScrollToEnd();
        }
        catch (Exception ex)
        {
            LogTextBox.Text = $"加载日志失败: {ex.Message}";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _logDirectory,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"日志目录不存在: {_logDirectory}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要清除所有日志文件吗？\n注意：此操作不可恢复。",
            "确认清除日志",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    foreach (var file in Directory.GetFiles(_logDirectory, "*.log*"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // 忽略单个文件删除失败
                        }
                    }
                }
                LoadLogs();
                MessageBox.Show("日志已清除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除日志失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
