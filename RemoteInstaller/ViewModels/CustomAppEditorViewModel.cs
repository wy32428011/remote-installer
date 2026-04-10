using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteInstaller.Models;

namespace RemoteInstaller.ViewModels;

public partial class CustomAppEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _dialogTitle = "添加自定义应用";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "📦";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _appType = "GENERIC";

    [ObservableProperty]
    private string _remoteDirectory = "/opt";

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
    private string _configFileName = "application-prod.properties";

    [ObservableProperty]
    private string _logDirectory = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public Action<bool?>? CloseAction { get; set; }

    public CustomAppEditorViewModel()
    {
    }

    public CustomAppEditorViewModel(CustomAppDefinition app)
    {
        DialogTitle = "编辑自定义应用";
        Name = app.Name;
        Icon = string.IsNullOrWhiteSpace(app.Icon) ? "📦" : app.Icon;
        Description = app.Description;
        AppType = string.IsNullOrWhiteSpace(app.AppType) ? "GENERIC" : app.AppType;
        RemoteDirectory = app.RemoteDirectory;
        StartCommand = app.StartCommand;
        StopCommand = app.StopCommand;
        ConfigFilePath = app.ConfigFilePath;
        RemoteFrontendDirectory = app.RemoteFrontendDirectory;
        PidFilePath = app.PidFilePath;
        ConfigDirectory = app.ConfigDirectory;
        ConfigFileName = string.IsNullOrWhiteSpace(app.ConfigFileName) ? "application-prod.properties" : app.ConfigFileName;
        LogDirectory = app.LogDirectory;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "应用名称不能为空";
            return;
        }

        if (string.IsNullOrWhiteSpace(AppType) ||
            (AppType != "SUPPORT" && AppType != "GENERIC" && AppType != "CUSTOM"))
        {
            ErrorMessage = "部署类型必须为 SUPPORT / GENERIC / CUSTOM";
            return;
        }

        ErrorMessage = string.Empty;
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    public CustomAppDefinition ToDefinition()
    {
        var remoteDirectory = RemoteDirectory?.Trim() ?? string.Empty;
        var normalizedRemoteDirectory = string.IsNullOrWhiteSpace(remoteDirectory) ? "/opt" : remoteDirectory;
        var normalizedConfigDirectory = string.IsNullOrWhiteSpace(ConfigDirectory)
            ? $"{normalizedRemoteDirectory.TrimEnd('/')}/conf"
            : ConfigDirectory.Trim();
        var normalizedConfigFileName = string.IsNullOrWhiteSpace(ConfigFileName)
            ? "application-prod.properties"
            : ConfigFileName.Trim();
        var normalizedConfigFilePath = string.IsNullOrWhiteSpace(ConfigFilePath)
            ? $"{normalizedConfigDirectory.TrimEnd('/')}/{normalizedConfigFileName}"
            : ConfigFilePath.Trim();

        return new CustomAppDefinition
        {
            Name = Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? "📦" : Icon.Trim(),
            Description = Description?.Trim() ?? string.Empty,
            AppType = AppType.Trim().ToUpperInvariant(),
            RemoteDirectory = normalizedRemoteDirectory,
            StartCommand = StartCommand?.Trim() ?? string.Empty,
            StopCommand = StopCommand?.Trim() ?? string.Empty,
            ConfigFilePath = normalizedConfigFilePath,
            RemoteFrontendDirectory = string.IsNullOrWhiteSpace(RemoteFrontendDirectory)
                ? $"/var/www/{Name.Trim().ToLowerInvariant()}"
                : RemoteFrontendDirectory.Trim(),
            PidFilePath = string.IsNullOrWhiteSpace(PidFilePath)
                ? $"{normalizedRemoteDirectory.TrimEnd('/')}/run/run.PID"
                : PidFilePath.Trim(),
            ConfigDirectory = normalizedConfigDirectory,
            ConfigFileName = normalizedConfigFileName,
            LogDirectory = string.IsNullOrWhiteSpace(LogDirectory)
                ? $"{normalizedRemoteDirectory.TrimEnd('/')}/log"
                : LogDirectory.Trim(),
            IsEnabled = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }
}
