using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;
using RemoteInstaller.Services;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 添加/编辑主机对话框 ViewModel
/// </summary>
public partial class AddHostViewModel : ObservableObject
{
    private readonly SshService _sshService;
    private readonly DatabaseService _databaseService;
    private readonly ILogger _logger;
    private readonly RemoteHost? _editingHost;
    private bool _connectionTested = false;
    private string _error = string.Empty;

    // 属性
    private string _dialogTitle = "添加远程主机";
    public string DialogTitle
    {
        get => _dialogTitle;
        set => SetProperty(ref _dialogTitle, value);
    }

    private string _hostName = string.Empty;
    public string HostName
    {
        get => _hostName;
        set
        {
            if (SetProperty(ref _hostName, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private string _ipAddress = string.Empty;
    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (SetProperty(ref _ipAddress, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private int _port = 22;
    public int Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private bool _showPassword;
    public bool ShowPassword
    {
        get => _showPassword;
        set => SetProperty(ref _showPassword, value);
    }

    private AuthType _authType = AuthType.Password;
    public AuthType AuthType
    {
        get => _authType;
        set
        {
            if (SetProperty(ref _authType, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private string _keyPath = string.Empty;
    public string KeyPath
    {
        get => _keyPath;
        set
        {
            if (SetProperty(ref _keyPath, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private string _keyPassphrase = string.Empty;
    public string KeyPassphrase
    {
        get => _keyPassphrase;
        set => SetProperty(ref _keyPassphrase, value);
    }

    /// <summary>
    /// 操作系统类型（内部使用）
    /// </summary>
    private OperatingSystemType _osType = OperatingSystemType.CentOS;
    public OperatingSystemType OsType
    {
        get => _osType;
        set
        {
            if (SetProperty(ref _osType, value))
            {
                UpdateOsTypeDisplay();
            }
        }
    }

    /// <summary>
    /// 操作系统类型显示（UI 绑定，只读）
    /// </summary>
    private string _osTypeDisplay = "未检测";
    public string OsTypeDisplay
    {
        get => _osTypeDisplay;
        set => SetProperty(ref _osTypeDisplay, value);
    }

    private string _groupName = string.Empty;
    public string GroupName
    {
        get => _groupName;
        set => SetProperty(ref _groupName, value);
    }

    private string _testResult = string.Empty;
    public string TestResult
    {
        get => _testResult;
        set => SetProperty(ref _testResult, value);
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                UpdateCanTestConnection();
                UpdateCanSave();
            }
        }
    }

    private bool _testSuccess;
    public bool TestSuccess
    {
        get => _testSuccess;
        set => SetProperty(ref _testSuccess, value);
    }

    /// <summary>
    /// 是否可以测试连接
    /// </summary>
    private bool _canTestConnection = false;
    public bool CanTestConnection
    {
        get => _canTestConnection;
        set => SetProperty(ref _canTestConnection, value);
    }

    /// <summary>
    /// 是否可以保存
    /// </summary>
    private bool _canSave = false;
    public bool CanSave
    {
        get => _canSave;
        set => SetProperty(ref _canSave, value);
    }

    /// <summary>
    /// 是否繁忙（用于禁用按钮）
    /// </summary>
    public bool IsBusy => IsTesting;

    /// <summary>
    /// 错误消息
    /// </summary>
    public string Error => _error;

    public AddHostViewModel(SshService sshService, DatabaseService databaseService, ILogger logger, RemoteHost? editingHost = null)
    {
        _sshService = sshService;
        _databaseService = databaseService;
        _logger = logger;
        _editingHost = editingHost;

        InitializeCommands();

        if (_editingHost != null)
        {
            LoadHost(_editingHost);
        }
    }

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => CanTestConnection);
        BrowseKeyPathCommand = new RelayCommand(BrowseKeyPath);
        SaveCommand = new RelayCommand(Save, () => CanSave);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    /// 测试连接命令
    /// </summary>
    public ICommand TestConnectionCommand { get; private set; } = null!;

    /// <summary>
    /// 保存命令
    /// </summary>
    public ICommand BrowseKeyPathCommand { get; private set; } = null!;
    public ICommand SaveCommand { get; private set; } = null!;

    /// <summary>
    /// 取消命令
    /// </summary>
    public ICommand CancelCommand { get; private set; } = null!;

    /// <summary>
    /// 加载主机数据（编辑模式）
    /// </summary>
    private void LoadHost(RemoteHost host)
    {
        DialogTitle = "编辑远程主机";
        HostName = host.Name;
        IpAddress = host.IpAddress;
        Port = host.Port;
        Username = host.Username;
        AuthType = host.AuthType;
        KeyPath = host.KeyPath ?? string.Empty;
        OsType = host.OsType;
        GroupName = host.GroupName ?? string.Empty;
        _connectionTested = true; // 编辑模式下视为已测试

        if (host.AuthType == AuthType.Password)
        {
            Password = EncryptionService.Decrypt(host.EncryptedPassword ?? string.Empty);
        }
        else
        {
            KeyPassphrase = EncryptionService.Decrypt(host.EncryptedKeyPassphrase ?? string.Empty);
        }

        // 编辑模式下可以直接保存
        UpdateCanSave();
    }

    /// <summary>
    /// 更新操作系统类型显示文本
    /// </summary>
    private void UpdateOsTypeDisplay()
    {
        OsTypeDisplay = OsType switch
        {
            OperatingSystemType.Windows => "Windows",
            OperatingSystemType.Ubuntu => "Ubuntu",
            OperatingSystemType.CentOS => "CentOS",
            _ => OsType.ToString()
        };
    }

    /// <summary>
    /// 更新 CanTestConnection
    /// </summary>
    private void UpdateCanTestConnection()
    {
        bool isValid = !string.IsNullOrWhiteSpace(HostName) 
                     && !string.IsNullOrWhiteSpace(IpAddress) 
                     && IsValidIpAddress(IpAddress)
                     && Port >= 1 && Port <= 65535
                     && !string.IsNullOrWhiteSpace(Username)
                     && (AuthType != AuthType.Password || !string.IsNullOrWhiteSpace(Password))
                     && (AuthType != AuthType.PrivateKey || !string.IsNullOrWhiteSpace(KeyPath));
        
        CanTestConnection = isValid && !IsTesting;
    }

    /// <summary>
    /// 更新 CanSave
    /// </summary>
    private void UpdateCanSave()
    {
        // 保存需要：基本信息有效 + 已测试连接成功（或编辑模式）
        bool hasValidInfo = !string.IsNullOrWhiteSpace(HostName) 
                          && !string.IsNullOrWhiteSpace(IpAddress) 
                          && IsValidIpAddress(IpAddress)
                          && Port >= 1 && Port <= 65535
                          && !string.IsNullOrWhiteSpace(Username)
                          && (AuthType != AuthType.Password || !string.IsNullOrWhiteSpace(Password))
                          && (AuthType != AuthType.PrivateKey || !string.IsNullOrWhiteSpace(KeyPath));
        
        // 添加模式需要测试连接成功，编辑模式可以直接保存
        bool canSave = hasValidInfo && (_connectionTested || _editingHost != null);
        
        CanSave = canSave && !IsBusy;
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    private async Task TestConnectionAsync()
    {
        if (!Validate()) return;

        IsTesting = true;
        TestResult = "正在连接...";
        TestSuccess = false;
        _connectionTested = false;
        UpdateCanSave();

        try
        {
            var host = CreateHost();
            var result = await _sshService.TestConnectionAsync(host);

            if (result.Success)
            {
                // 连接成功，自动设置操作系统类型
                OsType = result.DetectedOsType;
                _connectionTested = true;
                TestSuccess = true;
                TestResult = result.Message;
                
                _logger?.Info($"连接测试成功，检测到操作系统：{result.DetectedOsType}");
            }
            else
            {
                _connectionTested = false;
                TestSuccess = false;
                TestResult = result.Message;

                _logger?.Warning($"连接测试失败：{result.Message}");
            }
        }
        catch (Exception ex)
        {
            _connectionTested = false;
            TestSuccess = false;
            TestResult = $"连接失败：{ex.Message}";

            _logger?.Error($"连接测试异常：{ex.Message}");
        }
        finally
        {
            IsTesting = false;
            UpdateCanTestConnection();
            UpdateCanSave();
        }
    }

    /// <summary>
    /// 浏览私钥文件
    /// </summary>
    private void BrowseKeyPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择私钥文件",
            Filter = "所有文件|*.*|私钥文件|*.pem;*.key;id_rsa|文本文件|*.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            KeyPath = dialog.FileName;
        }
    }

    /// <summary>
    /// 保存
    /// </summary>
    private void Save()
    {
        try
        {
            if (!Validate())
            {
                _logger?.Warning("表单验证失败，无法保存");
                return;
            }

            // 添加模式下必须已测试连接
            if (_editingHost == null && !_connectionTested)
            {
                SetError("请先测试连接以检测操作系统类型");
                return;
            }

            var host = CreateHost();

            if (_editingHost != null)
            {
                // 更新现有主机
                host.Id = _editingHost.Id;
                host.CreatedAt = _editingHost.CreatedAt;
                _logger?.Info($"更新主机：{host.Name}, ID: {host.Id}");
            }
            else
            {
                _logger?.Info($"添加新主机：{host.Name}, ID: {host.Id}");
            }

            host.UpdateModifiedTime();
            _databaseService.SaveHost(host);

            // 关闭对话框，返回 true 表示保存成功
            CloseDialog(true);
        }
        catch (Exception ex)
        {
            _logger?.Error($"保存主机错误：{ex.Message}");
            SetError($"保存失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 取消
    /// </summary>
    private void Cancel()
    {
        _logger?.Info("用户取消操作");
        // 关闭对话框，返回 false 表示取消
        CloseDialog(false);
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    private void CloseDialog(bool? result)
    {
        // 通过 Application.Windows 查找包含此 ViewModel 的窗口
        foreach (var window in Application.Current.Windows)
        {
            if (window is Views.AddHostDialog dialog && dialog.ViewModel == this)
            {
                try
                {
                    dialog.DialogResult = result;
                }
                catch
                {
                    dialog.Close();
                }
                return;
            }
        }
    }

    /// <summary>
    /// 验证输入
    /// </summary>
    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName))
        {
            SetError("请输入主机名称");
            return false;
        }

        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            SetError("请输入 IP 地址");
            return false;
        }

        if (!IsValidIpAddress(IpAddress))
        {
            SetError("IP 地址格式不正确");
            return false;
        }

        if (Port < 1 || Port > 65535)
        {
            SetError("端口必须在 1-65535 之间");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            SetError("请输入用户名");
            return false;
        }

        if (AuthType == AuthType.Password && string.IsNullOrWhiteSpace(Password))
        {
            SetError("请输入密码");
            return false;
        }

        if (AuthType == AuthType.PrivateKey && string.IsNullOrWhiteSpace(KeyPath))
        {
            SetError("请选择私钥文件");
            return false;
        }

        ClearError();
        return true;
    }

    /// <summary>
    /// 验证 IP 地址
    /// </summary>
    private bool IsValidIpAddress(string ip)
    {
        return System.Net.IPAddress.TryParse(ip, out _);
    }

    /// <summary>
    /// 创建主机对象
    /// </summary>
    private RemoteHost CreateHost()
    {
        var now = DateTime.Now;
        var host = new RemoteHost
        {
            Name = HostName,
            IpAddress = IpAddress,
            Port = Port,
            Username = Username,
            EncryptedPassword = AuthType == AuthType.Password ? EncryptionService.Encrypt(Password) : null,
            AuthType = AuthType,
            KeyPath = AuthType == AuthType.PrivateKey ? KeyPath : null,
            EncryptedKeyPassphrase = AuthType == AuthType.PrivateKey && !string.IsNullOrEmpty(KeyPassphrase)
                ? EncryptionService.Encrypt(KeyPassphrase) : null,
            OsType = OsType,
            GroupName = string.IsNullOrWhiteSpace(GroupName) ? null : GroupName,
            Status = HostStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        return host;
    }

    /// <summary>
    /// 设置错误消息
    /// </summary>
    private void SetError(string error)
    {
        _error = error;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>
    /// 清除错误消息
    /// </summary>
    private void ClearError()
    {
        _error = string.Empty;
        OnPropertyChanged(nameof(Error));
    }
}

/// <summary>
/// 简化的 RelayCommand 实现
/// </summary>
public class RelayCommand<T> : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Func<Task> execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> execute) : this(execute, () => true) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute();

    public void Execute(object? parameter) => _execute().GetAwaiter().GetResult();

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute) : this(execute, () => true) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute();

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
