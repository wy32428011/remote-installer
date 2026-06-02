using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly StringBuilder _plainTextOutputBuilder = new(100000);
    private readonly StringBuilder _streamBuffer = new();
    private readonly TerminalAnsiParser _ansiParser = new();
    private readonly List<TerminalOutputSpan> _visibleRenderSpans = new();
    private readonly List<TerminalOutputSpan> _pendingUiAppendSpans = new();

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

    /// <summary>
    /// 终端右侧文件侧栏。
    /// 与终端会话共用同一个 SSH/SFTP 服务，但保持独立职责。
    /// </summary>
    [ObservableProperty]
    private TerminalFilePaneViewModel? _filePane;

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

    public event Action<IReadOnlyList<TerminalOutputSpan>>? OutputAppended;
    public event Action? OutputReloadRequested;

    public string WindowTitle => HostViewModel != null
        ? $"终端 - {HostViewModel.Name} ({HostViewModel.IpAddress})"
        : "终端";

    public bool IsWindowsHost => _remoteHost.OsType == OperatingSystemType.Windows;

    public bool IsLinuxHost => !IsWindowsHost;

    public bool CanReconnect => !IsExecuting;

    public string ShortcutHint => "回车执行 | Ctrl+C 中断 | Ctrl+L 清屏 | F5 重连";

    /// <summary>
    /// 当前命令输入行使用的终端提示符。
    /// </summary>
    public string CommandPrompt => BuildCommandPrompt();

    public string OutputSnapshot => GetOutputSnapshot();

    public IReadOnlyList<TerminalOutputSpan> VisibleRenderSpans => GetVisibleRenderSpansSnapshot();

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
        FilePane = new TerminalFilePaneViewModel(_sshService, _remoteHost);

        _ = ConnectAsync();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReconnect));
    }

    partial void OnCurrentDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPrompt));
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

            if (FilePane != null)
            {
                await FilePane.InitializeAsync(_connectCts.Token);
            }
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

    /// <summary>
    /// 把右侧文件栏同步到当前终端目录。
    /// 第一版只做显式同步，避免把 shell 的每次目录变化都做成脆弱的实时双向绑定。
    /// </summary>
    [RelayCommand]
    private async Task SyncFilePaneToCurrentDirectoryAsync()
    {
        if (FilePane == null || !FilePane.IsSftpAvailable || string.IsNullOrWhiteSpace(CurrentDirectory))
        {
            return;
        }

        await FilePane.LoadDirectoryCommand.ExecuteAsync(CurrentDirectory);
        AddSystemLine($"文件侧栏已同步到 {CurrentDirectory}");
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

        AppendTerminalOutput(pendingChunk.ToString());
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

        lock (_outputLock)
        {
            _ansiParser.Reset();
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
            _plainTextOutputBuilder.Clear();
            _visibleRenderSpans.Clear();
            _pendingUiAppendSpans.Clear();
            _ansiParser.Reset();
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
        AddMetaLine(message, "[系统]", "InfoBrush");
    }

    private void AddErrorLine(string message)
    {
        AddMetaLine(message, "[错误]", "ErrorBrush");
    }

    private void AddMetaLine(string message, string prefix, string foregroundKey)
    {
        AppendRenderSpans(new[]
        {
            new TerminalOutputSpan
            {
                Text = $"{prefix} {message}\n",
                ForegroundKey = foregroundKey,
                IsBold = true
            }
        });
    }

    private void AppendTerminalOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        IReadOnlyList<TerminalOutputSpan> spans;
        lock (_outputLock)
        {
            spans = _ansiParser.Parse(text);
        }

        AppendRenderSpans(spans);
    }

    private void AppendRenderSpans(IReadOnlyList<TerminalOutputSpan> spans)
    {
        if (spans.Count == 0)
        {
            return;
        }

        lock (_outputLock)
        {
            var plainText = ToPlainText(spans);
            var trimmed = EnsureOutputCapacityLocked(plainText.Length);
            _plainTextOutputBuilder.Append(plainText);
            AppendVisibleRenderSpansLocked(spans);
            UpdateOutputSizeLocked();
            if (trimmed)
            {
                QueueUiReloadLocked();
            }
            else
            {
                QueueUiAppendLocked(spans);
            }
        }
    }

    private bool EnsureOutputCapacityLocked(int incomingLength)
    {
        if (_plainTextOutputBuilder.Length + incomingLength <= MaxOutputLength)
        {
            return false;
        }

        var targetLength = Math.Min(TrimLength, Math.Max(0, MaxOutputLength - incomingLength));
        var removeLength = Math.Max(0, _plainTextOutputBuilder.Length - targetLength);
        if (removeLength > 0)
        {
            _plainTextOutputBuilder.Remove(0, removeLength);
        }

        _visibleRenderSpans.Clear();
        _pendingUiAppendSpans.Clear();
        return true;
    }

    private void AppendVisibleRenderSpansLocked(IReadOnlyList<TerminalOutputSpan> spans)
    {
        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            if (_visibleRenderSpans.Count > 0 && CanMergeSpan(_visibleRenderSpans[^1], span))
            {
                _visibleRenderSpans[^1].Text += span.Text;
                continue;
            }

            _visibleRenderSpans.Add(CloneSpan(span));
        }

        TrimVisibleRenderSpansLocked();
    }

    private void TrimVisibleRenderSpansLocked()
    {
        var totalLength = 0;
        for (var index = _visibleRenderSpans.Count - 1; index >= 0; index--)
        {
            totalLength += _visibleRenderSpans[index].Text.Length;
            if (totalLength <= MaxVisibleOutputLength)
            {
                continue;
            }

            var overflow = totalLength - MaxVisibleOutputLength;
            if (overflow >= _visibleRenderSpans[index].Text.Length)
            {
                _visibleRenderSpans.RemoveRange(0, index + 1);
            }
            else
            {
                _visibleRenderSpans[index].Text = _visibleRenderSpans[index].Text[overflow..];
                if (index > 0)
                {
                    _visibleRenderSpans.RemoveRange(0, index);
                }
            }

            break;
        }
    }

    private void UpdateOutputSizeLocked()
    {
        _pendingOutputLength = _plainTextOutputBuilder.Length;
        _outputMetricsDirty = true;
    }

    private void QueueUiAppendLocked(IReadOnlyList<TerminalOutputSpan> spans)
    {
        if (spans.Count == 0 || _uiNeedsReload)
        {
            return;
        }

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            if (_pendingUiAppendSpans.Count > 0 && CanMergeSpan(_pendingUiAppendSpans[^1], span))
            {
                _pendingUiAppendSpans[^1].Text += span.Text;
                continue;
            }

            _pendingUiAppendSpans.Add(CloneSpan(span));
        }

        ScheduleUiFlushLocked();
    }

    private void QueueUiReloadLocked()
    {
        _pendingUiAppendSpans.Clear();
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
        List<TerminalOutputSpan> appendedSpans;

        lock (_outputLock)
        {
            outputMetricsChanged = _outputMetricsDirty;
            outputLength = _pendingOutputLength;
            reloadRequested = _uiNeedsReload;
            appendedSpans = reloadRequested
                ? new List<TerminalOutputSpan>()
                : _pendingUiAppendSpans.Select(CloneSpan).ToList();
            _outputMetricsDirty = false;
            _pendingUiAppendSpans.Clear();
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

                if (appendedSpans.Count > 0)
                {
                    OutputAppended?.Invoke(appendedSpans);
                }
            },
            System.Windows.Threading.DispatcherPriority.Background);

        lock (_outputLock)
        {
            if (_uiNeedsReload || _pendingUiAppendSpans.Count > 0)
            {
                ScheduleUiFlushLocked();
            }
        }
    }

    private string GetOutputSnapshot()
    {
        lock (_outputLock)
        {
            return _plainTextOutputBuilder.ToString();
        }
    }

    private IReadOnlyList<TerminalOutputSpan> GetVisibleRenderSpansSnapshot()
    {
        lock (_outputLock)
        {
            return _visibleRenderSpans.Select(CloneSpan).ToList();
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

        return output
            .Replace("\0", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\b", string.Empty);
    }

    private static bool NeedsOutputNormalization(string output)
    {
        foreach (var character in output)
        {
            if (character is '\0' or '\r' or '\b')
            {
                return true;
            }
        }

        return false;
    }

    private static string ToPlainText(IReadOnlyList<TerminalOutputSpan> spans)
    {
        if (spans.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var span in spans)
        {
            builder.Append(span.Text);
        }

        return builder.ToString();
    }

    private static bool CanMergeSpan(TerminalOutputSpan left, TerminalOutputSpan right)
    {
        return string.Equals(left.ForegroundKey, right.ForegroundKey, StringComparison.Ordinal)
            && string.Equals(left.BackgroundKey, right.BackgroundKey, StringComparison.Ordinal)
            && left.IsBold == right.IsBold;
    }

    private static TerminalOutputSpan CloneSpan(TerminalOutputSpan source)
    {
        return new TerminalOutputSpan
        {
            Text = source.Text,
            ForegroundKey = source.ForegroundKey,
            BackgroundKey = source.BackgroundKey,
            IsBold = source.IsBold
        };
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

    /// <summary>
    /// 根据主机系统和当前路径生成接近原生 shell 的提示符。
    /// </summary>
    private string BuildCommandPrompt()
    {
        var directory = NormalizePromptDirectory(CurrentDirectory);
        if (IsWindowsHost)
        {
            return $"PS {directory}>";
        }

        var userName = string.IsNullOrWhiteSpace(_remoteHost.Username)
            ? "user"
            : _remoteHost.Username.Trim();
        var hostName = string.IsNullOrWhiteSpace(HostViewModel?.Name)
            ? _remoteHost.IpAddress
            : HostViewModel!.Name.Trim();
        var suffix = string.Equals(userName, "root", StringComparison.OrdinalIgnoreCase) ? "#" : "$";

        return $"[{userName}@{hostName} {directory}]{suffix}";
    }

    /// <summary>
    /// 规范化提示符中的目录，避免空路径造成输入行抖动。
    /// </summary>
    private static string NormalizePromptDirectory(string? directory)
    {
        return string.IsNullOrWhiteSpace(directory)
            ? "~"
            : directory.Trim();
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
            FilePane?.ResetUnavailable("文件侧栏未连接");

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
