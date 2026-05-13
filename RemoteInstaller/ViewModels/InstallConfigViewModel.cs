using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.Services.Operations;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 安装配置对话框 ViewModel
/// </summary>
public partial class InstallConfigViewModel : BaseViewModel
{
    private readonly ApplicationInfo _application;
    private readonly RemoteHost _host;
    private readonly ILogger _logger;
    private readonly bool _isJdkUploadMode;
    private readonly Dictionary<string, string> _parameterValues = new();
    private static readonly AsyncLocal<Func<IEnumerable<string>>?> ScriptRootOverridesFactoryHolder = new();
    internal static Func<IEnumerable<string>>? ScriptRootOverridesFactory
    {
        get => ScriptRootOverridesFactoryHolder.Value;
        set => ScriptRootOverridesFactoryHolder.Value = value;
    }

    [ObservableProperty]
    private string _dialogTitle;

    [ObservableProperty]
    private string _applicationName;

    [ObservableProperty]
    private string _applicationVersion;

    [ObservableProperty]
    private string _targetHost;

    [ObservableProperty]
    private string _packageSource = "local";

    [ObservableProperty]
    private string _localPackagePath = string.Empty;

    [ObservableProperty]
    private bool _isInstallMode = true;

    /// <summary>
    /// 可用版本列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new();

    /// <summary>
    /// 选中的版本
    /// </summary>
    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    /// <summary>
    /// 是否本地包
    /// </summary>
    [ObservableProperty]
    private bool _isLocalPackage;

    /// <summary>
    /// 本地包版本（从文件名提取）
    /// </summary>
    [ObservableProperty]
    private string _localPackageVersion = string.Empty;

    [ObservableProperty]
    private string _localResourceHint = "请将安装资源放入 Scripts 对应应用目录后点击刷新检测。";

    [ObservableProperty]
    private string _remoteUploadPath = string.Empty;

    public bool IsMySqlApplication => IsMySql;

    public bool IsNonMySqlApplication => !IsMySql;

    public bool SupportsOnlineInstall => !IsMariaDb && !IsMosquitto;

    public bool IsJdkApplication => IsJdk;

    public bool IsJdkUploadMode => IsJdk && _isJdkUploadMode;

    public bool ShowVersionSelectionSection => !IsJdkUploadMode;

    public bool ShowPackageSourceSection => !IsJdkUploadMode;

    public bool ShowGenericLocalPackageControls => ShowPackageSourceSection && PackageSource == "local";

    public bool ShowOnlineInstallOption => SupportsOnlineInstall && !IsJdkUploadMode;

    public bool ShowJdkSection => IsJdk;

    public bool ShowJdkVersionSelector => IsJdk && !IsJdkUploadMode;

    public bool ShowJdkLocalControls => IsJdk && PackageSource == "local";

    public bool ShowJdkFolderPicker => IsJdk && PackageSource == "local";

    public bool ShowJdkRefreshButton => IsJdk && !IsJdkUploadMode;

    public bool ShowRefreshDetectionButton => !IsMySql && !IsJdk;

    public bool ShowRefreshDetectionHint => ShowRefreshDetectionButton && PackageSource == "local";

    public bool ShowLocalPackageIntroText => (IsMySql || IsJdk || IsMosquitto) && PackageSource == "local";

    public string LocalPackageInputHint => IsMySql
        ? "请选择 MySQL 本地资源目录"
        : IsJdk
            ? IsJdkUploadMode
                ? "请选择本地 JDK 文件夹"
                : "可选择本地 JDK 目录，或从 Scripts 对应目录自动检测本地资源"
            : IsMosquitto
                ? "请从 Scripts/Mosquitto 对应离线目录自动检测本地资源"
                : "从 Scripts 对应目录自动检测本地资源";

    public string LocalPackageIntroText => IsMySql
        ? "请选择本地 MySQL 离线资源目录"
        : IsJdk
            ? IsJdkUploadMode
                ? "JDK 上传入口会直接根据所选本地 JDK 文件夹识别版本，并上传到远端临时目录后安装"
                : "JDK 支持选择本地目录上传到远端指定位置，也支持继续使用 Scripts 目录中的离线资源"
            : IsMosquitto
                ? "Mosquitto 仅支持离线安装，请将资源放入 Scripts/Mosquitto 对应系统目录后再刷新检测"
                : "推荐优先使用本地资源安装，请将安装包放入 Scripts 对应应用目录后再刷新检测";

    public string LocalOnlyInstallHintText => IsMariaDb
        ? "MariaDB 当前仅支持 Scripts/MariaDB 下的离线资源安装。"
        : IsMosquitto
            ? "Mosquitto 当前仅支持 Scripts/Mosquitto 下的离线资源安装。"
            : "当前应用仅支持离线资源安装。";

    public ObservableCollection<ParameterViewModel> ParameterViewModels { get; } = new();



    /// <summary>
    /// 详细输出 CanConfirm 的检查结果
    /// </summary>
    private void LogCanConfirmDetails()
    {
    }

    /// <summary>
    /// 初始化版本列表
    /// </summary>
    private void InitializeVersions()
    {
        if (_application.Versions != null && _application.Versions.Count > 0)
        {
            foreach (var version in _application.Versions)
            {
                AvailableVersions.Add(version);
            }

            var preferredMosquittoVersion = GetDefaultMosquittoVersionForCurrentHost();

            // 默认选中第一个版本或应用的 SelectedVersion
            if (!string.IsNullOrEmpty(_application.SelectedVersion) && AvailableVersions.Contains(_application.SelectedVersion))
            {
                SelectedVersion = _application.SelectedVersion;
            }
            else if (IsMosquitto && !string.IsNullOrEmpty(preferredMosquittoVersion) && AvailableVersions.Contains(preferredMosquittoVersion))
            {
                SelectedVersion = preferredMosquittoVersion;
            }
            else if (AvailableVersions.Count > 0)
            {
                SelectedVersion = AvailableVersions[0];
            }
        }
        else
        {
            // 如果没有预置版本列表，使用应用默认版本
            if (!string.IsNullOrEmpty(_application.Version))
            {
                AvailableVersions.Add(_application.Version);
                SelectedVersion = _application.Version;
            }
        }
    }

    private string GetDefaultMosquittoVersionForCurrentHost()
    {
        if (!IsMosquitto)
        {
            return string.Empty;
        }

        if (_host.OsType == OperatingSystemType.Windows)
        {
            return "2.0.21";
        }

        if (_host.OsType == OperatingSystemType.Ubuntu && TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor))
        {
            return ubuntuMajor switch
            {
                22 => "2.0.22",
                24 => "2.1.2",
                _ => string.Empty
            };
        }

        if (_host.OsType == OperatingSystemType.CentOS && TryGetMajorVersion(_host.OsVersion, out var centOsMajor) && centOsMajor == 7)
        {
            return "1.6.10";
        }

        return string.Empty;
    }

    private void UpdateDisplayedApplicationVersion()
    {
        var version = _application.Version;

        if (IsMosquitto)
        {
            version = PackageSource == "local" && !string.IsNullOrWhiteSpace(LocalPackageVersion)
                ? LocalPackageVersion
                : !string.IsNullOrWhiteSpace(SelectedVersion)
                    ? SelectedVersion
                    : !string.IsNullOrWhiteSpace(GetDefaultMosquittoVersionForCurrentHost())
                        ? GetDefaultMosquittoVersionForCurrentHost()
                        : _application.Version;
        }

        ApplicationVersion = version;
        DialogTitle = string.IsNullOrWhiteSpace(version)
            ? $"安装 {ApplicationName}"
            : $"安装 {ApplicationName} {version}";
    }

    /// <summary>
    /// 从本地包文件名提取版本
    /// </summary>
    private void ExtractVersionFromLocalPackage()
    {
        if (string.IsNullOrEmpty(LocalPackagePath))
        {
            LocalPackageVersion = string.Empty;
            return;
        }

        try
        {
            var version = Directory.Exists(LocalPackagePath)
                ? ExtractVersionFromLocalPackageDirectory(LocalPackagePath)
                : ExtractVersionFromFileName(Path.GetFileName(LocalPackagePath).ToLower());

            if (!string.IsNullOrEmpty(version))
            {
                LocalPackageVersion = version;

                if (!AvailableVersions.Contains(version))
                {
                    AvailableVersions.Add(version);
                }

                if (!IsJdk)
                {
                    SelectedVersion = version;
                }
            }
            else
            {
                LocalPackageVersion = "未知版本";
            }
        }
        catch
        {
            LocalPackageVersion = "未知版本";
        }
    }

    private string ExtractVersionFromLocalPackageDirectory(string directoryPath)
    {
        if (IsJdk)
        {
            return ExtractVersionFromJdkDirectory(directoryPath);
        }

        if (IsMosquitto && _host.OsType != OperatingSystemType.Windows)
        {
            var mosquittoPackageFile = GetPreferredMosquittoPackageFiles(directoryPath)
                .OrderByDescending(path => IsVersionMatch(path, GetExpectedMosquittoOfflineVersion()))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return string.IsNullOrEmpty(mosquittoPackageFile)
                ? string.Empty
                : ExtractVersionFromFileName(Path.GetFileName(mosquittoPackageFile).ToLower());
        }

        // MySQL 兼容根目录平铺与 mysql 子目录；Redis 仅扫描主包目录，避免误读 deps 依赖版本。
        var searchDirectories = IsMySql
            ? GetMySqlOfflinePackageDirectories(directoryPath)
            : new[] { directoryPath };

        var packageFile = searchDirectories
            .Where(Directory.Exists)
            .SelectMany(path => GetOfflineVersionCandidateFiles(path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(packageFile))
        {
            return ExtractVersionFromFileName(Path.GetFileName(packageFile).ToLower());
        }

        if (IsMosquitto && _host.OsType == OperatingSystemType.Windows)
        {
            return ExtractVersionFromMosquittoWindowsDirectory(directoryPath);
        }

        return string.Empty;
    }

    private string ExtractVersionFromMosquittoWindowsDirectory(string directoryPath)
    {
        var directoryNameVersion = ExtractVersionFromFileName(Path.GetFileName(directoryPath).ToLower());
        if (!string.IsNullOrEmpty(directoryNameVersion))
        {
            return directoryNameVersion;
        }

        var executableVersion = Directory.GetFiles(directoryPath, "mosquitto*.exe", SearchOption.TopDirectoryOnly)
            .Select(path => ExtractVersionFromFileName(Path.GetFileName(path).ToLower()))
            .FirstOrDefault(version => !string.IsNullOrEmpty(version));

        return executableVersion ?? string.Empty;
    }

    private string ExtractVersionFromJdkDirectory(string directoryPath)
    {
        var releaseFilePath = Path.Combine(directoryPath, "release");
        if (File.Exists(releaseFilePath))
        {
            try
            {
                var releaseContent = File.ReadAllText(releaseFilePath);
                var releaseVersion = Regex.Match(releaseContent, "JAVA_VERSION\\s*=\\s*\"(?<version>[^\"]+)\"");
                if (releaseVersion.Success)
                {
                    var normalizedReleaseVersion = NormalizeJdkVersion(releaseVersion.Groups["version"].Value);
                    if (!string.IsNullOrEmpty(normalizedReleaseVersion))
                    {
                        return normalizedReleaseVersion;
                    }
                }
            }
            catch
            {
                // 忽略 release 文件读取异常，继续尝试其他识别方式
            }
        }

        if (!HasJdkDirectoryStructure(directoryPath))
        {
            return string.Empty;
        }

        return NormalizeJdkVersion(ExtractVersionFromJdkText(Path.GetFileName(directoryPath)));
    }

    private static bool HasJdkDirectoryStructure(string directoryPath)
    {
        return File.Exists(Path.Combine(directoryPath, "release"))
            || File.Exists(Path.Combine(directoryPath, "bin", "java.exe"))
            || File.Exists(Path.Combine(directoryPath, "bin", "java"));
    }

    private string ExtractVersionFromJdkText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalizedText = text.Trim();

        if (Regex.IsMatch(normalizedText, @"(?:^|[^0-9])1\.8(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])8u\d+(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])jdk8(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])java-?8(?:[^0-9]|$)", RegexOptions.IgnoreCase))
        {
            return "8";
        }

        if (Regex.IsMatch(normalizedText, @"(?:^|[^0-9])11(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])jdk11(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])java-?11(?:[^0-9]|$)", RegexOptions.IgnoreCase))
        {
            return "11";
        }

        if (Regex.IsMatch(normalizedText, @"(?:^|[^0-9])17(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])jdk17(?:[^0-9]|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalizedText, @"(?:^|[^0-9])java-?17(?:[^0-9]|$)", RegexOptions.IgnoreCase))
        {
            return "17";
        }

        return string.Empty;
    }

    private string NormalizeJdkVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var normalizedVersion = version.Trim();
        if (normalizedVersion.StartsWith("1.8", StringComparison.OrdinalIgnoreCase)
            || normalizedVersion.StartsWith("8", StringComparison.OrdinalIgnoreCase))
        {
            return "8";
        }

        if (normalizedVersion.StartsWith("11", StringComparison.OrdinalIgnoreCase))
        {
            return "11";
        }

        if (normalizedVersion.StartsWith("17", StringComparison.OrdinalIgnoreCase))
        {
            return "17";
        }

        return string.Empty;
    }

    private static readonly char[] InvalidRemoteUploadPathCharacters = new[] { '"', '\'', '`', ';', '\r', '\n' };

    private bool TryValidateRemoteUploadPath(out string? errorMessage)
    {
        errorMessage = null;

        if (!IsJdk || string.IsNullOrWhiteSpace(RemoteUploadPath))
        {
            return true;
        }

        var trimmedPath = RemoteUploadPath.Trim();
        if (trimmedPath.Contains("..", StringComparison.Ordinal))
        {
            errorMessage = "远端上传目录不能包含 .. 路径跳转。";
            return false;
        }

        if (trimmedPath.IndexOfAny(InvalidRemoteUploadPathCharacters) >= 0)
        {
            errorMessage = "远端上传目录不能包含引号、分号、反引号或换行。";
            return false;
        }

        if (_host.OsType == OperatingSystemType.Windows)
        {
            var normalizedPath = trimmedPath.Replace("/", "\\");
            if (!Regex.IsMatch(normalizedPath, @"^[a-zA-Z]:\\"))
            {
                errorMessage = "Windows 远端上传目录必须使用绝对路径，例如 C:\\Windows\\Temp\\jdk-upload。";
                return false;
            }
        }
        else
        {
            var normalizedPath = trimmedPath.Replace("\\", "/");
            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            {
                errorMessage = "Linux 远端上传目录必须使用绝对路径，例如 /tmp/jdk-upload。";
                return false;
            }
        }

        return true;
    }

    private IEnumerable<string> GetOfflineVersionCandidateFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Enumerable.Empty<string>();
        }

        if (IsRedis)
        {
            var redisPatterns = _host.OsType switch
            {
                OperatingSystemType.CentOS => new[] { "redis-*.rpm" },
                OperatingSystemType.Ubuntu => new[] { "redis-server*.deb", "redis-*.tar.gz", "redis-*.tgz", "redis-*.tar.xz" },
                _ => Array.Empty<string>()
            };

            return redisPatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly));
        }

        if (IsRabbitMq)
        {
            var rabbitMqPatterns = _host.OsType switch
            {
                OperatingSystemType.CentOS => new[] { "rabbitmq-server-*.rpm" },
                OperatingSystemType.Ubuntu => new[] { "rabbitmq-server*.deb", "rabbitmq-server*.tar.gz", "rabbitmq-server*.tgz", "rabbitmq-server*.tar.xz" },
                _ => Array.Empty<string>()
            };

            return rabbitMqPatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly));
        }

        if (IsNginx)
        {
            var nginxPatterns = _host.OsType switch
            {
                OperatingSystemType.CentOS => new[] { "nginx-*.rpm" },
                OperatingSystemType.Ubuntu => new[] { "nginx_*.deb", "nginx-common_*.deb" },
                _ => Array.Empty<string>()
            };

            return nginxPatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly));
        }

        if (IsElasticsearch)
        {
            var elasticsearchPatterns = _host.OsType switch
            {
                OperatingSystemType.CentOS => new[] { "elasticsearch-*.rpm", "elasticsearch-*.tar.gz", "elasticsearch-*.tar" },
                OperatingSystemType.Ubuntu => new[] { "elasticsearch-*.deb", "elasticsearch-*.tar.gz", "elasticsearch-*.tar" },
                _ => Array.Empty<string>()
            };

            return elasticsearchPatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly));
        }

        if (IsMosquitto)
        {
            var mosquittoPatterns = _host.OsType switch
            {
                OperatingSystemType.CentOS => new[] { "mosquitto-*.rpm" },
                OperatingSystemType.Ubuntu => new[] { "mosquitto_*.deb", "mosquitto-*.deb" },
                OperatingSystemType.Windows => new[] { "mosquitto-*.zip" },
                _ => Array.Empty<string>()
            };

            return mosquittoPatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly));
        }

        return Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => GetPackageExtensions().Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 从文件名提取版本号
    /// </summary>
    private string ExtractVersionFromFileName(string fileName)
    {
        // 移除扩展名
        var nameWithoutExt = fileName;
        var extIndices = new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".deb", ".rpm" };
        foreach (var ext in extIndices)
        {
            if (nameWithoutExt.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - ext.Length);
                break;
            }
        }

        // 尝试匹配版本号模式（数字。数字.数字）
        // 例如：elasticsearch-8.11.0, redis_7.2.3, mysql-8.0.35
        var separators = new[] { '-', '_' };
        var parts = nameWithoutExt.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        // 查找看起来像版本号的最后一个部分（包含点）
        foreach (var part in parts.Reverse())
        {
            // 处理 v1.2.3 这种情况
            var cleanPart = part.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? part.Substring(1) : part;

            // 版本号通常包含点和数字，如 8.0.35, 7.2.3, 1.24.0
            // 排除架构信息（如 amd64, x86_64）
            if (cleanPart.Contains('.') &&
                cleanPart.All(c => char.IsDigit(c) || c == '.') &&
                !part.Contains("amd64") &&
                !part.Contains("x86_64") &&
                !part.Contains("i386") &&
                !part.Contains("arm64"))
            {
                return part;
            }
        }

        // 如果没有找到，尝试使用正则表达式匹配
        var versionPattern = System.Text.RegularExpressions.Regex.Match(nameWithoutExt, @"\d+\.\d+(?:\.\d+)?");
        if (versionPattern.Success)
        {
            var version = versionPattern.Value;
            return version;
        }

        return string.Empty;
    }

    /// <summary>
    /// SelectedVersion 改变时的处理
    /// </summary>
    partial void OnSelectedVersionChanged(string value)
    {
        if (IsJdk && PackageSource == "local" && !IsJdkUploadMode)
        {
            InitializeLocalResourceDefaults();
        }

        UpdateDisplayedApplicationVersion();
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// LocalPackageVersion 改变时的处理
    /// </summary>
    partial void OnLocalPackageVersionChanged(string value)
    {
        if (IsJdkUploadMode)
        {
            SelectedVersion = NormalizeJdkVersion(value);
        }

        UpdateDisplayedApplicationVersion();
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// PackageSource 改变时的处理
    /// </summary>
    partial void OnPackageSourceChanged(string value)
    {
        if (IsJdkUploadMode && value != "local")
        {
            PackageSource = "local";
            return;
        }

        if (value == "local")
        {
            IsLocalPackage = true;
            // 本地包时，版本从文件名提取，不可修改
            if (!string.IsNullOrEmpty(LocalPackagePath))
            {
                ExtractVersionFromLocalPackage();
            }
        }
        else
        {
            IsLocalPackage = false;
            LocalPackageVersion = string.Empty;
        }

        UpdateDisplayedApplicationVersion();
        OnPropertyChanged(nameof(ShowVersionSelectionSection));
        OnPropertyChanged(nameof(ShowPackageSourceSection));
        OnPropertyChanged(nameof(ShowGenericLocalPackageControls));
        OnPropertyChanged(nameof(ShowOnlineInstallOption));
        OnPropertyChanged(nameof(ShowJdkSection));
        OnPropertyChanged(nameof(ShowJdkVersionSelector));
        OnPropertyChanged(nameof(ShowJdkLocalControls));
        OnPropertyChanged(nameof(ShowJdkFolderPicker));
        OnPropertyChanged(nameof(ShowJdkRefreshButton));
        OnPropertyChanged(nameof(ShowRefreshDetectionButton));
        OnPropertyChanged(nameof(ShowRefreshDetectionHint));
        OnPropertyChanged(nameof(ShowLocalPackageIntroText));
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// LocalPackagePath 改变时的处理
    /// </summary>
    partial void OnLocalPackagePathChanged(string value)
    {
        if (PackageSource == "local" && !string.IsNullOrEmpty(value))
        {
            ExtractVersionFromLocalPackage();
        }

        OnPropertyChanged(nameof(ShowJdkSection));
        OnPropertyChanged(nameof(ShowGenericLocalPackageControls));
        OnPropertyChanged(nameof(ShowJdkLocalControls));
        OnPropertyChanged(nameof(ShowJdkFolderPicker));
        OnPropertyChanged(nameof(ShowJdkRefreshButton));
        NotifyCanExecuteChanged();
    }

    partial void OnRemoteUploadPathChanged(string value)
    {
        NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 初始化参数
    /// </summary>
    private void InitializeParameters()
    {
        if (_application.Parameters == null)
        {
            _application.Parameters = new List<InstallParameter>();
        }

        foreach (var param in _application.Parameters)
        {
            NormalizeMosquittoCredentialParameter(param);

            var paramVm = new ParameterViewModel(param);
            // 设置回调，当参数值变化时触发 CanExecute 重新评估
            paramVm.OnValueChangeNotify = () =>
            {
                NotifyCanExecuteChanged();
            };
            ParameterViewModels.Add(paramVm);
            _parameterValues[param.Key] = param.DefaultValue ?? string.Empty;
        }
    }

    private void NormalizeMosquittoCredentialParameter(InstallParameter parameter)
    {
        if (!IsMosquitto)
        {
            return;
        }

        if (!string.Equals(parameter.Key, "USERNAME", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parameter.Key, "PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        parameter.Required = false;
        parameter.DefaultValue = string.Empty;
    }

    private string GetParameterValue(string key)
    {
        return ParameterViewModels.FirstOrDefault(param => string.Equals(param.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private bool TryValidateMosquittoCredentialPair(out string errorMessage)
    {
        var username = GetParameterValue("USERNAME");
        var password = GetParameterValue("PASSWORD");
        var hasUsername = !string.IsNullOrWhiteSpace(username);
        var hasPassword = !string.IsNullOrWhiteSpace(password);

        if (hasUsername == hasPassword)
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = "Mosquitto 用户名和密码必须同时填写，或同时留空以启用匿名访问。";
        return false;
    }

    private void InitializeLocalResourceDefaults()
    {
        if (IsJdkUploadMode)
        {
            PackageSource = "local";
            LocalPackagePath = string.Empty;
            LocalPackageVersion = string.Empty;
            SelectedVersion = string.Empty;
            _application.LocalPackagePath = string.Empty;
            _application.UseLocalPackage = false;
            LocalResourceHint = "请选择本地 JDK 文件夹，系统会自动识别 JDK 8 / 11 / 17 版本。";
            return;
        }

        if (TryResolveLocalPackage(out var packagePath, out var hint))
        {
            PackageSource = "local";
            LocalPackagePath = packagePath;
            _application.LocalPackagePath = packagePath;
            _application.UseLocalPackage = true;
            LocalResourceHint = hint;
            _logger?.Info($"默认使用 Scripts 本地资源：{packagePath}");
            return;
        }

        LocalPackagePath = string.Empty;
        LocalPackageVersion = string.Empty;
        _application.LocalPackagePath = string.Empty;
        _application.UseLocalPackage = false;
        LocalResourceHint = hint;
    }

    private bool TryResolveLocalPackage(out string packagePath, out string hint)
    {
        if (IsRabbitMq)
        {
            return TryResolveRabbitMqLocalPackage(out packagePath, out hint);
        }

        if (IsRedis)
        {
            return TryResolveRedisLocalPackage(out packagePath, out hint);
        }

        if (IsNginx)
        {
            return TryResolveNginxLocalPackage(out packagePath, out hint);
        }

        if (IsElasticsearch)
        {
            return TryResolveElasticsearchLocalPackage(out packagePath, out hint);
        }

        if (IsMySql)
        {
            return TryResolveMySqlLocalPackage(out packagePath, out hint);
        }

        if (IsMariaDb)
        {
            return TryResolveMariaDbLocalPackage(out packagePath, out hint);
        }

        if (IsMosquitto)
        {
            return TryResolveMosquittoLocalPackage(out packagePath, out hint);
        }

        if (IsConsul)
        {
            return TryResolveConsulLocalPackage(out packagePath, out hint);
        }

        if (IsTraefik)
        {
            return TryResolveTraefikLocalPackage(out packagePath, out hint);
        }

        if (IsJdk)
        {
            return TryResolveJdkLocalPackage(out packagePath, out hint);
        }

        var roots = GetApplicationScriptRoots();
        var packageExtensions = GetPackageExtensions();

        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = TryFindPreferredGenericPackageFile(root, packageExtensions);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    packagePath = selectedPath;
                    hint = $"已从 Scripts 目录自动匹配本地资源：{selectedPath}";
                    return true;
                }
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts 对应目录找到本地资源，请将安装包放入 Scripts/{_application.Name} 或 Scripts/{_application.Id} 后点击刷新检测。";
        return false;
    }

    private bool TryResolveRedisLocalPackage(out string packagePath, out string hint)
    {
        return TryResolveRedisLocalPackage(_application.Version, _host, out packagePath, out hint);
    }

    public static bool TryResolveRedisLocalPackage(string version, RemoteHost host, out string packagePath, out string hint)
    {
        if (!TryGetCompatibleRedisOfflineFolders(host, out var offlineFolders, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        string? validationHint = null;
        var packagePatterns = GetRedisMainPackagePatterns(host.OsType);

        foreach (var offlineFolder in offlineFolders)
        {
            foreach (var root in GetRedisScriptRoots(offlineFolder))
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    if (!TryValidateRedisOfflineDirectory(root, host, out var currentHint))
                    {
                        validationHint ??= currentHint;
                        continue;
                    }

                    var selectedPath = packagePatterns
                        .SelectMany(pattern => Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
                        .OrderByDescending(path => IsVersionMatch(path, version))
                        .ThenByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                    if (string.IsNullOrEmpty(selectedPath))
                    {
                        continue;
                    }

                    packagePath = root;
                    hint = $"已从 Scripts 目录自动匹配 Redis 本地资源目录：{root}";
                    return true;
                }
                catch
                {
                    // 忽略当前路径异常，继续尝试其他路径
                }
            }
        }

        packagePath = string.Empty;
        hint = validationHint ?? $"未在 Scripts/Redis/{string.Join(" 或 ", offlineFolders)} 中找到 {GetRedisOfflinePlatformDisplayName(host)} 可用本地资源。";
        return false;
    }

    private static bool TryGetCompatibleRedisOfflineFolders(RemoteHost host, out IReadOnlyList<string> offlineFolders, out string hint)
    {
        offlineFolders = Array.Empty<string>();

        if (host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 Redis 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolders = new[] { Path.Combine("redis-ubuntu", "22") };
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolders = new[] { Path.Combine("redis-ubuntu", "24"), "redis-ubuntu" };
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {host.OsVersion} 与本地 Redis 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 Redis 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {host.OsVersion} 与本地 Redis 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolders = new[] { "redis-centos7" };
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 Redis 本地资源目录。";
        return false;
    }

    private static bool TryValidateRedisOfflinePath(string path, RemoteHost host, out string hint)
    {
        if (Directory.Exists(path))
        {
            return TryValidateRedisOfflineDirectory(path, host, out hint);
        }

        if (!File.Exists(path))
        {
            hint = $"Scripts 对应目录中的本地资源不存在：{path}";
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            hint = $"无法定位 Redis 本地资源所属目录：{path}";
            return false;
        }

        return TryValidateRedisOfflineDirectory(parentDirectory, host, out hint);
    }

    private bool TryValidateRedisOfflinePath(string path, out string hint)
    {
        return TryValidateRedisOfflinePath(path, _host, out hint);
    }

    private static bool TryValidateRedisOfflineDirectory(string root, RemoteHost host, out string hint)
    {
        if (!TryValidateRedisOfflineDirectoryCompatibility(root, host, out hint))
        {
            return false;
        }

        if (host.OsType == OperatingSystemType.Ubuntu)
        {
            var serverPackageExists = Directory.GetFiles(root, "redis-server*.deb", SearchOption.TopDirectoryOnly).Any();
            if (!serverPackageExists)
            {
                hint = $"{GetRedisOfflinePlatformDisplayName(host)} 离线资源目录缺少主包：redis-server*.deb。请补齐后再点击刷新检测。";
                return false;
            }

            var toolsPackageExists = Directory.GetFiles(root, "redis-tools*.deb", SearchOption.TopDirectoryOnly).Any();
            if (!toolsPackageExists)
            {
                hint = $"{GetRedisOfflinePlatformDisplayName(host)} 离线资源目录缺少工具包：redis-tools*.deb。请补齐后再点击刷新检测。";
                return false;
            }

            hint = string.Empty;
            return true;
        }

        if (host.OsType == OperatingSystemType.CentOS)
        {
            var rpmPackageExists = Directory.GetFiles(root, "redis-*.rpm", SearchOption.TopDirectoryOnly).Any();
            if (!rpmPackageExists)
            {
                hint = $"{GetRedisOfflinePlatformDisplayName(host)} 离线资源目录缺少主包：redis-*.rpm。请补齐后再点击刷新检测。";
                return false;
            }

            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 Redis 本地资源目录。";
        return false;
    }

    private static bool TryValidateRedisOfflineDirectoryCompatibility(string root, RemoteHost host, out string hint)
    {
        hint = string.Empty;

        if (host.OsType != OperatingSystemType.Ubuntu)
        {
            return true;
        }

        if (!TryGetMajorVersion(host.OsVersion, out var ubuntuMajor))
        {
            hint = "未检测到 Ubuntu 版本，无法验证 Redis 离线目录，请先完成连接测试。";
            return false;
        }

        var segments = root
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var redisUbuntuIndex = Array.FindLastIndex(segments, segment =>
            string.Equals(segment, "redis-ubuntu", StringComparison.OrdinalIgnoreCase));

        if (redisUbuntuIndex < 0)
        {
            return true;
        }

        if (redisUbuntuIndex + 1 < segments.Length &&
            int.TryParse(segments[redisUbuntuIndex + 1], out var directoryUbuntuMajor))
        {
            if (directoryUbuntuMajor == ubuntuMajor)
            {
                return true;
            }

            hint = $"Redis Ubuntu {ubuntuMajor} 需要使用 Scripts/Redis/redis-ubuntu/{ubuntuMajor} 离线目录，当前选择的是 redis-ubuntu/{directoryUbuntuMajor}：{root}";
            return false;
        }

        if (ubuntuMajor == 24)
        {
            return true;
        }

        hint = $"Redis Ubuntu {ubuntuMajor} 需要使用 Scripts/Redis/redis-ubuntu/{ubuntuMajor} 离线目录；当前 legacy redis-ubuntu 目录内置的是 Ubuntu 24.04 包：{root}";
        return false;
    }

    private static string[] GetRedisMainPackagePatterns(OperatingSystemType osType)
    {
        return osType switch
        {
            OperatingSystemType.CentOS => new[] { "redis-*.rpm" },
            OperatingSystemType.Ubuntu => new[] { "redis-server*.deb" },
            _ => Array.Empty<string>()
        };
    }

    private static string GetRedisOfflinePlatformDisplayName(RemoteHost host)
    {
        if (host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryGetMajorVersion(host.OsVersion, out var ubuntuMajor)
                ? $"Redis Ubuntu {ubuntuMajor}"
                : "Redis Ubuntu";
        }

        if (host.OsType == OperatingSystemType.CentOS)
        {
            return TryGetMajorVersion(host.OsVersion, out var centOsMajor)
                ? $"Redis CentOS {centOsMajor}"
                : "Redis CentOS";
        }

        return "Redis";
    }

    private bool TryResolveNginxLocalPackage(out string packagePath, out string hint)
    {
        if (!TryGetCompatibleNginxOfflineFolder(out var offlineFolder, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        string? validationHint = null;

        foreach (var root in GetNginxScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                if (!TryValidateNginxOfflineDirectory(root, out var currentHint))
                {
                    validationHint ??= currentHint;
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 Nginx 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = validationHint ?? $"未在 Scripts/Nginx/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryGetCompatibleNginxOfflineFolder(out string offlineFolder, out string hint)
    {
        offlineFolder = string.Empty;

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 Nginx 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolder = Path.Combine("nginx-ubuntu", "22");
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolder = Path.Combine("nginx-ubuntu", "24");
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {_host.OsVersion} 与本地 Nginx 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 Nginx 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {_host.OsVersion} 与本地 Nginx 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolder = "nginx-centos7";
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 Nginx 本地资源目录。";
        return false;
    }

    private bool TryValidateNginxOfflineDirectory(string root, out string hint)
    {
        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryValidateNginxUbuntuOfflineDirectory(root, out hint);
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryValidateNginxCentOsOfflineDirectory(root, out hint);
        }

        hint = "当前系统未配置 Nginx 本地资源目录。";
        return false;
    }

    private bool TryValidateNginxOfflinePath(string path, out string hint)
    {
        if (Directory.Exists(path))
        {
            return TryValidateNginxOfflineDirectory(path, out hint);
        }

        if (!File.Exists(path))
        {
            hint = $"Scripts 对应目录中的本地资源不存在：{path}";
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            hint = $"无法定位 Nginx 本地资源所属目录：{path}";
            return false;
        }

        return TryValidateNginxOfflineDirectory(parentDirectory, out hint);
    }

    private bool TryValidateNginxUbuntuOfflineDirectory(string root, out string hint)
    {
        var mainPackageExists = Directory.GetFiles(root, "nginx_*.deb", SearchOption.TopDirectoryOnly).Any();
        if (!mainPackageExists)
        {
            hint = $"{GetNginxOfflinePlatformDisplayName()} 离线资源目录缺少主包：nginx_*.deb。请补齐后再点击刷新检测。";
            return false;
        }

        if (RequiresNginxCommonPackage())
        {
            var commonPackageExists = Directory.GetFiles(root, "nginx-common_*.deb", SearchOption.TopDirectoryOnly).Any();
            if (!commonPackageExists)
            {
                hint = $"{GetNginxOfflinePlatformDisplayName()} 离线资源目录缺少公共包：nginx-common_*.deb。请补齐后再点击刷新检测。";
                return false;
            }
        }

        var dependencyFileNames = GetNginxOfflineDependencyDirectories(root)
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.deb", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var missingDependencies = GetRequiredNginxUbuntuDebPrefixes()
            .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingDependencies.Count > 0)
        {
            hint = $"{GetNginxOfflinePlatformDisplayName()} 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。";
            return false;
        }

        hint = string.Empty;
        return true;
    }

    private bool TryValidateNginxCentOsOfflineDirectory(string root, out string hint)
    {
        var mainPackageExists = Directory.GetFiles(root, "nginx-*.rpm", SearchOption.TopDirectoryOnly).Any();
        if (!mainPackageExists)
        {
            hint = $"{GetNginxOfflinePlatformDisplayName()} 离线资源目录缺少主包：nginx-*.rpm。请补齐后再点击刷新检测。";
            return false;
        }

        var dependencyFileNames = GetNginxOfflineDependencyDirectories(root)
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.rpm", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var missingDependencies = GetRequiredNginxCentOsRpmPrefixes()
            .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingDependencies.Count > 0)
        {
            hint = $"{GetNginxOfflinePlatformDisplayName()} 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。";
            return false;
        }

        hint = string.Empty;
        return true;
    }

    private static IEnumerable<string> GetNginxOfflineDependencyDirectories(string root)
    {
        return new[]
        {
            Path.Combine(root, "deps"),
            root
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> GetRequiredNginxUbuntuDebPrefixes()
    {
        if (TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor) && ubuntuMajor == 24)
        {
            return new[] { "iproute2", "libssl3t64", "libc6", "libcrypt1", "libpcre2-8-0", "zlib1g" };
        }

        return new[] { "adduser", "lsb-base", "libssl3", "libc6", "libcrypt1", "libpcre2-8-0", "zlib1g" };
    }

    private bool RequiresNginxCommonPackage()
    {
        return TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor) && ubuntuMajor == 24;
    }

    private static IReadOnlyList<string> GetRequiredNginxCentOsRpmPrefixes()
    {
        return new[] { "pcre2-", "openssl-libs-", "glibc-", "procps-ng-", "shadow-utils-", "systemd-" };
    }

    private string GetNginxOfflinePlatformDisplayName()
    {
        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor)
                ? $"Nginx Ubuntu {ubuntuMajor}"
                : "Nginx Ubuntu";
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryGetMajorVersion(_host.OsVersion, out var centOsMajor)
                ? $"Nginx CentOS {centOsMajor}"
                : "Nginx CentOS";
        }

        return "Nginx";
    }

    private bool TryResolveElasticsearchLocalPackage(out string packagePath, out string hint)
    {
        if (!TryGetCompatibleElasticsearchOfflineFolder(out var offlineFolder, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        string? validationHint = null;

        foreach (var root in GetElasticsearchScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                if (!TryValidateElasticsearchOfflineDirectory(root, out var currentHint))
                {
                    validationHint ??= currentHint;
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 Elasticsearch 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = validationHint ?? $"未在 Scripts/Elasticsearch/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryGetCompatibleElasticsearchOfflineFolder(out string offlineFolder, out string hint)
    {
        offlineFolder = string.Empty;

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 Elasticsearch 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolder = Path.Combine("elasticsearch-ubuntu", "22");
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolder = Path.Combine("elasticsearch-ubuntu", "24");
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {_host.OsVersion} 与本地 Elasticsearch 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 Elasticsearch 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {_host.OsVersion} 与本地 Elasticsearch 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolder = "elasticsearch-centos7";
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 Elasticsearch 本地资源目录。";
        return false;
    }

    private bool TryValidateElasticsearchOfflineDirectory(string root, out string hint)
    {
        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryValidateElasticsearchUbuntuOfflineDirectory(root, out hint);
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryValidateElasticsearchCentOsOfflineDirectory(root, out hint);
        }

        hint = "当前系统未配置 Elasticsearch 本地资源目录。";
        return false;
    }

    private bool TryValidateElasticsearchOfflinePath(string path, out string hint)
    {
        if (Directory.Exists(path))
        {
            return TryValidateElasticsearchOfflineDirectory(path, out hint);
        }

        if (!File.Exists(path))
        {
            hint = $"Scripts 对应目录中的本地资源不存在：{path}";
            return false;
        }

        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            hint = "Elasticsearch Linux 本地资源仅支持目录型离线包，请选择包含主包和 deps 的离线目录。";
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            hint = $"无法定位 Elasticsearch 本地资源所属目录：{path}";
            return false;
        }

        return TryValidateElasticsearchOfflineDirectory(parentDirectory, out hint);
    }

    private bool TryValidateElasticsearchUbuntuOfflineDirectory(string root, out string hint)
    {
        var expectedVersion = GetExpectedElasticsearchOfflineVersion();
        var mainPackagePatterns = new[] { $"elasticsearch-{expectedVersion}*.deb" };
        var mainPackageExists = mainPackagePatterns
            .SelectMany(pattern => Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
            .Any();

        if (!mainPackageExists)
        {
            hint = $"{GetElasticsearchOfflinePlatformDisplayName()} 离线资源目录缺少主包：elasticsearch-{expectedVersion}*.deb。请补齐后再点击刷新检测。";
            return false;
        }

        var dependencyFileNames = GetElasticsearchOfflineDependencyDirectories(root)
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.deb", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var missingDependencies = GetRequiredElasticsearchUbuntuDebPrefixes()
            .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingDependencies.Count > 0)
        {
            hint = $"{GetElasticsearchOfflinePlatformDisplayName()} 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。";
            return false;
        }

        hint = string.Empty;
        return true;
    }

    private bool TryValidateElasticsearchCentOsOfflineDirectory(string root, out string hint)
    {
        var expectedVersion = GetExpectedElasticsearchOfflineVersion();
        var mainPackageExists = Directory.GetFiles(root, $"elasticsearch-{expectedVersion}*.rpm", SearchOption.TopDirectoryOnly).Any();

        if (!mainPackageExists)
        {
            hint = $"{GetElasticsearchOfflinePlatformDisplayName()} 离线资源目录缺少主包：elasticsearch-{expectedVersion}*.rpm。请补齐后再点击刷新检测。";
            return false;
        }

        var dependencyFileNames = GetElasticsearchOfflineDependencyDirectories(root)
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.rpm", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var missingDependencies = GetRequiredElasticsearchCentOsRpmPrefixes()
            .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingDependencies.Count > 0)
        {
            hint = $"{GetElasticsearchOfflinePlatformDisplayName()} 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。";
            return false;
        }

        hint = string.Empty;
        return true;
    }

    private static IEnumerable<string> GetElasticsearchOfflineDependencyDirectories(string root)
    {
        // 兼容新的 deps 目录以及旧版平铺目录，避免历史资源立即失效。
        return new[]
        {
            Path.Combine(root, "deps"),
            root
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetRequiredElasticsearchUbuntuDebPrefixes()
    {
        return new[] { "bash", "lsb-base", "libc6", "adduser", "coreutils" };
    }

    private static IReadOnlyList<string> GetRequiredElasticsearchCentOsRpmPrefixes()
    {
        return new[] { "bash-", "coreutils-" };
    }

    private string GetExpectedElasticsearchOfflineVersion()
    {
        return string.IsNullOrWhiteSpace(_application.Version) ? "8.5.3" : _application.Version;
    }

    private string GetElasticsearchOfflinePlatformDisplayName()
    {
        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor)
                ? $"Elasticsearch Ubuntu {ubuntuMajor}"
                : "Elasticsearch Ubuntu";
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryGetMajorVersion(_host.OsVersion, out var centOsMajor)
                ? $"Elasticsearch CentOS {centOsMajor}"
                : "Elasticsearch CentOS";
        }

        return "Elasticsearch";
    }

    private bool TryResolveMySqlLocalPackage(out string packagePath, out string hint)
    {
        if (!TryGetCompatibleMySqlOfflineFolder(out var offlineFolder, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        string[] packagePatterns = _host.OsType switch
        {
            OperatingSystemType.CentOS => new[] { "mysql-community-server-*.rpm", "mysql-server-*.rpm" },
            OperatingSystemType.Ubuntu => new[] { "mysql-server*.deb", "mysql-community-server*.deb" },
            _ => Array.Empty<string>()
        };

        foreach (var root in GetMySqlScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = GetMySqlOfflinePackageDirectories(root)
                    .Where(Directory.Exists)
                    .SelectMany(searchRoot => packagePatterns
                        .SelectMany(pattern => Directory.GetFiles(searchRoot, pattern, SearchOption.TopDirectoryOnly)))
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 MySQL 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/MySQL/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryGetCompatibleMySqlOfflineFolder(out string offlineFolder, out string hint)
    {
        offlineFolder = string.Empty;

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 MySQL 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolder = Path.Combine("mysql-ubuntu", "22");
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolder = Path.Combine("mysql-ubuntu", "24");
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {_host.OsVersion} 与本地 MySQL 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 MySQL 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {_host.OsVersion} 与本地 MySQL 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolder = "mysql-centos7";
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 MySQL 本地资源目录。";
        return false;
    }

    private bool TryResolveMariaDbLocalPackage(out string packagePath, out string hint)
    {
        return TryResolveMariaDbLocalPackage(_application.Version, _host, out packagePath, out hint);
    }

    public static bool TryResolveMariaDbLocalPackage(string version, RemoteHost host, out string packagePath, out string hint)
    {
        if (!TryGetCompatibleMariaDbOfflineFolder(host, out var offlineFolder, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        string[] packagePatterns = host.OsType switch
        {
            OperatingSystemType.CentOS => new[] { "MariaDB-server-*.rpm", "mariadb-server-*.rpm" },
            OperatingSystemType.Ubuntu => new[] { "mariadb-server_*.deb", "mariadb-server-core_*.deb" },
            _ => Array.Empty<string>()
        };

        foreach (var root in GetMariaDbScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = packagePatterns
                    .SelectMany(pattern => Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
                    .OrderByDescending(path => IsVersionMatch(path, version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 MariaDB 本地资源目录：{root}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/MariaDB/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryGetCompatibleMariaDbOfflineFolder(out string offlineFolder, out string hint)
    {
        return TryGetCompatibleMariaDbOfflineFolder(_host, out offlineFolder, out hint);
    }

    public static bool TryGetCompatibleMariaDbOfflineFolder(RemoteHost host, out string offlineFolder, out string hint)
    {
        offlineFolder = string.Empty;

        if (host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 MariaDB 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolder = Path.Combine("mariadb-ubuntu", "22");
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolder = Path.Combine("mariadb-ubuntu", "24");
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {host.OsVersion} 与本地 MariaDB 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 MariaDB 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {host.OsVersion} 与本地 MariaDB 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolder = "mariadb-centos7";
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 MariaDB 本地资源目录。";
        return false;
    }

    private static bool TryGetMajorVersion(string version, out int majorVersion)
    {
        majorVersion = 0;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var match = Regex.Match(version, @"\d+");
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Value, out majorVersion);
    }

    private bool TryResolveMosquittoLocalPackage(out string packagePath, out string hint)
    {
        if (!TryGetCompatibleMosquittoOfflineFolder(out var offlineFolder, out hint))
        {
            packagePath = string.Empty;
            return false;
        }

        if (_host.OsType == OperatingSystemType.Windows)
        {
            return TryResolveMosquittoWindowsLocalPackage(offlineFolder, out packagePath, out hint);
        }

        string? validationHint = null;

        foreach (var root in GetMosquittoScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                if (!TryValidateMosquittoOfflineDirectory(root, out var currentHint))
                {
                    validationHint ??= currentHint;
                    continue;
                }

                packagePath = root;
                hint = $"已从 Scripts 目录自动匹配 Mosquitto 本地资源目录：{root}";
                return true;
            }
            catch
            {
            }
        }

        packagePath = string.Empty;
        hint = validationHint ?? $"未在 Scripts/Mosquitto/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveMosquittoWindowsLocalPackage(string offlineFolder, out string packagePath, out string hint)
    {
        string? validationHint = null;
        var candidates = new List<(string Path, bool IsDirectory, bool VersionMatch, DateTime LastWriteTimeUtc)>();

        foreach (var root in GetMosquittoScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                candidates.AddRange(GetMosquittoWindowsZipFiles(root)
                    .Select(path => (
                        Path: path,
                        IsDirectory: false,
                        VersionMatch: IsVersionMatch(path, GetExpectedMosquittoOfflineVersion()),
                        LastWriteTimeUtc: File.GetLastWriteTimeUtc(path))));

                foreach (var directory in GetMosquittoWindowsExtractedDirectories(root))
                {
                    if (!TryValidateMosquittoWindowsOfflineDirectory(directory, out var currentHint))
                    {
                        validationHint ??= currentHint;
                        continue;
                    }

                    var directoryVersion = ExtractVersionFromMosquittoWindowsDirectory(directory);
                    candidates.Add((
                        Path: directory,
                        IsDirectory: true,
                        VersionMatch: string.Equals(directoryVersion, GetExpectedMosquittoOfflineVersion(), StringComparison.OrdinalIgnoreCase)
                            || IsVersionMatch(directory, GetExpectedMosquittoOfflineVersion()),
                        LastWriteTimeUtc: Directory.GetLastWriteTimeUtc(directory)));
                }
            }
            catch
            {
            }
        }

        if (candidates.Count > 0)
        {
            var selected = candidates
                .OrderByDescending(candidate => candidate.VersionMatch)
                .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
                .First();

            packagePath = selected.Path;
            hint = selected.IsDirectory
                ? $"已从 Scripts 目录自动匹配 Mosquitto Windows 离线目录：{selected.Path}"
                : $"已从 Scripts 目录自动匹配 Mosquitto Windows 离线包：{selected.Path}";
            return true;
        }

        packagePath = string.Empty;
        hint = validationHint ?? $"未在 Scripts/Mosquitto/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryGetCompatibleMosquittoOfflineFolder(out string offlineFolder, out string hint)
    {
        offlineFolder = string.Empty;

        if (_host.OsType == OperatingSystemType.Windows)
        {
            offlineFolder = "windows";
            hint = string.Empty;
            return true;
        }

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor))
            {
                hint = "未检测到 Ubuntu 版本，无法自动选择 Mosquitto 离线目录，请先完成连接测试。";
                return false;
            }

            if (ubuntuMajor == 22)
            {
                offlineFolder = Path.Combine("mosquitto-ubuntu", "22");
                hint = string.Empty;
                return true;
            }

            if (ubuntuMajor == 24)
            {
                offlineFolder = Path.Combine("mosquitto-ubuntu", "24");
                hint = string.Empty;
                return true;
            }

            hint = $"当前 Ubuntu {_host.OsVersion} 与本地 Mosquitto 离线资源不兼容，仅支持 Ubuntu 22.04 和 Ubuntu 24.04。";
            return false;
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            if (!TryGetMajorVersion(_host.OsVersion, out var centOsMajor))
            {
                hint = "未检测到 CentOS/EL 版本，无法自动选择 Mosquitto 离线目录，请先完成连接测试。";
                return false;
            }

            if (centOsMajor != 7)
            {
                hint = $"当前 CentOS/EL {_host.OsVersion} 与本地 Mosquitto 离线资源不兼容，仅支持 EL7/CentOS 7。";
                return false;
            }

            offlineFolder = "mosquitto-centos7";
            hint = string.Empty;
            return true;
        }

        hint = "当前系统未配置 Mosquitto 本地资源目录。";
        return false;
    }

    private bool TryValidateMosquittoOfflineDirectory(string root, out string hint)
    {
        if (_host.OsType == OperatingSystemType.Windows)
        {
            return TryValidateMosquittoWindowsOfflineDirectory(root, out hint);
        }

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryValidateMosquittoUbuntuOfflineDirectory(root, out hint);
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryValidateMosquittoCentOsOfflineDirectory(root, out hint);
        }

        hint = "当前系统未配置 Mosquitto 本地资源目录。";
        return false;
    }

    private bool TryValidateMosquittoOfflinePath(string path, out string hint)
    {
        if (Directory.Exists(path))
        {
            return TryValidateMosquittoOfflineDirectory(path, out hint);
        }

        if (!File.Exists(path))
        {
            hint = $"Scripts 对应目录中的本地资源不存在：{path}";
            return false;
        }

        if (_host.OsType == OperatingSystemType.Windows)
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                hint = IsMosquittoWindowsZipFile(path)
                    ? string.Empty
                    : "Mosquitto Windows 离线包文件名应为 mosquitto-*.zip。";
                return string.IsNullOrEmpty(hint);
            }

            var parentDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                hint = $"无法定位 Mosquitto Windows 本地资源所属目录：{path}";
                return false;
            }

            return TryValidateMosquittoOfflineDirectory(parentDirectory, out hint);
        }

        hint = "Mosquitto Linux 本地资源仅支持目录型离线包，请选择包含主包的离线目录。";
        return false;
    }

    private bool TryValidateMosquittoUbuntuOfflineDirectory(string root, out string hint)
    {
        return TryValidateMosquittoLinuxOfflineDirectory(
            root,
            GetMosquittoUbuntuDebFiles(root),
            $"{GetMosquittoOfflinePlatformDisplayName()} 离线资源目录缺少主包：mosquitto-{GetExpectedMosquittoOfflineVersion()}*.deb 或 mosquitto_{GetExpectedMosquittoOfflineVersion()}*.deb。请补齐后再点击刷新检测。",
            out hint);
    }

    private bool TryValidateMosquittoCentOsOfflineDirectory(string root, out string hint)
    {
        return TryValidateMosquittoLinuxOfflineDirectory(
            root,
            GetMosquittoCentOsRpmFiles(root),
            $"{GetMosquittoOfflinePlatformDisplayName()} 离线资源目录缺少主包：mosquitto-{GetExpectedMosquittoOfflineVersion()}*.rpm。请补齐后再点击刷新检测。",
            out hint);
    }

    private bool TryValidateMosquittoWindowsOfflineDirectory(string root, out string hint)
    {
        var windowsPackagePath = GetMosquittoWindowsZipFiles(root)
            .OrderByDescending(path => IsVersionMatch(path, _application.Version))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(windowsPackagePath))
        {
            hint = string.Empty;
            return true;
        }

        var requiredFiles = new[]
        {
            Path.Combine(root, "mosquitto.exe"),
            Path.Combine(root, "mosquitto_passwd.exe"),
            Path.Combine(root, "mosquitto.conf")
        };

        var missingFiles = requiredFiles
            .Where(path => !File.Exists(path))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        if (missingFiles.Count > 0)
        {
            hint = $"Mosquitto Windows 离线资源目录缺少文件：{string.Join(", ", missingFiles)}。请补齐 zip 包或完整解压目录后再点击刷新检测。";
            return false;
        }

        hint = string.Empty;
        return true;
    }

    private static IEnumerable<string> GetMosquittoWindowsExtractedDirectories(string root)
    {
        return new[] { root }
            .Concat(Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetMosquittoUbuntuDebFiles(string root)
    {
        return Directory.GetFiles(root, "*.deb", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith("mosquitto_", StringComparison.OrdinalIgnoreCase)
                    || fileName.StartsWith("mosquitto-", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IEnumerable<string> GetMosquittoCentOsRpmFiles(string root)
    {
        return Directory.GetFiles(root, "*.rpm", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith("mosquitto-", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IEnumerable<string> GetMosquittoWindowsZipFiles(string root)
    {
        return Directory.GetFiles(root, "*.zip", SearchOption.TopDirectoryOnly)
            .Where(IsMosquittoWindowsZipFile);
    }

    private static bool IsMosquittoWindowsZipFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("mosquitto-", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryValidateMosquittoLinuxOfflineDirectory(IEnumerable<string> packageFiles, string missingPackageHint, out string hint)
    {
        return TryValidateMosquittoLinuxOfflineDirectory(string.Empty, packageFiles, missingPackageHint, out hint);
    }

    private bool TryValidateMosquittoLinuxOfflineDirectory(string root, IEnumerable<string> packageFiles, string missingPackageHint, out string hint)
    {
        var packages = packageFiles.ToList();
        if (packages.Count == 0)
        {
            hint = missingPackageHint;
            return false;
        }

        var preferredPackages = GetPreferredMosquittoPackageFiles(root, packages).ToList();
        if (preferredPackages.Count > 0)
        {
            hint = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(GetNormalizedHostCpuArchitecture()))
        {
            var availableArchitectures = packages
                .Select(TryGetMosquittoPackageArchitecture)
                .Where(arch => !string.IsNullOrWhiteSpace(arch))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(arch => arch, StringComparer.Ordinal)
                .ToList();

            if (availableArchitectures.Count > 1)
            {
                hint = $"{GetMosquittoOfflinePlatformDisplayName()} 离线目录中检测到多种 CPU 架构：{string.Join("、", availableArchitectures)}。CPU 架构未知，请先重新测试连接获取 CPU 架构后再刷新检测。";
                return false;
            }
        }

        hint = missingPackageHint;
        return false;
    }

    private IEnumerable<string> GetPreferredMosquittoPackageFiles(string root)
    {
        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return GetPreferredMosquittoPackageFiles(root, GetMosquittoUbuntuDebFiles(root));
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return GetPreferredMosquittoPackageFiles(root, GetMosquittoCentOsRpmFiles(root));
        }

        return Enumerable.Empty<string>();
    }

    private IEnumerable<string> GetPreferredMosquittoPackageFiles(string root, IEnumerable<string> packageFiles)
    {
        var packages = packageFiles.ToList();
        var expectedVersion = GetExpectedMosquittoOfflineVersion();
        var normalizedArchitecture = GetNormalizedHostCpuArchitecture();

        if (!string.IsNullOrWhiteSpace(normalizedArchitecture))
        {
            var matchedArchitecturePackages = packages
                .Where(path => string.Equals(TryGetMosquittoPackageArchitecture(path), normalizedArchitecture, StringComparison.Ordinal))
                .ToList();

            if (matchedArchitecturePackages.Count > 0)
            {
                return matchedArchitecturePackages
                    .Where(path => IsVersionMatch(path, expectedVersion))
                    .DefaultIfEmpty()
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Concat(matchedArchitecturePackages.Where(path => !IsVersionMatch(path, expectedVersion)))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }

            return Enumerable.Empty<string>();
        }

        var singleArchitecturePackages = packages
            .GroupBy(TryGetMosquittoPackageArchitecture, StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToList();

        if (singleArchitecturePackages.Count == 1)
        {
            var groupedPackages = singleArchitecturePackages[0].ToList();
            return groupedPackages
                .Where(path => IsVersionMatch(path, expectedVersion))
                .DefaultIfEmpty()
                .Where(path => !string.IsNullOrEmpty(path))
                .Concat(groupedPackages.Where(path => !IsVersionMatch(path, expectedVersion)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        if (singleArchitecturePackages.Count == 0)
        {
            return packages
                .Where(path => IsVersionMatch(path, expectedVersion))
                .DefaultIfEmpty()
                .Where(path => !string.IsNullOrEmpty(path))
                .Concat(packages.Where(path => !IsVersionMatch(path, expectedVersion)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        return Enumerable.Empty<string>();
    }

    private string GetNormalizedHostCpuArchitecture()
    {
        return NormalizeMosquittoArchitecture(_host.CpuArchitecture);
    }

    private static string NormalizeMosquittoArchitecture(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return string.Empty;
        }

        return architecture.Trim().ToLowerInvariant() switch
        {
            "x86_64" => "amd64",
            "x64" => "amd64",
            "amd64" => "amd64",
            "aarch64" => "arm64",
            "arm64" => "arm64",
            _ => string.Empty
        };
    }

    private static string TryGetMosquittoPackageArchitecture(string path)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();

        if (fileName.Contains("_amd64") || fileName.Contains("-amd64") || fileName.Contains("x86_64") || fileName.Contains("-x64") || fileName.Contains("_x64"))
        {
            return "amd64";
        }

        if (fileName.Contains("_arm64") || fileName.Contains("-arm64") || fileName.Contains("aarch64"))
        {
            return "arm64";
        }

        return string.Empty;
    }

    private string GetExpectedMosquittoOfflineVersion()
    {
        if (!string.IsNullOrWhiteSpace(SelectedVersion))
        {
            return SelectedVersion;
        }

        var preferredVersion = GetDefaultMosquittoVersionForCurrentHost();
        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            return preferredVersion;
        }

        return string.IsNullOrWhiteSpace(_application.Version) ? "2.0.21" : _application.Version;
    }

    private string GetMosquittoOfflinePlatformDisplayName()
    {
        if (_host.OsType == OperatingSystemType.Windows)
        {
            return "Mosquitto Windows";
        }

        if (_host.OsType == OperatingSystemType.Ubuntu)
        {
            return TryGetMajorVersion(_host.OsVersion, out var ubuntuMajor)
                ? $"Mosquitto Ubuntu {ubuntuMajor}"
                : "Mosquitto Ubuntu";
        }

        if (_host.OsType == OperatingSystemType.CentOS)
        {
            return TryGetMajorVersion(_host.OsVersion, out var centOsMajor)
                ? $"Mosquitto CentOS {centOsMajor}"
                : "Mosquitto CentOS";
        }

        return "Mosquitto";
    }

    private bool TryResolveRabbitMqLocalPackage(out string packagePath, out string hint)
    {
        var resolver = ScriptRootOverridesFactory is null
            ? new DefaultPackageResolver()
            : new DefaultPackageResolver(ScriptRootOverridesFactory);

        var resolution = resolver.Resolve(_application, _host);
        packagePath = resolution.Path;
        hint = resolution.Hint;
        return resolution.Found;
    }

    private bool TryResolveConsulLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "consul-centos7",
            OperatingSystemType.Ubuntu => "consul-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Consul 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "consul_*_linux_amd64.zip",
            OperatingSystemType.Ubuntu => "consul_*_linux_amd64.zip",
            _ => string.Empty
        };

        foreach (var root in GetConsulScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = "已从 Scripts 目录自动匹配 Consul 本地资源目录：" + root;
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Consul/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveTraefikLocalPackage(out string packagePath, out string hint)
    {
        string? offlineFolder = _host.OsType switch
        {
            OperatingSystemType.CentOS => "traefik-centos7",
            OperatingSystemType.Ubuntu => "traefik-ubuntu",
            _ => null
        };

        if (string.IsNullOrEmpty(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 Traefik 本地资源目录。";
            return false;
        }

        string packagePattern = _host.OsType switch
        {
            OperatingSystemType.CentOS => "traefik_v*_linux_amd64.tar.gz",
            OperatingSystemType.Ubuntu => "traefik_v*_linux_amd64.tar.gz",
            _ => string.Empty
        };

        foreach (var root in GetTraefikScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = root;
                hint = "已从 Scripts 目录自动匹配 Traefik 本地资源目录：" + root;
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/Traefik/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private bool TryResolveJdkLocalPackage(out string packagePath, out string hint)
    {
        var offlineFolder = _host.OsType switch
        {
            OperatingSystemType.Windows => "windows",
            OperatingSystemType.Ubuntu => "ubuntu",
            OperatingSystemType.CentOS => "centos",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(offlineFolder))
        {
            packagePath = string.Empty;
            hint = "当前系统未配置 JDK 本地资源目录。";
            return false;
        }

        string[] packagePatterns = _host.OsType switch
        {
            OperatingSystemType.Windows => new[] { "*windows*.zip", "*windows*.msi", "*windows*.exe" },
            OperatingSystemType.Ubuntu => new[] { "*linux*.tar.gz", "*linux*.tar.xz", "*.tgz" },
            OperatingSystemType.CentOS => new[] { "*linux*.tar.gz", "*linux*.tar.xz", "*.tgz" },
            _ => Array.Empty<string>()
        };

        foreach (var root in GetJdkScriptRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                        var expectedVersion = !string.IsNullOrWhiteSpace(SelectedVersion)
                    ? SelectedVersion
                    : _application.Version;

                var selectedPath = packagePatterns
                    .SelectMany(pattern => Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
                    .OrderByDescending(path => IsVersionMatch(path, expectedVersion))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                packagePath = selectedPath;
                hint = $"已从 Scripts 目录自动匹配 JDK 本地资源：{selectedPath}";
                return true;
            }
            catch
            {
                // 忽略当前路径异常，继续尝试其他路径
            }
        }

        packagePath = string.Empty;
        hint = $"未在 Scripts/{ResolveJdkScriptFolderName()}/{offlineFolder} 中找到可用本地资源。";
        return false;
    }

    private IEnumerable<string> GetApplicationScriptRoots()
    {
        var appFolders = new[] { _application.Name, _application.Id }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts")
        };

        return baseRoots
            .SelectMany(root => appFolders.Select(folder => Path.Combine(root, folder)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string? TryFindPreferredGenericPackageFile(string root, string[] packageExtensions)
    {
        foreach (var preferredDirectory in GetPreferredApplicationPackageDirectories(root))
        {
            if (!Directory.Exists(preferredDirectory))
            {
                continue;
            }

            var preferredPackage = Directory.GetFiles(preferredDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => packageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(path => IsVersionMatch(path, _application.Version))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(preferredPackage))
            {
                return preferredPackage;
            }
        }

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => packageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(path => IsVersionMatch(path, _application.Version))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private IEnumerable<string> GetPreferredApplicationPackageDirectories(string root)
    {
        var osDirectoryKeywords = GetCurrentOsDirectoryKeywords();
        if (osDirectoryKeywords.Length == 0)
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => osDirectoryKeywords.Any(keyword =>
                Path.GetFileName(directory).Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string[] GetCurrentOsDirectoryKeywords()
    {
        return _host.OsType switch
        {
            OperatingSystemType.CentOS => new[] { "centos", "centos7", "el7" },
            OperatingSystemType.Ubuntu => new[] { "ubuntu", "debian" },
            _ => Array.Empty<string>()
        };
    }

    private IEnumerable<string> GetMosquittoScriptRoots(string offlineFolder)
    {
        if (ScriptRootOverridesFactory is not null)
        {
            return ScriptRootOverridesFactory()
                .Select(root => Path.Combine(root, "Mosquitto", offlineFolder))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        var mosquittoRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Mosquitto", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Mosquitto", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Mosquitto", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Mosquitto", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Mosquitto", offlineFolder)
        };

        return mosquittoRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetRedisScriptRoots(string offlineFolder)
    {
        if (ScriptRootOverridesFactory is not null)
        {
            return ScriptRootOverridesFactory()
                .Select(root => Path.Combine(root, "Redis", offlineFolder))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        var redisRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Redis", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Redis", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Redis", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Redis", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Redis", offlineFolder)
        };

        return redisRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetNginxScriptRoots(string offlineFolder)
    {
        var nginxRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Nginx", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Nginx", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Nginx", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Nginx", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Nginx", offlineFolder)
        };

        return nginxRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetMySqlOfflinePackageDirectories(string root)
    {
        // 兼容旧版平铺目录，以及新增的 mysql 子目录结构。
        return new[]
        {
            Path.Combine(root, "mysql"),
            root
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetMySqlScriptRoots(string offlineFolder)
    {
        var mySqlRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "MySQL", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "MySQL", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "MySQL", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "MySQL", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "MySQL", offlineFolder)
        };

        return mySqlRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetElasticsearchScriptRoots(string offlineFolder)
    {
        var elasticsearchRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Elasticsearch", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Elasticsearch", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Elasticsearch", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Elasticsearch", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Elasticsearch", offlineFolder)
        };

        return elasticsearchRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetMariaDbScriptRoots(string offlineFolder)
    {
        var mariaDbRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "MariaDB", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "MariaDB", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "MariaDB", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "MariaDB", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "MariaDB", offlineFolder)
        };

        return mariaDbRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetConsulScriptRoots(string offlineFolder)
    {
        var consulRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Consul", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Consul", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Consul", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Consul", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Consul", offlineFolder)
        };

        return consulRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetTraefikScriptRoots(string offlineFolder)
    {
        var traefikRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Traefik", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", "Traefik", offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Traefik", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Traefik", offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", "Traefik", offlineFolder)
        };

        return traefikRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetJdkScriptRoots(string offlineFolder)
    {
        var appFolder = ResolveJdkScriptFolderName();
        var jdkRoots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", appFolder, offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", appFolder, offlineFolder),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", appFolder, offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", appFolder, offlineFolder),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", appFolder, offlineFolder)
        };

        return jdkRoots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string ResolveJdkScriptFolderName()
    {
        var selectedVersion = !string.IsNullOrWhiteSpace(SelectedVersion)
            ? SelectedVersion
            : _application.Version;

        if (selectedVersion.StartsWith("17", StringComparison.OrdinalIgnoreCase))
        {
            return "JDK17";
        }

        if (selectedVersion.StartsWith("11", StringComparison.OrdinalIgnoreCase))
        {
            return "JDK11";
        }

        if (selectedVersion.StartsWith("8", StringComparison.OrdinalIgnoreCase)
            || selectedVersion.StartsWith("1.8", StringComparison.OrdinalIgnoreCase))
        {
            return "JDK8";
        }

        return (_application.Id ?? _application.Name ?? "JDK").ToUpperInvariant();
    }

    private string[] GetPackageExtensions()
    {
        return _host.OsType == OperatingSystemType.Windows
            ? new[] { ".zip", ".msi", ".exe" }
            : new[] { ".tar.gz", ".tar.xz", ".tgz", ".zip", ".rpm", ".deb" };
    }

    private static bool IsVersionMatch(string path, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return Path.GetFileName(path).Contains(version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取参数值
    /// </summary>
    public Dictionary<string, string> GetParameters()
    {
        var result = new Dictionary<string, string>();

        foreach (var paramVm in ParameterViewModels)
        {
            // 处理布尔类型参数
            if (paramVm.Type == ParameterType.Boolean)
            {
                result[paramVm.Key] = paramVm.BoolValue ? "true" : "false";
            }
            else
            {
                result[paramVm.Key] = paramVm.Value;
            }
        }

        // 添加版本信息
        if (IsLocalPackage)
        {
            result["version"] = IsJdk ? NormalizeJdkVersion(LocalPackageVersion) : LocalPackageVersion;
        }
        else
        {
            result["version"] = SelectedVersion;
        }

        if (IsJdk && !string.IsNullOrWhiteSpace(RemoteUploadPath))
        {
            result["REMOTE_UPLOAD_PATH"] = RemoteUploadPath.Trim();
        }

        return result;
    }

    /// <summary>
    /// 确认安装
    /// </summary>
    public RelayCommand ConfirmCommand { get; private set; }

    public InstallConfigViewModel(ApplicationInfo application, RemoteHost host, ILogger logger, bool isJdkUploadMode = false)
    {
        _application = application;
        _host = host;
        _logger = logger;
        _isJdkUploadMode = isJdkUploadMode;

        DialogTitle = $"安装 {application.Name} {application.Version}";
        ApplicationName = application.Name;
        ApplicationVersion = application.Version;
        TargetHost = host.Name;

        // 先初始化版本和参数，再创建命令
        InitializeVersions();
        if (IsJdkUploadMode)
        {
            SelectedVersion = string.Empty;
        }
        InitializeParameters();
        InitializeLocalResourceDefaults();

        // 初始化 ConfirmCommand（在 InitializeVersions 和 InitializeParameters 之后）
        ConfirmCommand = new RelayCommand(Confirm, CanConfirmGetter);
        NotifyCanExecuteChanged();

        // 监听 IsBusy 变化（因为 IsBusy 在基类中定义）
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
            {
                NotifyCanExecuteChanged();
            }
        };
    }

    /// <summary>
    /// CanConfirm 的 getter 方法（用于 RelayCommand）
    /// </summary>
    private bool CanConfirmGetter()
    {
        return CanConfirmCore();
    }

    /// <summary>
    /// 是否允许确认（用于 UI 按钮启用/禁用）
    /// </summary>
    public bool CanConfirm => CanConfirmGetter();

    /// <summary>
    /// 通知命令重新评估 CanExecute
    /// </summary>
    private void NotifyCanExecuteChanged()
    {
        if (CanConfirmCore(out var errorMessage))
        {
            ClearError();
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            SetError(errorMessage);
        }

        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand?.RaiseCanExecuteChanged();
    }

    private void Confirm()
    {
        if (!ValidateParameters())
        {
            return;
        }

        if (IsMariaDb && PackageSource != "local")
        {
            SetError("MariaDB 仅支持本地资源安装，请使用 Scripts/MariaDB 下的离线目录。");
            return;
        }

        // 设置版本信息到 ApplicationInfo
        if (IsLocalPackage)
        {
            _application.LocalPackagePath = LocalPackagePath;
            _application.UseLocalPackage = true;
            _application.SelectedVersion = IsJdk
                ? NormalizeJdkVersion(LocalPackageVersion)
                : LocalPackageVersion;
            _logger?.Info($"使用本地安装包：{LocalPackagePath}");
        }
        else
        {
            _application.LocalPackagePath = string.Empty;
            _application.UseLocalPackage = false;
            _application.SelectedVersion = SelectedVersion;
            _logger?.Info($"在线安装版本：{SelectedVersion}");
        }

        // 关闭对话框，返回 true 表示确认
        CloseDialog(true);
    }

    /// <summary>
    /// 判断是否是 Redis 应用
    /// </summary>
    private bool IsRedis => _application.Id?.ToLower() == "redis" || _application.Name?.ToLower() == "redis";

    /// <summary>
    /// 判断是否是 RabbitMQ 应用
    /// </summary>
    private bool IsRabbitMq => _application.Id?.ToLower() == "rabbitmq" || _application.Name?.ToLower() == "rabbitmq";

    /// <summary>
    /// 判断是否是 Nginx 应用
    /// </summary>
    private bool IsNginx => _application.Id?.ToLower() == "nginx" || _application.Name?.ToLower() == "nginx";

    /// <summary>
    /// 判断是否是 MySQL 应用
    /// </summary>
    private bool IsMySql => _application.Id?.ToLower() == "mysql" || _application.Name?.ToLower() == "mysql";

    /// <summary>
    /// 判断是否是 Elasticsearch 应用
    /// </summary>
    private bool IsElasticsearch => _application.Id?.ToLower() == "elasticsearch" || _application.Name?.ToLower() == "elasticsearch";

    /// <summary>
    /// 判断是否是 MariaDB 应用
    /// </summary>
    private bool IsMariaDb => _application.Id?.ToLower() == "mariadb" || _application.Name?.ToLower() == "mariadb";

    /// <summary>
    /// 判断是否是 Mosquitto 应用
    /// </summary>
    private bool IsMosquitto => _application.Id?.ToLower() == "mosquitto" || _application.Name?.ToLower().Contains("mosquitto") == true;

    /// <summary>
    /// 判断是否是 Consul 应用
    /// </summary>
    private bool IsConsul => _application.Id?.ToLower() == "consul" || _application.Name?.ToLower() == "consul";

    /// <summary>
    /// 判断是否是 Traefik 应用
    /// </summary>
    private bool IsTraefik => _application.Id?.ToLower() == "traefik" || _application.Name?.ToLower() == "traefik";

    /// <summary>
    /// 判断是否是 JDK 应用
    /// </summary>
    private bool IsJdk => (_application.Id?.ToLower().StartsWith("jdk") == true) || (_application.Name?.ToLower().StartsWith("jdk") == true);

    /// <summary>
    /// 验证参数
    /// </summary>
    private bool TryValidateParameters(out string? errorMessage, bool requireMosquittoLocalPackage = true)
    {
        errorMessage = null;

        if (IsMariaDb && PackageSource != "local")
        {
            errorMessage = "MariaDB 仅支持本地资源安装，请使用 Scripts/MariaDB 下的离线目录。";
            return false;
        }

        if (IsMosquitto && PackageSource != "local")
        {
            errorMessage = "Mosquitto 仅支持本地资源安装，请使用 Scripts/Mosquitto 下的离线目录。";
            return false;
        }

        if (PackageSource == "local")
        {
            if (IsJdkUploadMode)
            {
                if (string.IsNullOrWhiteSpace(LocalPackagePath))
                {
                    errorMessage = "请选择本地 JDK 文件夹。";
                    return false;
                }

                if (!Directory.Exists(LocalPackagePath))
                {
                    errorMessage = $"本地 JDK 文件夹不存在：{LocalPackagePath}";
                    return false;
                }

                if (!HasJdkDirectoryStructure(LocalPackagePath))
                {
                    errorMessage = "所选目录不是有效的 JDK 根目录，请选择包含 release 或 bin/java 的完整 JDK 文件夹。";
                    return false;
                }

                var detectedJdkVersion = NormalizeJdkVersion(LocalPackageVersion);
                if (string.IsNullOrWhiteSpace(detectedJdkVersion))
                {
                    errorMessage = "无法识别所选 JDK 文件夹的版本，请选择包含 JDK 8 / 11 / 17 的正确目录。";
                    return false;
                }
            }
            else
            {
                if (IsElasticsearch && !TryGetCompatibleElasticsearchOfflineFolder(out _, out var elasticsearchCompatibilityHint))
                {
                    errorMessage = elasticsearchCompatibilityHint;
                    return false;
                }

                if (IsNginx && !TryGetCompatibleNginxOfflineFolder(out _, out var nginxCompatibilityHint))
                {
                    errorMessage = nginxCompatibilityHint;
                    return false;
                }

                if (IsRedis && !TryGetCompatibleRedisOfflineFolders(_host, out _, out var redisCompatibilityHint))
                {
                    errorMessage = redisCompatibilityHint;
                    return false;
                }

                if (IsMosquitto && !TryGetCompatibleMosquittoOfflineFolder(out _, out var mosquittoCompatibilityHint))
                {
                    errorMessage = mosquittoCompatibilityHint;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(LocalPackagePath))
                {
                    if (IsMySql && !TryGetCompatibleMySqlOfflineFolder(out _, out var compatibilityHint))
                    {
                        errorMessage = compatibilityHint;
                        return false;
                    }

                    if (IsMariaDb && !TryGetCompatibleMariaDbOfflineFolder(out _, out var mariaDbCompatibilityHint))
                    {
                        errorMessage = mariaDbCompatibilityHint;
                        return false;
                    }

                    if (!IsMosquitto || requireMosquittoLocalPackage)
                    {
                        errorMessage = LocalResourceHint;
                        return false;
                    }
                }

                if (!string.IsNullOrWhiteSpace(LocalPackagePath) && !File.Exists(LocalPackagePath) && !Directory.Exists(LocalPackagePath))
                {
                    errorMessage = $"Scripts 对应目录中的本地资源不存在：{LocalPackagePath}";
                    return false;
                }

                if (IsElasticsearch && !TryValidateElasticsearchOfflinePath(LocalPackagePath, out var elasticsearchDirectoryHint))
                {
                    errorMessage = elasticsearchDirectoryHint;
                    return false;
                }

                if (IsNginx && !TryValidateNginxOfflinePath(LocalPackagePath, out var nginxDirectoryHint))
                {
                    errorMessage = nginxDirectoryHint;
                    return false;
                }

                if (IsRedis && !TryValidateRedisOfflinePath(LocalPackagePath, out var redisDirectoryHint))
                {
                    errorMessage = redisDirectoryHint;
                    return false;
                }

                if (IsMosquitto && !string.IsNullOrWhiteSpace(LocalPackagePath) && !TryValidateMosquittoOfflinePath(LocalPackagePath, out var mosquittoDirectoryHint))
                {
                    errorMessage = mosquittoDirectoryHint;
                    return false;
                }
            }
        }

        foreach (var paramVm in ParameterViewModels)
        {
            if (paramVm.Parameter.Required && string.IsNullOrWhiteSpace(paramVm.Value))
            {
                errorMessage = $"{paramVm.Parameter.Name} 是必填项";
                return false;
            }

            if (IsRedis && paramVm.Key?.ToLower() == "password" && string.IsNullOrWhiteSpace(paramVm.Value))
            {
                continue;
            }

            if (ParameterType.Port == paramVm.Parameter.Type)
            {
                if (!string.IsNullOrWhiteSpace(paramVm.Value) && int.TryParse(paramVm.Value, out var port))
                {
                    if (paramVm.Parameter.MinValue > 0 && paramVm.Parameter.MaxValue > 0)
                    {
                        if (port < paramVm.Parameter.MinValue || port > paramVm.Parameter.MaxValue)
                        {
                            errorMessage = $"{paramVm.Parameter.Name} 范围：{paramVm.Parameter.MinValue}-{paramVm.Parameter.MaxValue}";
                            return false;
                        }
                    }
                }
            }
        }

        if (IsMosquitto && !TryValidateMosquittoCredentialPair(out errorMessage))
        {
            return false;
        }

        if (!TryValidateRemoteUploadPath(out errorMessage))
        {
            return false;
        }

        return true;
    }

    private bool ValidateParameters()
    {
        var valid = TryValidateParameters(out var errorMessage);
        if (valid)
        {
            ClearError();
        }
        else
        {
            SetError(errorMessage ?? string.Empty);
        }

        return valid;
    }

    private bool CanConfirmCore()
    {
        return CanConfirmCore(out _);
    }

    private bool CanConfirmCore(out string? errorMessage)
    {
        errorMessage = null;
        return !IsBusy && TryValidateParameters(out errorMessage, requireMosquittoLocalPackage: false);
    }

    /// <summary>
    /// 刷新 Scripts 本地资源检测结果
    /// </summary>
    [RelayCommand]
    private void BrowsePackagePath()
    {
        try
        {
            if (IsJdkUploadMode)
            {
                LocalResourceHint = "JDK 上传入口请直接选择本地 JDK 文件夹。";
                NotifyCanExecuteChanged();
                return;
            }

            InitializeLocalResourceDefaults();
            NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger?.Error($"刷新本地资源检测失败：{ex.Message}");
            MessageBox.Show($"刷新本地资源检测失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void BrowseMySqlLocalPackageFolder()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择 MySQL 本地资源目录",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return;
            }

            LocalPackagePath = dialog.FolderName;
            LocalResourceHint = $"已选择 MySQL 本地资源目录：{dialog.FolderName}";
            NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger?.Error($"选择 MySQL 本地资源目录失败：{ex.Message}");
            MessageBox.Show($"选择 MySQL 本地资源目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void BrowseJdkLocalPackageFolder()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = $"选择 {ApplicationName} 本地目录",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return;
            }

            LocalPackagePath = dialog.FolderName;
            var normalizedVersion = NormalizeJdkVersion(LocalPackageVersion);
            if (!string.IsNullOrEmpty(normalizedVersion))
            {
                SelectedVersion = normalizedVersion;
                LocalResourceHint = $"已选择 JDK 本地目录：{dialog.FolderName}，识别版本为 JDK {normalizedVersion}";
            }
            else
            {
                SelectedVersion = string.Empty;
                LocalResourceHint = $"已选择 JDK 本地目录：{dialog.FolderName}，但未能识别版本，请选择包含 JDK 8 / 11 / 17 的正确目录。";
            }
            NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger?.Error($"选择 JDK 本地目录失败：{ex.Message}");
            MessageBox.Show($"选择 JDK 本地目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 取消
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 关闭对话框，返回 false 表示取消
        CloseDialog(false);
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    private void CloseDialog(bool? result)
    {
        foreach (var window in Application.Current.Windows)
        {
            if (window is Views.InstallConfigDialog dialog && dialog.ViewModel == this)
            {
                dialog.DialogResult = result;  // 设置 DialogResult
                dialog.Close();
                return;
            }
        }
    }
}

/// <summary>
/// 参数 ViewModel
/// </summary>
public partial class ParameterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private ParameterType _type;

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private string _hint;

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"选择 {Name}",
            Filter = "所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            Value = dialog.FileName;
        }
    }

    public InstallParameter Parameter { get; }

    /// <summary>
    /// 父 ViewModel 的回调，用于通知 CanExecute 变化
    /// </summary>
    public Action? OnValueChangeNotify { get; set; }

    public ParameterViewModel(InstallParameter parameter)
    {
        Parameter = parameter;
        Key = parameter.Key;
        Name = parameter.Name;
        Description = parameter.Description;
        Type = parameter.Type;
        Required = parameter.Required;
        Value = parameter.DefaultValue ?? string.Empty;

        // 处理布尔类型参数
        if (parameter.Type == ParameterType.Boolean)
        {
            BoolValue = bool.TryParse(parameter.DefaultValue, out var boolVal) ? boolVal : true;
        }

        Hint = BuildHint();

        // 监听 Value 属性变化
        PropertyChanged += ParameterViewModel_PropertyChanged;
    }

    private void ParameterViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Value))
        {
            OnValueChangeNotify?.Invoke();
        }
    }

    private string BuildHint()
    {
        var hints = new List<string>();
        
        if (Required) hints.Add("*必填");
        if (!string.IsNullOrEmpty(Parameter.DefaultValue)) hints.Add($"默认：{Parameter.DefaultValue}");
        if (Parameter.Type == ParameterType.Port && Parameter.MinValue > 0 && Parameter.MaxValue > 0)
        {
            hints.Add($"范围：{Parameter.MinValue}-{Parameter.MaxValue}");
        }

        return string.Join(" | ", hints);
    }
}
