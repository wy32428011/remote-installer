using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels
{
    /// <summary>
    /// 系统设置对话框 ViewModel
    /// 处理系统级别的配置参数，包括仓库、代理、连接、缓存和主题设置
    /// </summary>
    public partial class SettingsViewModel : BaseViewModel
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger _logger;
        private SystemSettings _originalSettings; // 用于跟踪是否有所更改

        #region 远程仓库配置属性

        [ObservableProperty]
        private string _repositoryUrl = string.Empty;

        [ObservableProperty]
        private string _repositoryToken = string.Empty;

        [ObservableProperty]
        private string _updateCheckUrl = string.Empty;

        #endregion

        #region 代理设置属性

        [ObservableProperty]
        private bool _useProxy = false;

        [ObservableProperty]
        private ProxyType _proxyType = ProxyType.None;

        [ObservableProperty]
        private string _proxyHost = string.Empty;

        [ObservableProperty]
        private int _proxyPort = 0;

        [ObservableProperty]
        private string _proxyUsername = string.Empty;

        [ObservableProperty]
        private string _proxyPassword = string.Empty;

        /// <summary>
        /// 代理类型选项列表
        /// </summary>
        public ProxyType[] ProxyTypes => Enum.GetValues<ProxyType>();

        #endregion

        #region 连接设置属性

        [ObservableProperty]
        private int _connectionTimeout = 60;

        [ObservableProperty]
        private int _retryCount = 3;

        [ObservableProperty]
        private int _retryInterval = 5;

        #endregion

        #region 缓存设置属性

        [ObservableProperty]
        private string _cacheDirectory = string.Empty;

        [ObservableProperty]
        private long _maxCacheSizeMB = 5000;

        /// <summary>
        /// 是否显示浏览文件夹按钮
        /// </summary>
        public bool ShowBrowseButton => true;

        #endregion

        #region 并发设置属性

        [ObservableProperty]
        private int _maxConcurrentTasks = 3;

        #endregion

        #region 主题设置属性

        [ObservableProperty]
        private ThemeType _currentTheme = ThemeType.Dark;

        /// <summary>
        /// 主题选项列表
        /// </summary>
        public ThemeType[] ThemeTypes => Enum.GetValues<ThemeType>();

        /// <summary>
        /// 当前主题显示文本
        /// </summary>
        public string CurrentThemeDisplay => CurrentTheme == ThemeType.Dark ? "深色主题" : "浅色主题";

        #endregion

        #region 对话框状态属性

        [ObservableProperty]
        private bool _isSaving = false;

        [ObservableProperty]
        private string _saveStatus = string.Empty;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化 SettingsViewModel
        /// </summary>
        /// <param name="databaseService">数据库服务，用于持久化设置</param>
        /// <param name="logger">日志服务</param>
        public SettingsViewModel(DatabaseService databaseService, ILogger logger)
        {
            _databaseService = databaseService;
            _logger = logger;
            LoadSettings();
        }

        #endregion

        #region 命令

        /// <summary>
        /// 浏览缓存目录命令
        /// 打开文件夹选择对话框
        /// </summary>
        [RelayCommand]
        private void BrowseCacheDirectory()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择缓存目录"
                };

                // 注意：OpenFolderDialog 在 .NET 中不可用，使用替代方案
                // 这里使用 MessageBox 提示用户手动输入
                MessageBox.Show(
                    "请直接在文本框中输入缓存目录路径，或点击按钮使用默认路径。",
                    "选择缓存目录",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // 设置为默认路径
                CacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RemoteInstaller",
                    "Cache");
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, $"浏览缓存目录失败：{ex.Message}");
                MessageBox.Show($"浏览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 使用默认缓存路径命令
        /// </summary>
        [RelayCommand]
        private void UseDefaultCachePath()
        {
            CacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemoteInstaller",
                "Cache");
            SaveStatus = "已设置为默认缓存路径";
        }

        /// <summary>
        /// 测试仓库连接命令
        /// </summary>
        [RelayCommand]
        private async void TestRepositoryConnection()
        {
            if (string.IsNullOrWhiteSpace(RepositoryUrl))
            {
                MessageBox.Show("请先输入仓库地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 规范化 URL
            var url = RepositoryUrl.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            // 移除末尾的斜杠
            url = url.TrimEnd('/');

            SaveStatus = "正在测试连接...";
            IsSaving = true;

            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                // 设置 Authorization header 如果有 token
                if (!string.IsNullOrWhiteSpace(RepositoryToken))
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RepositoryToken);
                }

                // 尝试访问仓库 API 端点（常用的健康检查或版本检查端点）
                var testEndpoints = new[]
                {
                    $"{url}/api/version",
                    $"{url}/api/v1/version",
                    $"{url}/version",
                    $"{url}/health",
                    $"{url}"
                };

                Exception? lastError = null;
                foreach (var endpoint in testEndpoints)
                {
                    try
                    {
                        var response = await httpClient.GetAsync(endpoint);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            SaveStatus = "连接成功！";
                            _logger?.Log(LogLevel.Success, $"仓库连接测试成功：{url} ({endpoint})");
                            MessageBox.Show(
                                $"仓库连接成功！\n\n地址：{url}\n响应状态：{response.StatusCode}",
                                "连接测试",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }
                        else if ((int)response.StatusCode < 500)
                        {
                            // 4xx 错误表示服务器可达，但路径可能不对
                            // 仍视为连接成功（仓库本身可达）
                            SaveStatus = "连接成功！";
                            _logger?.Log(LogLevel.Success, $"仓库连接成功（{response.StatusCode}）：{url}");
                            MessageBox.Show(
                                $"仓库服务器可达！\n\n地址：{url}\n响应状态：{response.StatusCode}\n\n注意：部分 API 端点可能不存在，但服务器本身可访问。",
                                "连接测试",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        // 尝试下一个端点
                    }
                }

                // 所有端点都失败
                throw lastError ?? new Exception("无法连接到仓库服务器");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                var errorMsg = ex.Message.Contains("name or timeout")
                    ? "无法解析仓库地址，请检查 URL 是否正确"
                    : ex.Message.Contains("timeout")
                        ? "连接超时，请检查网络或仓库地址"
                        : $"连接失败：{ex.Message}";

                SaveStatus = "连接失败";
                _logger?.Log(LogLevel.Error, $"仓库连接测试失败：{errorMsg}");
                MessageBox.Show(
                    $"仓库连接失败！\n\n地址：{url}\n\n{errorMsg}\n\n请检查：\n1. 仓库地址是否正确\n2. 网络是否可达\n3. 仓库服务是否启动",
                    "连接测试失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (TaskCanceledException)
            {
                SaveStatus = "连接超时";
                _logger?.Log(LogLevel.Error, $"仓库连接测试超时：{url}");
                MessageBox.Show(
                    $"连接超时！\n\n地址：{url}\n\n请检查网络连接或尝试增加超时时间。",
                    "连接超时",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                SaveStatus = $"连接失败：{ex.Message}";
                _logger?.Log(LogLevel.Error, $"仓库连接测试失败：{ex.Message}");
                MessageBox.Show(
                    $"连接失败！\n\n地址：{url}\n错误：{ex.Message}",
                    "连接测试失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// 保存设置命令
        /// 将所有设置持久化到 SQLite 数据库
        /// </summary>
        [RelayCommand]
        private async Task SaveSettings()
        {
            try
            {
                IsSaving = true;
                SaveStatus = "正在保存...";

                // 验证输入
                if (!ValidateSettings())
                {
                    SaveStatus = "请修正错误后再保存";
                    IsSaving = false;
                    return;
                }

                // 创建设置对象
                var settings = new SystemSettings
                {
                    RepositoryUrl = RepositoryUrl,
                    RepositoryToken = RepositoryToken,
                    UpdateCheckUrl = UpdateCheckUrl,
                    UseProxy = UseProxy,
                    ProxyType = ProxyType,
                    ProxyHost = ProxyHost,
                    ProxyPort = ProxyPort,
                    ProxyUsername = ProxyUsername,
                    ProxyPassword = ProxyPassword,
                    ConnectionTimeout = ConnectionTimeout,
                    RetryCount = RetryCount,
                    RetryInterval = RetryInterval,
                    CacheDirectory = CacheDirectory,
                    MaxCacheSizeMB = MaxCacheSizeMB,
                    MaxConcurrentTasks = MaxConcurrentTasks,
                    CurrentTheme = CurrentTheme,
                    LastUpdated = DateTime.Now
                };

                // 保存到数据库
                await SaveSettingsToDatabaseAsync(settings);

                SaveStatus = "设置已保存！";
                _logger?.Log(LogLevel.Success, "系统设置已保存");

                // 通知主窗口主题变更
                if (_originalSettings.CurrentTheme != CurrentTheme)
                {
                    OnThemeChanged(CurrentTheme);
                }

                // 延迟关闭对话框
                await Task.Delay(500);
                System.Windows.Application.Current.MainWindow?.Dispatcher.Invoke(() =>
                {
                    var dialog = System.Windows.Application.Current.Windows.OfType<Views.SettingsDialog>().FirstOrDefault();
                    if (dialog != null)
                    {
                        dialog.DialogResult = true;
                        dialog.Close();
                    }
                });
            }
            catch (Exception ex)
            {
                SaveStatus = $"保存失败：{ex.Message}";
                _logger?.Log(LogLevel.Error, $"保存设置失败：{ex.Message}");
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// 取消命令
        /// 放弃所有更改并关闭对话框
        /// </summary>
        [RelayCommand]
        private void CancelSettings()
        {
            System.Windows.Application.Current.MainWindow?.Dispatcher.Invoke(() =>
            {
                var dialog = System.Windows.Application.Current.Windows.OfType<Views.SettingsDialog>().FirstOrDefault();
                if (dialog != null)
                {
                    dialog.DialogResult = false;
                    dialog.Close();
                }
            });
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 从数据库加载设置
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // 从数据库读取设置
                RepositoryUrl = _databaseService.GetSetting("RepositoryUrl", string.Empty);
                RepositoryToken = _databaseService.GetSetting("RepositoryToken", string.Empty);
                UpdateCheckUrl = _databaseService.GetSetting("UpdateCheckUrl", string.Empty);
                UseProxy = bool.TryParse(_databaseService.GetSetting("UseProxy", "false"), out var useProxy) && useProxy;
                ProxyType = (ProxyType)(int.TryParse(_databaseService.GetSetting("ProxyType", "0"), out var proxyType) ? proxyType : 0);
                ProxyHost = _databaseService.GetSetting("ProxyHost", string.Empty);
                ProxyPort = int.TryParse(_databaseService.GetSetting("ProxyPort", "0"), out var proxyPort) ? proxyPort : 0;
                ProxyUsername = _databaseService.GetSetting("ProxyUsername", string.Empty);
                ProxyPassword = _databaseService.GetSetting("ProxyPassword", string.Empty);
                ConnectionTimeout = int.TryParse(_databaseService.GetSetting("ConnectionTimeout", "60"), out var connTimeout) ? connTimeout : 60;
                RetryCount = int.TryParse(_databaseService.GetSetting("RetryCount", "3"), out var retryCount) ? retryCount : 3;
                RetryInterval = int.TryParse(_databaseService.GetSetting("RetryInterval", "5"), out var retryInterval) ? retryInterval : 5;
                CacheDirectory = _databaseService.GetSetting("CacheDirectory",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteInstaller", "Cache"));
                MaxCacheSizeMB = long.TryParse(_databaseService.GetSetting("MaxCacheSizeMB", "5000"), out var cacheSize) ? cacheSize : 5000;
                MaxConcurrentTasks = int.TryParse(_databaseService.GetSetting("MaxConcurrentTasks", "3"), out var maxTasks) ? maxTasks : 3;
                CurrentTheme = (ThemeType)(int.TryParse(_databaseService.GetSetting("CurrentTheme", "0"), out var theme) ? theme : 0);

                // 保存原始设置用于比较
                _originalSettings = new SystemSettings
                {
                    CurrentTheme = CurrentTheme
                };

                _logger?.Log(LogLevel.Info, "系统设置已加载");
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, $"加载设置失败：{ex.Message}");
                // 使用默认值
                CacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RemoteInstaller",
                    "Cache");
            }
        }

        /// <summary>
        /// 将设置保存到数据库
        /// </summary>
        private async Task SaveSettingsToDatabaseAsync(SystemSettings settings)
        {
            await Task.Run(() =>
            {
                _databaseService.SaveSetting("RepositoryUrl", settings.RepositoryUrl);
                _databaseService.SaveSetting("RepositoryToken", settings.RepositoryToken);
                _databaseService.SaveSetting("UpdateCheckUrl", settings.UpdateCheckUrl);
                _databaseService.SaveSetting("UseProxy", settings.UseProxy.ToString());
                _databaseService.SaveSetting("ProxyType", ((int)settings.ProxyType).ToString());
                _databaseService.SaveSetting("ProxyHost", settings.ProxyHost);
                _databaseService.SaveSetting("ProxyPort", settings.ProxyPort.ToString());
                _databaseService.SaveSetting("ProxyUsername", settings.ProxyUsername);
                _databaseService.SaveSetting("ProxyPassword", settings.ProxyPassword);
                _databaseService.SaveSetting("ConnectionTimeout", settings.ConnectionTimeout.ToString());
                _databaseService.SaveSetting("RetryCount", settings.RetryCount.ToString());
                _databaseService.SaveSetting("RetryInterval", settings.RetryInterval.ToString());
                _databaseService.SaveSetting("CacheDirectory", settings.CacheDirectory);
                _databaseService.SaveSetting("MaxCacheSizeMB", settings.MaxCacheSizeMB.ToString());
                _databaseService.SaveSetting("MaxConcurrentTasks", settings.MaxConcurrentTasks.ToString());
                _databaseService.SaveSetting("CurrentTheme", ((int)settings.CurrentTheme).ToString());
            });
        }

        /// <summary>
        /// 验证设置输入
        /// </summary>
        private bool ValidateSettings()
        {
            // 验证代理端口
            if (UseProxy && (ProxyPort < 1 || ProxyPort > 65535))
            {
                MessageBox.Show("代理端口必须在 1-65535 之间", "验证错误", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 验证超时时间
            if (ConnectionTimeout < 10 || ConnectionTimeout > 300)
            {
                MessageBox.Show("连接超时时间必须在 10-300 秒之间", "验证错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 验证重试次数
            if (RetryCount < 0 || RetryCount > 10)
            {
                MessageBox.Show("重试次数必须在 0-10 之间", "验证错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 验证批量检测和批量卸载使用的并发任务数
            if (MaxConcurrentTasks < 1 || MaxConcurrentTasks > 20)
            {
                MessageBox.Show("最大并发任务数必须在 1-20 之间", "验证错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 验证缓存目录
            if (!string.IsNullOrWhiteSpace(CacheDirectory))
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    var result = MessageBox.Show(
                        $"缓存目录不存在：{CacheDirectory}\n是否创建该目录？",
                        "目录不存在",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Directory.CreateDirectory(CacheDirectory);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"创建目录失败：{ex.Message}", "错误",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 主题变更事件
        /// </summary>
        private void OnThemeChanged(ThemeType newTheme)
        {
            // 触发主题变更事件，由主窗口处理
            ThemeChanged?.Invoke(newTheme);
        }

        #endregion

        #region 事件

        /// <summary>
        /// 主题变更事件
        /// </summary>
        public event Action<ThemeType>? ThemeChanged;

        #endregion
    }
}
