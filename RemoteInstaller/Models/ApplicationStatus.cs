using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteInstaller.Models;

/// <summary>
/// 应用检测结果
/// </summary>
public partial class ApplicationStatus : ObservableObject
{
    [ObservableProperty]
    private bool _isInstalled;
    
    [ObservableProperty]
    private string _installedVersion;
    
    [ObservableProperty]
    private bool _isRunning;
    
    [ObservableProperty]
    private string _port;
    
    [ObservableProperty]
    private string _installPath;
    
    [ObservableProperty]
    private string _processName;
    
    [ObservableProperty]
    private string _errorMessage;

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusDisplayText
    {
        get
        {
            if (!IsInstalled) return "未安装";
            if (IsRunning) return $"运行中 (v{InstalledVersion})";
            return $"已停止 (v{InstalledVersion})";
        }
    }
}
