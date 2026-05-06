using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
        private TaskViewModel? _boundTask;

        private static readonly IReadOnlyList<(string Title, string Description)> StageDefinitions = new[]
        {
            ("连接主机", "建立到目标服务器的连接并准备执行环境。"),
            ("上传资源", "上传脚本、安装包和所需依赖资源。"),
            ("写入配置", "准备安装参数并写入必要配置。"),
            ("执行安装", "执行安装脚本或部署命令。"),
            ("启动服务", "启动目标服务并等待其进入运行状态。"),
            ("校验结果", "核对安装结果、端口和运行状态。")
        };

        [ObservableProperty]
        private string _title = "任务进度";

        [ObservableProperty]
        private string _applicationName = "当前任务";

        [ObservableProperty]
        private string _hostName = "目标主机";

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _progressText = "0%";

        [ObservableProperty]
        private string _currentStage = "准备任务";

        [ObservableProperty]
        private string _currentStageTitle = "准备任务";

        [ObservableProperty]
        private string _currentStageDescription = "正在等待任务开始。";

        [ObservableProperty]
        private string _statusText = "待开始";

        [ObservableProperty]
        private string _statusColor = "#9CA3AF";

        [ObservableProperty]
        private string _taskIdentifier = "-";

        [ObservableProperty]
        private string _lastActivityText = "最近更新 -";

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private ObservableCollection<ProgressStageItemViewModel> _stageItems = new();

        [ObservableProperty]
        private ObservableCollection<LogViewModel> _logEntries = new();

        public InstallProgressViewModel(MainViewModel mainViewModel, Views.Dialogs.InstallProgressDialog dialog)
        {
            _mainViewModel = mainViewModel;
            _dialog = dialog;
            _dialog.Closed += (_, _) => DetachTask();
            InitializeStages();
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause()
        {
            IsPaused = true;
            IsRunning = false;
            StatusText = "已暂停";
            StatusColor = "#F59E0B";
            CurrentStageDescription = "任务已暂停，等待继续操作。";
            AddLog("安装已暂停", LogLevel.Warning);
        }

        private bool CanPause() => IsRunning && !IsPaused;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume()
        {
            IsPaused = false;
            IsRunning = true;
            StatusText = "进行中";
            StatusColor = "#3B82F6";
            CurrentStageDescription = GetStageDescription(CurrentStageTitle);
            AddLog("安装已恢复", LogLevel.Info);
        }

        private bool CanResume() => IsPaused;

        [RelayCommand]
        private void Cancel()
        {
            IsRunning = false;
            IsPaused = false;
            StatusText = "已取消";
            StatusColor = "#F59E0B";
            CurrentStage = "已取消";
            CurrentStageTitle = "已取消";
            CurrentStageDescription = "任务已被取消，窗口即将关闭。";
            UpdateStageItems(CurrentStageTitle, Progress);
            AddLog("安装已取消", LogLevel.Warning);
            _dialog?.Close();
        }

        [RelayCommand]
        private void CloseDialog()
        {
            _dialog?.Close();
        }

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

        public void SetTaskContext(string applicationName, string hostName, string? title = null)
        {
            ApplicationName = string.IsNullOrWhiteSpace(applicationName) ? "当前任务" : applicationName;
            HostName = string.IsNullOrWhiteSpace(hostName) ? "目标主机" : hostName;
            if (!string.IsNullOrWhiteSpace(title))
            {
                Title = title;
            }
        }

        public void BindTask(TaskViewModel task)
        {
            if (task == null)
            {
                return;
            }

            DetachTask();
            _boundTask = task;

            SetTaskContext(task.ApplicationName, task.HostName, $"任务进度 · {task.ApplicationName}");
            TaskIdentifier = string.IsNullOrWhiteSpace(task.TaskId) ? "未分配" : task.TaskId;
            LastActivityText = task.LastActivityText;
            UpdateFromTask(task);
            ReloadLogs(task);

            task.PropertyChanged += Task_PropertyChanged;
            task.LogEntries.CollectionChanged += TaskLogEntries_CollectionChanged;
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

            TrimLogs();
            ScrollToEndIfNeeded();
        }

        /// <summary>
        /// 更新进度
        /// </summary>
        public void UpdateProgress(double progress, string stage)
        {
            Progress = Math.Max(0, Math.Min(100, progress));
            ProgressText = $"{Progress:0}%";
            CurrentStage = string.IsNullOrWhiteSpace(stage) ? "准备任务" : stage;
            CurrentStageTitle = CurrentStage;
            CurrentStageDescription = GetStageDescription(CurrentStageTitle);

            if (!IsPaused && !IsTerminalStage(CurrentStageTitle))
            {
                IsRunning = true;
                StatusText = "进行中";
                StatusColor = "#3B82F6";
            }

            if (CurrentStageTitle.Contains("完成", StringComparison.OrdinalIgnoreCase))
            {
                IsRunning = false;
                StatusText = "已完成";
                StatusColor = "#10B981";
            }
            else if (CurrentStageTitle.Contains("失败", StringComparison.OrdinalIgnoreCase))
            {
                IsRunning = false;
                StatusText = "失败";
                StatusColor = "#EF4444";
            }
            else if (CurrentStageTitle.Contains("取消", StringComparison.OrdinalIgnoreCase))
            {
                IsRunning = false;
                StatusText = "已取消";
                StatusColor = "#F59E0B";
            }

            UpdateStageItems(CurrentStageTitle, Progress);
        }

        /// <summary>
        /// 开始安装
        /// </summary>
        public void StartInstallation()
        {
            IsRunning = true;
            IsPaused = false;
            StatusText = "进行中";
            StatusColor = "#3B82F6";
            Progress = 0;
            ProgressText = "0%";
            CurrentStage = "准备任务";
            CurrentStageTitle = "准备任务";
            CurrentStageDescription = "正在初始化任务并准备执行环境。";
            UpdateStageItems(CurrentStageTitle, Progress);
            AddLog("开始安装...", LogLevel.Info);
        }

        /// <summary>
        /// 完成安装
        /// </summary>
        public void CompleteInstallation(bool success)
        {
            IsRunning = false;
            IsPaused = false;
            Progress = 100;
            ProgressText = "100%";

            if (success)
            {
                CurrentStage = "安装完成";
                CurrentStageTitle = "安装完成";
                CurrentStageDescription = "任务已成功完成，可以关闭窗口或保存日志。";
                StatusText = "已完成";
                StatusColor = "#10B981";
                MarkAllStagesCompleted();
                AddLog("安装完成！", LogLevel.Success);
            }
            else
            {
                CurrentStage = "执行失败";
                CurrentStageTitle = "执行失败";
                CurrentStageDescription = "任务未能完成，请查看下方日志定位问题。";
                StatusText = "失败";
                StatusColor = "#EF4444";
                UpdateStageItems(CurrentStageTitle, Progress);
                AddLog("安装失败", LogLevel.Error);
            }
        }

        private void DetachTask()
        {
            if (_boundTask == null)
            {
                return;
            }

            _boundTask.PropertyChanged -= Task_PropertyChanged;
            _boundTask.LogEntries.CollectionChanged -= TaskLogEntries_CollectionChanged;
            _boundTask = null;
        }

        private void Task_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not TaskViewModel task)
            {
                return;
            }

            if (e.PropertyName == nameof(TaskViewModel.ApplicationName)
                || e.PropertyName == nameof(TaskViewModel.HostName))
            {
                SetTaskContext(task.ApplicationName, task.HostName, $"任务进度 · {task.ApplicationName}");
                return;
            }

            if (e.PropertyName == nameof(TaskViewModel.TaskId))
            {
                TaskIdentifier = string.IsNullOrWhiteSpace(task.TaskId) ? "未分配" : task.TaskId;
                return;
            }

            if (e.PropertyName == nameof(TaskViewModel.LastActivityText)
                || e.PropertyName == nameof(TaskViewModel.LastActivityAt))
            {
                LastActivityText = task.LastActivityText;
                return;
            }

            if (e.PropertyName == nameof(TaskViewModel.Progress)
                || e.PropertyName == nameof(TaskViewModel.CurrentStage)
                || e.PropertyName == nameof(TaskViewModel.StatusMessage)
                || e.PropertyName == nameof(TaskViewModel.IsCompleted)
                || e.PropertyName == nameof(TaskViewModel.IsFailed)
                || e.PropertyName == nameof(TaskViewModel.IsCanceled))
            {
                UpdateFromTask(task);
            }
        }

        private void TaskLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender is not ObservableCollection<LogViewModel> taskLogs)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Clear();
                foreach (var log in taskLogs)
                {
                    LogEntries.Add(new LogViewModel
                    {
                        Message = log.Message,
                        Level = log.Level,
                        Timestamp = log.Timestamp
                    });
                }

                TrimLogs();
                ScrollToEndIfNeeded();
            });
        }

        private void ReloadLogs(TaskViewModel task)
        {
            LogEntries.Clear();
            foreach (var log in task.LogEntries)
            {
                LogEntries.Add(new LogViewModel
                {
                    Message = log.Message,
                    Level = log.Level,
                    Timestamp = log.Timestamp
                });
            }

            TrimLogs();
            ScrollToEndIfNeeded();
        }

        private void UpdateFromTask(TaskViewModel task)
        {
            Progress = Math.Max(0, Math.Min(100, task.Progress));
            ProgressText = task.ProgressText;
            CurrentStage = string.IsNullOrWhiteSpace(task.CurrentStage) ? "准备任务" : task.CurrentStage;
            CurrentStageTitle = CurrentStage;

            if (task.IsCompleted)
            {
                CurrentStageDescription = "任务已成功完成，可以关闭窗口或保存日志。";
                StatusText = "已完成";
                StatusColor = "#10B981";
                MarkAllStagesCompleted();
            }
            else if (task.IsFailed)
            {
                CurrentStageDescription = string.IsNullOrWhiteSpace(task.StatusMessage)
                    ? "任务未能完成，请查看下方日志定位问题。"
                    : task.StatusMessage;
                StatusText = "失败";
                StatusColor = "#EF4444";
                UpdateStageItems(CurrentStageTitle, Progress);
            }
            else if (task.IsCanceled)
            {
                CurrentStageDescription = string.IsNullOrWhiteSpace(task.StatusMessage)
                    ? "任务已被取消。"
                    : task.StatusMessage;
                StatusText = "已取消";
                StatusColor = "#F59E0B";
                UpdateStageItems(CurrentStageTitle, Progress);
            }
            else
            {
                CurrentStageDescription = string.IsNullOrWhiteSpace(task.StatusMessage)
                    ? GetStageDescription(CurrentStageTitle)
                    : task.StatusMessage;
                StatusText = task.Progress <= 0 ? "待开始" : "进行中";
                StatusColor = task.Progress <= 0 ? "#9CA3AF" : "#3B82F6";
                UpdateStageItems(CurrentStageTitle, Progress);
            }

            IsRunning = !task.IsCompleted && !task.IsFailed && !task.IsCanceled && !IsPaused;
        }

        private void TrimLogs()
        {
            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        }

        private void ScrollToEndIfNeeded()
        {
            if (AutoScroll && _dialog != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var scrollViewer = _dialog.FindName("LogScrollViewer") as System.Windows.Controls.ScrollViewer;
                    scrollViewer?.ScrollToEnd();
                });
            }
        }

        private void InitializeStages()
        {
            StageItems.Clear();
            foreach (var (title, description) in StageDefinitions)
            {
                StageItems.Add(new ProgressStageItemViewModel
                {
                    Title = title,
                    Description = description
                });
            }
        }

        private void MarkAllStagesCompleted()
        {
            foreach (var item in StageItems)
            {
                item.IsCompleted = true;
                item.IsActive = false;
            }
        }

        private void UpdateStageItems(string stage, double progress)
        {
            if (StageItems.Count == 0)
            {
                InitializeStages();
            }

            var activeIndex = ResolveStageIndex(stage, progress);
            var isSuccess = stage.Contains("完成", StringComparison.OrdinalIgnoreCase);
            var isFailure = stage.Contains("失败", StringComparison.OrdinalIgnoreCase);
            var isCanceled = stage.Contains("取消", StringComparison.OrdinalIgnoreCase);

            for (var i = 0; i < StageItems.Count; i++)
            {
                StageItems[i].IsCompleted = isSuccess ? i <= activeIndex : i < activeIndex;
                StageItems[i].IsActive = !isSuccess && !isCanceled && i == activeIndex;

                if (isFailure && i == activeIndex)
                {
                    StageItems[i].IsActive = true;
                }
            }
        }

        private static int ResolveStageIndex(string stage, double progress)
        {
            if (string.IsNullOrWhiteSpace(stage))
            {
                return progress switch
                {
                    <= 10 => 0,
                    <= 35 => 1,
                    <= 55 => 2,
                    <= 80 => 3,
                    <= 92 => 4,
                    _ => 5
                };
            }

            if (stage.Contains("连接", StringComparison.OrdinalIgnoreCase)) return 0;
            if (stage.Contains("上传", StringComparison.OrdinalIgnoreCase)) return 1;
            if (stage.Contains("配置", StringComparison.OrdinalIgnoreCase)) return 2;
            if (stage.Contains("安装", StringComparison.OrdinalIgnoreCase) || stage.Contains("解压", StringComparison.OrdinalIgnoreCase)) return 3;
            if (stage.Contains("启动", StringComparison.OrdinalIgnoreCase)) return 4;
            if (stage.Contains("校验", StringComparison.OrdinalIgnoreCase) || stage.Contains("验证", StringComparison.OrdinalIgnoreCase) || stage.Contains("完成", StringComparison.OrdinalIgnoreCase) || stage.Contains("失败", StringComparison.OrdinalIgnoreCase) || stage.Contains("取消", StringComparison.OrdinalIgnoreCase)) return 5;

            return progress switch
            {
                <= 10 => 0,
                <= 35 => 1,
                <= 55 => 2,
                <= 80 => 3,
                <= 92 => 4,
                _ => 5
            };
        }

        private static bool IsTerminalStage(string stage)
        {
            return stage.Contains("完成", StringComparison.OrdinalIgnoreCase)
                   || stage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                   || stage.Contains("取消", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStageDescription(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage))
            {
                return "正在等待任务开始。";
            }

            if (stage.Contains("准备", StringComparison.OrdinalIgnoreCase)) return "正在初始化任务并整理所需上下文。";
            if (stage.Contains("连接", StringComparison.OrdinalIgnoreCase)) return "正在建立到目标主机的连接。";
            if (stage.Contains("上传", StringComparison.OrdinalIgnoreCase)) return "正在上传脚本、安装包或依赖资源。";
            if (stage.Contains("配置", StringComparison.OrdinalIgnoreCase)) return "正在准备或写入安装参数与配置。";
            if (stage.Contains("安装", StringComparison.OrdinalIgnoreCase) || stage.Contains("解压", StringComparison.OrdinalIgnoreCase)) return "正在执行核心安装流程，请稍候。";
            if (stage.Contains("启动", StringComparison.OrdinalIgnoreCase)) return "正在启动目标服务并等待其就绪。";
            if (stage.Contains("校验", StringComparison.OrdinalIgnoreCase) || stage.Contains("验证", StringComparison.OrdinalIgnoreCase)) return "正在核对安装结果与运行状态。";
            if (stage.Contains("完成", StringComparison.OrdinalIgnoreCase)) return "任务已成功完成，可以关闭窗口或保存日志。";
            if (stage.Contains("失败", StringComparison.OrdinalIgnoreCase)) return "任务未能完成，请查看下方日志定位问题。";
            if (stage.Contains("取消", StringComparison.OrdinalIgnoreCase)) return "任务已被取消。";

            return "任务正在执行中，请结合日志查看详细输出。";
        }
    }

    public partial class ProgressStageItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private bool _isCompleted;

        [ObservableProperty]
        private bool _isActive;

        partial void OnIsCompletedChanged(bool value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        public string StatusText => IsCompleted
            ? "已完成"
            : IsActive
                ? "进行中"
                : "待开始";

        public string StatusColor => IsCompleted
            ? "#10B981"
            : IsActive
                ? "#3B82F6"
                : "#9CA3AF";
    }
}
