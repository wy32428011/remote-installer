using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;

namespace RemoteInstaller.ViewModels
{
    /// <summary>
    /// 安装进度监控 ViewModel
    /// </summary>
    public partial class InstallProgressViewModel : ObservableObject
    {
        private readonly MainViewModel _mainViewModel;
        private readonly Views.Dialogs.InstallProgressDialog _dialog;

        #region 属性

        [ObservableProperty]
        private string _title = "安装进度";

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _currentStage = "准备中...";

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isStep1Complete;

        [ObservableProperty]
        private bool _isStep2Complete;

        [ObservableProperty]
        private bool _isStep3Complete;

        [ObservableProperty]
        private bool _isStep3Running;

        [ObservableProperty]
        private bool _isStep4Complete;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private ObservableCollection<LogViewModel> _logEntries = new();

        #endregion

        #region 命令

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause()
        {
            IsPaused = true;
            AddLog("安装已暂停", LogLevel.Warning);
        }

        private bool CanPause() => IsRunning && !IsPaused;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume()
        {
            IsPaused = false;
            AddLog("安装已恢复", LogLevel.Info);
            // TODO: 恢复安装进程
        }

        private bool CanResume() => IsPaused;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel()
        {
            IsRunning = false;
            AddLog("安装已取消", LogLevel.Warning);
            _dialog?.Close();
        }

        private bool CanCancel() => IsRunning;

        [RelayCommand]
        private void ClearLog()
        {
            LogEntries.Clear();
            AddLog("日志已清空", LogLevel.Info);
        }

        [RelayCommand]
        private void SaveLog()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "日志文件|*.log|文本文件|*.txt|所有文件|*.*",
                    DefaultExt = ".log",
                    FileName = $"安装日志_{DateTime.Now:yyyyMMdd_HHmmss}.log"
                };

                if (dialog.ShowDialog() == true)
                {
                    var lines = new System.Text.StringBuilder();
                    foreach (var log in LogEntries)
                    {
                        lines.AppendLine(log.DisplayText);
                    }
                    File.WriteAllText(dialog.FileName, lines.ToString());
                    AddLog($"日志已保存到：{dialog.FileName}", LogLevel.Success);
                }
            }
            catch (Exception ex)
            {
                AddLog($"保存日志失败：{ex.Message}", LogLevel.Error);
            }
        }

        [RelayCommand]
        private void Minimize()
        {
            _dialog.WindowState = WindowState.Minimized;
        }

        #endregion

        #region 方法

        public InstallProgressViewModel(MainViewModel mainViewModel, Views.Dialogs.InstallProgressDialog dialog)
        {
            _mainViewModel = mainViewModel;
            _dialog = dialog;
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        public void AddLog(string message, LogLevel level)
        {
            LogEntries.Add(new LogViewModel
            {
                Message = message,
                Level = level
            });

            // 限制日志条目数量
            if (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }

            // 自动滚动
            if (AutoScroll && _dialog != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var scrollViewer = _dialog.FindName("LogScrollViewer") as System.Windows.Controls.ScrollViewer;
                    scrollViewer?.ScrollToEnd();
                });
            }
        }

        /// <summary>
        /// 更新进度
        /// </summary>
        public void UpdateProgress(double progress, string stage)
        {
            Progress = Math.Max(0, Math.Min(100, progress));
            CurrentStage = stage;

            // 更新步骤状态
            if (progress >= 20) IsStep1Complete = true;
            if (progress >= 40) IsStep2Complete = true;
            if (progress >= 80) IsStep3Complete = true;
            if (progress >= 100) IsStep4Complete = true;
        }

        /// <summary>
        /// 开始安装
        /// </summary>
        public void StartInstallation()
        {
            IsRunning = true;
            IsStep3Running = true;
            AddLog("开始安装...", LogLevel.Info);
        }

        /// <summary>
        /// 完成安装
        /// </summary>
        public void CompleteInstallation(bool success)
        {
            IsRunning = false;
            IsStep3Running = false;
            IsStep4Complete = true;
            Progress = 100;

            if (success)
            {
                AddLog("安装完成！", LogLevel.Success);
            }
            else
            {
                AddLog("安装失败", LogLevel.Error);
            }
        }

        #endregion
    }
}
