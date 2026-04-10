using System;

namespace RemoteInstaller.Models;

/// <summary>
/// 自定义应用定义（持久化模型）
/// </summary>
public class CustomAppDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public string Description { get; set; } = string.Empty;
    public string AppType { get; set; } = "GENERIC";
    public string RemoteDirectory { get; set; } = string.Empty;
    public string StartCommand { get; set; } = string.Empty;
    public string StopCommand { get; set; } = string.Empty;
    public string ConfigFilePath { get; set; } = string.Empty;
    public string RemoteFrontendDirectory { get; set; } = string.Empty;
    public string PidFilePath { get; set; } = string.Empty;
    public string ConfigDirectory { get; set; } = string.Empty;
    public string ConfigFileName { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
