using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteInstaller.Models;

/// <summary>
/// 应用模型
/// </summary>
public partial class ApplicationInfo : ObservableObject
{
    [ObservableProperty]
    private string _id;
    
    [ObservableProperty]
    private string _name;
    
    [ObservableProperty]
    private string _version;
    
    [ObservableProperty]
    private string _description;
    
    [ObservableProperty]
    private string _iconPath;
    
    [ObservableProperty]
    private string _category;
    
    [ObservableProperty]
    private bool _supportWindows = true;
    
    [ObservableProperty]
    private bool _supportCentOS = true;
    
    [ObservableProperty]
    private bool _supportUbuntu = true;
    
    [ObservableProperty]
    private string _downloadUrl;
    
    [ObservableProperty]
    private string _installScriptLinux;
    
    [ObservableProperty]
    private string _installScriptWindows;
    
    [ObservableProperty]
    private string _uninstallScriptLinux;
    
    [ObservableProperty]
    private string _uninstallScriptWindows;
    
    [ObservableProperty]
    private string _checkScriptLinux;
    
    [ObservableProperty]
    private string _checkScriptWindows;
    
    [ObservableProperty]
    private List<InstallParameter> _parameters = new();
    
    [ObservableProperty]
    private List<string> _dependencies = new();

    /// <summary>
    /// 可用版本列表
    /// </summary>
    [ObservableProperty]
    private List<string> _versions = new();

    /// <summary>
    /// 选中的版本
    /// </summary>
    [ObservableProperty]
    private string? _selectedVersion;

    /// <summary>
    /// 本地安装包路径
    /// </summary>
    [ObservableProperty]
    private string? _localPackagePath;

    /// <summary>
    /// 是否使用本地包
    /// </summary>
    [ObservableProperty]
    private bool _useLocalPackage;

    /// <summary>
    /// 应用安装状态（运行时状态，不持久化）
    /// </summary>
    [ObservableProperty]
    private bool _isInstalled;
    
    /// <summary>
    /// 应用运行状态
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;
    
    /// <summary>
    /// 已安装版本
    /// </summary>
    [ObservableProperty]
    private string _installedVersion;
    
    /// <summary>
    /// 关联的服务器 ID
    /// </summary>
    [ObservableProperty]
    private string _hostId;
    
    /// <summary>
    /// 操作系统类型（用于显示）
    /// </summary>
    [ObservableProperty]
    private string _osType;

    // 命令
    private ICommand? _installCommand;
    private ICommand? _checkStatusCommand;
    private ICommand? _uninstallCommand;
    private ICommand? _configureCommand;

    // 命令回调
    private Action? _onInstallCallback;
    private Action? _onCheckStatusCallback;
    private Action? _onUninstallCallback;
    private Action? _onConfigureCallback;

    /// <summary>
    /// 安装命令
    /// </summary>
    public ICommand InstallCommand => _installCommand ??= CreateInstallCommand();

    /// <summary>
    /// 检测状态命令
    /// </summary>
    public ICommand CheckStatusCommand => _checkStatusCommand ??= CreateCheckStatusCommand();

    /// <summary>
    /// 卸载命令
    /// </summary>
    public ICommand UninstallCommand => _uninstallCommand ??= CreateUninstallCommand();

    /// <summary>
    /// 配置命令
    /// </summary>
    public ICommand ConfigureCommand => _configureCommand ??= CreateConfigureCommand();

    /// <summary>
    /// 图标（用于 UI 显示）
    /// </summary>
    public string Icon => Name[0].ToString();

    /// <summary>
    /// 设置命令回调
    /// </summary>
    public void SetCommands(Action? onInstall, Action? onCheckStatus, Action? onUninstall, Action? onConfigure)
    {
        _onInstallCallback = onInstall;
        _onCheckStatusCallback = onCheckStatus;
        _onUninstallCallback = onUninstall;
        _onConfigureCallback = onConfigure;
        
        // 重新创建命令以使用新的回调
        _installCommand = CreateInstallCommand();
        _checkStatusCommand = CreateCheckStatusCommand();
        _uninstallCommand = CreateUninstallCommand();
        _configureCommand = CreateConfigureCommand();
    }

    private ICommand CreateInstallCommand() => new RelayCommand(OnInstall, CanInstall);
    private ICommand CreateCheckStatusCommand() => new RelayCommand(OnCheckStatus);
    private ICommand CreateUninstallCommand() => new RelayCommand(OnUninstall, CanUninstall);
    private ICommand CreateConfigureCommand() => new RelayCommand(OnConfigure);

    private bool CanInstall() => !IsInstalled;
    private bool CanUninstall() => IsInstalled;

    // 当 IsInstalled 改变时，通知命令更新
    partial void OnIsInstalledChanged(bool value)
    {
        ((RelayCommand?)InstallCommand)?.RaiseCanExecuteChanged();
        ((RelayCommand?)UninstallCommand)?.RaiseCanExecuteChanged();
    }

    private void OnInstall()
    {
        _onInstallCallback?.Invoke();
    }

    private void OnCheckStatus()
    {
        _onCheckStatusCallback?.Invoke();
    }

    private void OnUninstall()
    {
        _onUninstallCallback?.Invoke();
    }

    private void OnConfigure()
    {
        _onConfigureCallback?.Invoke();
    }

    /// <summary>
    /// 是否支持指定操作系统
    /// </summary>
    public bool SupportsOs(OperatingSystemType osType)
    {
        return osType switch
        {
            OperatingSystemType.Windows => SupportWindows,
            OperatingSystemType.CentOS => SupportCentOS,
            OperatingSystemType.Ubuntu => SupportUbuntu,
            _ => false
        };
    }

    /// <summary>
    /// 获取安装脚本
    /// </summary>
    public string GetInstallScript(OperatingSystemType osType)
    {
        return osType == OperatingSystemType.Windows 
            ? InstallScriptWindows 
            : InstallScriptLinux;
    }

    /// <summary>
    /// 获取卸载脚本
    /// </summary>
    public string GetUninstallScript(OperatingSystemType osType)
    {
        return osType == OperatingSystemType.Windows 
            ? UninstallScriptWindows 
            : UninstallScriptLinux;
    }

    /// <summary>
    /// 获取检测脚本
    /// </summary>
    public string GetCheckScript(OperatingSystemType osType)
    {
        return osType == OperatingSystemType.Windows 
            ? CheckScriptWindows 
            : CheckScriptLinux;
    }
}

/// <summary>
/// 简化的 RelayCommand 实现
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// 安装参数定义
/// </summary>
public partial class InstallParameter : ObservableObject
{
    [ObservableProperty]
    private string _key;
    
    [ObservableProperty]
    private string _name;
    
    [ObservableProperty]
    private string _description;
    
    [ObservableProperty]
    private ParameterType _type = ParameterType.Text;
    
    [ObservableProperty]
    private bool _required = false;  // 默认为非必填，需要必填时显式设置 Required = true
    
    [ObservableProperty]
    private string _defaultValue;
    
    [ObservableProperty]
    private string _regexPattern;
    
    [ObservableProperty]
    private int _minValue;
    
    [ObservableProperty]
    private int _maxValue;
    
    [ObservableProperty]
    private List<string> _options = new();
}

/// <summary>
/// 参数类型
/// </summary>
public enum ParameterType
{
    /// <summary>
    /// 文本
    /// </summary>
    Text,
    
    /// <summary>
    /// 密码
    /// </summary>
    Password,
    
    /// <summary>
    /// 数字
    /// </summary>
    Number,
    
    /// <summary>
    /// 端口
    /// </summary>
    Port,
    
    /// <summary>
    /// 路径
    /// </summary>
    Path,
    
    /// <summary>
    /// 下拉选择
    /// </summary>
    Dropdown,
    
    /// <summary>
    /// 布尔值
    /// </summary>
    Boolean
}
