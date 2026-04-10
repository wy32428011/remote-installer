using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 自定义应用卡片 ViewModel
/// </summary>
public partial class CustomAppViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _appKey;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _appType = "GENERIC";

    [ObservableProperty]
    private string _remoteDirectory = string.Empty;

    [ObservableProperty]
    private string _startCommand = string.Empty;

    [ObservableProperty]
    private string _stopCommand = string.Empty;

    [ObservableProperty]
    private string _configFilePath = string.Empty;

    [ObservableProperty]
    private string _remoteFrontendDirectory = string.Empty;

    [ObservableProperty]
    private string _pidFilePath = string.Empty;

    [ObservableProperty]
    private string _configDirectory = string.Empty;

    [ObservableProperty]
    private string _configFileName = string.Empty;

    [ObservableProperty]
    private string _logDirectory = string.Empty;

    [ObservableProperty]
    private bool _isBuiltIn;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "未检测";

    private readonly ICommand _originalCommand;

    public ICommand DeployCommand { get; }

    public CustomAppViewModel(
        string id,
        string appKey,
        string name,
        string icon,
        string description,
        string appType,
        string remoteDirectory,
        string startCommand,
        string stopCommand,
        string configFilePath,
        string remoteFrontendDirectory,
        string pidFilePath,
        string configDirectory,
        string configFileName,
        string logDirectory,
        bool isBuiltIn,
        int sortOrder,
        ICommand deployCommand)
    {
        _id = id;
        _appKey = appKey;
        _name = name;
        _icon = icon;
        _description = description;
        _appType = appType;
        _remoteDirectory = remoteDirectory;
        _startCommand = startCommand;
        _stopCommand = stopCommand;
        _configFilePath = configFilePath;
        _remoteFrontendDirectory = remoteFrontendDirectory;
        _pidFilePath = pidFilePath;
        _configDirectory = configDirectory;
        _configFileName = configFileName;
        _logDirectory = logDirectory;
        _isBuiltIn = isBuiltIn;
        _sortOrder = sortOrder;
        _originalCommand = deployCommand;

        DeployCommand = new RelayCommand(() =>
        {
            _originalCommand.Execute(Id);
        });
    }
}
