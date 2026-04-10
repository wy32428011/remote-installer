using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.Views;
using MaterialDesignThemes.Wpf;

namespace RemoteInstaller.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel
    /// 负责管理服务器列表、应用市场、任务列表和日志显示
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly SshService _sshService;
        private readonly DatabaseService _databaseService;
        private readonly ILogger _logger;
        private readonly AppConfigurationService? _appConfigurationService;
        private readonly ConfigurationService _configurationService;

        // 批量安装并发控制
        private SemaphoreSlim? _batchInstallSemaphore;
        private SemaphoreSlim? _batchUninstallSemaphore;
        private readonly object _semaphoreLock = new();

        // 批量操作取消控制
        private CancellationTokenSource? _batchInstallCts;
        private CancellationTokenSource? _batchUninstallCts;
        private CancellationTokenSource? _batchCheckCts;

        // 过滤防抖 Timer，避免频繁过滤导致 UI 卡顿
        private readonly System.Timers.Timer _filterDebounceTimer;
        private readonly object _filterLock = new();
        private const int FilterDebounceIntervalMs = 150;

        #region 属性

        [ObservableProperty]
        private ObservableCollection<HostViewModel> _hosts = new();

        [ObservableProperty]
        private HostViewModel? _selectedHost;

        /// <summary>
        /// 选中的服务器列表 (多选)
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<HostViewModel> _selectedHosts = new();

        /// <summary>
        /// 是否有选中的服务器
        /// </summary>
        [ObservableProperty]
        private bool _hasSelectedServers;

        /// <summary>
        /// 是否有服务器被选中 (用于启用批量操作按钮)
        /// </summary>
        public bool AreServersSelected => SelectedHosts?.Count > 0;

        /// <summary>
        /// 选中服务器数量显示文本
        /// </summary>
        public string SelectedServersCountDisplay => 
            SelectedHosts?.Count > 0 ? $"已选择 {SelectedHosts.Count} 台服务器" : string.Empty;

        [ObservableProperty]
        private ObservableCollection<TaskViewModel> _tasks = new();

        [ObservableProperty]
        private TaskViewModel? _selectedTask;

        [ObservableProperty]
        private ObservableCollection<LogViewModel> _logs = new();

        [ObservableProperty]
        private ObservableCollection<ApplicationCardViewModel> _applications = new();

        [ObservableProperty]
        private ObservableCollection<ApplicationCardViewModel> _filteredApplications = new();

        [ObservableProperty]
        private ObservableCollection<CustomAppViewModel> _customApps = new();

        [ObservableProperty]
        private ApplicationCardViewModel? _selectedApplication;

        [ObservableProperty]
        private string _serverSearchText = string.Empty;

        [ObservableProperty]
        private string _appSearchText = string.Empty;

        [ObservableProperty]
        private string _selectedGroup = "全部";

        [ObservableProperty]
        private ObservableCollection<string> _groupFilters = new() { "全部", "生产", "测试", "开发" };

        [ObservableProperty]
        private ObservableCollection<string> _appCategoryFilters = new() { "全部", "数据库", "中间件", "Web 服务", "自定义应用", "其他" };

        [ObservableProperty]
        private string _selectedAppCategory = "全部";

        [ObservableProperty]
        private int _selectedAppTabIndex;

        [ObservableProperty]
        private int _connectedCount;

        [ObservableProperty]
        private int _taskCount;

        [ObservableProperty]
        private string _repositoryUrl = "未配置";

        [ObservableProperty]
        private bool _isServerSelected;

        /// <summary>
        /// 是否正在刷新应用状态
        /// </summary>
        [ObservableProperty]
        private bool _isRefreshingAppStatus;

        /// <summary>
        /// 当前批量安装任务数量
        /// </summary>
        [ObservableProperty]
        private int _batchInstallCount;

        /// <summary>
        /// 是否正在批量安装
        /// </summary>
        [ObservableProperty]
        private bool _isBatchInstalling;

        /// <summary>
        /// 是否正在批量检测
        /// </summary>
        [ObservableProperty]
        private bool _isBatchChecking;

        /// <summary>
        /// 是否正在批量卸载
        /// </summary>
        [ObservableProperty]
        private bool _isBatchUninstalling;

        /// <summary>
        /// 是否有正在进行的批量任务
        /// </summary>
        public bool HasActiveBatchTask => IsBatchInstalling || IsBatchChecking || IsBatchUninstalling;

        /// <summary>
        /// 是否已选中应用
        /// </summary>
        public bool HasSelectedApplication => SelectedApplication != null;

        /// <summary>
        /// 是否已选中应用（批量）
        /// </summary>
        public bool HasSelectedApplications => Applications.Any(app => app.IsSelected);

        /// <summary>
        /// 当前批量操作目标应用的显示文本
        /// </summary>
        public string SelectedApplicationsDisplay
        {
            get
            {
                var selectedApps = Applications.Where(app => app.IsSelected).ToList();
                if (selectedApps.Count == 0)
                {
                    return "请选择一个或多个应用作为批量操作目标";
                }

                var appNames = string.Join("、", selectedApps.Select(a => a.Name).Take(3));
                return selectedApps.Count <= 3
                    ? $"已选应用：{appNames}"
                    : $"已选应用：{appNames} 等（共 {selectedApps.Count} 个）";
            }
        }

        /// <summary>
        /// 是否可以执行批量安装
        /// </summary>
        public bool CanRunBatchInstallAction => AreServersSelected && HasSelectedApplications && !HasActiveBatchTask;

        /// <summary>
        /// 是否可以执行批量卸载
        /// </summary>
        public bool CanRunBatchUninstallAction => AreServersSelected && HasSelectedApplications && !HasActiveBatchTask;

        /// <summary>
        /// 当前主题类型
        /// </summary>
        [ObservableProperty]
        private ThemeType _currentTheme = ThemeType.Dark;

        /// <summary>
        /// 脚本管理 ViewModel
        /// </summary>
        [ObservableProperty]
        private ScriptManagementViewModel? _scriptManagementViewModel;

        #endregion

        #region 计算属性

        /// <summary>
        /// 服务器信息文本显示
        /// </summary>
        public string ServerInfoText => SelectedHost != null 
            ? $"🖥️ {SelectedHost.Name} | {SelectedHost.IpAddress}:{SelectedHost.Port} | {SelectedHost.OsIcon} {SelectedHost.OsType}"
            : "请选择一台服务器以继续操作";

        /// <summary>
        /// 过滤后的服务器列表
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<HostViewModel> FilteredHosts { get; } = new();

        #endregion

        #region 属性变更通知

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(ServerSearchText) || e.PropertyName == nameof(SelectedGroup))
            {
                ApplyServerFilter();
            }
            else if (e.PropertyName == nameof(AppSearchText) || e.PropertyName == nameof(SelectedAppCategory))
            {
                ApplyAppFilter();
            }
            else if (e.PropertyName == nameof(SelectedHost))
            {
                IsServerSelected = SelectedHost != null;
                if (SelectedHost != null)
                {
                    // 切换主机时自动刷新应用状态
                    _ = CheckAllAppsStatusAsync(SelectedHost);
                }
            }
            else if (e.PropertyName == nameof(SelectedHosts))
            {
                UpdateSelectedServersStatus();
            }
        }

        /// <summary>
        /// 更新选中服务器状态
        /// </summary>
        private void UpdateSelectedServersStatus()
        {
            HasSelectedServers = SelectedHosts?.Count > 0;
            OnPropertyChanged(nameof(AreServersSelected));
            OnPropertyChanged(nameof(SelectedServersCountDisplay));
            NotifyBatchActionStateChanged();
        }

        private void SelectedHosts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateSelectedServersStatus();
        }

        partial void OnSelectedApplicationChanged(ApplicationCardViewModel? value)
        {
            NotifyBatchActionStateChanged();
        }

        partial void OnIsBatchInstallingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasActiveBatchTask));
            NotifyBatchActionStateChanged();
        }

        partial void OnIsBatchCheckingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasActiveBatchTask));
            NotifyBatchActionStateChanged();
        }

        partial void OnIsBatchUninstallingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasActiveBatchTask));
            NotifyBatchActionStateChanged();
        }

        private void NotifyBatchActionStateChanged()
        {
            OnPropertyChanged(nameof(HasSelectedApplication));
            OnPropertyChanged(nameof(HasSelectedApplications));
            OnPropertyChanged(nameof(SelectedApplicationsDisplay));
            OnPropertyChanged(nameof(CanRunBatchInstallAction));
            OnPropertyChanged(nameof(CanRunBatchUninstallAction));
            BatchInstallCommand.NotifyCanExecuteChanged();
            BatchUninstallCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedAppTabIndexChanged(int value)
        {
            // Tab 0 = 应用市场 (全部), Tab 1 = 自定义应用, Tab 2 = 脚本管理
            if (value == 1)
            {
                SelectedAppCategory = "自定义应用";
            }
            else if (value == 2)
            {
                // 初始化脚本管理 ViewModel
                InitializeScriptManagement();
            }
            AppSearchText = string.Empty; // 清空搜索框
        }

        private void InitializeScriptManagement()
        {
            if (ScriptManagementViewModel != null) return; // 已初始化

            if (SelectedHost == null)
            {
                MessageBox.Show("请先选择一台服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var remoteHost = GetHostFromViewModel(SelectedHost);
            if (remoteHost == null) return;

            var customApplicationService = new CustomApplicationService(_sshService, _configurationService);
            ScriptManagementViewModel = new ScriptManagementViewModel(customApplicationService, remoteHost);
        }

        #endregion

        #region 命令

        /// <summary>
        /// 添加主机命令
        /// </summary>
        [RelayCommand]
        private void AddHost()
        {
            try
            {
                var viewModel = new AddHostViewModel(_sshService, _databaseService, _logger, null);
                var dialog = new Views.AddHostDialog(viewModel);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                
                if (dialog.ShowDialog() == true)
                {
                    LoadHosts(); // 刷新列表
                    AddLog("添加主机成功", LogLevel.Success);
                }
            }
            catch (Exception ex)
            {
                AddLog($"AddHost 错误：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 测试连接命令
        /// </summary>
        [RelayCommand]
        private async Task TestConnection(HostViewModel? host = null)
        {
            var targetHost = host ?? SelectedHost;
            if (targetHost != null)
            {
                AddLog($"测试连接 {targetHost.Name}...", LogLevel.Info);
                await CheckAllAppsStatusAsync(targetHost);
            }
            else
            {
                AddLog("请先选择一台主机", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 查看历史记录命令
        /// </summary>
        [RelayCommand]
        private void ViewHistory()
        {
            try
            {
                var dialog = new Views.HistoryViewDialog(_databaseService);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddLog($"ViewHistory 错误：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 打开终端命令
        /// </summary>
        [RelayCommand]
        private void OpenTerminal()
        {
            if (SelectedHost == null)
            {
                MessageBox.Show("请先选择一台服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var remoteHost = GetHostFromViewModel(SelectedHost);
                if (remoteHost == null)
                {
                    MessageBox.Show("无法获取服务器信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var terminalViewModel = new TerminalViewModel(_sshService, SelectedHost, remoteHost);
                var dialog = new Views.Dialogs.TerminalDialog(terminalViewModel);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddLog($"OpenTerminal 错误：{ex.Message}", LogLevel.Error);
            }
        }

        [RelayCommand]
        private void OpenCustomAppDeploy(string? appId = null)
        {
            if (SelectedHost == null)
            {
                MessageBox.Show("请先选择一台服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var remoteHost = GetHostFromViewModel(SelectedHost);
                if (remoteHost == null)
                {
                    MessageBox.Show("无法获取服务器信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(appId))
                {
                    OpenGenericCustomAppDeployDialog(remoteHost, null);
                    return;
                }

                var app = CustomApps.FirstOrDefault(x => x.Id == appId);
                if (app == null)
                {
                    OpenGenericCustomAppDeployDialog(remoteHost, null);
                    return;
                }

                if (!app.IsBuiltIn)
                {
                    OpenSupportDeployDialog(remoteHost, app);
                    return;
                }

                switch (app.AppType.ToUpperInvariant())
                {
                    case "SUPPORT":
                        OpenSupportDeployDialog(remoteHost, app);
                        break;
                    case "GENERIC":
                        var remoteDir = string.IsNullOrWhiteSpace(app.RemoteDirectory) ? "/opt" : app.RemoteDirectory;
                        OpenGenericAppDeployDialog(
                            remoteHost,
                            app.Name,
                            remoteDir,
                            string.IsNullOrWhiteSpace(app.StartCommand) ? $"cd {remoteDir} && bash start.sh" : app.StartCommand,
                            string.IsNullOrWhiteSpace(app.StopCommand) ? $"cd {remoteDir} && bash stop.sh" : app.StopCommand,
                            app.ConfigFilePath ?? string.Empty);
                        break;
                    case "CUSTOM":
                        OpenGenericCustomAppDeployDialog(remoteHost, app);
                        break;
                    default:
                        OpenGenericCustomAppDeployDialog(remoteHost, app);
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog($"OpenCustomAppDeploy 错误：{ex.Message}", LogLevel.Error);
            }
        }

        private void OpenGenericCustomAppDeployDialog(RemoteHost remoteHost, CustomAppViewModel? app)
        {
            var customApplicationService = new CustomApplicationService(_sshService, _configurationService);
            var deployViewModel = new CustomAppDeployViewModel(
                customApplicationService,
                _configurationService,
                remoteHost,
                (message, level) => AddLog($"[Custom App] {message}", level));

            if (app != null)
            {
                deployViewModel.ApplicationName = app.Name;
                deployViewModel.RemoteDirectory = string.IsNullOrWhiteSpace(app.RemoteDirectory) ? deployViewModel.RemoteDirectory : app.RemoteDirectory;
                deployViewModel.StartCommand = app.StartCommand;
                deployViewModel.ConfigFilePath = app.ConfigFilePath;
            }

            var dialog = new Views.Dialogs.CustomAppDeployDialog(deployViewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            dialog.ShowDialog();
            RefreshCustomAppsStatusAfterDialog();
        }

        private void OpenGenericAppDeployDialog(RemoteHost remoteHost, string appName, string remoteDir, string startCmd, string stopCmd, string configFile)
        {
            var customApplicationService = new CustomApplicationService(_sshService, _configurationService);
            var deployViewModel = new GenericAppDeployViewModel(
                customApplicationService,
                _configurationService,
                remoteHost,
                appName,
                remoteDir,
                startCmd,
                stopCmd,
                (message, level) => AddLog($"[{appName}] {message}", level))
            {
                ConfigFilePath = configFile
            };

            var dialog = new Views.Dialogs.GenericAppDeployDialog(deployViewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            dialog.ShowDialog();
            RefreshCustomAppsStatusAfterDialog();
        }

        private void OpenSupportDeployDialog(RemoteHost remoteHost, CustomAppViewModel? app = null)
        {
            var customApplicationService = new CustomApplicationService(_sshService, _configurationService);
            var deployDefinition = app == null
                ? null
                : new CustomAppDefinition
                {
                    Id = app.Id,
                    AppKey = app.AppKey,
                    Name = app.Name,
                    Icon = app.Icon,
                    Description = app.Description,
                    AppType = app.AppType,
                    RemoteDirectory = app.RemoteDirectory,
                    StartCommand = app.StartCommand,
                    StopCommand = app.StopCommand,
                    ConfigFilePath = app.ConfigFilePath,
                    RemoteFrontendDirectory = app.RemoteFrontendDirectory,
                    PidFilePath = app.PidFilePath,
                    ConfigDirectory = app.ConfigDirectory,
                    ConfigFileName = app.ConfigFileName,
                    LogDirectory = app.LogDirectory,
                    IsBuiltIn = app.IsBuiltIn,
                    SortOrder = app.SortOrder,
                    IsEnabled = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

            var logTag = string.IsNullOrWhiteSpace(app?.Name) ? "SUPPORT" : app.Name;
            var deployViewModel = new SupportDeployViewModel(
                customApplicationService,
                _configurationService,
                remoteHost,
                deployDefinition,
                (message, level) => AddLog($"[{logTag}] {message}", level));

            var dialog = new Views.Dialogs.SupportDeployDialog(deployViewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            dialog.ShowDialog();
            RefreshCustomAppsStatusAfterDialog();
        }

        private void RefreshCustomAppsStatusAfterDialog()
        {
            if (SelectedHost == null)
            {
                return;
            }

            _ = CheckAllAppsStatusAsync(SelectedHost);
        }

        /// <summary>
        /// 系统设置命令
        /// </summary>
        [RelayCommand]
        private void Settings()
        {
            try
            {
                var settingsViewModel = new SettingsViewModel(_databaseService, _logger);
                var dialog = new Views.SettingsDialog(settingsViewModel);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }

                // 订阅主题变更事件
                settingsViewModel.ThemeChanged += (newTheme) =>
                {
                    CurrentTheme = newTheme;
                    ApplyTheme(newTheme);
                };

                var result = dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddLog($"Settings 错误：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 打开日志查看器命令
        /// </summary>
        [RelayCommand]
        private void OpenLogViewer()
        {
            try
            {
                var dialog = new Views.Dialogs.LogViewerDialog();
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AddLog($"打开日志查看器错误：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 编辑主机命令
        /// </summary>
        [RelayCommand]
        private void EditHost(HostViewModel host)
        {
            if (host == null) return;

            try
            {
                var remoteHost = _databaseService.GetAllHosts().FirstOrDefault(h => h.Id == host.Id);
                if (remoteHost == null)
                {
                    AddLog("找不到主机信息", LogLevel.Warning);
                    return;
                }

                var viewModel = new AddHostViewModel(_sshService, _databaseService, _logger, remoteHost);
                var dialog = new Views.AddHostDialog(viewModel);
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                dialog.Title = "✏️ 编辑主机";
                
                if (dialog.ShowDialog() == true)
                {
                    LoadHosts();
                    AddLog("更新主机成功", LogLevel.Success);
                }
            }
            catch (Exception ex)
            {
                AddLog($"EditHost 错误：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 删除主机命令
        /// </summary>
        [RelayCommand]
        private void DeleteHost(HostViewModel host)
        {
            if (host == null) return;

            var result = MessageBox.Show(
                $"确定要删除主机 \"{host.Name}\" 吗？", 
                "确认删除", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 从数据库删除
                    var remoteHost = GetHostFromViewModel(host);
                    if (remoteHost != null)
                    {
                        _databaseService.DeleteHost(remoteHost.Id);
                        AddLog($"已从数据库删除主机：{host.Name}", LogLevel.Info);
                    }
                    
                    // 从 UI 列表删除
                    Hosts.Remove(host);
                    SelectedHosts.Remove(host);
                    ConnectedCount = Hosts.Count(h => h.IsOnline);
                    ApplyServerFilter();
                    
                    AddLog($"已删除主机：{host.Name}", LogLevel.Info);
                    MessageBox.Show($"主机 {host.Name} 已删除", "提示", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AddLog($"删除失败：{ex.Message}", LogLevel.Error);
                    MessageBox.Show($"删除失败：{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 批量检测状态命令
        /// </summary>
        [RelayCommand]
        private async Task BatchCheckStatus()
        {
            if (!AreServersSelected)
            {
                MessageBox.Show("请先选择要检测的服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 取消之前的批量检测
            _batchCheckCts?.Cancel();
            _batchCheckCts = new CancellationTokenSource();

            IsBatchChecking = true;
            AddLog($"开始批量检测 {SelectedHosts.Count} 台服务器的应用状态...", LogLevel.Info);

            try
            {
                var token = _batchCheckCts.Token;
                var tasks = SelectedHosts.Select(host => CheckAllAppsStatusAsync(host, token)).ToList();
                await Task.WhenAll(tasks);
                AddLog($"批量检测完成", LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                AddLog($"批量检测已取消", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                AddLog($"批量检测异常：{ex.Message}", LogLevel.Error);
            }
            finally
            {
                IsBatchChecking = false;
            }
        }

        /// <summary>
        /// 取消批量检测命令
        /// </summary>
        [RelayCommand]
        private void CancelBatchCheck()
        {
            if (IsBatchChecking && _batchCheckCts != null)
            {
                _batchCheckCts.Cancel();
                AddLog("正在取消批量检测...", LogLevel.Info);
            }
        }

        /// <summary>
        /// 批量安装命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRunBatchInstallAction))]
        private async Task BatchInstall()
        {
            if (!AreServersSelected)
            {
                MessageBox.Show("请先选择要安装应用的服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_applications.Count == 0)
            {
                MessageBox.Show("应用市场为空，请先添加应用", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedApps = Applications.Where(app => app.IsSelected).ToList();
            if (selectedApps.Count == 0)
            {
                MessageBox.Show("请先选择一个或多个应用进行安装", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 取消之前的批量安装
            _batchInstallCts?.Cancel();
            _batchInstallCts = new CancellationTokenSource();

            var totalTasks = selectedApps.Count * SelectedHosts.Count;
            AddLog($"开始批量安装 {selectedApps.Count} 个应用到 {SelectedHosts.Count} 台服务器（每台主机内按应用顺序串行），共 {totalTasks} 个任务...", LogLevel.Info);
            IsBatchInstalling = true;

            var completedCount = 0;
            var successCount = 0;
            var failedCount = 0;
            var canceledCount = 0;

            // 初始化信号量 (根据设置中的并发数)
            var maxConcurrentTasks = int.TryParse(_databaseService.GetSetting("MaxConcurrentTasks", "3"), out var val) ? val : 3;
            _batchInstallSemaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);

            try
            {
                var token = _batchInstallCts.Token;

                var tasks = SelectedHosts.Select(async host =>
                {
                    await _batchInstallSemaphore!.WaitAsync(token);
                    try
                    {
                        foreach (var app in selectedApps)
                        {
                            token.ThrowIfCancellationRequested();

                            var result = await InstallApplicationToHost(app, host, token);
                            Interlocked.Increment(ref completedCount);

                            switch (result)
                            {
                                case BatchTaskResult.Success:
                                    Interlocked.Increment(ref successCount);
                                    break;
                                case BatchTaskResult.Failed:
                                    Interlocked.Increment(ref failedCount);
                                    break;
                                case BatchTaskResult.Canceled:
                                    Interlocked.Increment(ref canceledCount);
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        _batchInstallSemaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);

                AddLog($"批量安装完成，总任务 {totalTasks}，成功 {successCount}，失败 {failedCount}，取消 {canceledCount}", LogLevel.Success);

                if (SelectedHost != null && SelectedHosts.Contains(SelectedHost))
                {
                    await CheckAllAppsStatusAsync(SelectedHost);
                }
            }
            catch (OperationCanceledException)
            {
                AddLog($"批量安装已取消，已完成 {completedCount}/{totalTasks}，成功 {successCount}，失败 {failedCount}，取消 {canceledCount}", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                AddLog($"批量安装异常：{ex.Message}", LogLevel.Error);
            }
            finally
            {
                IsBatchInstalling = false;
                _batchInstallSemaphore?.Dispose();
                _batchInstallCts?.Dispose();
                _batchInstallCts = null;
            }
        }

        /// <summary>
        /// 取消批量安装命令
        /// </summary>
        [RelayCommand]
        private void CancelBatchInstall()
        {
            if (IsBatchInstalling && _batchInstallCts != null)
            {
                _batchInstallCts.Cancel();
                AddLog("正在取消批量安装...", LogLevel.Info);
            }
        }

        /// <summary>
        /// 批量卸载命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRunBatchUninstallAction))]
        private async Task BatchUninstall()
        {
            if (!AreServersSelected)
            {
                MessageBox.Show("请先选择要卸载应用的服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedApps = Applications.Where(app => app.IsSelected).ToList();
            if (selectedApps.Count == 0)
            {
                MessageBox.Show("请先选择一个或多个应用进行卸载", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalTasks = selectedApps.Count * SelectedHosts.Count;
            var appSummary = selectedApps.Count == 1 ? selectedApps[0].Name : $"{selectedApps.Count} 个应用";
            var result = MessageBox.Show(
                $"确定要在 {SelectedHosts.Count} 台服务器上卸载 {appSummary} 吗？共 {totalTasks} 个任务。",
                "确认批量卸载",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // 取消之前的批量卸载
            _batchUninstallCts?.Cancel();
            _batchUninstallCts = new CancellationTokenSource();

            AddLog($"开始批量卸载 {selectedApps.Count} 个应用（每台主机内按应用顺序串行），共 {totalTasks} 个任务...", LogLevel.Info);
            IsBatchUninstalling = true;

            var completedCount = 0;
            var successCount = 0;
            var failedCount = 0;
            var canceledCount = 0;

            // 初始化信号量 (根据设置中的并发数)
            var maxConcurrentTasks = int.TryParse(_databaseService.GetSetting("MaxConcurrentTasks", "3"), out var val) ? val : 3;
            _batchUninstallSemaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);

            try
            {
                var token = _batchUninstallCts.Token;

                var tasks = SelectedHosts.Select(async host =>
                {
                    await _batchUninstallSemaphore!.WaitAsync(token);
                    try
                    {
                        foreach (var app in selectedApps)
                        {
                            token.ThrowIfCancellationRequested();

                            var uninstallResult = await UninstallApplicationFromHost(app, host, token);
                            Interlocked.Increment(ref completedCount);

                            switch (uninstallResult)
                            {
                                case BatchTaskResult.Success:
                                    Interlocked.Increment(ref successCount);
                                    break;
                                case BatchTaskResult.Failed:
                                    Interlocked.Increment(ref failedCount);
                                    break;
                                case BatchTaskResult.Canceled:
                                    Interlocked.Increment(ref canceledCount);
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        _batchUninstallSemaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
                AddLog($"批量卸载完成，总任务 {totalTasks}，成功 {successCount}，失败 {failedCount}，取消 {canceledCount}", LogLevel.Success);

                if (SelectedHost != null && SelectedHosts.Contains(SelectedHost))
                {
                    await CheckAllAppsStatusAsync(SelectedHost);
                }
            }
            catch (OperationCanceledException)
            {
                AddLog($"批量卸载已取消，已完成 {completedCount}/{totalTasks}，成功 {successCount}，失败 {failedCount}，取消 {canceledCount}", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                AddLog($"批量卸载异常：{ex.Message}", LogLevel.Error);
            }
            finally
            {
                IsBatchUninstalling = false;
                _batchUninstallSemaphore?.Dispose();
                _batchUninstallCts?.Dispose();
                _batchUninstallCts = null;
            }
        }

        /// <summary>
        /// 取消批量卸载命令
        /// </summary>
        [RelayCommand]
        private void CancelBatchUninstall()
        {
            if (IsBatchUninstalling && _batchUninstallCts != null)
            {
                _batchUninstallCts.Cancel();
                AddLog("正在取消批量卸载...", LogLevel.Info);
            }
        }

        /// <summary>
        /// 切换主题命令
        /// </summary>
        [RelayCommand]
        private void ToggleTheme()
        {
            var newTheme = CurrentTheme == ThemeType.Dark ? ThemeType.Light : ThemeType.Dark;
            CurrentTheme = newTheme;
            ApplyTheme(newTheme);
            
            // 保存到数据库
            _databaseService.SaveSetting("CurrentTheme", ((int)newTheme).ToString());
            AddLog($"已切换到{(newTheme == ThemeType.Dark ? "深色" : "浅色")}主题", LogLevel.Info);
        }

        /// <summary>
        /// 刷新命令
        /// </summary>
        [RelayCommand]
        private async Task Refresh()
        {
            if (SelectedHost != null)
            {
                await CheckAllAppsStatusAsync(SelectedHost);
            }
            else
            {
                LoadHosts();
                AddLog("服务器列表已刷新", LogLevel.Info);
            }
        }

        /// <summary>
        /// 导出日志命令
        /// 将当前日志列表导出为文本文件
        /// </summary>
        [RelayCommand]
        private void ExportLogs()
        {
            try
            {
                if (Logs == null || Logs.Count == 0)
                {
                    MessageBox.Show("没有可导出的日志", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出日志",
                    Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FilterIndex = 1,
                    DefaultExt = ".log",
                    FileName = $"RemoteInstaller_Logs_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var lines = new List<string>
                    {
                        $"RemoteInstaller 日志导出",
                        $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"日志数量：{Logs.Count}",
                        new string('=', 60),
                        ""
                    };

                    foreach (var log in Logs)
                    {
                        lines.Add($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {log.Message}");
                    }

                    lines.Add("");
                    lines.Add(new string('=', 60));
                    lines.Add($"导出完成");

                    System.IO.File.WriteAllLines(saveDialog.FileName, lines, System.Text.Encoding.UTF8);

                    AddLog($"日志已导出到：{saveDialog.FileName}", LogLevel.Success);
                    MessageBox.Show(
                        $"日志导出成功！\n\n文件：{saveDialog.FileName}\n日志条数：{Logs.Count}",
                        "导出成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog($"导出日志失败：{ex.Message}", LogLevel.Error);
                MessageBox.Show(
                    $"导出日志失败！\n\n错误：{ex.Message}",
                    "导出失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导出任务日志命令
        /// 从数据库导出指定任务的完整日志
        /// </summary>
        [RelayCommand]
        private void ExportTaskLogs(TaskViewModel? task)
        {
            if (task == null)
            {
                MessageBox.Show("请先选择一个任务", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出任务日志",
                    Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FilterIndex = 1,
                    DefaultExt = ".log",
                    FileName = $"Task_{task.AppName}_{task.HostName}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var lines = new List<string>
                    {
                        "RemoteInstaller 任务日志",
                        "=".PadRight(60, '='),
                        "任务：" + task.AppName,
                        "主机：" + task.HostName,
                        "进度：" + task.Progress.ToString("F0") + "%",
                        "状态：" + task.CurrentStage,
                        "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        new string('=', 60),
                        ""
                    };

                    // 尝试从数据库获取任务日志
                    try
                    {
                        var logQueryService = new LogQueryService(_databaseService);
                        var dbLogs = logQueryService.GetTaskLogs(task.AppName + "_" + task.HostName);
                        if (dbLogs != null && dbLogs.Count > 0)
                        {
                            foreach (var log in dbLogs.OrderBy(l => l.Timestamp))
                            {
                                lines.Add("[" + log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + log.Level + "] " + log.Message);
                            }
                        }
                        else
                        {
                            lines.Add("（无数据库日志记录）");
                        }
                    }
                    catch
                    {
                        lines.Add("（无法从数据库获取日志）");
                    }

                    lines.Add("");
                    lines.Add(new string('=', 60));
                    lines.Add("导出完成");

                    System.IO.File.WriteAllLines(saveDialog.FileName, lines, System.Text.Encoding.UTF8);

                    AddLog("任务日志已导出到：" + saveDialog.FileName, LogLevel.Success);
                    MessageBox.Show(
                        "任务日志导出成功！\n\n文件：" + saveDialog.FileName,
                        "导出成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog("导出任务日志失败：" + ex.Message, LogLevel.Error);
                MessageBox.Show(
                    "导出任务日志失败！\n\n错误：" + ex.Message,
                    "导出失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region 过滤方法

        /// <summary>
        /// 应用服务器过滤（核心逻辑，无锁）
        /// </summary>
        private void ApplyServerFilterCore()
        {
            FilteredHosts.Clear();

            var filtered = Hosts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(ServerSearchText))
            {
                var searchText = ServerSearchText.ToLower();
                filtered = filtered.Where(h =>
                    h.Name.ToLower().Contains(searchText) ||
                    h.IpAddress.ToLower().Contains(searchText) ||
                    h.Username.ToLower().Contains(searchText));
            }

            if (!string.IsNullOrWhiteSpace(SelectedGroup) && SelectedGroup != "全部")
            {
                filtered = filtered.Where(h => h.GroupName == SelectedGroup);
            }

            foreach (var host in filtered)
            {
                FilteredHosts.Add(host);
            }

            OnPropertyChanged(nameof(FilteredHosts));
        }

        /// <summary>
        /// 应用服务器过滤（带防抖）
        /// </summary>
        private void ApplyServerFilter()
        {
            lock (_filterLock)
            {
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
            }
        }

        /// <summary>
        /// 应用应用市场过滤（核心逻辑，无锁）
        /// </summary>
        private void ApplyAppFilterCore()
        {
            FilteredApplications.Clear();

            var filtered = Applications.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(AppSearchText))
            {
                var searchText = AppSearchText.ToLower();
                filtered = filtered.Where(a =>
                    a.Name.ToLower().Contains(searchText) ||
                    a.Description.ToLower().Contains(searchText) ||
                    a.Version.ToLower().Contains(searchText));
            }

            foreach (var app in filtered)
            {
                FilteredApplications.Add(app);
            }

            OnPropertyChanged(nameof(FilteredApplications));
        }

        /// <summary>
        /// 应用应用市场过滤（带防抖）
        /// </summary>
        private void ApplyAppFilter()
        {
            lock (_filterLock)
            {
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
            }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 构造函数
        /// </summary>
        public MainViewModel(SshService sshService, DatabaseService databaseService, ILogger logger, ConfigurationService configurationService, AppConfigurationService? appConfigurationService = null)
        {
            _sshService = sshService;
            _databaseService = databaseService;
            _logger = logger;
            _configurationService = configurationService;
            _appConfigurationService = appConfigurationService;

            // 初始化过滤防抖 Timer
            _filterDebounceTimer = new System.Timers.Timer(FilterDebounceIntervalMs);
            _filterDebounceTimer.AutoReset = false;
            _filterDebounceTimer.Elapsed += (s, e) =>
            {
                _filterDebounceTimer.Stop();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    lock (_filterLock)
                    {
                        ApplyServerFilterCore();
                        ApplyAppFilterCore();
                    }
                });
            };

            Tasks.CollectionChanged += (s, e) => TaskCount = Tasks.Count;
            SelectedHosts.CollectionChanged += SelectedHosts_CollectionChanged;

            LoadSettings();
            LoadHosts();
            InitializeSampleData();
            ApplyServerFilter();
            ApplyAppFilter();

            LoadCustomApps();
        }

        public void UpdateSelectedHostsFromView(IEnumerable<HostViewModel> selectedHosts)
        {
            var selectedHostList = selectedHosts.Distinct().ToList();

            SelectedHosts.Clear();
            foreach (var host in selectedHostList)
            {
                SelectedHosts.Add(host);
            }

            UpdateSelectedServersStatus();
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        private void LoadSettings()
        {
            // 加载主题设置
            var themeValue = _databaseService.GetSetting("CurrentTheme", "0");
            if (int.TryParse(themeValue, out var themeInt))
            {
                CurrentTheme = (ThemeType)themeInt;
            }
            ApplyTheme(CurrentTheme);

            // 加载仓库地址
            RepositoryUrl = _databaseService.GetSetting("RepositoryUrl", "未配置");
        }

        /// <summary>
        /// 应用主题
        /// 完整主题切换逻辑：更新所有控件样式、MaterialDesign 主题和窗口背景
        /// 带平滑过渡动画效果
        /// </summary>
        private void ApplyTheme(ThemeType theme)
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    // 创建淡出动画
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new System.TimeSpan(0, 0, 0, 0, 150));

                    fadeOut.Completed += (s, e) =>
                    {
                        // 淡出完成后切换主题
                        App.SwitchTheme(theme);

                        // 设置主题对应的背景色
                        if (theme == ThemeType.Dark)
                        {
                            mainWindow.Background = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(15, 15, 19));
                        }
                        else
                        {
                            mainWindow.Background = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(249, 250, 251));
                        }

                        // 强制刷新窗口视觉
                        mainWindow.InvalidateVisual();
                        mainWindow.UpdateLayout();

                        // 创建淡入动画
                        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new System.TimeSpan(0, 0, 0, 0, 200));
                        mainWindow.BeginAnimation(System.Windows.Window.OpacityProperty, fadeIn);
                    };

                    // 开始淡出动画
                    mainWindow.BeginAnimation(System.Windows.Window.OpacityProperty, fadeOut);
                }
                else
                {
                    // 没有窗口时直接切换
                    App.SwitchTheme(theme);
                }

                AddLog($"已切换到{(theme == ThemeType.Dark ? "深色" : "浅色")}主题", LogLevel.Success);
            }
            catch (Exception ex)
            {
                AddLog($"主题切换失败：{ex.Message}", LogLevel.Error);
            }
        }

        private void LoadCustomApps()
        {
            try
            {
                var apps = _databaseService.GetAllCustomApps();
                CustomApps.Clear();

                foreach (var app in apps)
                {
                    CustomApps.Add(new CustomAppViewModel(
                        app.Id,
                        app.AppKey,
                        app.Name,
                        app.Icon,
                        app.Description,
                        app.AppType,
                        app.RemoteDirectory,
                        app.StartCommand,
                        app.StopCommand,
                        app.ConfigFilePath,
                        app.RemoteFrontendDirectory,
                        app.PidFilePath,
                        app.ConfigDirectory,
                        app.ConfigFileName,
                        app.LogDirectory,
                        app.IsBuiltIn,
                        app.SortOrder,
                        OpenCustomAppDeployCommand));
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载自定义应用失败：{ex.Message}", LogLevel.Error);
            }
        }

        [RelayCommand]
        private void AddCustomApp()
        {
            var viewModel = new CustomAppEditorViewModel();
            var dialog = new Views.Dialogs.CustomAppEditorDialog(viewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            if (dialog.ShowDialog() == true)
            {
                var app = viewModel.ToDefinition();
                app.Id = Guid.NewGuid().ToString("N");
                app.AppKey = app.Id;
                app.IsBuiltIn = false;
                app.SortOrder = (CustomApps.Count > 0 ? CustomApps.Max(x => x.SortOrder) : 0) + 10;
                _databaseService.SaveCustomApp(app);
                LoadCustomApps();
                AddLog($"已添加自定义应用：{app.Name}", LogLevel.Success);
            }
        }

        [RelayCommand]
        private void EditCustomApp(CustomAppViewModel? app)
        {
            if (app == null) return;

            var model = new CustomAppDefinition
            {
                Id = app.Id,
                AppKey = app.AppKey,
                Name = app.Name,
                Icon = app.Icon,
                Description = app.Description,
                AppType = app.AppType,
                RemoteDirectory = app.RemoteDirectory,
                StartCommand = app.StartCommand,
                StopCommand = app.StopCommand,
                ConfigFilePath = app.ConfigFilePath,
                RemoteFrontendDirectory = app.RemoteFrontendDirectory,
                PidFilePath = app.PidFilePath,
                ConfigDirectory = app.ConfigDirectory,
                ConfigFileName = app.ConfigFileName,
                LogDirectory = app.LogDirectory,
                IsBuiltIn = app.IsBuiltIn,
                IsEnabled = true,
                SortOrder = app.SortOrder,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            var viewModel = new CustomAppEditorViewModel(model);
            var dialog = new Views.Dialogs.CustomAppEditorDialog(viewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            if (dialog.ShowDialog() == true)
            {
                var updated = viewModel.ToDefinition();
                updated.Id = app.Id;
                updated.AppKey = app.AppKey;
                updated.IsBuiltIn = app.IsBuiltIn;
                updated.SortOrder = app.SortOrder;
                _databaseService.SaveCustomApp(updated);
                LoadCustomApps();
                AddLog($"已更新自定义应用：{updated.Name}", LogLevel.Success);
            }
        }

        [RelayCommand]
        private void DeleteCustomApp(CustomAppViewModel? app)
        {
            if (app == null) return;
            if (app.IsBuiltIn)
            {
                MessageBox.Show("内置应用不允许删除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除应用 \"{app.Name}\" 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _databaseService.DeleteCustomApp(app.Id);
            LoadCustomApps();
            AddLog($"已删除自定义应用：{app.Name}", LogLevel.Info);
        }

        /// <summary>
        /// 加载主机列表
        /// </summary>
        private void LoadHosts()
        {
            try
            {
                var hosts = _databaseService.GetAllHosts();
                Hosts.Clear();

                foreach (var host in hosts)
                {
                    Hosts.Add(new HostViewModel
                    {
                        Id = host.Id,
                        Name = host.Name,
                        IpAddress = host.IpAddress,
                        Port = host.Port.ToString(),
                        Username = host.Username,
                        OsType = host.OsType == OperatingSystemType.Ubuntu ? "Linux" : "Windows",
                        IsOnline = host.Status == HostStatus.Online
                    });
                }

                ConnectedCount = Hosts.Count(h => h.IsOnline);
                AddLog($"已加载 {hosts.Count} 台主机", LogLevel.Info);
                
                // 应用过滤器
                ApplyServerFilter();
            }
            catch (Exception ex)
            {
                AddLog($"加载主机失败：{ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 保存主机
        /// </summary>
        public void SaveHost(HostViewModel hostViewModel)
        {
            try
            {
                var host = new RemoteHost
                {
                    Name = hostViewModel.Name,
                    IpAddress = hostViewModel.IpAddress,
                    Port = int.TryParse(hostViewModel.Port, out var port) ? port : 22,
                    Username = hostViewModel.Username,
                    OsType = hostViewModel.OsType.Contains("Linux") ? OperatingSystemType.Ubuntu : OperatingSystemType.Windows,
                    Status = hostViewModel.IsOnline ? HostStatus.Online : HostStatus.Offline
                };

                _databaseService.SaveHost(host);
                LoadHosts();
                AddLog($"已保存主机：{hostViewModel.Name}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                AddLog($"保存主机失败：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 初始化示例数据
        /// </summary>
        private void InitializeSampleData()
        {
            // 优先从 AppConfigurationService 加载应用
            LoadApplicationsFromConfig();

            AddLog("系统启动成功", LogLevel.Success);
            AddLog("连接到应用市场...", LogLevel.Info);
            AddLog($"加载 {Applications.Count} 个应用", LogLevel.Info);
            AddLog("就绪", LogLevel.Success);
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        private void AddLog(string message, LogLevel level)
        {
            Logs.Add(new LogViewModel { Message = message, Level = level, Timestamp = DateTime.Now });
            if (Logs.Count > 100)
            {
                Logs.RemoveAt(0);
            }
        }

        /// <summary>
        /// 检测所有应用状态 - 优化版
        /// </summary>
        private async Task CheckAllAppsStatusAsync(HostViewModel hostViewModel, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? cts = null;
            try
            {
                var host = GetHostFromViewModel(hostViewModel);
                if (host == null) return;

                // 如果传入了外部 token，创建链接的 token source
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                IsRefreshingAppStatus = true;
                AddLog($"正在检测 {host.Name} 上的应用状态...", LogLevel.Info);

                var installerService = new InstallerService(_sshService, _logger);

                // 先尝试连接，更新在线状态
                try
                {
                    await _sshService.ConnectAsync(host, cts.Token);
                    hostViewModel.IsOnline = true;
                }
                catch (Exception ex)
                {
                    hostViewModel.IsOnline = false;
                    AddLog($"{host.Name} 连接失败: {ex.Message}", LogLevel.Error);
                    return;
                }

                // 获取要检测的应用列表
                var appsToCheck = Applications.ToList();
                var customAppsToCheck = CustomApps.ToList();
                var results = new Dictionary<string, (bool IsInstalled, bool IsRunning, string Version)>();
                var customResults = new Dictionary<string, (bool IsInstalled, bool IsRunning, string StatusText)>();
                var token = cts.Token;

                // 并行检测应用状态（限制并发数为3）
                var semaphore = new SemaphoreSlim(3);
                var tasks = appsToCheck.Select(async appCard =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var appInfo = GetApplicationInfo(appCard.Id);
                        if (appInfo == null) return;

                        var status = await installerService.CheckStatusAsync(host, appInfo, token);
                        lock (results)
                        {
                            results[appCard.Id] = (status.IsInstalled, status.IsRunning, status.InstalledVersion ?? "未知");
                        }
                    }
                    catch
                    {
                        // 检测失败时记录为未安装
                        lock (results)
                        {
                            results[appCard.Id] = (false, false, "未知");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var customTasks = customAppsToCheck.Select(async customApp =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var customStatus = await CheckSingleCustomAppStatusAsync(host, customApp, token);
                        lock (customResults)
                        {
                            customResults[customApp.Id] = customStatus;
                        }
                    }
                    catch
                    {
                        lock (customResults)
                        {
                            customResults[customApp.Id] = (false, false, "检测失败");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks.Concat(customTasks));

                // 批量更新 UI（所有结果一次性更新，避免频繁 Dispatcher 调用）
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    hostViewModel.IsOnline = true;
                    foreach (var appCard in appsToCheck)
                    {
                        if (results.TryGetValue(appCard.Id, out var result))
                        {
                            appCard.IsInstalled = result.IsInstalled;
                            appCard.IsRunning = result.IsRunning;
                            appCard.InstalledVersion = result.Version;
                        }
                    }

                    foreach (var customApp in customAppsToCheck)
                    {
                        if (customResults.TryGetValue(customApp.Id, out var customResult))
                        {
                            customApp.IsInstalled = customResult.IsInstalled;
                            customApp.IsRunning = customResult.IsRunning;
                            customApp.StatusText = customResult.StatusText;
                        }
                    }
                });

                AddLog($"{host.Name} 应用状态检测完成", LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                AddLog("检测已取消", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                AddLog($"检测失败：{ex.Message}", LogLevel.Error);
            }
            finally
            {
                IsRefreshingAppStatus = false;
                cts?.Dispose();
            }
        }

        /// <summary>
        /// 安装应用到指定主机
        /// </summary>
        private async Task<(bool IsInstalled, bool IsRunning, string StatusText)> CheckSingleCustomAppStatusAsync(RemoteHost host, CustomAppViewModel app, CancellationToken cancellationToken)
        {
            try
            {
                var isInstalled = await DirectoryExistsAsync(host, app.RemoteDirectory, cancellationToken);
                var isRunning = false;

                if (isInstalled)
                {
                    isRunning = await IsRunningByPidFileAsync(host, app.PidFilePath, cancellationToken);
                }

                var statusText = isRunning
                    ? "运行中"
                    : isInstalled
                        ? "未运行"
                        : "未部署";

                return (isInstalled, isRunning, statusText);
            }
            catch
            {
                return (false, false, "检测失败");
            }
        }

        private async Task<bool> DirectoryExistsAsync(RemoteHost host, string remoteDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(remoteDirectory))
            {
                return false;
            }

            var normalizedPath = remoteDirectory.Trim().Replace("\\", "/");
            var safePath = normalizedPath.Replace("'", "'\\''");
            var command = host.OsType == OperatingSystemType.Windows
                ? $"if exist \"{normalizedPath}\" (echo true) else (echo false)"
                : $"if [ -d '{safePath}' ]; then echo true; else echo false; fi";

            var output = await _sshService.ExecuteCommandAsync(command, cancellationToken: cancellationToken, throwOnError: false);
            return output.Trim().Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> IsRunningByPidFileAsync(RemoteHost host, string pidFilePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pidFilePath))
            {
                return false;
            }

            if (host.OsType == OperatingSystemType.Windows)
            {
                return false;
            }

            var normalizedPath = pidFilePath.Trim().Replace("\\", "/");
            var safePath = normalizedPath.Replace("'", "'\\''");
            var command = $"if [ -f '{safePath}' ]; then pid=$(cat '{safePath}' 2>/dev/null); if [ -n \"$pid\" ] && ps -p $pid > /dev/null 2>&1; then echo true; else echo false; fi; else echo false; fi";

            var output = await _sshService.ExecuteCommandAsync(command, cancellationToken: cancellationToken, throwOnError: false);
            return output.Trim().Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<BatchTaskResult> InstallApplicationToHost(ApplicationCardViewModel app, HostViewModel hostViewModel, CancellationToken cancellationToken = default)
        {
            try
            {
                var host = GetHostFromViewModel(hostViewModel);
                var appInfo = GetApplicationInfo(app.Id);

                if (host == null || appInfo == null)
                {
                    AddLog($"无法找到主机或应用信息: {hostViewModel.Name} / {app.Name}", LogLevel.Error);
                    return BatchTaskResult.Failed;
                }

                // 创建任务项
                var taskViewModel = new TaskViewModel
                {
                    ApplicationName = app.Name,
                    HostName = host.Name,
                    Progress = 0,
                    CurrentStage = "等待中"
                };

                Application.Current.Dispatcher.Invoke(() => Tasks.Add(taskViewModel));

                var progress = new Progress<InstallTask>(t =>
                {
                    taskViewModel.Progress = t.Progress;
                    taskViewModel.CurrentStage = t.StageDisplayText;
                });

                var installerService = new InstallerService(_sshService, _logger);
                // 批量安装默认使用基础配置
                var parameters = new Dictionary<string, string>
                {
                    ["version"] = appInfo.Version
                };

                AddLog($"正在开始批量任务：{app.Name} -> {host.Name}", LogLevel.Info);
                var result = await installerService.InstallAsync(
                    host,
                    appInfo,
                    parameters,
                    progressReporter: progress,
                    cancellationToken: cancellationToken);

                if (result.Status == Models.TaskStatus.Completed)
                {
                    AddLog($"批量任务成功：{app.Name} -> {host.Name}", LogLevel.Success);
                    return BatchTaskResult.Success;
                }

                if (result.Status == Models.TaskStatus.Cancelled)
                {
                    AddLog($"批量任务取消：{app.Name} -> {host.Name}", LogLevel.Warning);
                    return BatchTaskResult.Canceled;
                }

                AddLog($"批量任务失败：{app.Name} -> {host.Name} - {result.ErrorMessage}", LogLevel.Error);
                return BatchTaskResult.Failed;
            }
            catch (OperationCanceledException)
            {
                AddLog($"安装 {app.Name} 到 {hostViewModel.Name} 已取消", LogLevel.Warning);
                return BatchTaskResult.Canceled;
            }
            catch (Exception ex)
            {
                AddLog($"安装 {app.Name} 到 {hostViewModel.Name} 失败：{ex.Message}", LogLevel.Error);
                return BatchTaskResult.Failed;
            }
        }

        /// <summary>
        /// 从指定主机卸载应用
        /// </summary>
        private async Task<BatchTaskResult> UninstallApplicationFromHost(ApplicationCardViewModel app, HostViewModel hostViewModel, CancellationToken cancellationToken = default)
        {
            try
            {
                var host = GetHostFromViewModel(hostViewModel);
                if (host == null)
                {
                    return BatchTaskResult.Failed;
                }

                var installerService = new InstallerService(_sshService, _logger);
                var appInfo = GetApplicationInfo(app.Id);
                if (appInfo == null)
                {
                    AddLog($"无法找到应用信息：{app.Name}", LogLevel.Error);
                    return BatchTaskResult.Failed;
                }

                AddLog($"正在从 {host.Name} 卸载 {app.Name}...", LogLevel.Info);

                var taskViewModel = new TaskViewModel
                {
                    ApplicationName = app.Name,
                    HostName = host.Name,
                    Progress = 0,
                    CurrentStage = "卸载中"
                };
                Application.Current.Dispatcher.Invoke(() => Tasks.Add(taskViewModel));

                var progress = new Progress<InstallTask>(t =>
                {
                    taskViewModel.Progress = t.Progress;
                    taskViewModel.CurrentStage = t.StageDisplayText;
                });

                var result = await installerService.UninstallAsync(host, appInfo, false, progress, cancellationToken);
                if (result.Status == Models.TaskStatus.Completed)
                {
                    AddLog($"成功从 {host.Name} 卸载 {app.Name}", LogLevel.Success);
                    return BatchTaskResult.Success;
                }

                if (result.Status == Models.TaskStatus.Cancelled)
                {
                    AddLog($"从 {host.Name} 卸载 {app.Name} 已取消", LogLevel.Warning);
                    return BatchTaskResult.Canceled;
                }

                AddLog($"从 {host.Name} 卸载 {app.Name} 失败：{result.ErrorMessage}", LogLevel.Error);
                return BatchTaskResult.Failed;
            }
            catch (OperationCanceledException)
            {
                AddLog($"从 {hostViewModel.Name} 卸载 {app.Name} 已取消", LogLevel.Warning);
                return BatchTaskResult.Canceled;
            }
            catch (Exception ex)
            {
                AddLog($"从 {hostViewModel.Name} 卸载 {app.Name} 失败：{ex.Message}", LogLevel.Error);
                return BatchTaskResult.Failed;
            }
        }

        // 安装方法补丁 - 替换原有的 InstallApplication 和辅助方法

        /// <summary>
        /// 安装应用 (使用配置对话框)
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanInstallApplication))]
        private async Task InstallApplication(ApplicationCardViewModel app)
        {
            SelectApplication(app);

            if (SelectedHost == null)
            {
                MessageBox.Show("请先选择要安装的目标服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 1. 获取基础信息
                var host = GetHostFromViewModel(SelectedHost);
                if (host == null)
                {
                    AddLog($"无法找到服务器：{SelectedHost.Name}", LogLevel.Error);
                    return;
                }

                var appInfo = GetApplicationInfo(app.Id);
                if (appInfo == null)
                {
                    AddLog($"无法找到应用信息：{app.Name}", LogLevel.Error);
                    return;
                }

                // 2. 显示安装配置对话框
                AddLog($"正在为 {app.Name} 准备安装配置...", LogLevel.Info);
                var dialog = new InstallConfigDialog();
                var configViewModel = new InstallConfigViewModel(appInfo, host, _logger);
                dialog.DataContext = configViewModel;
                dialog.Owner = Application.Current.MainWindow;

                if (dialog.ShowDialog() != true)
                {
                    AddLog("用户取消了安装配置", LogLevel.Warning);
                    return;
                }

                // 3. 提取配置
                var parameters = configViewModel.GetParameters();
                var localPackagePath = configViewModel.PackageSource == "local"
                    ? configViewModel.LocalPackagePath
                    : string.Empty;
                var version = configViewModel.SelectedVersion ?? appInfo.Version;

                // 同步版本到应用信息
                appInfo.Version = version;
                appInfo.LocalPackagePath = localPackagePath;
                appInfo.UseLocalPackage = configViewModel.PackageSource == "local" && !string.IsNullOrEmpty(localPackagePath);

                // 4. 执行安装
                AddLog($"开始安装 {app.Name} v{version} 到 {SelectedHost.Name}...", LogLevel.Info);
                
                // 创建任务列表项
                var taskViewModel = new TaskViewModel
                {
                    ApplicationName = app.Name,
                    HostName = SelectedHost.Name,
                    Progress = 0,
                    CurrentStage = "准备中"
                };
                Application.Current.Dispatcher.Invoke(() => Tasks.Add(taskViewModel));
                SelectedTask = taskViewModel;

                var installerService = new InstallerService(_sshService, _logger);
                var cts = new CancellationTokenSource();

                // 创建进度报告器
                var progress = new Progress<InstallTask>(t =>
                {
                    taskViewModel.Progress = t.Progress;
                    taskViewModel.CurrentStage = t.StageDisplayText;
                });

                // 执行安装任务
                var taskResult = await installerService.InstallAsync(
                    host,
                    appInfo,
                    parameters,
                    localPackagePath,
                    progress,
                    cts.Token);

                // 5. 更新应用状态
                if (taskResult.Status == Models.TaskStatus.Completed)
                {
                    app.IsInstalled = true;
                    app.IsRunning = true;
                    app.InstalledVersion = version;
                    AddLog($"✅ 安装成功：{app.Name} v{version}", LogLevel.Success);
                    MessageBox.Show($"安装成功！{app.Name} 已安装到 {SelectedHost.Name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (taskResult.Status == Models.TaskStatus.Failed)
                {
                    AddLog($"❌ 安装失败：{taskResult.ErrorMessage}", LogLevel.Error);
                    MessageBox.Show($"安装失败：{taskResult.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("安装已取消", LogLevel.Warning);
                MessageBox.Show("安装已取消", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"❌ 安装过程发生异常：{ex.Message}", LogLevel.Error);
                MessageBox.Show($"安装失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 检测应用状态
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCheckApplicationStatus))]
        private async Task CheckApplicationStatus(ApplicationCardViewModel appCard)
        {
            SelectApplication(appCard);

            if (SelectedHost == null) return;
            
            try
            {
                var host = GetHostFromViewModel(SelectedHost);
                var appInfo = GetApplicationInfo(appCard.Id);
                
                if (host == null || appInfo == null) return;
                
                AddLog($"正在检测 {appCard.Name} 状态...", LogLevel.Info);
                
                var installerService = new InstallerService(_sshService, _logger);
                var status = await installerService.CheckStatusAsync(host, appInfo);
                
                appCard.IsInstalled = status.IsInstalled;
                appCard.IsRunning = status.IsRunning;
                appCard.InstalledVersion = status.InstalledVersion;
                
                AddLog($"{appCard.Name} 状态已更新", LogLevel.Success);
            }
            catch (Exception ex)
            {
                AddLog($"检测 {appCard.Name} 状态失败: {ex.Message}", LogLevel.Error);
            }
        }
        private bool CanCheckApplicationStatus(ApplicationCardViewModel app) => SelectedHost != null;
        /// <summary>
        /// 配置应用
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanConfigureApplication))]
        private async void ConfigureApplication(ApplicationCardViewModel app)
        {
            SelectApplication(app);

            if (SelectedHost == null) return;

            AddLog($"正在加载 {app.Name} 配置文件...", LogLevel.Info);

            try
            {
                // 1. 获取远程主机对象
                var remoteHost = GetHostFromViewModel(SelectedHost);
                if (remoteHost == null)
                {
                    AddLog($"无法获取服务器信息", LogLevel.Error);
                    MessageBox.Show("无法获取服务器信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 连接到远程主机
                await _sshService.ConnectAsync(remoteHost);

                // 3. 从HostViewModel.OsType字符串转换为枚举类型
                var osType = SelectedHost.OsType switch
                {
                    "Linux" => OperatingSystemType.Linux,
                    "CentOS" => OperatingSystemType.CentOS,
                    "Ubuntu" => OperatingSystemType.Ubuntu,
                    "Windows" => OperatingSystemType.Windows,
                    _ => OperatingSystemType.Linux // 默认为Linux
                };

                // 4. 查找配置文件路径
                var configPath = await _configurationService.GetConfigFilePathAsync(
                    remoteHost,
                    app.Name,
                    osType);

                if (string.IsNullOrEmpty(configPath))
                {
                    MessageBox.Show($"未找到 {app.Name} 的配置文件，请确认应用已正确安装。",
                        "配置文件不存在", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AddLog($"未找到 {app.Name} 的配置文件", LogLevel.Warning);
                    return;
                }

                // 5. 读取配置文件内容
                var configContent = await _configurationService.ReadConfigAsync(configPath);
                var switchableFiles = string.Equals(app.Name, "Traefik", StringComparison.OrdinalIgnoreCase)
                    ? await _configurationService.GetSwitchableConfigFilesAsync(app.Name)
                    : null;

                // 6. 创建ViewModel并打开对话框
                var configViewModel = new ConfigEditorViewModel(
                    _configurationService,
                    remoteHost,
                    app.Name,
                    osType,
                    configPath,
                    configContent,
                    switchableFiles: switchableFiles);

                var dialog = new Views.Dialogs.ConfigEditorDialog(configViewModel);
                if (Application.Current.MainWindow != null)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }

                dialog.ShowDialog();

                AddLog($"{app.Name} 配置编辑器已关闭", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"加载配置文件失败: {ex.Message}", LogLevel.Error);
                MessageBox.Show($"加载配置文件失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool CanConfigureApplication(ApplicationCardViewModel app) => SelectedHost != null && app.IsInstalled;
        /// <summary>
        /// 卸载应用
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanUninstallApplication))]
        private async Task UninstallApplication(ApplicationCardViewModel app)
        {
            SelectApplication(app);

            if (SelectedHost == null) return;

            var result = MessageBox.Show($"确定要从 {SelectedHost.Name} 卸载 {app.Name} 吗？\n注意：这将停止并移除该应用的服务及相关二进制文件。", 
                "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;

            // P1 功能：保留数据选项确认 (简化版，默认不保留数据)
            bool keepData = false;
            
            AddLog($"开始从 {SelectedHost.Name} 卸载 {app.Name}...", LogLevel.Info);

            try
            {
                var appInfo = GetApplicationInfo(app.Id);
                if (appInfo == null)
                {
                    AddLog($"无法加载 {app.Name} 的元数据", LogLevel.Error);
                    return;
                }
                
                var host = GetHostFromViewModel(SelectedHost);
                if (host == null)
                {
                    AddLog($"无法加载主机 {SelectedHost.Name} 的信息", LogLevel.Error);
                    return;
                }

                // 创建并添加任务视图模型
                var taskViewModel = new TaskViewModel
                {
                    HostName = host.Name,
                    ApplicationName = appInfo.Name,
                    Progress = 0,
                    CurrentStage = "初始化..."
                };
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    Tasks.Insert(0, taskViewModel);
                    OnPropertyChanged(nameof(TaskCount));
                    SelectedTask = taskViewModel;
                });

                // 创建进度报告器
                var progress = new Progress<InstallTask>(t => 
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        taskViewModel.Progress = t.Progress;
                        taskViewModel.CurrentStage = t.StageDisplayText;
                        if (t.Status == RemoteInstaller.Models.TaskStatus.Failed)
                        {
                            taskViewModel.CurrentStage = $"❌ {t.ErrorMessage}";
                        }
                    });
                });

                var installerService = new InstallerService(_sshService, _logger);
                var task = await installerService.UninstallAsync(host, appInfo, keepData, progress);

                if (task.Status == RemoteInstaller.Models.TaskStatus.Completed)
                {
                    AddLog($"{app.Name} 卸载成功", LogLevel.Success);
                    // 刷新状态
                    await CheckAllAppsStatusAsync(SelectedHost);
                }
                else
                {
                    AddLog($"{app.Name} 卸载失败: {task.ErrorMessage}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"卸载过程发生异常: {ex.Message}", LogLevel.Error);
            }
        }
        private bool CanUninstallApplication(ApplicationCardViewModel app) => SelectedHost != null && app.IsInstalled;
        private bool CanInstallApplication(ApplicationCardViewModel app) => SelectedHost != null && !app.IsInstalled;

        [RelayCommand]
        private void SelectApplication(ApplicationCardViewModel? app)
        {
            if (app == null)
            {
                return;
            }

            SelectedApplication = app;
        }

        [RelayCommand]
        private void ToggleApplicationSelection(ApplicationCardViewModel? app)
        {
            if (app == null)
            {
                return;
            }

            app.IsSelected = !app.IsSelected;

            if (app.IsSelected)
            {
                SelectedApplication = app;
            }
            else if (ReferenceEquals(SelectedApplication, app))
            {
                SelectedApplication = Applications.FirstOrDefault(a => a.IsSelected);
            }

            NotifyBatchActionStateChanged();
        }

        private enum BatchTaskResult
        {
            Success,
            Failed,
            Canceled
        }

        private enum InstallMode { None, Network, Local }

        private InstallMode ShowInstallModeDialog(string appName)
        {
            var window = new Window
            {
                Title = $"安装 {appName}",
                Width = 400, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Content = new Grid
                {
                    Margin = new Thickness(20),
                    RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } }
                }
            };

            ((Grid)window.Content).Children.Add(new TextBlock { Text = "请选择安装方式：", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 15) });
            
            var networkBtn = new Button { Content = "🌐 网络安装（从仓库下载）", Height = 40, Margin = new Thickness(0, 0, 0, 10), FontSize = 13 };
            networkBtn.Click += (s, e) => { window.DialogResult = true; window.Close(); };
            Grid.SetRow(networkBtn, 1);
            ((Grid)window.Content).Children.Add(networkBtn);

            var localBtn = new Button { Content = "📁 本地安装（选择安装包）", Height = 40, FontSize = 13 };
            localBtn.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            Grid.SetRow(localBtn, 2);
            ((Grid)window.Content).Children.Add(localBtn);

            var cancelBtn = new Button { Content = "取消", Height = 30, Margin = new Thickness(0, 10, 0, 0), Style = (Style)Application.Current.FindResource("MaterialDesignFlatButton") };
            cancelBtn.Click += (s, e) => { window.DialogResult = null; window.Close(); };
            Grid.SetRow(cancelBtn, 3);
            ((Grid)window.Content).Children.Add(cancelBtn);

            var result = window.ShowDialog();
            return result.HasValue ? (result.Value ? InstallMode.Network : InstallMode.Local) : InstallMode.None;
        }

        private string? ShowVersionSelectDialog(string appName, string defaultVersion)
        {
            var window = new Window
            {
                Title = $"选择 {appName} 版本",
                Width = 400, Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Grid
                {
                    Margin = new Thickness(20),
                    RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(double.NaN) }, new RowDefinition { Height = GridLength.Auto } },
                    ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(double.NaN) } }
                }
            };

            ((Grid)window.Content).Children.Add(new TextBlock { Text = "请选择要安装的版本号：", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
            
            var label = new TextBlock { Text = "版本号:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(label, 1);
            ((Grid)window.Content).Children.Add(label);

            var versionBox = new TextBox { Text = defaultVersion, FontSize = 14 };
            Grid.SetRow(versionBox, 1); Grid.SetColumn(versionBox, 1);
            ((Grid)window.Content).Children.Add(versionBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            Grid.SetRow(btnPanel, 2);
            
            var okBtn = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            okBtn.Click += (s, e) => { window.DialogResult = true; window.Close(); };
            btnPanel.Children.Add(okBtn);

            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 30, Style = (Style)Application.Current.FindResource("MaterialDesignFlatButton") };
            cancelBtn.Click += (s, e) => { window.DialogResult = null; window.Close(); };
            btnPanel.Children.Add(cancelBtn);
            
            ((Grid)window.Content).Children.Add(btnPanel);

            var result = window.ShowDialog();
            return result == true ? versionBox.Text : null;
        }

        private string? ShowPackageFileDialog(string appName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"选择 {appName} 安装包",
                Filter = "所有安装包|*.zip;*.tar.gz;*.tgz;*.rpm;*.deb;*.msi;*.exe|压缩文件|*.zip;*.tar.gz;*.tgz|RPM 包|*.rpm|DEB 包|*.deb|MSI 包|*.msi|可执行文件|*.exe|所有文件|*.*",
                FilterIndex = 1,
                Multiselect = false
            };
            var result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
        /// <summary>
        /// 显示远程路径输入对话框
        /// </summary>
        private string? ShowRemotePathDialog(string appName)
        {
            var window = new Window
            {
                Title = $"选择远程路径 - {appName}",
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Content = new Grid
                {
                    Margin = new Thickness(20),
                    RowDefinitions = 
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto }
                    }
                }
            };
            var title = new TextBlock
            {
                Text = "请输入上传到远程服务器的路径：",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(title, 0);
            ((Grid)window.Content).Children.Add(title);
            var label = new TextBlock
            {
                Text = "远程路径:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetRow(label, 1);
            ((Grid)window.Content).Children.Add(label);
            var pathBox = new TextBox
            {
                Text = "/tmp",
                FontSize = 13
            };
            Grid.SetRow(pathBox, 1);
            Grid.SetColumn(pathBox, 1);
            ((Grid)window.Content).Children.Add(pathBox);
            var hint = new TextBlock
            {
                Text = "留空或使用 /tmp 表示使用默认临时目录",
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 10)
            };
            Grid.SetRow(hint, 2);
            Grid.SetColumnSpan(hint, 2);
            ((Grid)window.Content).Children.Add(hint);
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(btnPanel, 3);
            Grid.SetColumnSpan(btnPanel, 2);
            var okBtn = new Button
            {
                Content = "确定",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okBtn.Click += (s, e) =>
            {
                window.DialogResult = true;
                window.Close();
            };
            btnPanel.Children.Add(okBtn);
            var cancelBtn = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                Style = (Style?)Application.Current.FindResource("MaterialDesignFlatButton")
            };
            cancelBtn.Click += (s, e) =>
            {
                window.DialogResult = null;
                window.Close();
            };
            btnPanel.Children.Add(cancelBtn);
            ((Grid)window.Content).Children.Add(btnPanel);
            var result = window.ShowDialog();
            return result == true ? pathBox.Text : null;
        }

        private string ExtractVersionFromFileName(string filePath, string defaultVersion)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLower();
            var versionMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+\.\d+\.\d+(?:\.\d+)?)");
            return versionMatch.Success ? versionMatch.Value : defaultVersion;
        }
        #endregion
#region 辅助方法

        /// <summary>
        /// 从 ViewModel 获取 Host 对象
        /// </summary>
        private RemoteHost? GetHostFromViewModel(HostViewModel viewModel)
        {
            if (string.IsNullOrEmpty(viewModel.Id))
            {
                // 如果没有 Id，回退到根据名称和 IP 查找
                return _databaseService.GetAllHosts().FirstOrDefault(h => 
                    h.Name == viewModel.Name && 
                    h.IpAddress == viewModel.IpAddress);
            }

            return _databaseService.GetAllHosts().FirstOrDefault(h => h.Id == viewModel.Id);
        }

        /// <summary>
        /// 获取应用信息（从配置文件加载）
        /// </summary>
        private ApplicationInfo? GetApplicationInfo(string appId)
        {
            // 优先从 AppConfigurationService 加载
            if (_appConfigurationService != null)
            {
                var appConfig = _appConfigurationService.GetApplicationById(appId);
                if (appConfig != null)
                {
                    return ConvertToApplicationInfo(appConfig);
                }
            }

            // 回退到硬编码配置
            return GetHardcodedApplicationInfo(appId);
        }

        /// <summary>
        /// 将 ApplicationConfig 转换为 ApplicationInfo
        /// </summary>
        private ApplicationInfo ConvertToApplicationInfo(ApplicationConfig config)
        {
            var info = new ApplicationInfo
            {
                Id = config.Id,
                Name = config.Name,
                Version = config.Versions?.FirstOrDefault()?.Version ?? "1.0.0",
                Description = config.Description,
                Category = config.Category,
                SupportWindows = config.OsSupport?.Contains("Windows") == true,
                SupportCentOS = config.OsSupport?.Contains("Linux") == true || config.OsSupport?.Contains("CentOS") == true,
                SupportUbuntu = config.OsSupport?.Contains("Linux") == true || config.OsSupport?.Contains("Ubuntu") == true,
                InstallScriptLinux = config.Scripts?.Install?.Linux ?? string.Empty,
                InstallScriptWindows = config.Scripts?.Install?.Windows ?? string.Empty,
                UninstallScriptLinux = config.Scripts?.Uninstall?.Linux ?? string.Empty,
                UninstallScriptWindows = config.Scripts?.Uninstall?.Windows ?? string.Empty,
                CheckScriptLinux = config.Scripts?.Detect?.Linux ?? string.Empty,
                CheckScriptWindows = config.Scripts?.Detect?.Windows ?? string.Empty,
                Versions = config.Versions?.Select(v => v.Version).ToList() ?? new List<string>(),
                Parameters = new List<InstallParameter>()
            };

            // 转换参数
            if (config.Versions?.Count > 0)
            {
                var firstVersion = config.Versions[0];
                foreach (var param in firstVersion.Parameters ?? [])
                {
                    var installParam = new InstallParameter
                    {
                        Key = param.Name.ToUpper().Replace(" ", "_"),
                        Name = param.Name,
                        Description = param.Description,
                        DefaultValue = param.Default,
                        Required = param.Required
                    };

                    installParam.Type = param.Type.ToLower() switch
                    {
                        "password" => ParameterType.Password,
                        "number" => ParameterType.Number,
                        "port" => ParameterType.Port,
                        "path" => ParameterType.Path,
                        "boolean" => ParameterType.Boolean,
                        _ => ParameterType.Text
                    };

                    info.Parameters.Add(installParam);
                }
            }

            return info;
        }

        /// <summary>
        /// 从 AppConfigurationService 加载应用到 UI 列表
        /// </summary>
        private void LoadApplicationsFromConfig()
        {
            Applications.Clear();
            SelectedApplication = null;

            if (_appConfigurationService == null)
            {
                AddLog("AppConfigurationService 未初始化，使用硬编码配置", LogLevel.Warning);
                LoadHardcodedApplications();
                return;
            }

            var apps = _appConfigurationService.GetApplications();
            if (apps == null || apps.Count == 0)
            {
                AddLog("配置文件中的应用列表为空，使用硬编码配置", LogLevel.Warning);
                LoadHardcodedApplications();
                return;
            }

            foreach (var appConfig in apps)
            {
                var appCard = new ApplicationCardViewModel(this)
                {
                    Id = appConfig.Id,
                    Name = appConfig.Name,
                    Description = appConfig.Description,
                    Category = appConfig.Category,
                    Icon = string.IsNullOrEmpty(appConfig.Icon) ? appConfig.Name[0].ToString() : appConfig.Icon,
                    Versions = appConfig.Versions?.Select(v => v.Version).ToList() ?? new List<string>(),
                    IsSupported = true,
                    OsType = GetOsTypeString(appConfig.OsSupport)
                };

                // 设置默认版本（第一个版本）
                if (appConfig.Versions?.Count > 0)
                {
                    appCard.Version = appConfig.Versions[0].Version;
                }

                Applications.Add(appCard);
            }

            AddLog($"成功加载 {Applications.Count} 个应用到 UI", LogLevel.Success);
            ApplyAppFilter();
        }

        /// <summary>
        /// 获取操作系统类型字符串
        /// </summary>
        private string GetOsTypeString(ObservableCollection<string>? osSupport)
        {
            if (osSupport == null || osSupport.Count == 0)
                return "未知";

            var labels = new List<string>();
            var hasWindows = osSupport.Contains("Windows");
            var hasLinux = osSupport.Contains("Linux");
            var hasCentOS = hasLinux || osSupport.Contains("CentOS");
            var hasUbuntu = hasLinux || osSupport.Contains("Ubuntu");

            if (hasWindows) labels.Add("Windows Server");
            if (hasCentOS) labels.Add("CentOS");
            if (hasUbuntu) labels.Add("Ubuntu");

            return labels.Count > 0 ? string.Join("/", labels) : "未知";
        }

        /// <summary>
        /// 加载硬编码应用（回退方案）
        /// </summary>
        private void LoadHardcodedApplications()
        {
            SelectedApplication = null;

            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "mysql",
                Name = "MySQL",
                Version = "8.0.35",
                Versions = new List<string> { "8.4.0", "8.0.42", "8.0.35", "8.0.28", "5.7.44", "5.7.35" },
                Description = "开源关系型数据库",
                Icon = "🐬",
                IsSupported = true,
                OsType = "Linux/Windows"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "redis",
                Name = "Redis",
                Version = "7.2.3",
                Versions = new List<string> { "7.2.5", "7.2.3", "7.0.15", "6.2.14", "6.0.20", "5.0.14" },
                Description = "高性能键值数据库",
                Icon = "📦",
                IsInstalled = true,
                IsSupported = true,
                OsType = "Linux/Windows"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "nginx",
                Name = "Nginx",
                Version = "1.24.0",
                Versions = new List<string> { "1.26.1", "1.24.0", "1.22.1", "1.20.2", "1.18.0" },
                Description = "高性能 Web 服务器",
                Icon = "🌐",
                IsSupported = true,
                OsType = "Linux"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "elasticsearch",
                Name = "Elasticsearch",
                Version = "8.14.0",
                Versions = new List<string> { "8.14.0", "8.13.4", "8.12.2", "7.17.18", "7.10.2" },
                Description = "分布式搜索引擎",
                Icon = "🔍",
                IsSupported = true,
                OsType = "Linux"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "rabbitmq",
                Name = "RabbitMQ",
                Version = "3.12.0",
                Versions = new List<string> { "3.13.3", "3.12.0", "3.11.20", "3.10.25" },
                Description = "消息队列中间件",
                Icon = "🐰",
                IsSupported = true,
                OsType = "Linux"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "nacos",
                Name = "Nacos",
                Version = "2.3.0",
                Versions = new List<string> { "2.3.2", "2.3.0", "2.2.3", "1.4.6" },
                Description = "服务发现与配置中心",
                Icon = "🐱",
                IsSupported = true,
                OsType = "Both"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "consul",
                Name = "Consul",
                Version = "1.22.6",
                Versions = new List<string> { "1.22.6" },
                Description = "服务发现、KV 存储与健康检查平台",
                Icon = "🧭",
                IsSupported = true,
                OsType = "Linux"
            });
            Applications.Add(new ApplicationCardViewModel(this)
            {
                Id = "traefik",
                Name = "Traefik",
                Version = "3.6.13",
                Versions = new List<string> { "3.6.13" },
                Description = "云原生反向代理与边缘网关",
                Icon = "🛣️",
                IsSupported = true,
                OsType = "Linux"
            });
        }

        /// <summary>
        /// 获取硬编码的应用信息（回退方案）
        /// </summary>
        private ApplicationInfo? GetHardcodedApplicationInfo(string appId)
        {
            return appId.ToLower() switch
            {
                "redis" => new ApplicationInfo
                {
                    Id = "redis",
                    Name = "Redis",
                    Version = "7.2.3",
                    Versions = new List<string> { "7.2.5", "7.2.3", "7.0.15", "6.2.14", "6.0.20", "5.0.14" },
                    Description = "高性能键值数据库",
                    Category = "数据库",
                    SupportWindows = true,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Redis/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\Redis\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "PASSWORD", Name = "访问密码", Description = "Redis 访问密码 (为空则不设置)", Type = ParameterType.Password, DefaultValue = "", Required = false },
                        new InstallParameter { Key = "PORT", Name = "服务端口", Description = "Redis 服务监听端口", Type = ParameterType.Number, DefaultValue = "6379", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "ALLOW_REMOTE", Name = "允许远程访问", Description = "是否允许远程连接 Redis", Type = ParameterType.Boolean, DefaultValue = "true", Required = true }
                    }
                },
                "nginx" => new ApplicationInfo
                {
                    Id = "nginx",
                    Name = "Nginx",
                    Version = "1.24.0",
                    Versions = new List<string> { "1.26.1", "1.24.0", "1.22.1", "1.20.2", "1.18.0" },
                    Description = "高性能 Web 服务器",
                    Category = "Web 服务",
                    SupportWindows = false,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Nginx/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\Nginx\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "PORT", Name = "服务端口", Description = "Nginx 服务监听端口", Type = ParameterType.Number, DefaultValue = "80", Required = true, MinValue = 1, MaxValue = 65535 }
                    }
                },
                "elasticsearch" => new ApplicationInfo
                {
                    Id = "elasticsearch",
                    Name = "Elasticsearch",
                    Version = "8.14.0",
                    Versions = new List<string> { "8.14.0", "8.13.4", "8.12.2", "7.17.18", "7.10.2" },
                    Description = "分布式搜索引擎",
                    Category = "搜索",
                    SupportWindows = false,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Elasticsearch/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\Elasticsearch\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "HTTP_PORT", Name = "HTTP 端口", Description = "Elasticsearch HTTP 监听端口", Type = ParameterType.Number, DefaultValue = "9200", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "CLUSTER_NAME", Name = "集群名称", Description = "Elasticsearch 集群名称", Type = ParameterType.Text, DefaultValue = "my-cluster", Required = true },
                        new InstallParameter { Key = "NODE_NAME", Name = "节点名称", Description = "Elasticsearch 节点名称", Type = ParameterType.Text, DefaultValue = "node-1", Required = true },
                        new InstallParameter { Key = "MEMORY_LIMIT", Name = "内存限制", Description = "Elasticsearch JVM 堆内存限制 (例如 2g, 4g)", Type = ParameterType.Text, DefaultValue = "2g", Required = true }
                    }
                },
                "mysql" => new ApplicationInfo
                {
                    Id = "mysql",
                    Name = "MySQL",
                    Version = "8.0.35",
                    Versions = new List<string> { "8.4.0", "8.0.42", "8.0.35", "8.0.28", "5.7.44", "5.7.35" },
                    Description = "流行的开源关系型数据库",
                    Category = "数据库",
                    SupportWindows = false,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/MySQL/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\MySQL\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "ROOT_PASSWORD", Name = "Root 密码", Description = "MySQL root 用户的初始密码", Type = ParameterType.Password, DefaultValue = "MySql@123", Required = true },
                        new InstallParameter { Key = "PORT", Name = "服务端口", Description = "MySQL 服务监听端口", Type = ParameterType.Number, DefaultValue = "3306", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "ALLOW_REMOTE", Name = "允许远程访问", Description = "是否允许 root 用户远程连接", Type = ParameterType.Boolean, DefaultValue = "true", Required = true }
                    }
                },
                "rabbitmq" => new ApplicationInfo
                {
                    Id = "rabbitmq",
                    Name = "RabbitMQ",
                    Version = "3.12.0",
                    Versions = new List<string> { "3.13.3", "3.12.0", "3.11.20", "3.10.25" },
                    Description = "消息队列中间件",
                    Category = "中间件",
                    SupportWindows = true,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/RabbitMQ/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\RabbitMQ\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "AMQP_PORT", Name = "AMQP 端口", Description = "AMQP 服务端口", Type = ParameterType.Port, DefaultValue = "5672", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "MANAGEMENT_PORT", Name = "管理端口", Description = "Web 管理界面端口", Type = ParameterType.Port, DefaultValue = "15672", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "USERNAME", Name = "用户名", Description = "RabbitMQ 登录用户名", Type = ParameterType.Text, DefaultValue = "guest", Required = true },
                        new InstallParameter { Key = "PASSWORD", Name = "密码", Description = "RabbitMQ 登录密码", Type = ParameterType.Password, DefaultValue = "guest", Required = true },
                        new InstallParameter { Key = "ENABLE_REMOTE_ACCESS", Name = "允许远程访问", Description = "是否允许远程连接", Type = ParameterType.Boolean, DefaultValue = "true", Required = false }
                    }
                },
                "nacos" => new ApplicationInfo
                {
                    Id = "nacos",
                    Name = "Nacos",
                    Version = "2.3.0",
                    Versions = new List<string> { "2.3.2", "2.3.0", "2.2.3", "1.4.6" },
                    Description = "服务发现与配置中心",
                    Category = "中间件",
                    SupportWindows = true,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Nacos/check_status_linux.sh",
                    CheckScriptWindows = "Scripts\\Nacos\\check_status_windows.ps1",
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "HTTP_PORT", Name = "HTTP 端口", Description = "Nacos HTTP 服务端口", Type = ParameterType.Port, DefaultValue = "8848", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "RAFT_PORT", Name = "Raft 端口", Description = "Raft 集群通信端口", Type = ParameterType.Port, DefaultValue = "9848", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "GRPC_PORT", Name = "gRPC 端口", Description = "gRPC 服务端口", Type = ParameterType.Port, DefaultValue = "9849", Required = true, MinValue = 1024, MaxValue = 65535 },
                        new InstallParameter { Key = "MODE", Name = "运行模式", Description = "运行模式 (standalone/cluster)", Type = ParameterType.Text, DefaultValue = "standalone", Required = true },
                        new InstallParameter { Key = "USERNAME", Name = "用户名", Description = "Nacos 登录用户名", Type = ParameterType.Text, DefaultValue = "nacos", Required = true },
                        new InstallParameter { Key = "PASSWORD", Name = "密码", Description = "Nacos 登录密码", Type = ParameterType.Password, DefaultValue = "nacos", Required = true }
                    }
                },
                "consul" => new ApplicationInfo
                {
                    Id = "consul",
                    Name = "Consul",
                    Version = "1.22.6",
                    Versions = new List<string> { "1.22.6" },
                    Description = "服务发现、KV 存储与健康检查平台",
                    Category = "中间件",
                    SupportWindows = false,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Consul/check_status_linux.sh",
                    CheckScriptWindows = string.Empty,
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "HTTP_PORT", Name = "HTTP 端口", Description = "Consul HTTP API 端口", Type = ParameterType.Port, DefaultValue = "8500", Required = true, MinValue = 1, MaxValue = 65535 },
                        new InstallParameter { Key = "DNS_PORT", Name = "DNS 端口", Description = "Consul DNS 服务端口", Type = ParameterType.Port, DefaultValue = "8600", Required = true, MinValue = 1, MaxValue = 65535 },
                        new InstallParameter { Key = "BIND_ADDR", Name = "监听地址", Description = "Consul 服务监听地址", Type = ParameterType.Text, DefaultValue = "0.0.0.0", Required = true },
                        new InstallParameter { Key = "DATA_DIR", Name = "数据目录", Description = "Consul 数据目录路径", Type = ParameterType.Path, DefaultValue = "/var/lib/consul", Required = true },
                        new InstallParameter { Key = "NODE_NAME", Name = "节点名称", Description = "Consul 节点名称", Type = ParameterType.Text, DefaultValue = "consul-node", Required = true },
                        new InstallParameter { Key = "UI_ENABLED", Name = "启用 Web UI", Description = "是否启用 Consul Web UI", Type = ParameterType.Boolean, DefaultValue = "true", Required = false }
                    }
                },
                "traefik" => new ApplicationInfo
                {
                    Id = "traefik",
                    Name = "Traefik",
                    Version = "3.6.13",
                    Versions = new List<string> { "3.6.13" },
                    Description = "云原生反向代理与边缘网关",
                    Category = "Web 服务",
                    SupportWindows = false,
                    SupportCentOS = true,
                    SupportUbuntu = true,
                    CheckScriptLinux = "Scripts/Traefik/check_status_linux.sh",
                    CheckScriptWindows = string.Empty,
                    Parameters = new List<InstallParameter>
                    {
                        new InstallParameter { Key = "HTTP_PORT", Name = "HTTP 端口", Description = "Traefik HTTP 入口端口", Type = ParameterType.Port, DefaultValue = "80", Required = true, MinValue = 1, MaxValue = 65535 },
                        new InstallParameter { Key = "HTTPS_PORT", Name = "HTTPS 端口", Description = "Traefik HTTPS 入口端口", Type = ParameterType.Port, DefaultValue = "443", Required = true, MinValue = 1, MaxValue = 65535 },
                        new InstallParameter { Key = "DASHBOARD_PORT", Name = "Dashboard 端口", Description = "Traefik Dashboard 端口", Type = ParameterType.Port, DefaultValue = "8080", Required = true, MinValue = 1, MaxValue = 65535 },
                        new InstallParameter { Key = "INSTALL_DIR", Name = "安装目录", Description = "Traefik 安装目录", Type = ParameterType.Path, DefaultValue = "/opt/traefik", Required = true },
                        new InstallParameter { Key = "CONFIG_DIR", Name = "配置目录", Description = "Traefik 配置目录", Type = ParameterType.Path, DefaultValue = "/etc/traefik", Required = true },
                        new InstallParameter { Key = "ENABLE_DASHBOARD", Name = "启用 Dashboard", Description = "是否启用 Traefik Dashboard", Type = ParameterType.Boolean, DefaultValue = "true", Required = false }
                    }
                },
                _ => null
            };
        }

        #endregion
    }

    #region 子 ViewModel

    /// <summary>
    /// 主机 ViewModel
    /// </summary>
    public partial class HostViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private string _port = "22";

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _osType = string.Empty;

        [ObservableProperty]
        private string _groupName = string.Empty;

        [ObservableProperty]
        private bool _isOnline;

        public string OsIcon => OsType switch
        {
            "Linux" => "🐧",
            "Windows" => "🪟",
            _ => "🖥️"
        };

        public string StatusColor => IsOnline ? "#00c853" : "#e53935";
    }

    /// <summary>
    /// 任务 ViewModel
    /// </summary>
    public partial class TaskViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationName = string.Empty;

        [ObservableProperty]
        private string _hostName = string.Empty;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _currentStage = string.Empty;

        public string StageDisplayText => CurrentStage;
        public string AppName => ApplicationName;
    }

    /// <summary>
    /// 日志 ViewModel
    /// </summary>
    public partial class LogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private LogLevel _level;

        /// <summary>
        /// 日志时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string DisplayText => $"[{Timestamp:HH:mm:ss}] [{GetLevelText()}] {Message}";

        private string GetLevelText() => Level switch
        {
            LogLevel.Info => "INFO",
            LogLevel.Success => "SUCCESS",
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            _ => "INFO"
        };

        public string LogText => $"[{Level}] {Message}";

        public string LogColor => Level switch
        {
            LogLevel.Info => "#4fc1ff",
            LogLevel.Success => "#4ec9b0",
            LogLevel.Warning => "#dcdcaa",
            LogLevel.Error => "#f14c4c",
            _ => "#d4d4d4"
        };
    }

    #endregion
}


















