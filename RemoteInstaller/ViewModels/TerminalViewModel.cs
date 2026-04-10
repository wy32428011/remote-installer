using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    private readonly SshService _sshService;
    private readonly RemoteHost _remoteHost;
    private readonly object _outputLock = new();
    private readonly object _commandLock = new();
    private readonly StringBuilder _outputBuilder = new(100000);
    private readonly StringBuilder _streamBuffer = new();
    private readonly StringBuilder _pendingUiAppendBuilder = new();

    private readonly ObservableCollection<string> _commandHistory = new();
    private CancellationTokenSource? _connectCts;
    private TaskCompletionSource<bool>? _pendingCommandCompletion;
    private string? _pendingCommandMarker;
    private bool _cancelRequested;
    private bool _isDisconnecting;
    private bool _outputMetricsDirty;
    private bool _uiFlushScheduled;
    private bool _uiNeedsReload;
    private int _historyIndex = -1;
    private int _pendingOutputLength;
    private string _lastCommandBeforeNavigate = string.Empty;

    private const int MaxOutputLength = 1_000_000;
    private const int TrimLength = 500_000;
    private const int MaxVisibleOutputLength = 200_000;
    private const int OutputBatchFlushThreshold = 16_384;
    private const int UiFlushIntervalMs = 33;
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\a]*(\a|\x1B\\)", RegexOptions.Compiled);

    [ObservableProperty]
    private string _currentCommand = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "连接中...";

    [ObservableProperty]
    private HostViewModel? _hostViewModel;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private ObservableCollection<string> _commandHistoryList = null!;

    [ObservableProperty]
    private string _currentDirectory = "~";

    [ObservableProperty]
    private string _lastExitCode = "--";

    [ObservableProperty]
    private int _executedCommandCount;

    [ObservableProperty]
    private string _outputSize = "0 B";

    [ObservableProperty]
    private string _operatingSystemLabel = "未知";

    [ObservableProperty]
    private string _shellLabel = "Shell";

    public event Action<string>? OutputAppended;
    public event Action? OutputReloadRequested;

    public string WindowTitle => HostViewModel != null
        ? $"终端 - {HostViewModel.Name} ({HostViewModel.IpAddress})"
        : "终端";

    public bool IsWindowsHost => _remoteHost.OsType == OperatingSystemType.Windows;

    public bool IsLinuxHost => !IsWindowsHost;

    public bool CanReconnect => !IsExecuting;

    public string ShortcutHint => "回车执行 | Ctrl+C 中断 | Ctrl+L 清屏 | F5 重连";

    public string OutputSnapshot => GetOutputSnapshot();

    public string VisibleOutputSnapshot => GetVisibleOutputSnapshot();

    public int VisibleOutputLimit => MaxVisibleOutputLength;

    public TerminalViewModel(SshService sshService, HostViewModel hostViewModel, RemoteHost remoteHost)
    {
        _sshService = sshService;
        HostViewModel = hostViewModel;
        _remoteHost = remoteHost;
        _commandHistoryList = _commandHistory;
        OperatingSystemLabel = GetOperatingSystemLabel(remoteHost.OsType);
        ShellLabel = IsWindowsHost ? "PowerShell" : "Bash / sh";
        CurrentDirectory = IsWindowsHost ? @"C:\" : "~";

        _ = ConnectAsync();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReconnect));
    }

    private async Task ConnectAsync()
    {
        try
        {
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();

            StatusMessage = "连接中...";
            AddSystemLine($"正在连接到 {HostViewModel?.Name} ({HostViewModel?.IpAddress})...");

            await _sshService.StartTerminalSessionAsync(_remoteHost, OnTerminalOutput, _connectCts.Token);
            await _sshService.SendTerminalInputAsync(string.Empty, appendNewLine: true, cancellationToken: _connectCts.Token);

            IsConnected = true;
            StatusMessage = "已连接";
            AddSystemLine($"已使用 {HostViewModel?.Username} 登录");
            AddSystemLine($"会话已就绪（{ShellLabel}）");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = "连接失败";
            AddErrorLine($"连接失败: {ex.Message}");
        }
    }

    public void NavigateHistoryUp()
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }

        if (_historyIndex == -1 && !string.IsNullOrEmpty(CurrentCommand))
        {
            _lastCommandBeforeNavigate = CurrentCommand;
        }

        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            CurrentCommand = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
        }
        else if (_historyIndex == _commandHistory.Count - 1)
        {
            CurrentCommand = _commandHistory[0];
        }
    }

    public void NavigateHistoryDown()
    {
        if (_historyIndex == -1)
        {
            return;
        }

        if (_historyIndex == 0)
        {
            _historyIndex = -1;
            CurrentCommand = _lastCommandBeforeNavigate;
        }
        else
        {
            _historyIndex--;
            CurrentCommand = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
        }
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        await ExecuteTerminalCommandAsync(CurrentCommand, clearInput: true);
    }

    [RelayCommand]
    private async Task RunQuickCommandAsync(string? command)
    {
        await ExecuteTerminalCommandAsync(command, clearInput: false);
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (IsExecuting)
        {
            return;
        }

        await DisconnectAsync(logMessage: false);
        AddSystemLine("正在重连终端会话...");
        await ConnectAsync();
    }

    [RelayCommand]
    private async Task CancelCommandAsync()
    {
        if (!IsExecuting)
        {
            return;
        }

        _cancelRequested = true;
        StatusMessage = "正在取消...";
        await SafeInterruptAsync();
    }

    [RelayCommand]
    private void ClearOutput()
    {
        ClearOutputCore(addMessage: true);
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
        AddSystemLine($"自动滚动已{(AutoScroll ? "开启" : "关闭")}。");
    }

    [RelayCommand]
    private void ExportOutput()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".log",
                FileName = $"终端日志_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, GetOutputSnapshot(), new UTF8Encoding(false));
                AddSystemLine($"日志已导出到: {dialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            AddErrorLine($"导出失败: {ex.Message}");
        }
    }

    private async Task ExecuteTerminalCommandAsync(string? rawCommand, bool clearInput)
    {
        if (!IsConnected || IsExecuting)
        {
            return;
        }

        var command = rawCommand?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (IsClearCommand(command))
        {
            ClearOutputCore(addMessage: false);
            StatusMessage = "已清屏";
            return;
        }

        if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
        {
            _commandHistory.Add(command);
        }

        _historyIndex = -1;
        if (clearInput)
        {
            CurrentCommand = string.Empty;
        }

        _cancelRequested = false;
        IsExecuting = true;
        StatusMessage = "执行中...";

        var marker = $"__RI_CMD_DONE_{Guid.NewGuid():N}__";
        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_commandLock)
        {
            _pendingCommandMarker = marker;
            _pendingCommandCompletion = completionSource;
        }

        try
        {
            await _sshService.SendTerminalInputAsync(command);
            await _sshService.SendTerminalInputAsync(BuildCompletionCommand(marker));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completedTask != completionSource.Task)
            {
                throw new TimeoutException("命令执行超时。");
            }

            await completionSource.Task;

            StatusMessage = _cancelRequested
                ? "已取消"
                : $"已完成（退出码 {LastExitCode}）";

            if (_cancelRequested)
            {
                AddSystemLine("命令已取消。");
            }
        }
        catch (TimeoutException ex)
        {
            StatusMessage = "超时";
            AddErrorLine(ex.Message);
            await SafeInterruptAsync();
            ClearPendingCommand();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消";
            ClearPendingCommand();
        }
        catch (Exception ex)
        {
            StatusMessage = "失败";
            AddErrorLine($"执行错误: {ex.Message}");
            ClearPendingCommand();
        }
        finally
        {
            IsExecuting = false;
            _cancelRequested = false;
        }
    }

    private void OnTerminalOutput(string outputChunk)
    {
        if (string.IsNullOrEmpty(outputChunk))
        {
            return;
        }

        var normalized = NormalizeTerminalOutput(outputChunk);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        lock (_commandLock)
        {
            _streamBuffer.Append(normalized);
            FlushStreamBufferLocked();
        }
    }

    private void FlushStreamBufferLocked()
    {
        StringBuilder? pendingChunk = null;

        while (true)
        {
            var lineEnd = FindNewLineIndex(_streamBuffer);
            if (lineEnd < 0)
            {
                FlushPendingCommittedOutput(pendingChunk);
                return;
            }

            var line = _streamBuffer.ToString(0, lineEnd + 1);
            _streamBuffer.Remove(0, lineEnd + 1);

            if (ContainsPendingCommandMarker(line))
            {
                FlushPendingCommittedOutput(pendingChunk);
                TryHandleControlLine(line);
                continue;
            }

            pendingChunk ??= new StringBuilder(Math.Max(line.Length, OutputBatchFlushThreshold));
            pendingChunk.Append(line);
            if (pendingChunk.Length >= OutputBatchFlushThreshold)
            {
                FlushPendingCommittedOutput(pendingChunk);
            }
        }
    }

    private bool ContainsPendingCommandMarker(string line)
    {
        var marker = _pendingCommandMarker;
        return !string.IsNullOrEmpty(marker) && line.Contains(marker, StringComparison.Ordinal);
    }

    private bool TryHandleControlLine(string line)
    {
        var marker = _pendingCommandMarker;
        if (string.IsNullOrEmpty(marker) || !line.Contains(marker, StringComparison.Ordinal))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith(marker + "|", StringComparison.Ordinal))
        {
            var payload = trimmed[(marker.Length + 1)..];
            ApplyCommandMetadata(payload);
            CompletePendingCommandLocked();
        }

        return true;
    }

    private void FlushPendingCommittedOutput(StringBuilder? pendingChunk)
    {
        if (pendingChunk == null || pendingChunk.Length == 0)
        {
            return;
        }

        AppendCommittedOutput(pendingChunk.ToString());
        pendingChunk.Clear();
    }

    private void ApplyCommandMetadata(string payload)
    {
        var parts = payload.Split('|', 2);
        LastExitCode = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])
            ? parts[0].Trim()
            : "--";

        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            CurrentDirectory = parts[1].Trim();
        }

        ExecutedCommandCount++;
    }

    private void CompletePendingCommandLocked()
    {
        var completion = _pendingCommandCompletion;
        _pendingCommandCompletion = null;
        _pendingCommandMarker = null;
        completion?.TrySetResult(true);
    }

    private void ClearPendingCommand()
    {
        TaskCompletionSource<bool>? completion;

        lock (_commandLock)
        {
            completion = _pendingCommandCompletion;
            _pendingCommandCompletion = null;
            _pendingCommandMarker = null;
            _streamBuffer.Clear();
        }

        completion?.TrySetCanceled();
    }

    private async Task SafeInterruptAsync()
    {
        try
        {
            await _sshService.SendTerminalInterruptAsync();
        }
        catch (Exception ex)
        {
            AddErrorLine($"中断失败: {ex.Message}");
        }
    }

    private void ClearOutputCore(bool addMessage)
    {
        lock (_outputLock)
        {
            _outputBuilder.Clear();
            if (!IsExecuting)
            {
                _streamBuffer.Clear();
            }

            UpdateOutputSizeLocked();
            QueueUiReloadLocked();
        }

        if (addMessage)
        {
            AddSystemLine("输出已清空。");
        }
    }

    private void AddSystemLine(string message)
    {
        AddMetaLine(message, "[系统]");
    }

    private void AddErrorLine(string message)
    {
        AddMetaLine(message, "[错误]");
    }

    private void AddMetaLine(string message, string prefix)
    {
        AppendCommittedOutput($"{prefix} {message}\n");
    }

    private void AppendCommittedOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_outputLock)
        {
            var trimmed = EnsureOutputCapacityLocked(text.Length);
            _outputBuilder.Append(text);
            UpdateOutputSizeLocked();
            if (trimmed)
            {
                QueueUiReloadLocked();
            }
            else
            {
                QueueUiAppendLocked(text);
            }
        }
    }

    private bool EnsureOutputCapacityLocked(int incomingLength)
    {
        if (_outputBuilder.Length + incomingLength <= MaxOutputLength)
        {
            return false;
        }

        var targetLength = Math.Min(TrimLength, Math.Max(0, MaxOutputLength - incomingLength));
        var removeLength = Math.Max(0, _outputBuilder.Length - targetLength);
        if (removeLength > 0)
        {
            _outputBuilder.Remove(0, removeLength);
        }

        return true;
    }

    private void UpdateOutputSizeLocked()
    {
        _pendingOutputLength = _outputBuilder.Length;
        _outputMetricsDirty = true;
    }

    private void QueueUiAppendLocked(string text)
    {
        if (string.IsNullOrEmpty(text) || _uiNeedsReload)
        {
            return;
        }

        _pendingUiAppendBuilder.Append(text);
        ScheduleUiFlushLocked();
    }

    private void QueueUiReloadLocked()
    {
        _pendingUiAppendBuilder.Clear();
        _uiNeedsReload = true;
        ScheduleUiFlushLocked();
    }

    private void ScheduleUiFlushLocked()
    {
        if (_uiFlushScheduled)
        {
            return;
        }

        _uiFlushScheduled = true;
        _ = FlushUiOutputAsync();
    }

    private async Task FlushUiOutputAsync()
    {
        await Task.Delay(UiFlushIntervalMs);

        bool outputMetricsChanged;
        int outputLength;
        bool reloadRequested;
        string appendedText;

        lock (_outputLock)
        {
            outputMetricsChanged = _outputMetricsDirty;
            outputLength = _pendingOutputLength;
            reloadRequested = _uiNeedsReload;
            appendedText = reloadRequested ? string.Empty : _pendingUiAppendBuilder.ToString();
            _outputMetricsDirty = false;
            _pendingUiAppendBuilder.Clear();
            _uiNeedsReload = false;
            _uiFlushScheduled = false;
        }

        await Application.Current!.Dispatcher.InvokeAsync(
            () =>
            {
                if (outputMetricsChanged)
                {
                    OutputSize = FormatSize(outputLength);
                }

                if (reloadRequested)
                {
                    OutputReloadRequested?.Invoke();
                    return;
                }

                if (!string.IsNullOrEmpty(appendedText))
                {
                    OutputAppended?.Invoke(appendedText);
                }
            },
            System.Windows.Threading.DispatcherPriority.Background);

        lock (_outputLock)
        {
            if (_uiNeedsReload || _pendingUiAppendBuilder.Length > 0)
            {
                ScheduleUiFlushLocked();
            }
        }
    }

    private string GetOutputSnapshot()
    {
        lock (_outputLock)
        {
            return _outputBuilder.ToString();
        }
    }

    private string GetVisibleOutputSnapshot()
    {
        lock (_outputLock)
        {
            if (_outputBuilder.Length <= MaxVisibleOutputLength)
            {
                return _outputBuilder.ToString();
            }

            var startIndex = _outputBuilder.Length - MaxVisibleOutputLength;
            return _outputBuilder.ToString(startIndex, MaxVisibleOutputLength);
        }
    }

    private static int FindNewLineIndex(StringBuilder buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeTerminalOutput(string output)
    {
        if (!NeedsOutputNormalization(output))
        {
            return output;
        }

        var normalized = output
            .Replace("\0", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\b", string.Empty);

        if (normalized.IndexOf('\u001b') < 0)
        {
            return normalized;
        }

        return AnsiEscapeRegex.Replace(normalized, string.Empty);
    }

    private static bool NeedsOutputNormalization(string output)
    {
        foreach (var character in output)
        {
            if (character is '\0' or '\r' or '\b' or '\u001b')
            {
                return true;
            }
        }

        return false;
    }

    private string BuildCompletionCommand(string marker)
    {
        if (IsWindowsHost)
        {
            return "Write-Output ''; " +
                   "$__ri_exit = if ($null -eq $LASTEXITCODE) { if ($?) { 0 } else { 1 } } else { $LASTEXITCODE }; " +
                   $"Write-Output (\"{marker}|$($__ri_exit)|$((Get-Location).Path)\")";
        }

        return $"printf '\\n{marker}|%s|%s\\n' \"$?\" \"$(pwd)\"";
    }

    private static bool IsClearCommand(string command)
    {
        return string.Equals(command, "clear", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "cls", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOperatingSystemLabel(OperatingSystemType operatingSystemType)
    {
        return operatingSystemType switch
        {
            OperatingSystemType.Windows => "Windows",
            OperatingSystemType.Ubuntu => "Ubuntu",
            OperatingSystemType.CentOS => "CentOS",
            OperatingSystemType.Linux => "Linux",
            _ => "未知"
        };
    }

    private static string FormatSize(int length)
    {
        var value = (double)Math.Max(0, length);
        var units = new[] { "B", "KB", "MB" };
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    public async Task DisconnectAsync(bool logMessage = true)
    {
        if (_isDisconnecting || (!IsConnected && _connectCts == null))
        {
            return;
        }

        _isDisconnecting = true;
        try
        {
            _connectCts?.Cancel();
            ClearPendingCommand();

            await _sshService.StopTerminalSessionAsync();
            _sshService.Disconnect();

            IsConnected = false;
            IsExecuting = false;
            StatusMessage = "已断开";

            if (logMessage)
            {
                AddSystemLine("连接已关闭。");
            }
        }
        catch (Exception ex)
        {
            AddErrorLine($"断开连接时发生错误: {ex.Message}");
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            _isDisconnecting = false;
        }
    }
}
