using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;

namespace RemoteInstaller.Services;

/// <summary>
/// 安装服务
/// 负责执行远程应用的安装、卸载和状态检测
/// </summary>
public class InstallerService : IDisposable
{
    private readonly SshService _sshService;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly FileLoggerService _fileLogger;
    private readonly ScriptResolver _scriptResolver = new();
    private readonly bool _disposeSshService;
    private bool _disposed;

    public InstallerService(SshService sshService, ILogger logger, bool disposeSshService = false)
    {
        _sshService = sshService;
        _logger = logger;
        _disposeSshService = disposeSshService;
        _databaseService = new DatabaseService();
        _fileLogger = new FileLoggerService();

        _fileLogger.Info("InstallerService", $"InstallerService 初始化，SSH服务: {sshService.GetType().Name}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileLogger.Dispose();
        _databaseService.Dispose();

        if (_disposeSshService)
        {
            _sshService.Dispose();
        }
    }

    /// <summary>
    /// 执行安装
    /// </summary>
    public async Task<InstallTask> InstallAsync(
        RemoteHost host,
        ApplicationInfo app,
        Dictionary<string, string> parameters,
        string? localPackagePath = null,
        IProgress<InstallTask>? progressReporter = null,
        CancellationToken cancellationToken = default,
        IProgress<LogEntry>? logReporter = null)
    {
        
        var task = new InstallTask
        {
            HostId = host.Id,
            HostName = host.Name,
            AppId = app.Id,
            AppName = app.Name,
            AppVersion = app.Version
        };

        // 创建日志收集器
        var logCollector = new LogCollector(_logger, task.Id);
        
        // 监听进度更新
        logCollector.ProgressUpdated += (stage, progressVal) =>
        {
            task.UpdateProgress(ParseStage(stage), progressVal);
            progressReporter?.Report(task);

            // 实时更新数据库中的任务状态
            try { _databaseService.SaveTask(task); } catch { /* 忽略更新错误 */ }
        };

        logCollector.LogReceived += entry =>
        {
            logReporter?.Report(entry);
        };

        void AddTaskLog(LogLevel level, string message)
        {
            logCollector.AddLog(level, message);
        }

        var customRemoteUploadPath = parameters.TryGetValue("REMOTE_UPLOAD_PATH", out var remoteUploadPathValue)
            ? NormalizeRemoteDirectoryPath(host.OsType, remoteUploadPathValue)
            : string.Empty;
        parameters.Remove("REMOTE_UPLOAD_PATH");

        var operationStopwatch = Stopwatch.StartNew();
        string? remoteWorkDir = null;
        try
        {
            task.Start();
                        
            // 初始保存任务记录，确保外键一致性
            _databaseService.SaveTask(task);

            // 1. 连接服务器
            task.UpdateProgress(InstallStage.Connecting, 5);
            progressReporter?.Report(task);
            _logger.Info($"正在连接 {host.Name}...");
            await _sshService.ConnectAsync(host, cancellationToken);
            _logger.Success("连接成功");

            // 1.5 创建远程工作目录
            var workDirName = $"{app.Id}_{Guid.NewGuid():N}";
            remoteWorkDir = host.OsType == OperatingSystemType.Windows 
                ? $"C:\\Windows\\Temp\\remote_install\\{workDirName}"
                : $"/tmp/remote_install/{workDirName}";
            
            _logger.Info($"创建远程工作目录: {remoteWorkDir}");
            if (host.OsType == OperatingSystemType.Windows)
            {
                await _sshService.ExecuteCommandAsync($"New-Item -ItemType Directory -Force -Path \"{remoteWorkDir}\"", cancellationToken: cancellationToken);
            }
            else
            {
                await _sshService.ExecuteCommandAsync($"mkdir -p \"{remoteWorkDir}\"", cancellationToken: cancellationToken);
            }

            // 2. 检查并上传安装脚本
            string? remoteScriptPath = null;
            var version = parameters.ContainsKey("version") ? parameters["version"] : app.Version;
            var localScriptPath = GetLocalScriptPath(app.Id, version, host.OsType);
            if (!string.IsNullOrEmpty(localScriptPath) && File.Exists(localScriptPath))
            {
                _logger.Info($"发现本地安装脚本：{Path.GetFileName(localScriptPath)}，准备上传...");
                AddTaskLog(LogLevel.Info, $"本地安装脚本路径：{localScriptPath}");
                var remoteScriptFileName = Path.GetFileName(localScriptPath);
                remoteScriptPath = host.OsType == OperatingSystemType.Windows
                    ? Path.Combine(remoteWorkDir, remoteScriptFileName).Replace("/", "\\")
                    : $"{remoteWorkDir}/{remoteScriptFileName}";

                if (host.OsType != OperatingSystemType.Windows)
                {
                    // Linux 脚本需要处理换行符 (CRLF -> LF)，否则会导致 shebang 解析错误
                    var content = (await File.ReadAllTextAsync(localScriptPath, cancellationToken))
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n");
                    await _sshService.UploadTextAsync(content, remoteScriptPath, host.OsType, cancellationToken);
                    await _sshService.ExecuteCommandAsync($"chmod +x \"{remoteScriptPath}\"", cancellationToken: cancellationToken);
                }
                else
                {
                    await _sshService.UploadFileAsync(localScriptPath, remoteScriptPath);
                }

                AddTaskLog(LogLevel.Info, $"远程安装脚本路径：{remoteScriptPath}");
                _logger.Success($"安装脚本上传完成");
            }

            // 3. 上传安装包（如果使用本地包）
            string? remotePackagePath = null;
            
            if (!string.IsNullOrEmpty(localPackagePath))
            {
                var localPackageIsFile = File.Exists(localPackagePath);
                var localPackageIsDirectory = Directory.Exists(localPackagePath);

                if (!localPackageIsFile && !localPackageIsDirectory)
                {
                    throw new FileNotFoundException($"本地安装包不存在：{localPackagePath}");
                }

                task.UpdateProgress(InstallStage.Uploading, 15);
                progressReporter?.Report(task);

                if (localPackageIsDirectory)
                {
                    var directoryName = Path.GetFileName(localPackagePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var uniqueDirectoryName = $"{directoryName}_{Guid.NewGuid():N}";
                    var useCustomRemoteUploadPath = IsJdkApplication(app) && !string.IsNullOrWhiteSpace(customRemoteUploadPath);
                    var remoteDirectoryBase = useCustomRemoteUploadPath
                        ? customRemoteUploadPath
                        : remoteWorkDir;
                    remotePackagePath = CombineRemotePath(host.OsType, remoteDirectoryBase, uniqueDirectoryName);

                    var fileCount = Directory.GetFiles(localPackagePath, "*", SearchOption.AllDirectories).Length;
                    _logger.Info($"正在上传目录型本地安装包：{directoryName} (文件数：{fileCount})...");

                    if (useCustomRemoteUploadPath)
                    {
                        AddTaskLog(LogLevel.Info, $"JDK 目录将上传到用户指定目录：{remotePackagePath}");
                    }

                    await _sshService.UploadDirectoryAsync(
                        localPackagePath,
                        remotePackagePath,
                        progress =>
                        {
                            var currentProgress = 15 + (progress * 0.25);
                            task.UpdateProgress(InstallStage.Uploading, currentProgress);
                            progressReporter?.Report(task);
                        },
                        cancellationToken);

                    AddTaskLog(LogLevel.Info, $"远端离线目录：{remotePackagePath}");
                    _logger.Success($"目录上传完成：{remotePackagePath}");
                }
                else
                {
                    var fileSize = new FileInfo(localPackagePath).Length;
                    var fileSizeMB = fileSize / (1024.0 * 1024.0);
                    _logger.Info($"正在上传 {Path.GetFileName(localPackagePath)} (大小：{fileSizeMB:F2} MB)...");

                    var fileName = Path.GetFileNameWithoutExtension(localPackagePath);
                    var extension = Path.GetExtension(localPackagePath);
                    var uniqueFileName = $"{fileName}_{Guid.NewGuid():N}{extension}";
                    remotePackagePath = host.OsType == OperatingSystemType.Windows
                        ? Path.Combine(remoteWorkDir, uniqueFileName).Replace("/", "\\")
                        : $"{remoteWorkDir}/{uniqueFileName}";

                    await _sshService.UploadFileAsync(
                        localPackagePath,
                        remotePackagePath,
                        progress =>
                        {
                            var currentProgress = 15 + (progress * 0.25);
                            task.UpdateProgress(InstallStage.Uploading, currentProgress);
                            progressReporter?.Report(task);
                        },
                        cancellationToken);

                    AddTaskLog(LogLevel.Info, $"远端离线包路径：{remotePackagePath}");
                    _logger.Success($"上传完成：{remotePackagePath}");
                }

                parameters["PACKAGE_PATH"] = remotePackagePath;
                AddTaskLog(LogLevel.Info, $"最终 PACKAGE_PATH：{remotePackagePath}");
            }
            else
            {
                AddTaskLog(LogLevel.Warning, "未指定本地包，将使用在线安装或脚本自定义逻辑");
                _logger.Info("未指定本地包，将使用在线安装或脚本自定义逻辑");
            }

            if (string.Equals(app.Id, "mariadb", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(localPackagePath))
                {
                    throw new InvalidOperationException("MariaDB 仅支持本地资源安装，请先在安装配置中使用 Scripts/MariaDB 下的离线资源目录。");
                }

                if (string.IsNullOrWhiteSpace(remotePackagePath) ||
                    !parameters.TryGetValue("PACKAGE_PATH", out var packagePath) ||
                    string.IsNullOrWhiteSpace(packagePath))
                {
                    throw new InvalidOperationException("MariaDB 离线安装资源路径未准备完成，请重新选择 Scripts/MariaDB 下的本地离线资源目录后再试。");
                }
            }

            if (string.Equals(app.Id, "mosquitto", StringComparison.OrdinalIgnoreCase))
            {
                await PrepareMosquittoSecretFilesAsync(host, parameters, remoteWorkDir, cancellationToken);
            }

            // 4. 执行安装
            RemoteCommandResult? scriptResult = null;
            if (remoteScriptPath != null || !string.IsNullOrEmpty(app.GetInstallScript(host.OsType)))
            {
                task.UpdateProgress(InstallStage.Installing, 40);
                progressReporter?.Report(task);
                _logger.Info("正在执行安装脚本...");
                try
                {
                    scriptResult = await ExecuteInstallScriptAsync(host, app, parameters, logCollector, remoteScriptPath, cancellationToken);
                    if (scriptResult.Failed)
                    {
                        AddTaskLog(LogLevel.Warning, $"安装脚本退出异常，继续通过状态检测确认真实安装结果：{scriptResult.ExitCode}");
                        _logger.Warning($"安装脚本退出异常，将继续验证真实安装状态：{scriptResult.ExitCode}");
                    }
                    else
                    {
                        _logger.Success("安装脚本执行完成");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scriptResult = new RemoteCommandResult
                    {
                        Command = "install script",
                        ExitCode = -1,
                        Stderr = ex.Message
                    };
                    AddTaskLog(LogLevel.Warning, $"安装脚本退出异常，继续通过状态检测确认真实安装结果：{ex.Message}");
                    _logger.Warning($"安装脚本退出异常，将继续验证真实安装状态：{ex.Message}");
                }
            }
            else if (remotePackagePath != null)
            {
                // 如果没有脚本但有包，使用默认的解压安装逻辑
                task.UpdateProgress(InstallStage.Extracting, 40);
                progressReporter?.Report(task);
                _logger.Info("未发现安装脚本，使用默认包安装逻辑...");
                await ExtractPackageAsync(host, remotePackagePath, app.Name, logCollector, remoteWorkDir, cancellationToken);
                _logger.Success("包安装完成");
            }
            else
            {
                throw new Exception("没有可用的安装脚本或安装包");
            }

            // 5. 验证安装
            task.UpdateProgress(InstallStage.Verifying, 90);
            progressReporter?.Report(task);
            _logger.Info("正在验证安装状态...");

            // 智能等待：轮询检测应用状态，最长10秒，每500ms检测一次
            var maxWaitMs = 10000;
            var checkIntervalMs = 500;
            var elapsed = 0;
            ApplicationStatus? status = null;

            while (elapsed < maxWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = await CheckStatusAsync(host, app, cancellationToken);

                if (status.IsInstalled)
                {
                    _logger.Info($"应用已检测到（等待{elapsed}ms），正在确认运行状态...");
                    // 如果已安装，再多等1秒让服务完全启动
                    await Task.Delay(1000, cancellationToken);
                    status = await CheckStatusAsync(host, app, cancellationToken);
                    break;
                }

                await Task.Delay(checkIntervalMs, cancellationToken);
                elapsed += checkIntervalMs;
            }

            // 如果超时未检测到，尝试最后检测一次
            if (status == null || !status.IsInstalled)
            {
                status = await CheckStatusAsync(host, app, cancellationToken);
            }

            var decision = OperationDecisionPolicy.DecideInstall(scriptResult, status ?? new ApplicationStatus());
            if (decision.HasWarning)
            {
                AddTaskLog(LogLevel.Warning, decision.Message);
                _logger.Warning(decision.Message);
            }

            if (decision.Outcome == OperationOutcome.Completed)
            {
                task.Complete();
                progressReporter?.Report(task);
                _logger.Success($"安装完成！{app.Name} { (status?.IsRunning == true ? "已成功启动" : "已安装但未运行") }");
            }
            else
            {
                task.Fail(decision.Message);
                AddTaskLog(LogLevel.Error, decision.Message);
                progressReporter?.Report(task);
                _logger.Error(decision.Message);
            }
            
            // 更新最终任务状态
            _databaseService.SaveTask(task);
            
            // 保存任务日志
            _databaseService.SaveTaskLogs(task.Id, logCollector.GetLogs());
        }
        catch (OperationCanceledException)
        {
            task.Cancel();
            _databaseService.SaveTask(task);
            progressReporter?.Report(task);
            _logger.Warning("安装已取消");
        }
        catch (Exception ex)
        {
            _logger.Error($"安装发生致命错误: {ex}");
            task.Fail(ex.Message);
            _databaseService.SaveTask(task);
            progressReporter?.Report(task);
            _logger.Error($"安装失败：{ex.Message}");
        }
        finally
        {
            if (string.Equals(app.Id, "mosquitto", StringComparison.OrdinalIgnoreCase))
            {
                await CleanupMosquittoSecretFilesAsync(host, parameters, cancellationToken);
            }

            // 尝试清理远程临时工作目录 (如果已创建)
            // 只有在安装成功或手动取消的情况下才清理。
            // 如果安装失败，则保留该目录，以便用户查看日志文件。
            if (!string.IsNullOrEmpty(remoteWorkDir))
            {
                if (task.Status == Models.TaskStatus.Failed)
                {
                    _logger.Warning($"安装失败，已保留远程工作目录以供排查: {remoteWorkDir}");
                }
                else
                {
                    try
                    {
                        if (host.OsType == OperatingSystemType.Windows)
                        {
                            await _sshService.ExecuteCommandAsync($"Remove-Item -Recurse -Force -Path \"{remoteWorkDir}\"", cancellationToken: CancellationToken.None);
                        }
                        else
                        {
                            await _sshService.ExecuteCommandAsync($"rm -rf \"{remoteWorkDir}\"", cancellationToken: CancellationToken.None);
                        }
                        _logger.Info($"已清理远程工作目录: {remoteWorkDir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"清理远程目录失败: {ex.Message}");
                    }
                }
            }
            
            operationStopwatch.Stop();
            _fileLogger.Info("InstallAsync", $"安装任务结束，状态: {task.Status}, 耗时: {operationStopwatch.ElapsedMilliseconds}ms");
            logCollector.Dispose();
        }

        return task;
    }

    /// <summary>
    /// 解析进度上报的阶段名称为 InstallStage 枚举
    /// </summary>
    private InstallStage ParseStage(string stageName)
    {
        var stage = stageName.ToLower();
        return stage switch
        {
            "检查依赖" or "dependencies" or "initializing" or "preparing" => InstallStage.Preparing,
            "下载安装包" or "download" or "connecting" or "uploading" => InstallStage.Connecting,
            "解压安装包" or "extract" or "extracting" => InstallStage.Extracting,
            "安装" or "install" or "installing" or "uninstalling" or "uninstall" or "stopping" => InstallStage.Installing,
            "配置" or "config" or "configuring" or "settingpassword" or "cleaning" => InstallStage.Configuring,
            "启动服务" or "start" or "starting" => InstallStage.Starting,
            "验证安装" or "verifying" => InstallStage.Verifying,
            "完成" or "complete" or "completed" or "finished" or "success" => InstallStage.Completed,
            _ => InstallStage.Installing
        };
    }

    /// <summary>
    /// 执行卸载
    /// </summary>
    public async Task<InstallTask> UninstallAsync(
        RemoteHost host,
        ApplicationInfo app,
        bool keepData = false,
        IProgress<InstallTask>? progressReporter = null,
        CancellationToken cancellationToken = default,
        IProgress<LogEntry>? logReporter = null)
    {
        var taskId = Guid.NewGuid().ToString("N");
        _fileLogger.Info("UninstallAsync", $"========== 卸载任务开始 ==========");
        _fileLogger.Info("UninstallAsync", $"任务ID: {taskId}");
        _fileLogger.Info("UninstallAsync", $"主机: {host.Name} ({host.IpAddress}:{host.Port})");
        _fileLogger.Info("UninstallAsync", $"应用: {app.Name} v{app.Version} (ID: {app.Id})");
        _fileLogger.Info("UninstallAsync", $"保留数据: {keepData}");
        _fileLogger.Info("UninstallAsync", $"操作系统: {host.OsType}");
        var operationStopwatch = Stopwatch.StartNew();

        var task = new InstallTask
        {
            Id = taskId,
            HostId = host.Id,
            HostName = host.Name,
            AppId = app.Id,
            AppName = app.Name,
            AppVersion = app.Version
        };

        // 创建进度收集器
        var logCollector = new LogCollector(_logger, task.Id);

        // 监听进度更新
        logCollector.ProgressUpdated += (stage, progressVal) =>
        {
            task.UpdateProgress(ParseStage(stage), progressVal);
            progressReporter?.Report(task);
            try { _databaseService.SaveTask(task); } catch { }
        };

        logCollector.LogReceived += entry =>
        {
            logReporter?.Report(entry);
        };

        string? remoteWorkDir = null;
        try
        {
            task.Start();
            // 初始保存
            _databaseService.SaveTask(task);

            _fileLogger.Info("UninstallAsync", "正在连接远程服务器...");
            await _sshService.ConnectAsync(host, cancellationToken);
            _fileLogger.Info("UninstallAsync", "连接成功");

            _fileLogger.Info("UninstallAsync", $"正在执行 {app.Name} 卸载...");

            // 获取卸载脚本路径
            var scriptPath = GetLocalScriptPath(app.Id, app.Version, host.OsType, "uninstall");
            _fileLogger.Info("UninstallAsync", $"脚本路径: {scriptPath ?? "未找到"}");

            string scriptContent = "";
            RemoteCommandResult? scriptResult = null;

            if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
            {
                scriptContent = await File.ReadAllTextAsync(scriptPath, cancellationToken);
                _fileLogger.Info("UninstallAsync", $"从文件加载卸载脚本成功，长度: {scriptContent.Length} 字符");
            }
            else
            {
                // 如果没有找到对应的脚本文件，尝试使用 ApplicationInfo 中的默认脚本
                var configuredScript = app.GetUninstallScript(host.OsType);
                var configuredScriptPath = _scriptResolver.TryResolveConfiguredScriptFilePath(configuredScript, host.OsType);
                if (!string.IsNullOrEmpty(configuredScriptPath) && File.Exists(configuredScriptPath))
                {
                    scriptContent = await File.ReadAllTextAsync(configuredScriptPath, cancellationToken);
                    _fileLogger.Info("UninstallAsync", $"从配置引用加载卸载脚本: {configuredScriptPath}，长度: {scriptContent.Length} 字符");
                }
                else
                {
                    scriptContent = configuredScript;
                    _fileLogger.Info("UninstallAsync", $"使用内置卸载脚本，长度: {scriptContent.Length} 字符");
                }
            }

            if (!string.IsNullOrEmpty(scriptContent))
            {
                // 创建远程工作目录 (兼容多操作系统)
                var workDirName = $"{app.Id}_{Guid.NewGuid():N}";
                remoteWorkDir = host.OsType == OperatingSystemType.Windows
                    ? $"C:\\Windows\\Temp\\remote_install\\{workDirName}"
                    : $"/tmp/remote_install/{workDirName}";

                _fileLogger.Info("UninstallAsync", $"创建远程工作目录: {remoteWorkDir}");

                if (host.OsType == OperatingSystemType.Windows)
                {
                    await _sshService.ExecuteCommandAsync($"New-Item -ItemType Directory -Force -Path \"{remoteWorkDir}\"", cancellationToken: cancellationToken);
                }
                else
                {
                    await _sshService.ExecuteCommandAsync($"mkdir -p \"{remoteWorkDir}\"", cancellationToken: cancellationToken);
                }

                var scriptExt = host.OsType == OperatingSystemType.Windows ? "ps1" : "sh";
                var remoteScriptPath = host.OsType == OperatingSystemType.Windows
                    ? $"{remoteWorkDir}\\uninstall.{scriptExt}"
                    : $"{remoteWorkDir}/uninstall.{scriptExt}";

                _fileLogger.Info("UninstallAsync", $"上传卸载脚本到: {remoteScriptPath}");
                if (host.OsType != OperatingSystemType.Windows)
                {
                    scriptContent = scriptContent
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n");
                }
                await _sshService.UploadTextAsync(scriptContent, remoteScriptPath, host.OsType, cancellationToken);

                if (host.OsType != OperatingSystemType.Windows)
                {
                    _fileLogger.Info("UninstallAsync", "设置脚本执行权限...");
                    await _sshService.ExecuteCommandAsync($"chmod +x \"{remoteScriptPath}\"", cancellationToken: cancellationToken);
                }

                string command;
                if (host.OsType != OperatingSystemType.Windows)
                {
                    var keepDataArg = keepData ? "--keep-data" : "--no-keep-data";

                    // 直接尝试用sudo执行脚本，让脚本自己检查权限
                    // 移除过于严格的 sudo -n true 检查（该检查会导致合法的免密sudo配置被误判）
                    // 如果sudo不可用，脚本会自己报错并输出错误信息
                    _fileLogger.Info("UninstallAsync", "使用 sudo 执行卸载脚本...");

                    // 关键修复：不使用 timeout 外层包裹，因为 timeout 会干扰 sudo 的执行
                    // 改为直接执行脚本，让脚本内部自己处理超时
                    // 添加 2>&1 确保 stderr 被捕获
                    command = $"cd \"{remoteWorkDir}\" && sudo bash \"{remoteScriptPath}\" {keepDataArg} 2>&1";

                                    }
                else
                {
                    // Windows PowerShell: 直接传递开关参数，不要用环境变量
                    var keepDataSwitch = keepData ? "$true" : "$false";
                    command = $"Set-Location \"{remoteWorkDir}\"; & \"{remoteScriptPath}\" -KeepData:${keepDataSwitch}";
                }
                
                var outputBuilder = new StringBuilder();
                _fileLogger.Info("UninstallAsync", $"执行卸载命令: {command}");
                try
                {
                    scriptResult = await _sshService.ExecuteCommandResultAsync(command,
                        line =>
                        {
                            logCollector.ProcessOutput(line);
                            outputBuilder.AppendLine(line);
                        },
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scriptResult = new RemoteCommandResult
                    {
                        Command = command,
                        ExitCode = -1,
                        Stderr = ex.Message
                    };
                    outputBuilder.AppendLine(ex.Message);
                    logCollector.AddLog(LogLevel.Warning, $"卸载脚本退出异常，继续通过状态检测确认真实卸载结果：{ex.Message}");
                    _logger.Warning($"卸载脚本退出异常，将继续验证真实卸载状态：{ex.Message}");
                }

                // 记录脚本执行输出
                _fileLogger.Info("UninstallAsync", $"脚本执行输出:\n{outputBuilder}");
                if (scriptResult?.Failed == true)
                {
                    logCollector.AddLog(LogLevel.Warning, $"卸载脚本退出异常，继续通过状态检测确认真实卸载结果：{scriptResult.ExitCode}");
                    _logger.Warning($"卸载脚本退出异常，将继续验证真实卸载状态：{scriptResult.ExitCode}");
                }

                // 尝试解析最终状态（如果脚本提供了机器可读输出）
                // 更加严谨的做法是：即使脚本运行完了，也主动调用 CheckStatusAsync 来验证真实状态
                var finalStatus = new ApplicationStatus();
                try
                {
                    finalStatus = await CheckStatusAsync(host, app, cancellationToken);
                    _fileLogger.Info("UninstallAsync", $"卸载后状态检测结果: IsInstalled={finalStatus.IsInstalled}, IsRunning={finalStatus.IsRunning}, Version={finalStatus.InstalledVersion}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"卸载后的状态验证失败：{ex.Message}");
                    _fileLogger.Error("UninstallAsync", "卸载后状态验证失败", ex);
                    ParseCheckOutput(outputBuilder.ToString(), finalStatus);
                }

                app.IsInstalled = finalStatus.IsInstalled;
                app.IsRunning = finalStatus.IsRunning;
                if (!string.IsNullOrEmpty(finalStatus.InstalledVersion) && finalStatus.InstalledVersion != "未知")
                {
                    app.InstalledVersion = finalStatus.InstalledVersion;
                }

                var protocolEvents = ScriptProtocolParser.Parse(
                    outputBuilder + Environment.NewLine +
                    string.Join(Environment.NewLine, logCollector.GetLogs().Select(log => log.Message))).ToList();
                var evidence = ApplicationStatusNormalizer.BuildEvidence(protocolEvents);
                if (finalStatus.IsInstalled || finalStatus.IsRunning)
                {
                    evidence = new ApplicationStatusEvidence
                    {
                        BinaryFound = evidence.BinaryFound || finalStatus.IsInstalled,
                        PackageFound = evidence.PackageFound,
                        ServiceFound = evidence.ServiceFound,
                        ServiceActive = evidence.ServiceActive || finalStatus.IsRunning,
                        ProcessFound = evidence.ProcessFound || finalStatus.IsRunning,
                        PortListening = evidence.PortListening,
                        ConfigOnlyResidue = evidence.ConfigOnlyResidue,
                        ServiceOnlyResidue = evidence.ServiceOnlyResidue
                    };
                }

                var decision = OperationDecisionPolicy.DecideUninstall(scriptResult, finalStatus, evidence);
                if (decision.HasWarning)
                {
                    logCollector.AddLog(LogLevel.Warning, decision.Message);
                    _logger.Warning(decision.Message);
                }

                if (decision.Outcome == OperationOutcome.Completed)
                {
                    task.Complete();
                    _logger.Success($"{app.Name} 卸载完成");
                    _fileLogger.Success("UninstallAsync", $"{app.Name} 卸载完成");
                }
                else
                {
                    task.Fail(decision.Message);
                    _logger.Error(decision.Message);
                }
            }
            else
            {
                task.Fail("未找到卸载脚本");
                _logger.Warning($"{app.Name} 未找到卸载脚本");
                _fileLogger.Error("UninstallAsync", "未找到卸载脚本");
            }
        }
        catch (OperationCanceledException)
        {
            task.Cancel();
            _logger.Warning("卸载已取消");
            _fileLogger.Warning("UninstallAsync", "卸载操作被取消");
        }
        catch (Exception ex)
        {
            task.Fail($"卸载失败: {ex.Message}");
            _logger.Error($"{app.Name} 卸载失败: {ex.Message}");
            _fileLogger.Error("UninstallAsync", $"卸载失败: {ex.Message}", ex);
        }
        finally
        {
            _fileLogger.Info("UninstallAsync", $"任务最终状态: {task.Status}, 错误信息: {task.ErrorMessage ?? "无"}");

            if (!string.IsNullOrEmpty(remoteWorkDir))
            {
                if (task.Status == RemoteInstaller.Models.TaskStatus.Failed)
                {
                    _logger.Warning($"卸载失败，已保留远程工作目录以供排查: {remoteWorkDir}");
                    _fileLogger.Warning("UninstallAsync", $"卸载失败，保留远程工作目录以供排查: {remoteWorkDir}");
                }
                else
                {
                    try
                    {
                        if (host.OsType == OperatingSystemType.Windows)
                        {
                            await _sshService.ExecuteCommandAsync($"Remove-Item -Recurse -Force -Path \"{remoteWorkDir}\"", cancellationToken: CancellationToken.None);
                        }
                        else
                        {
                            await _sshService.ExecuteCommandAsync($"rm -rf \"{remoteWorkDir}\"", cancellationToken: CancellationToken.None);
                        }
                        _logger.Info($"已清理远程工作目录: {remoteWorkDir}");
                        _fileLogger.Info("UninstallAsync", $"已清理远程工作目录: {remoteWorkDir}");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.Error("UninstallAsync", $"清理远程工作目录失败: {ex.Message}", ex);
                    }
                }
            }

            _databaseService.SaveTaskLogs(task.Id, logCollector.GetLogs());
            _databaseService.SaveTask(task);
            progressReporter?.Report(task);
            logCollector.Dispose();

            operationStopwatch.Stop();
            _fileLogger.Info("UninstallAsync", $"卸载任务结束，状态: {task.Status}, 耗时: {operationStopwatch.ElapsedMilliseconds}ms");
            _fileLogger.Info("UninstallAsync", "========== 卸载任务结束 ==========");
        }

        return task;
    }

    /// <summary>
    /// 检查应用状态
    /// </summary>
    public async Task<ApplicationStatus> CheckStatusAsync(
        RemoteHost host,
        ApplicationInfo app,
        CancellationToken cancellationToken = default)
    {
        var operationStopwatch = Stopwatch.StartNew();
        // 确保已连接（内部会检查是否已连接）
        await _sshService.ConnectAsync(host, cancellationToken);

        var status = new ApplicationStatus();

        var script = app.GetCheckScript(host.OsType);
        string? checkCommand = null;

        _fileLogger.Info("CheckStatusAsync", $"开始检测 {app.Name} 状态, OS: {host.OsType}");
        _fileLogger.Info("CheckStatusAsync", $"检测脚本路径: {script}");

        // 如果 script 指向一个本地存在的脚本文件，读取其内容执行
        if (!string.IsNullOrEmpty(script))
        {
            try
            {
                // 尝试多个可能的本地路径
                var pathsToTry = new List<string>
                {
                    Path.IsPathRooted(script) ? script : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, script),
                    Path.Combine(Directory.GetCurrentDirectory(), script),
                    Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", script),
                    // 针对 IDE 调试环境
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", script),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", script)
                };

                foreach (var path in pathsToTry)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            var scriptContent = await File.ReadAllTextAsync(path, cancellationToken);
                            checkCommand = host.OsType != OperatingSystemType.Windows &&
                                           path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                                ? ScriptResolver.BuildLinuxShellScriptCommand(scriptContent)
                                : scriptContent;
                            _fileLogger.Info("CheckStatusAsync", $"从文件加载检测脚本: {path}");
                            break;
                        }
                    }
                    catch { /* 忽略单个路径错误 */ }
                }

                if (checkCommand == null)
                {
                    var configuredScriptPath = _scriptResolver.TryResolveConfiguredScriptFilePath(script, host.OsType);
                    if (!string.IsNullOrEmpty(configuredScriptPath) && File.Exists(configuredScriptPath))
                    {
                        var scriptContent = await File.ReadAllTextAsync(configuredScriptPath, cancellationToken);
                        checkCommand = host.OsType != OperatingSystemType.Windows &&
                                       configuredScriptPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                            ? ScriptResolver.BuildLinuxShellScriptCommand(scriptContent)
                            : scriptContent;
                        _fileLogger.Info("CheckStatusAsync", $"从配置引用加载检测脚本: {configuredScriptPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"尝试加载本地脚本出错 ({script}): {ex.Message}");
                _fileLogger.Error("CheckStatusAsync", $"加载脚本失败: {ex.Message}");
            }

            // 如果没找到本地文件，且 script 看起来是一个路径（包含斜杠或后缀），则不应作为命令执行
            if (checkCommand == null && (script.Contains('/') || script.Contains('\\') || script.EndsWith(".sh") || script.EndsWith(".ps1")))
            {
                _logger.Error($"[CheckStatusAsync] 无法定位检测脚本文件：{script}");
                _fileLogger.Error("CheckStatusAsync", $"无法定位检测脚本文件: {script}");
                // 即使找不到脚本，也尝试执行默认检测逻辑
                checkCommand = null;
            }
            else if (checkCommand == null)
            {
                checkCommand = script; // 可能是简单的 shell 命令，如 "ls"
            }
        }

        _fileLogger.Info("CheckStatusAsync", $"检测命令: {checkCommand ?? "null"}");

        if (string.IsNullOrEmpty(checkCommand))
        {
            await PopulateDefaultStatusAsync(host, app, status, cancellationToken);
        }
        else
        {
            var output = await _sshService.ExecuteCommandAsync(checkCommand, cancellationToken: cancellationToken);
            _fileLogger.Info("CheckStatusAsync", $"检测命令输出:\n{output}");
            ParseCheckOutput(output, status);

            if (!HasMachineReadableStatusOutput(output))
            {
                _fileLogger.Warning("CheckStatusAsync", "检测命令未输出机器可读状态协议，回退到内置检测逻辑");
                await PopulateDefaultStatusAsync(host, app, status, cancellationToken);
            }
        }

        ApplicationStatusNormalizer.Normalize(status, new ApplicationStatusEvidence
        {
            BinaryFound = status.IsInstalled,
            ProcessFound = status.IsRunning
        });
        operationStopwatch.Stop();
        _fileLogger.Info("CheckStatusAsync", $"{app.Name} 状态检测耗时: {operationStopwatch.ElapsedMilliseconds}ms");
        _fileLogger.Info("CheckStatusAsync", $"最终检测结果: INSTALLED={status.IsInstalled}, RUNNING={status.IsRunning}, VERSION={status.InstalledVersion}");
        return status;
    }

    private async Task PopulateDefaultStatusAsync(
        RemoteHost host,
        ApplicationInfo app,
        ApplicationStatus status,
        CancellationToken cancellationToken)
    {
        status.IsInstalled = false;
        status.IsRunning = false;
        status.InstalledVersion = "未知";
        status.Port = string.Empty;

        // 使用组合检查脚本，一次 SSH 调用获取安装、版本和运行状态。
        var combinedScript = GetCombinedCheckScript(host, app);
        if (combinedScript != null)
        {
            try
            {
                var output = await _sshService.ExecuteCommandAsync(combinedScript, cancellationToken: cancellationToken);
                ParseCombinedCheckOutput(output, status);
                return;
            }
            catch
            {
                // 组合脚本失败时回退到分别检查，避免单点脚本问题导致 UI 显示未安装。
            }
        }

        _fileLogger.Info("CheckStatusAsync", "使用默认检测逻辑");
        status.IsInstalled = await IsInstalledAsync(host, app, cancellationToken);
        if (status.IsInstalled)
        {
            status.InstalledVersion = await GetVersionAsync(host, app, cancellationToken);
            status.IsRunning = await IsRunningAsync(host, app, cancellationToken, status);
        }

        _fileLogger.Info("CheckStatusAsync", $"默认检测结果: INSTALLED={status.IsInstalled}, VERSION={status.InstalledVersion}, RUNNING={status.IsRunning}");
    }

    private static bool HasMachineReadableStatusOutput(string output)
    {
        return ScriptProtocolParser.Parse(output)
            .Any(item => item.Kind == ScriptProtocolEventKind.Status &&
                         IsMachineReadableStatusKey(item.Key));
    }

    private static bool IsMachineReadableStatusKey(string key)
    {
        return key.Equals("INSTALLED", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("RUNNING", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("BINARY_FOUND", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("PACKAGE_INSTALLED", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("SERVICE_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("PROCESS_FOUND", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("PORT_LISTENING", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("SERVICE_ONLY_STALE", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("SERVICE_ONLY_RESIDUE", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("CONFIG_ONLY_RESIDUE", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("UNINSTALLED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成组合检查脚本 - 一次性获取安装状态、版本和运行状态
    /// </summary>
    private string? GetCombinedCheckScript(RemoteHost host, ApplicationInfo app)
    {
        var appName = app.Name.ToLower();

        if (host.OsType == OperatingSystemType.Windows)
        {
            return appName switch
            {
                "elasticsearch" => @"
$isInstalled=$false; $version='未知'; $isRunning=$false
$esHome = [Environment]::GetEnvironmentVariable('ES_HOME','Machine')
$defaultPath = 'C:\Program Files\Elasticsearch'
$altPath = 'C:\elasticsearch'
if ($esHome -and (Test-Path ""$esHome\bin\elasticsearch.exe"" -or Test-Path ""$esHome\bin\elasticsearch.bat"")) { $isInstalled=$true; $defaultPath=$esHome }
if (Test-Path ""$defaultPath\bin\elasticsearch.exe"") { $isInstalled=$true }
if (Test-Path ""$altPath\bin\elasticsearch.exe"") { $isInstalled=$true }
$svc = Get-Service -Name 'Elasticsearch*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) { $isInstalled=$true }
if ($esHome -and (Test-Path ""$esHome\bin\elasticsearch.bat"" -or Test-Path ""$esHome\bin\elasticsearch.exe"")) { $isInstalled=$true }
if ($isInstalled) {
    $exe = if (Test-Path ""$defaultPath\bin\elasticsearch.exe"") { ""$defaultPath\bin\elasticsearch.exe"" } elseif (Test-Path ""$esHome\bin\elasticsearch.exe"") { ""$esHome\bin\elasticsearch.exe"" } elseif (Test-Path ""$altPath\bin\elasticsearch.exe"") { ""$altPath\bin\elasticsearch.exe"" } else { $null }
    if ($exe -and (Test-Path $exe)) { $v = & $exe --version 2>&1; if ($v -match '(\d+\.\d+\.\d+)') { $version = $matches[1] } }
    $esProc = Get-CimInstance Win32_Process -Filter ""Name='java.exe'"" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*elasticsearch*' -or $_.CommandLine -like '*org.elasticsearch.bootstrap.Elasticsearch*' }
    if ($esProc) { $isRunning=$true }
    $port9200 = Get-NetTCPConnection -LocalPort 9200 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($port9200) { $isRunning=$true }
    if ($svc -and $svc.Status -eq 'Running') { $isRunning=$true }
}
Write-Output ""INSTALLED:$isInstalled""; Write-Output ""VERSION:$version""; Write-Output ""RUNNING:$isRunning""",

                "redis" => @"
$isInstalled=$false; $version='未知'; $isRunning=$false
if (Get-Command redis-server -ErrorAction SilentlyContinue) { $isInstalled=$true }
if (Test-Path 'C:\Program Files\Redis\redis-server.exe') { $isInstalled=$true }
if (Test-Path 'C:\Redis\redis-server.exe') { $isInstalled=$true }
$svc = Get-Service -Name 'Redis*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) { $isInstalled=$true }
if ($isInstalled) {
    $v = redis-server --version 2>&1; if ($v -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
    $proc = Get-Process redis-server -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) { $isRunning=$true }
    $port6379 = Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($port6379) { $isRunning=$true }
    if ($svc -and $svc.Status -eq 'Running') { $isRunning=$true }
}
Write-Output ""INSTALLED:$isInstalled""; Write-Output ""VERSION:$version""; Write-Output ""RUNNING:$isRunning""",

                "nginx" => @"
$isInstalled=$false; $version='未知'; $isRunning=$false
if (Get-Command nginx -ErrorAction SilentlyContinue) { $isInstalled=$true }
if (Test-Path 'C:\Program Files\Nginx\nginx.exe') { $isInstalled=$true }
if (Test-Path 'C:\nginx\nginx.exe') { $isInstalled=$true }
$svc = Get-Service -Name 'Nginx*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) { $isInstalled=$true }
if ($isInstalled) {
    $v = nginx -v 2>&1; if ($v -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
    $proc = Get-Process nginx -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) { $isRunning=$true }
    $port80 = Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($port80) { $isRunning=$true }
    if ($svc -and $svc.Status -eq 'Running') { $isRunning=$true }
}
Write-Output ""INSTALLED:$isInstalled""; Write-Output ""VERSION:$version""; Write-Output ""RUNNING:$isRunning""",

                "mysql" => @"
$isInstalled=$false; $version='未知'; $isRunning=$false
if (Get-Command mysqld -ErrorAction SilentlyContinue) { $isInstalled=$true }
if (Test-Path 'C:\Program Files\MySQL\MySQL Server*\bin\mysqld.exe') { $isInstalled=$true }
if (Test-Path 'C:\Program Files (x86)\MySQL\MySQL Server*\bin\mysqld.exe') { $isInstalled=$true }
$svc = Get-Service -Name 'MySQL*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) { $isInstalled=$true }
if ($isInstalled) {
    $v = mysqld --version 2>&1; if ($v -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
    $proc = Get-Process mysqld -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) { $isRunning=$true }
    $port3306 = Get-NetTCPConnection -LocalPort 3306 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($port3306) { $isRunning=$true }
    if ($svc -and $svc.Status -eq 'Running') { $isRunning=$true }
}
Write-Output ""INSTALLED:$isInstalled""; Write-Output ""VERSION:$version""; Write-Output ""RUNNING:$isRunning""",

                "rabbitmq" => @"
$isInstalled=$false; $version='未知'; $isRunning=$false; $binaryFound=$false; $processFound=$false; $serviceFound=$false; $serviceActive=$false; $portListening=$false
function Test-RabbitMqCommandLine([string]$CommandLine) { if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $false }; return $CommandLine -match 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot' }
if (Get-Command rabbitmq-server -ErrorAction SilentlyContinue) { $binaryFound=$true }
if (Test-Path 'C:\Program Files\RabbitMQ Server\rabbitmq_server*\sbin\rabbitmq-server.bat') { $binaryFound=$true }
$svc = Get-Service -Name 'RabbitMQ*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) { $serviceFound=$true; if ($svc.Status -eq 'Running') { $serviceActive=$true } }
$rabbitProc = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { ($_.Name -match '^(erl|erl\.exe|beam\.smp|beam\.smp\.exe)$') -and (Test-RabbitMqCommandLine $_.CommandLine) } | Select-Object -First 1
if ($rabbitProc) { $processFound=$true }
if ($serviceActive -or $processFound) { $isRunning=$true }
if ($binaryFound -or $serviceActive -or $processFound) { $isInstalled=$true }
if ($isInstalled) {
    $v = rabbitmqctl version 2>&1; if ($v -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
}
Write-Output ""INSTALLED:$isInstalled""; Write-Output ""VERSION:$version""; Write-Output ""RUNNING:$isRunning""; Write-Output ""BINARY_FOUND:$binaryFound""; Write-Output ""PROCESS_FOUND:$processFound""; Write-Output ""SERVICE_FOUND:$serviceFound""; Write-Output ""SERVICE_ACTIVE:$serviceActive""; Write-Output ""PORT_LISTENING:$portListening""",

                _ => null
            };
        }

        // Linux 组合检查脚本
        return appName switch
        {
            "elasticsearch" => @"
INSTALLED=false; VERSION='未知'; RUNNING=false;
# 检查安装状态（多种方式）
if [ -f /usr/share/elasticsearch/bin/elasticsearch ] || [ -f /opt/elasticsearch/bin/elasticsearch ]; then INSTALLED=true; fi
if which elasticsearch >/dev/null 2>&1; then INSTALLED=true; fi
if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'elasticsearch(\.service)?' | grep -qvE 'not-found|masked'; then INSTALLED=true; fi
if dpkg -l 2>/dev/null | grep -Ei 'elasticsearch' | grep -q '^ii'; then INSTALLED=true; fi
if rpm -qa 2>/dev/null | grep -Ei 'elasticsearch' | grep -qE 'elasticsearch'; then INSTALLED=true; fi
if [ -d /etc/elasticsearch ] && [ -f /etc/elasticsearch/elasticsearch.yml ]; then INSTALLED=true; fi
# 检查数据目录（仅在非保留数据模式时表明仍安装）
if [ -d /var/lib/elasticsearch ] && [ -n ""$(ls -A /var/lib/elasticsearch 2>/dev/null)"" ]; then INSTALLED=true; fi
if [ -d /opt/elasticsearch ]; then INSTALLED=true; fi
# 获取版本
if [ ""$INSTALLED"" = true ]; then
    ES_BIN=''
    if [ -f /usr/share/elasticsearch/bin/elasticsearch ]; then ES_BIN='/usr/share/elasticsearch/bin/elasticsearch'; fi
    if [ -f /opt/elasticsearch/bin/elasticsearch ]; then ES_BIN='/opt/elasticsearch/bin/elasticsearch'; fi
    if [ -n ""$ES_BIN"" ]; then
        VER_OUT=$($ES_BIN --version 2>/dev/null || echo '')
        VERSION=$(echo ""$VER_OUT"" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    fi
    if [ -z ""$VERSION"" ]; then VERSION='未知'; fi
    # 检查运行状态
    HTTP_PORT=9200
    if [ -f /etc/elasticsearch/elasticsearch.yml ]; then
        EXTRACTED_PORT=$(grep -E '^[[:space:]]*http\.port:' /etc/elasticsearch/elasticsearch.yml | awk '{print $2}')
        if [ -n ""$EXTRACTED_PORT"" ]; then HTTP_PORT=$EXTRACTED_PORT; fi
    fi
    if pgrep -a java 2>/dev/null | grep -q elasticsearch; then RUNNING=true; fi
    if curl -s -k --connect-timeout 3 http://127.0.0.1:$HTTP_PORT 2>/dev/null | grep -qi 'elasticsearch'; then RUNNING=true; fi
    if systemctl is-active --quiet elasticsearch 2>/dev/null; then RUNNING=true; fi
    if ss -tulnp 2>/dev/null | grep "":$HTTP_PORT"" | grep -q java; then RUNNING=true; fi
fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""",

"redis" => @"
INSTALLED=false; VERSION=""未知""; RUNNING=false;
# 检查安装状态
if which redis-server >/dev/null 2>&1 || [ -f /usr/bin/redis-server ] || [ -f /usr/sbin/redis-server ] || [ -f /usr/local/bin/redis-server ]; then INSTALLED=true; fi
if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'redis|redis-server' | grep -qvE 'not-found|masked'; then INSTALLED=true; fi
if dpkg -l 2>/dev/null | grep -Ei 'redis-server|redis-tools' | grep -q '^ii'; then INSTALLED=true; fi
if rpm -qa 2>/dev/null | grep -Ei 'redis|redis-server' | grep -q 'redis'; then INSTALLED=true; fi
# 获取版本
if [ ""$INSTALLED"" = true ]; then
    VER_OUT=$(redis-server --version 2>/dev/null || echo '')
    VERSION=$(echo ""$VER_OUT"" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -z ""$VERSION"" ]; then VERSION=""未知""; fi
    # 检查运行状态
    REDIS_PORT=6379
    if pgrep -x redis-server >/dev/null 2>&1 || pgrep -f ""redi[s]-server"" >/dev/null 2>&1; then RUNNING=true; fi
    if systemctl is-active --quiet redis 2>/dev/null || systemctl is-active --quiet redis-server 2>/dev/null; then RUNNING=true; fi
    if ss -tulnp 2>/dev/null | grep -q "":$REDIS_PORT""; then RUNNING=true; fi
fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""",

"nginx" => @"
INSTALLED=false; VERSION=""未知""; RUNNING=false;
# 检查安装状态
if which nginx >/dev/null 2>&1 || [ -f /usr/sbin/nginx ] || [ -f /usr/bin/nginx ]; then INSTALLED=true; fi
if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'nginx' | grep -qvE 'not-found|masked'; then INSTALLED=true; fi
if dpkg -l 2>/dev/null | grep -Ei 'nginx' | grep -q '^ii'; then INSTALLED=true; fi
if rpm -qa 2>/dev/null | grep -Ei 'nginx' | grep -q 'nginx'; then INSTALLED=true; fi
# 获取版本
if [ ""$INSTALLED"" = true ]; then
    VER_OUT=$(nginx -v 2>&1 || echo '')
    VERSION=$(echo ""$VER_OUT"" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -z ""$VERSION"" ]; then VERSION=""未知""; fi
    # 检查运行状态
    if pgrep -x nginx >/dev/null 2>&1; then RUNNING=true; fi
    if systemctl is-active --quiet nginx 2>/dev/null; then RUNNING=true; fi
    if ss -tulnp 2>/dev/null | grep -q "":80""; then RUNNING=true; fi
fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""",

"mysql" => @"
INSTALLED=false; VERSION=""未知""; RUNNING=false;
# 检查安装状态
if which mysqld >/dev/null 2>&1 || [ -x /usr/sbin/mysqld ] || [ -x /usr/bin/mysqld ]; then INSTALLED=true; fi
if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'mysql|mysqld' | grep -qvE 'not-found|masked'; then INSTALLED=true; fi
if dpkg -l 2>/dev/null | grep -Ei 'mysql-server|mysql-community-server' | grep -q '^ii'; then INSTALLED=true; fi
if rpm -qa 2>/dev/null | grep -Ei 'mysql-community-server|mysql-server' | grep -qE 'server'; then INSTALLED=true; fi
# 获取版本
if [ ""$INSTALLED"" = true ]; then
    VER_OUT=$(mysql --version 2>/dev/null || mysqld --version 2>/dev/null || echo '')
    VERSION=$(echo ""$VER_OUT"" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -z ""$VERSION"" ]; then VERSION=""未知""; fi
    # 检查运行状态
    if pgrep -x mysqld >/dev/null 2>&1; then RUNNING=true; fi
    if systemctl is-active --quiet mysqld 2>/dev/null || systemctl is-active --quiet mysql 2>/dev/null; then RUNNING=true; fi
    if ss -tulnp 2>/dev/null | grep -q "":3306""; then RUNNING=true; fi
fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""",

"mariadb" => @"
INSTALLED=false; VERSION=""未知""; RUNNING=false;
# 检查安装状态（仅认服务端，不将 client/common 误判为已安装）
if which mariadbd >/dev/null 2>&1 || [ -x /usr/sbin/mariadbd ] || [ -x /usr/bin/mariadbd ] || [ -x /usr/libexec/mariadbd ]; then INSTALLED=true; fi
if systemctl list-unit-files 2>/dev/null | grep -Eq '^mariadb\.service|^mysql\.service|^mysqld\.service'; then INSTALLED=true; fi
if dpkg -l 2>/dev/null | grep -Ei '^ii[[:space:]]+mariadb-server([[:space:]:-]|$)' | grep -q .; then INSTALLED=true; fi
if rpm -qa 2>/dev/null | grep -Ei '^(MariaDB-server|mariadb-server)(-|$)' | grep -q .; then INSTALLED=true; fi
# 获取版本
if [ ""$INSTALLED"" = true ]; then
    VER_OUT=$(mariadbd --version 2>/dev/null || mariadb --version 2>/dev/null || mysql --version 2>/dev/null || echo '')
    VERSION=$(echo ""$VER_OUT"" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1)
    if [ -z ""$VERSION"" ]; then VERSION=""未知""; fi
    # 检查运行状态
    if pgrep -x mariadbd >/dev/null 2>&1; then RUNNING=true; fi
    if systemctl is-active --quiet mariadb 2>/dev/null || systemctl is-active --quiet mysql 2>/dev/null || systemctl is-active --quiet mysqld 2>/dev/null; then RUNNING=true; fi
    if ss -tulnp 2>/dev/null | grep -E "":3306[[:space:]]"" | grep -qiE 'mariadb|mariadbd|mysqld'; then RUNNING=true; fi
fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""",

"rabbitmq" => @"
INSTALLED=false; VERSION=""未知""; RUNNING=false; PACKAGE_INSTALLED=false; BINARY_FOUND=false; PROCESS_FOUND=false; SERVICE_FOUND=false; SERVICE_ACTIVE=false; PORT_LISTENING=false
is_rabbitmq_command_line() { printf '%s' ""$1"" | grep -Eiq 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot'; }
if command -v dpkg-query >/dev/null 2>&1 && [ ""$(dpkg-query -W -f='${Status}' rabbitmq-server 2>/dev/null || true)"" = ""install ok installed"" ]; then PACKAGE_INSTALLED=true; fi
if command -v rpm >/dev/null 2>&1 && rpm -q rabbitmq-server >/dev/null 2>&1; then PACKAGE_INSTALLED=true; fi
if command -v rabbitmq-server >/dev/null 2>&1 || [ -x /usr/sbin/rabbitmq-server ] || [ -x /usr/lib/rabbitmq/bin/rabbitmq-server ] || [ -x /opt/rabbitmq/sbin/rabbitmq-server ] || [ -x /usr/local/bin/rabbitmq-server ]; then BINARY_FOUND=true; fi
if command -v systemctl >/dev/null 2>&1; then
    if systemctl is-active --quiet rabbitmq-server 2>/dev/null || systemctl is-active --quiet rabbitmq 2>/dev/null; then SERVICE_FOUND=true; SERVICE_ACTIVE=true; fi
    if [ ""$SERVICE_FOUND"" != true ] && (systemctl list-unit-files 2>/dev/null | grep -Eq '^(rabbitmq-server|rabbitmq)\.service' || systemctl list-units --all --type=service 2>/dev/null | grep -Eq '^\s*(rabbitmq-server|rabbitmq)\.service'); then SERVICE_FOUND=true; fi
fi
if command -v pgrep >/dev/null 2>&1; then
    while IFS= read -r pid; do
        CMD=$(ps -p ""$pid"" -o args= 2>/dev/null || true)
        if [ -n ""$CMD"" ] && is_rabbitmq_command_line ""$CMD""; then PROCESS_FOUND=true; break; fi
    done < <(pgrep -f '[b]eam\.smp|[e]rl' 2>/dev/null || true)
fi
if command -v rabbitmqctl >/dev/null 2>&1 && rabbitmqctl status >/dev/null 2>&1; then PROCESS_FOUND=true; fi
if [ ""$SERVICE_ACTIVE"" = true ] || [ ""$PROCESS_FOUND"" = true ]; then RUNNING=true; fi
if [ ""$PACKAGE_INSTALLED"" = true ] || [ ""$BINARY_FOUND"" = true ] || [ ""$SERVICE_ACTIVE"" = true ] || [ ""$PROCESS_FOUND"" = true ]; then INSTALLED=true; fi
if [ ""$INSTALLED"" = true ]; then VERSION=$(rabbitmqctl version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n 1); VERSION=${VERSION:-未知}; fi
echo ""INSTALLED:$INSTALLED""; echo ""VERSION:$VERSION""; echo ""RUNNING:$RUNNING""; echo ""PACKAGE_INSTALLED:$PACKAGE_INSTALLED""; echo ""BINARY_FOUND:$BINARY_FOUND""; echo ""PROCESS_FOUND:$PROCESS_FOUND""; echo ""SERVICE_FOUND:$SERVICE_FOUND""; echo ""SERVICE_ACTIVE:$SERVICE_ACTIVE""; echo ""PORT_LISTENING:$PORT_LISTENING""",

_ => null
        };
    }

    /// <summary>
    /// 解析组合检查输出
    /// </summary>
    private void ParseCombinedCheckOutput(string output, ApplicationStatus status)
    {
        ParseCheckOutput(output, status);
    }

    private async Task<bool> IsInstalledAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken)
    {
        var appName = app.Name.ToLower();
        string checkCommand;
        
        if (host.OsType == OperatingSystemType.Windows)
        {
            var executableName = GetExecutableName(app);
            var serviceName = GetServiceName(app.Name);
            checkCommand = $@"
                # 检查服务是否存在
                $service = Get-Service '{serviceName}' -ErrorAction SilentlyContinue
                if ($service) {{
                    # 检查服务路径是否有效（防止残留服务项）
                    $binPath = (Get-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$($service.Name)"").ImagePath
                    if ($binPath) {{
                        # 简单清理路径中的引号和参数
                        $path = $binPath -replace '^""', '' -replace '"" .*$', '' -replace ' .*$', ''
                        if (Test-Path $path) {{ echo 'installed'; exit 0 }}
                    }}
                }}
                # MySQL 常见服务名备选检测
                if ('{appName}' -eq 'mysql') {{
                    $mysqlServices = Get-Service 'MySQL*' -ErrorAction SilentlyContinue
                    foreach ($s in $mysqlServices) {{
                         $bp = (Get-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"").ImagePath
                         if ($bp) {{
                            $p = $bp -replace '^""', '' -replace '"" .*$', '' -replace ' .*$', ''
                            if (Test-Path $p) {{ echo 'installed'; exit 0 }}
                         }}
                    }}
                }}
                # Redis 常见服务名备选检测
                if ('{appName}' -eq 'redis') {{
                    $redisServices = Get-Service 'Redis*' -ErrorAction SilentlyContinue
                    foreach ($s in $redisServices) {{
                         $bp = (Get-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"").ImagePath
                         if ($bp) {{
                            $p = $bp -replace '^""', '' -replace '"" .*$', '' -replace ' .*$', ''
                            if (Test-Path $p) {{ echo 'installed'; exit 0 }}
                         }}
                    }}
                }}
                # RabbitMQ 只认可服务端二进制或有效服务，不能把 Erlang/端口/rabbitmqctl 残留当作安装证据
                if ('{appName}' -eq 'rabbitmq') {{
                    $rabbitServices = Get-Service 'RabbitMQ*' -ErrorAction SilentlyContinue
                    foreach ($s in $rabbitServices) {{
                         $bp = (Get-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"" -ErrorAction SilentlyContinue).ImagePath
                         if ($bp -and $bp -match 'rabbitmq') {{
                            $p = $bp -replace '^""', '' -replace '"" .*$', '' -replace ' .*$', ''
                            if (Test-Path $p) {{ echo 'installed'; exit 0 }}
                         }}
                    }}
                    if (Get-Command 'rabbitmq-server' -ErrorAction SilentlyContinue) {{ echo 'installed'; exit 0 }}
                    if (Get-ChildItem -Path 'C:\Program Files\RabbitMQ Server','C:\Program Files (x86)\RabbitMQ Server','C:\RabbitMQ' -Filter 'rabbitmq-server.bat' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1) {{ echo 'installed'; exit 0 }}
                    echo 'not_installed'
                    exit 0
                }}
                # 检查命令是否存在
                if (Get-Command '{executableName}' -ErrorAction SilentlyContinue) {{ echo 'installed'; exit 0 }}
                # 检查特定可执行文件路径
                if (Test-Path 'C:\Program Files\{app.Name}') {{ 
                    if (Get-ChildItem -Path 'C:\Program Files\{app.Name}' -Filter '{executableName}.exe' -Recurse -ErrorAction SilentlyContinue) {{ echo 'installed'; exit 0 }}
                }}
                if (Test-Path 'C:\Program Files (x86)\{app.Name}') {{ 
                    if (Get-ChildItem -Path 'C:\Program Files (x86)\{app.Name}' -Filter '{executableName}.exe' -Recurse -ErrorAction SilentlyContinue) {{ echo 'installed'; exit 0 }}
                }}
                echo 'not_installed'
            ";
        }
        else
        {
            switch (appName)
            {
                case "elasticsearch":
                    checkCommand = @"
                        # 1. 检查二进制文件路径
                        if [ -f /usr/share/elasticsearch/bin/elasticsearch ] || which elasticsearch >/dev/null 2>&1; then
                            echo 'installed'
                            exit 0
                        fi
                        # 2. 检查系统服务
                        # 使用 systemctl list-units --all 并过滤加载状态，排除 not-found 和 masked
                        if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'elasticsearch(\.service)?' | grep -qvE 'not-found|masked'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 3. 检查包管理器 (ii 表示已安装，排除仅包含 libs 的情况)
                        if dpkg -l 2>/dev/null | grep -Ei 'elasticsearch' | grep -q '^ii' || \
                           rpm -qa 2>/dev/null | grep -Ei 'elasticsearch' | grep -qE 'elasticsearch'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 4. 检查配置文件
                        if [ -d /etc/elasticsearch ] && [ -f /etc/elasticsearch/elasticsearch.yml ]; then
                            echo 'installed'
                            exit 0
                        fi
                        echo 'not_installed'
                    ";
                    break;
                    
                case "rabbitmq":
                    checkCommand = @"
                        SERVICE_FOUND=false
                        if command -v dpkg-query >/dev/null 2>&1 && [ ""$(dpkg-query -W -f='${Status}' rabbitmq-server 2>/dev/null || true)"" = 'install ok installed' ]; then
                            echo 'installed'
                            exit 0
                        fi
                        if command -v rpm >/dev/null 2>&1 && rpm -q rabbitmq-server >/dev/null 2>&1; then
                            echo 'installed'
                            exit 0
                        fi
                        if which rabbitmq-server >/dev/null 2>&1 || test -x /usr/sbin/rabbitmq-server || test -x /usr/lib/rabbitmq/bin/rabbitmq-server || test -x /opt/rabbitmq/sbin/rabbitmq-server || test -x /usr/local/bin/rabbitmq-server; then
                            echo 'installed'
                            exit 0
                        fi
                        if systemctl is-active --quiet rabbitmq-server 2>/dev/null || systemctl is-active --quiet rabbitmq 2>/dev/null; then
                            echo 'installed'
                            exit 0
                        fi
                        if systemctl list-unit-files 2>/dev/null | grep -Eq '^(rabbitmq-server|rabbitmq)\.service' || \
                           systemctl list-units --all --type=service 2>/dev/null | grep -Eq '^\s*(rabbitmq-server|rabbitmq)\.service'; then
                            SERVICE_FOUND=true
                        fi
                        if [ ""$SERVICE_FOUND"" = true ]; then
                            echo 'RabbitMQ 服务定义存在，但未发现服务端二进制、完整包或 RabbitMQ 运行进程，按残留服务处理'
                        fi
                        echo 'not_installed'
                    ";
                    break;

                case "mosquitto":
                    checkCommand = @"
                        SERVICE_FOUND=false
                        CONFIG_FOUND=false
                        DEB_STATUS=''
                        if command -v dpkg-query >/dev/null 2>&1; then
                            DEB_STATUS=$(dpkg-query -W -f='${Status}' mosquitto 2>/dev/null || true)
                            if [ ""$DEB_STATUS"" = 'install ok installed' ]; then
                                echo 'installed'
                                exit 0
                            fi
                        fi
                        if [ -z ""$DEB_STATUS"" ] && (which mosquitto >/dev/null 2>&1 || test -f /usr/sbin/mosquitto || test -f /usr/bin/mosquitto); then
                            echo 'installed'
                            exit 0
                        fi
                        if systemctl is-active --quiet mosquitto 2>/dev/null; then
                            echo 'installed'
                            exit 0
                        fi
                        if dpkg-query -W -f='${Status} ${Package}\n' mosquitto 2>/dev/null | grep -Eq '^install ok installed mosquitto$' || \
                           rpm -qa 2>/dev/null | grep -Ei '^mosquitto(-|$)' | grep -q .; then
                            echo 'installed'
                            exit 0
                        fi
                        if [ -n ""$DEB_STATUS"" ]; then
                            echo ""Mosquitto dpkg 状态不是完整安装：$DEB_STATUS""
                        fi
                        if [ -f /etc/systemd/system/mosquitto.service ] || [ -f /lib/systemd/system/mosquitto.service ] || [ -f /usr/lib/systemd/system/mosquitto.service ] || \
                           systemctl list-unit-files 2>/dev/null | grep -q '^mosquitto\.service'; then
                            SERVICE_FOUND=true
                        fi
                        if [ -f /etc/mosquitto/mosquitto.conf ] || [ -f /etc/mosquitto/conf.d/remote-installer.conf ]; then
                            CONFIG_FOUND=true
                        fi
                        if [ ""$SERVICE_FOUND"" = true ]; then
                            echo 'Mosquitto 服务定义存在，但未发现完整安装或 Mosquitto 运行进程，按残留服务处理'
                        fi
                        if [ ""$CONFIG_FOUND"" = true ]; then
                            echo 'Mosquitto 配置文件存在，但未发现完整安装或 Mosquitto 运行进程，按残留配置处理'
                        fi
                        echo 'not_installed'
                    ";
                    break;
                    
                case "mysql":
                    checkCommand = @"
                        # 1. 检查 mysqld 二进制文件（服务端）
                        if which mysqld >/dev/null 2>&1 || [ -x /usr/sbin/mysqld ] || [ -x /usr/bin/mysqld ] || [ -x /usr/local/mysql/bin/mysqld ]; then
                            echo 'installed'
                            exit 0
                        fi
                        # 2. 检查系统服务是否存在
                        if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'mysql|mysqld' | grep -qvE 'not-found|masked'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 3. 检查包管理器中是否存在服务端包 (ii 表示已安装)
                        if dpkg -l 2>/dev/null | grep -Ei 'mysql-server|mysql-community-server|percona-server-server' | grep -q '^ii'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 针对 RPM，优先匹配 server 包，排查仅包含 libs 的情况
                        if rpm -qa 2>/dev/null | grep -Ei 'mysql-community-server|mysql-server|percona-server-server' | grep -qE 'server'; then
                            echo 'installed'
                            exit 0
                        fi
                        echo 'not_installed'
                    ";
                    break;

                case "mariadb":
                    checkCommand = @"
                        if which mariadbd >/dev/null 2>&1 || [ -x /usr/sbin/mariadbd ] || [ -x /usr/bin/mariadbd ] || [ -x /usr/libexec/mariadbd ]; then
                            echo 'installed'
                            exit 0
                        fi
                        if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'mariadb|mysql|mysqld' | grep -qvE 'not-found|masked'; then
                            echo 'installed'
                            exit 0
                        fi
                        if dpkg -l 2>/dev/null | grep -Ei '^ii[[:space:]]+mariadb-server([[:space:]:-]|$)' | grep -q .; then
                            echo 'installed'
                            exit 0
                        fi
                        if rpm -qa 2>/dev/null | grep -Ei '^(MariaDB-server|mariadb-server)(-|$)' | grep -q .; then
                            echo 'installed'
                            exit 0
                        fi
                        echo 'not_installed'
                    ";
                    break;

                case "redis":
                    checkCommand = @"
                        # 1. 检查二进制文件
                        if which redis-server >/dev/null 2>&1 || test -f /usr/bin/redis-server || test -f /usr/sbin/redis-server || test -f /usr/local/bin/redis-server || test -f /opt/redis/bin/redis-server || test -f /snap/bin/redis-server; then
                            echo 'installed'
                            exit 0
                        fi
                        # 2. 检查系统服务
                        if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'redis|redis-server' | grep -qvE 'not-found|masked'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 3. 检查包管理器 (ii 表示已安装)
                        if dpkg -l 2>/dev/null | grep -Ei 'redis-server|redis-tools' | grep -q '^ii'; then
                            echo 'installed'
                            exit 0
                        fi
                        if rpm -qa 2>/dev/null | grep -Ei 'redis|redis-server' | grep -q 'redis'; then
                            echo 'installed'
                            exit 0
                        fi
                        echo 'not_installed'
                    ";
                    break;
                    
                case "nginx":
                    checkCommand = @"
                        # 1. 检查二进制文件
                        if which nginx >/dev/null 2>&1 || test -f /usr/sbin/nginx || test -f /usr/bin/nginx || test -f /usr/local/nginx/sbin/nginx; then
                            echo 'installed'
                            exit 0
                        fi
                        # 2. 检查系统服务
                        if systemctl list-units --all --type=service 2>/dev/null | grep -Eiw 'nginx' | grep -qvE 'not-found|masked'; then
                            echo 'installed'
                            exit 0
                        fi
                        # 3. 检查包管理器
                        if dpkg -l 2>/dev/null | grep -Ei 'nginx' | grep -q '^ii' || \
                           rpm -qa 2>/dev/null | grep -Ei 'nginx' | grep -q 'nginx'; then
                            echo 'installed'
                            exit 0
                        fi
                        echo 'not_installed'
                    ";
                    break;
                    
                default:
                    var executable = GetExecutableName(app);
                    checkCommand = $"if which {executable} >/dev/null 2>&1; then echo 'installed'; else echo 'not_installed'; fi";
                    break;
            }
        }

                
        try
        {
            var output = await _sshService.ExecuteCommandAsync(checkCommand, cancellationToken: cancellationToken);
            var trimmedOutput = output?.Trim();
                        
            // 使用 Contains 更加鲁棒，防止输出中包含多余的换行或提示语
            var isInstalled = trimmedOutput != null && (
                trimmedOutput.Equals("installed", StringComparison.OrdinalIgnoreCase) || 
                trimmedOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim().Equals("installed", StringComparison.OrdinalIgnoreCase))
            );
                        return isInstalled;
        }
        catch (Exception ex)
        {
            _logger.Error($"检测 {app.Name} 是否安装异常：{ex.Message}");
            return false;
        }
    }

    private async Task<string> GetVersionAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken)
    {
        var versionCommands = app.Name.ToLower() switch
        {
            "mysql" => host.OsType == OperatingSystemType.Windows
                ? "mysql --version 2>$null; if ($LASTEXITCODE -ne 0) { $file = Get-ChildItem -Path 'C:\\Program Files\\MySQL' -Filter 'mysql.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($file) { & $file.FullName --version } else { echo '未知' } }"
                : "mysql --version 2>/dev/null || /usr/local/mysql/bin/mysql --version 2>/dev/null || /usr/bin/mysql --version 2>/dev/null || /usr/sbin/mysqld --version 2>/dev/null || mysqld --version 2>/dev/null",

            "mariadb" => host.OsType == OperatingSystemType.Windows
                ? null
                : "mariadb --version 2>/dev/null || /usr/bin/mariadb --version 2>/dev/null || /usr/sbin/mariadbd --version 2>/dev/null || mariadbd --version 2>/dev/null || mysql --version 2>/dev/null",

            "redis" => host.OsType == OperatingSystemType.Windows
                ? "redis-server --version 2>$null; if ($LASTEXITCODE -ne 0) { $file = Get-ChildItem -Path 'C:\\Program Files\\Redis' -Filter 'redis-server.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($file) { & $file.FullName --version } else { echo '未知' } }"
                : "redis-server --version 2>/dev/null || /usr/local/bin/redis-server --version 2>/dev/null || /usr/bin/redis-server --version 2>/dev/null || /usr/sbin/redis-server --version 2>/dev/null || /opt/redis/bin/redis-server --version 2>/dev/null || /snap/bin/redis-server --version 2>/dev/null",
            
            "elasticsearch" => host.OsType == OperatingSystemType.Windows
                ? "(Get-ChildItem -Path 'C:\\Program Files\\Elastic' -Filter 'elasticsearch.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName + ' --version' | iex"
                : "/usr/share/elasticsearch/bin/elasticsearch --version 2>/dev/null || elasticsearch --version 2>/dev/null",
            
            "nginx" => host.OsType == OperatingSystemType.Windows
                ? "nginx -v 2>$null; if ($LASTEXITCODE -ne 0) { $file = Get-ChildItem -Path 'C:\\' -Filter 'nginx.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($file) { & $file.FullName -v } else { echo '未知' } }"
                : "nginx -v 2>&1 || /usr/sbin/nginx -v 2>&1 || /usr/bin/nginx -v 2>&1 || /usr/local/nginx/sbin/nginx -v 2>&1",

            "mosquitto" => host.OsType == OperatingSystemType.Windows
                ? "$file = Get-ChildItem -Path 'C:\\Program Files\\mosquitto' -Filter 'mosquitto.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($file) { & $file.FullName -h 2>&1 } else { echo '未知' }"
                : "mosquitto -h 2>&1 || /usr/sbin/mosquitto -h 2>&1 || /usr/bin/mosquitto -h 2>&1 || dpkg-query -W -f='${Version}' mosquitto 2>/dev/null || rpm -q --queryformat '%{VERSION}' mosquitto 2>/dev/null",
            _ => null
        };

        if (versionCommands != null)
        {
            try
            {
                var output = await _sshService.ExecuteCommandAsync(versionCommands, cancellationToken: cancellationToken);
                return ExtractVersion(output);
            }
            catch
            {
                return "未知";
            }
        }

        return "未知";
    }

    private async Task<bool> IsRunningAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken, ApplicationStatus? status = null)
    {
        var appName = app.Name.ToLower();
        string checkCommand;
        
        if (host.OsType == OperatingSystemType.Windows)
        {
            var processName = GetProcessName(app);
            var serviceName = GetServiceName(app.Name);
            checkCommand = $@"
                # 1. 检查进程
                if (Get-Process {processName} -ErrorAction SilentlyContinue) {{ echo 'running'; exit 0 }}

                # 2. 检查服务
                if (Get-Service '{serviceName}' -ErrorAction SilentlyContinue | Where-Object {{ $_.Status -eq 'Running' }}) {{ echo 'running'; exit 0 }}
                # MySQL 特殊处理：常见服务名如 MySQL80, MySQL57 等
                if ('{appName}' -eq 'mysql') {{
                    if (Get-Service 'MySQL*' -ErrorAction SilentlyContinue | Where-Object {{ $_.Status -eq 'Running' }}) {{ echo 'running'; exit 0 }}
                }}
                # Redis 特殊处理：常见服务名如 Redis, redis-server 等
                if ('{appName}' -eq 'redis') {{
                    if (Get-Service 'Redis*' -ErrorAction SilentlyContinue | Where-Object {{ $_.Status -eq 'Running' }}) {{ echo 'running'; exit 0 }}
                }}
                # Nginx 特殊处理：部分安装包可能以 Nginx 或 nginx-service 命名
                if ('{appName}' -eq 'nginx') {{
                    if (Get-Service 'Nginx*' -ErrorAction SilentlyContinue | Where-Object {{ $_.Status -eq 'Running' }}) {{ echo 'running'; exit 0 }}
                }}
                # RabbitMQ 运行态必须来自 RabbitMQ 服务、RabbitMQ 命令行进程或 rabbitmqctl status
                if ('{appName}' -eq 'rabbitmq') {{
                    if (Get-Service 'RabbitMQ*' -ErrorAction SilentlyContinue | Where-Object {{ $_.Status -eq 'Running' }}) {{ echo 'running'; exit 0 }}
                    $rabbitProc = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {{ ($_.Name -match '^(erl|erl\.exe|beam\.smp|beam\.smp\.exe)$') -and ($_.CommandLine -match 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot') }} | Select-Object -First 1
                    if ($rabbitProc) {{ echo 'running'; exit 0 }}
                    $rabbitmqctl = Get-Command rabbitmqctl -ErrorAction SilentlyContinue
                    if ($rabbitmqctl) {{
                        & rabbitmqctl status *> $null
                        if ($LASTEXITCODE -eq 0) {{ echo 'running'; exit 0 }}
                    }}
                    echo 'not_running'
                    exit 0
                }}
                # 3. 备选进程检查 (tasklist / command line)
                if (tasklist /FI ""IMAGENAME eq {processName}.exe"" | findstr {processName}) {{ echo 'running'; exit 0 }}
                echo 'not_running'
            ";
        }
        else
        {
            switch (appName)
            {
                case "elasticsearch":
                    checkCommand = @"
                        # 1. 尝试从配置文件中获取 HTTP 端口 (默认为 9200)
                        HTTP_PORT=9200
                        CONFIG_FILE=""/etc/elasticsearch/elasticsearch.yml""
                        if [ -f ""$CONFIG_FILE"" ]; then
                            # 提取 http.port 配置（处理空格和注释）
                            EXTRACTED_PORT=$(grep -E '^[[:space:]]*http\.port:' ""$CONFIG_FILE"" | awk '{print $2}')
                            if [ ! -z ""$EXTRACTED_PORT"" ]; then
                                HTTP_PORT=$EXTRACTED_PORT
                            fi
                        fi

                        # 2. 检查进程 (更精确匹配 java 进程且包含 elasticsearch 关键词)
                        if pgrep -a java 2>/dev/null | grep -q ""elasticsearch""; then
                            echo 'running'
                            echo ""PORT:$HTTP_PORT""
                            exit 0
                        fi

                        # 3. 检查 HTTP 端口响应 (使用发现或默认的端口)
                        if curl -s -k ""http://127.0.0.1:$HTTP_PORT"" 2>/dev/null | grep -qi 'elasticsearch'; then
                            echo 'running'
                            echo ""PORT:$HTTP_PORT""
                            exit 0
                        fi

                        # 4. 检查服务状态
                        if systemctl is-active --quiet elasticsearch 2>/dev/null; then
                            echo 'running'
                            echo ""PORT:$HTTP_PORT""
                            exit 0
                        fi

                        # 5. 检查监听端口 (如果上述失败，尝试通过端口检测确认)
                        if ss -tulnp 2>/dev/null | grep -E "":$HTTP_PORT[[:space:]]"" | grep -q java; then
                            echo 'running'
                            echo ""PORT:$HTTP_PORT""
                            exit 0
                        fi

                        echo 'not_running'
                    ";
                    break;
                    
                case "rabbitmq":
                    checkCommand = @"
                        is_rabbitmq_command_line() { printf '%s' ""$1"" | grep -Eiq 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot'; }
                        if systemctl is-active --quiet rabbitmq-server 2>/dev/null || systemctl is-active --quiet rabbitmq 2>/dev/null; then
                            echo 'running'
                            exit 0
                        fi
                        if rabbitmqctl status >/dev/null 2>&1; then
                            echo 'running'
                            exit 0
                        fi
                        if command -v pgrep >/dev/null 2>&1; then
                            while IFS= read -r pid; do
                                CMD=$(ps -p ""$pid"" -o args= 2>/dev/null || true)
                                if [ -n ""$CMD"" ] && is_rabbitmq_command_line ""$CMD""; then
                                    echo 'running'
                                    exit 0
                                fi
                            done < <(pgrep -f '[b]eam\.smp|[e]rl' 2>/dev/null || true)
                        fi
                        echo 'not_running'
                    ";
                    break;

                case "mosquitto":
                    checkCommand = @"
                        MQTT_PORT=1883
                        CONFIG_FILES=""/etc/mosquitto/conf.d/remote-installer.conf /etc/mosquitto/mosquitto.conf""
                        for config_file in $CONFIG_FILES; do
                            if [ -f ""$config_file"" ]; then
                                EXTRACTED_PORT=$(grep -E '^[[:space:]]*listener[[:space:]]+[0-9]+' ""$config_file"" | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
                                if [ -n ""$EXTRACTED_PORT"" ]; then
                                    MQTT_PORT=$EXTRACTED_PORT
                                    break
                                fi
                            fi
                        done
                        PORT_LISTENING=false
                        if ss -tulnp 2>/dev/null | grep -qE "":$MQTT_PORT[[:space:]]"" || \
                           netstat -tulnp 2>/dev/null | grep -qE "":$MQTT_PORT[[:space:]]""; then
                            PORT_LISTENING=true
                        fi
                        if pgrep -x mosquitto >/dev/null 2>&1 || pgrep -f '[m]osquitto' >/dev/null 2>&1 || \
                           systemctl is-active --quiet mosquitto 2>/dev/null; then
                            echo 'running'
                            echo ""PORT:$MQTT_PORT""
                            echo ""PORT_LISTENING:$PORT_LISTENING""
                            exit 0
                        fi
                        echo ""PORT:$MQTT_PORT""
                        echo 'PORT_LISTENING:false'
                        echo 'not_running'
                    ";
                    break;
                    
                case "mysql":
                    checkCommand = @"
                        # 1. 优先检查端口监听 (最可靠)
                        if ss -tulnp 2>/dev/null | grep -qE ':3306|:33060 ' || netstat -tulnp 2>/dev/null | grep -qE ':3306|:33060 '; then
                            echo 'running'
                            exit 0
                        fi
                        # 2. 检查服务端进程 (排除 mysql 客户端干扰，确保进程确实在运行)
                        if pgrep -x mysqld >/dev/null 2>&1 || pgrep -f ""/usr/sbin/mysqld"" >/dev/null 2>&1; then
                            echo 'running'
                            exit 0
                        fi
                        # 3. 检查服务状态
                        if systemctl is-active --quiet mysqld 2>/dev/null || systemctl is-active --quiet mysql 2>/dev/null; then
                            echo 'running'
                            exit 0
                        fi
                        echo 'not_running'
                    ";
                    break;

                case "mariadb":
                    checkCommand = @"
                        if ss -tulnp 2>/dev/null | grep -E "":3306[[:space:]]"" | grep -qiE 'mariadb|mariadbd|mysql|mysqld' || \
                           netstat -tulnp 2>/dev/null | grep -E "":3306[[:space:]]"" | grep -qiE 'mariadb|mariadbd|mysql|mysqld'; then
                            echo 'running'
                            exit 0
                        fi
                        if pgrep -x mariadbd >/dev/null 2>&1 || pgrep -x mysqld >/dev/null 2>&1 || pgrep -f ""/usr/sbin/mariadbd"" >/dev/null 2>&1; then
                            echo 'running'
                            exit 0
                        fi
                        if systemctl is-active --quiet mariadb 2>/dev/null || systemctl is-active --quiet mysql 2>/dev/null || systemctl is-active --quiet mysqld 2>/dev/null; then
                            echo 'running'
                            exit 0
                        fi
                        echo 'not_running'
                    ";
                    break;

                case "redis":
                    checkCommand = @"
                        # 1. 默认端口
                        REDIS_PORT=6379
                        
                        # 2. 尝试从常见路径配置文件中获取端口
                        for conf in /etc/redis/redis.conf /etc/redis.conf /usr/local/etc/redis.conf /opt/redis/etc/redis.conf /opt/redis/redis.conf /var/snap/redis/common/redis.conf; do
                            if [ -f ""$conf"" ]; then
                                EXTRACTED_PORT=$(grep -E '^[[:space:]]*port[[:space:]]+[0-9]+' ""$conf"" | head -n 1 | awk '{print $2}' | tr -d ';\r\n')
                                if [ ! -z ""$EXTRACTED_PORT"" ]; then
                                    REDIS_PORT=$EXTRACTED_PORT
                                    break
                                fi
                            fi
                        done

                        # 3. 检查进程 (使用 [r] 技巧避免匹配当前脚本，并确保是服务端进程)
                        if pgrep -x redis-server >/dev/null 2>&1 || pgrep -f ""redi[s]-server"" >/dev/null 2>&1 || pgrep -x redis >/dev/null 2>&1; then
                            echo 'running'
                            echo ""PORT:$REDIS_PORT""
                            exit 0
                        fi

                        # 4. 检查服务状态
                        if systemctl is-active --quiet redis 2>/dev/null || systemctl is-active --quiet redis-server 2>/dev/null || \
                           systemctl list-units --type=service --state=running 2>/dev/null | grep -Eiwq 'redis|redis-server'; then
                            echo 'running'
                            echo ""PORT:$REDIS_PORT""
                            exit 0
                        fi

                        # 5. 检查端口监听
                        if ss -tulnp 2>/dev/null | grep -qE "":$REDIS_PORT[[:space:]]"" | grep -qi redis || \
                           netstat -tulnp 2>/dev/null | grep -qE "":$REDIS_PORT[[:space:]]"" | grep -qi redis; then
                            echo 'running'
                            echo ""PORT:$REDIS_PORT""
                            exit 0
                        fi

                        echo 'not_running'
                    ";
                    break;
                    
                case "nginx":
                    checkCommand = @"
                        # 1. 默认端口
                        NGINX_PORT=80
                        
                        # 2. 尝试从配置文件提取端口 (优先 Ubuntu/Debian 路径)
                        CONF_FILES=""/etc/nginx/sites-enabled/default /etc/nginx/conf.d/default.conf /etc/nginx/nginx.conf /usr/local/nginx/conf/nginx.conf""
                        for f in $CONF_FILES; do
                            if [ -f ""$f"" ]; then
                                # 提取 listen 后的数字 (排除 IPv6 [::])
                                EXTRACTED_PORT=$(grep -E '^[[:space:]]*listen[[:space:]]+[0-9]+' ""$f"" | head -n 1 | awk '{print $2}' | tr -d ';')
                                if [ ! -z ""$EXTRACTED_PORT"" ]; then
                                    NGINX_PORT=$EXTRACTED_PORT
                                    break
                                fi
                            fi
                        done

                        # 3. 检查进程 (优先使用精确匹配)
                        if pgrep -x nginx >/dev/null 2>&1 || pgrep -x nginx-server >/dev/null 2>&1; then
                            echo 'running'
                            echo ""PORT:$NGINX_PORT""
                            exit 0
                        fi

                        # 4. 检查服务状态
                        if systemctl is-active --quiet nginx 2>/dev/null; then
                            echo 'running'
                            echo ""PORT:$NGINX_PORT""
                            exit 0
                        fi

                        # 5. 检查端口监听
                        if ss -tulnp 2>/dev/null | grep -qE "":$NGINX_PORT[[:space:]]"" | grep -qi nginx || \
                           netstat -tulnp 2>/dev/null | grep -qE "":$NGINX_PORT[[:space:]]"" | grep -qi nginx; then
                            echo 'running'
                            echo ""PORT:$NGINX_PORT""
                            exit 0
                        fi

                        echo 'not_running'
                    ";
                    break;
                    
                default:
                    var procName = GetProcessName(app);
                    checkCommand = $"if pgrep -x {procName} >/dev/null 2>&1; then echo 'running'; else echo 'not_running'; fi";
                    break;
            }
        }

                
        try
        {
            var output = await _sshService.ExecuteCommandAsync(checkCommand, cancellationToken: cancellationToken);
            var trimmedOutput = output?.Trim();
                        
            // 改进解析逻辑：只要有任何一行完全匹配 'running' (忽略空格和大小写)
            var isRunning = trimmedOutput != null && trimmedOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim().Equals("running", StringComparison.OrdinalIgnoreCase));
            
            // 提取端口 (如果有 PORT:xxx)
            if (status != null && !string.IsNullOrEmpty(trimmedOutput))
            {
                ParseCheckOutput(trimmedOutput, status);
            }
            
                        return isRunning;
        }
        catch (Exception ex)
        {
            _logger.Error($"检测 {app.Name} 是否运行异常：{ex.Message}");
            return false;
        }
    }

    private async Task ExtractPackageAsync(
        RemoteHost host, 
        string? remotePackagePath, 
        string appName, 
        LogCollector logCollector,
        string remoteWorkDir,
        CancellationToken cancellationToken)
    {
        var extractDir = host.OsType == OperatingSystemType.Windows
            ? Path.Combine(remoteWorkDir, "extract").Replace("/", "\\")
            : $"{remoteWorkDir}/extract";

        // 创建解压目录
        if (host.OsType == OperatingSystemType.Windows)
        {
            await _sshService.ExecuteCommandAsync($"New-Item -ItemType Directory -Force -Path \"{extractDir}\"", cancellationToken: cancellationToken);
        }
        else
        {
            await _sshService.ExecuteCommandAsync($"mkdir -p \"{extractDir}\"", cancellationToken: cancellationToken);
        }
        _logger.Info($"创建解压目录：{extractDir}");

        if (string.IsNullOrEmpty(remotePackagePath))
        {
            _logger.Warning("未指定远程包路径，尝试自动查找");
            var searchPattern = host.OsType == OperatingSystemType.Windows
                ? $"{remoteWorkDir}\\*"
                : $"{remoteWorkDir}/*";
            
            var findCommand = host.OsType == OperatingSystemType.Windows
                ? $"Get-ChildItem -Path \"{searchPattern}\" -Include *.zip,*.tar.gz,*.tgz,*.rpm,*.deb | Select-Object -ExpandProperty FullName"
                : $"ls -la {remoteWorkDir}/*.zip {remoteWorkDir}/*.tar.gz {remoteWorkDir}/*.tgz {remoteWorkDir}/*.rpm {remoteWorkDir}/*.deb 2>/dev/null";

            var findResult = await _sshService.ExecuteCommandAsync(findCommand, cancellationToken: cancellationToken);
            
            if (string.IsNullOrWhiteSpace(findResult))
            {
                _logger.Info("未找到安装包文件，跳过解压步骤");
                return;
            }
            
            var lines = findResult.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(remoteWorkDir))
                {
                    if (host.OsType == OperatingSystemType.Windows)
                    {
                        remotePackagePath = line.Trim();
                        break;
                    }
                    else
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 8)
                        {
                            remotePackagePath = parts[parts.Length - 1];
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(remotePackagePath))
        {
            _logger.Info("没有找到可解压的包文件");
            return;
        }

        _logger.Info($"准备解压：{remotePackagePath}");
        
        if (remotePackagePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || 
            remotePackagePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await _sshService.ExecuteCommandAsync(
                $"tar -xzf {remotePackagePath} -C {extractDir}",
                cancellationToken: cancellationToken,
                throwOnError: true);
            _logger.Info($"已解压 tar.gz 文件到 {extractDir}");
        }
        else if (remotePackagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await _sshService.ExecuteCommandAsync(
                $"unzip -o {remotePackagePath} -d {extractDir}",
                cancellationToken: cancellationToken,
                throwOnError: true);
            _logger.Info($"已解压 zip 文件到 {extractDir}");
        }
        else if (remotePackagePath.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info($"正在安装 RPM 包：{remotePackagePath}");
            await _sshService.ExecuteCommandAsync(
                $"rpm -ivh --force {remotePackagePath}",
                output => logCollector.ProcessOutput(output),
                cancellationToken: cancellationToken,
                throwOnError: true);
            _logger.Info($"已安装 RPM 包");
            
            var serviceName = GetServiceName(appName);
            _logger.Info($"正在启动服务：{serviceName}");
            await _sshService.ExecuteCommandAsync(
                $"systemctl daemon-reload && systemctl enable {serviceName} && systemctl start {serviceName}",
                output => logCollector.ProcessOutput(output),
                cancellationToken: cancellationToken,
                throwOnError: true);
            _logger.Info($"服务已启动：{serviceName}");
        }
        else if (remotePackagePath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info($"使用 apt install 安装 DEB 包：{remotePackagePath}");
                        
            try
            {
                                var installOutput = await _sshService.ExecuteCommandAsync(
                    $"apt update && apt install -y {remotePackagePath}",
                    output => 
                    {
                                                logCollector.ProcessOutput(output);
                    },
                    cancellationToken: cancellationToken,
                    throwOnError: true);
                                _logger.Info($"已安装 DEB 包");
            }
            catch (Exception ex)
            {
                _logger.Warning($"apt install 失败，尝试 dpkg -i: {ex.Message}");
                _logger.Warning($"apt install 失败，尝试 dpkg -i: {ex.Message}");
                
                try
                {
                    await _sshService.ExecuteCommandAsync(
                        $"dpkg -i {remotePackagePath}",
                        output => logCollector.ProcessOutput(output),
                        cancellationToken: cancellationToken,
                        throwOnError: true);

                    _logger.Info($"已安装 DEB 包（使用 dpkg）");
                }
                catch (Exception dpkgEx)
                {
                                        throw new Exception($"安装 DEB 包失败：{dpkgEx.Message}");
                }
            }
            
            var serviceName = GetServiceName(appName);
            _logger.Info($"正在启动服务：{serviceName}");
                        
            try
            {
                var serviceOutput = await _sshService.ExecuteCommandAsync(
                    $"systemctl daemon-reload && systemctl enable {serviceName} && systemctl start {serviceName}",
                    output => logCollector.ProcessOutput(output),
                    cancellationToken: cancellationToken,
                    throwOnError: true);
                                _logger.Info($"服务已启动：{serviceName}");
                
                await Task.Delay(10000, cancellationToken);
                var statusOutput = await _sshService.ExecuteCommandAsync(
                    $"systemctl status {serviceName} || echo '服务状态未知'",
                    output => logCollector.ProcessOutput(output),
                    cancellationToken: cancellationToken);
                            }
            catch (Exception serviceEx)
            {
                _logger.Warning($"启动服务失败：{serviceEx.Message}");
                _logger.Warning($"启动服务失败：{serviceEx.Message}");
            }
        }
        else if (host.OsType == OperatingSystemType.Windows && 
                 (remotePackagePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                  remotePackagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            if (remotePackagePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                await _sshService.ExecuteCommandAsync(
                    $"msiexec /i {remotePackagePath} /qn",
                    output => logCollector.ProcessOutput(output),
                    cancellationToken: cancellationToken);
                _logger.Info($"已安装 MSI 包");
            }
            else
            {
                await _sshService.ExecuteCommandAsync(
                    $"{remotePackagePath} /S",
                    output => logCollector.ProcessOutput(output),
                    cancellationToken: cancellationToken);
                _logger.Info($"已安装 EXE 包");
            }
        }
        else
        {
            _logger.Warning($"未知包类型：{Path.GetExtension(remotePackagePath)}，跳过解压");
        }

        try
        {
            await _sshService.ExecuteCommandAsync(
                $"rm -f {remotePackagePath}",
                cancellationToken: cancellationToken);
            _logger.Info($"已清理临时文件：{remotePackagePath}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"清理临时文件失败：{ex.Message}");
        }
    }

    private async Task ConfigureParametersAsync(RemoteHost host, ApplicationInfo app, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var configPath = host.OsType == OperatingSystemType.Windows
            ? $"C:\\ProgramData\\{app.Name}\\config.ini"
            : $"/etc/{app.Name.ToLower()}/config.ini";

        var configContent = new System.Text.StringBuilder();
        foreach (var param in parameters)
        {
            configContent.AppendLine($"{param.Key}={param.Value}");
        }

        var writeCommand = host.OsType == OperatingSystemType.Windows
            ? $"echo {configContent} > {configPath}"
            : $"mkdir -p $(dirname {configPath}) && echo '{configContent}' > {configPath}";

        await _sshService.ExecuteCommandAsync(writeCommand, cancellationToken: cancellationToken);
    }

    private async Task<RemoteCommandResult> ExecuteInstallScriptAsync(
        RemoteHost host, 
        ApplicationInfo app, 
        Dictionary<string, string> parameters,
        LogCollector logCollector,
        string? remoteScriptPath = null,
        CancellationToken cancellationToken = default)
    {
        string script;
        bool isFilePath = !string.IsNullOrEmpty(remoteScriptPath);
        
        if (isFilePath)
        {
            script = remoteScriptPath!;
        }
        else
        {
            script = app.GetInstallScript(host.OsType);
        }

        if (string.IsNullOrEmpty(script))
        {
            _logger.Warning("未找到安装脚本");
            return new RemoteCommandResult
            {
                Command = string.Empty,
                ExitCode = 0
            };
        }

        RemoteCommandResult result;
        
        if (host.OsType != OperatingSystemType.Windows)
        {

            var envParameters = parameters
                .Where(param => !string.Equals(param.Key, "version", StringComparison.OrdinalIgnoreCase))
                .Where(param => !string.Equals(param.Key, "PASSWORD", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(param => param.Key, param => param.Value, StringComparer.OrdinalIgnoreCase);

            var escapedEnvAssignments = string.Join(" ", envParameters.Select(param =>
                $"{ValidateBashEnvironmentVariableName(param.Key)}={EscapeBashSingleQuotedValue(param.Value)}"));

            string command;
            if (isFilePath)
            {
                // 脚本文件：先 cd 到脚本目录，再通过 sudo env 显式传递变量执行脚本
                var scriptPathLinux = script!.Replace("\\", "/");
                var scriptDir = Path.GetDirectoryName(scriptPathLinux)?.Replace("\\", "/") ?? "/tmp";
                var scriptName = Path.GetFileName(scriptPathLinux);

                command = string.IsNullOrEmpty(escapedEnvAssignments)
                    ? $"cd \"{scriptDir}\" && sudo bash \"./{scriptName}\""
                    : $"cd \"{scriptDir}\" && sudo env {escapedEnvAssignments} bash \"./{scriptName}\"";
            }
            else
            {
                // 字符串脚本：在同一个 bash 进程中注入环境变量并执行
                var normalizedScript = script.Trim()
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n");

                command = string.IsNullOrEmpty(escapedEnvAssignments)
                    ? normalizedScript
                    : $"env {escapedEnvAssignments} {normalizedScript}";
            }

            _logger.Info("准备执行远程安装脚本...");
            
            result = await _sshService.ExecuteCommandResultAsync(
                $"bash -c '{command.Replace("'", "'\"'\"'")}'", 
                output => 
                {
                    logCollector.ProcessOutput(output);
                }, 
                cancellationToken);
        }
        else
        {
            string command;
            if (isFilePath)
            {
                var version = parameters.TryGetValue("version", out var selectedVersion) ? selectedVersion : app.Version;
                var localScriptPath = GetLocalScriptPath(app.Id, version, host.OsType);
                var parameterMetadata = ExtractPowerShellScriptParameterMetadata(localScriptPath);
                var explicitArguments = BuildWindowsPowerShellArguments(parameters, parameterMetadata);
                var scriptDir = Path.GetDirectoryName(script) ?? @"C:\Windows\Temp";
                var escapedScriptDir = EscapePowerShellSingleQuotedValue(scriptDir);
                var escapedScriptPath = EscapePowerShellSingleQuotedValue(script);

                command = string.IsNullOrWhiteSpace(explicitArguments)
                    ? $"Set-Location {escapedScriptDir}; & {escapedScriptPath}"
                    : $"Set-Location {escapedScriptDir}; & {escapedScriptPath} {explicitArguments}";
            }
            else
            {
                command = ReplacePowerShellPlaceholders(script.Trim(), parameters);
            }

            result = await _sshService.ExecuteCommandResultAsync(command,
                output => logCollector.ProcessOutput(output),
                cancellationToken);
        }
        
        _logger.Success("脚本执行完成");
        return result;
    }

    /// <summary>
    /// 获取本地脚本路径
    /// </summary>
    private string? GetLocalScriptPath(string appId, string version, OperatingSystemType osType, string scriptPrefix = "install")
    {
        var extension = osType == OperatingSystemType.Windows ? "ps1" : "sh";
        var osSuffix = osType == OperatingSystemType.Windows ? "windows" : "linux";
        
        // 1. 尝试相对于运行目录的路径
        var searchRoots = new List<string>
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", appId),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts", appId),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", appId),
            // 针对 IDE 调试环境
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", appId),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts", appId)
        };

        var searchPatterns = new List<string>();
        
        // 1. 优先找版本特定的脚本 (例如: Scripts/Elasticsearch/8.11.0/install_linux.sh)
        if (!string.IsNullOrEmpty(version))
        {
            searchPatterns.Add(Path.Combine(version, $"{scriptPrefix}_{osSuffix}.{extension}"));
            searchPatterns.Add(Path.Combine(version, $"{scriptPrefix}.{extension}"));
            
            // 处理 v1.2.3 这种形式
            var versionWithV = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
            var versionWithoutV = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
            
            if (versionWithV != version)
            {
                searchPatterns.Add(Path.Combine(versionWithV, $"{scriptPrefix}_{osSuffix}.{extension}"));
                searchPatterns.Add(Path.Combine(versionWithV, $"{scriptPrefix}.{extension}"));
            }
            if (versionWithoutV != version)
            {
                searchPatterns.Add(Path.Combine(versionWithoutV, $"{scriptPrefix}_{osSuffix}.{extension}"));
                searchPatterns.Add(Path.Combine(versionWithoutV, $"{scriptPrefix}.{extension}"));
            }
        }
        
        // 2. 其次找通用的脚本 (例如: Scripts/Elasticsearch/install_linux.sh)
        searchPatterns.Add($"{scriptPrefix}_{osSuffix}.{extension}");
        searchPatterns.Add($"{scriptPrefix}.{extension}");

        foreach (var root in searchRoots)
        {
            foreach (var pattern in searchPatterns)
            {
                try 
                {
                    var fullPath = Path.Combine(root, pattern);
                    if (File.Exists(fullPath))
                    {
                                                return fullPath;
                    }
                }
                catch
                {
                    // 路径拼接可能抛出异常，忽略继续
                }
            }
        }

                return null;
    }

    private static string EscapeBashSingleQuotedValue(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static string ValidateBashEnvironmentVariableName(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Regex.IsMatch(key, "^[A-Z_][A-Z0-9_]*$"))
        {
            throw new InvalidOperationException($"无效的环境变量名：{key}");
        }

        return key;
    }

    private static string EscapePowerShellSingleQuotedValue(string value)
    {
        return $"'{(value ?? string.Empty).Replace("'", "''")}'";
    }

    private async Task PrepareMosquittoSecretFilesAsync(RemoteHost host, Dictionary<string, string> parameters, string remoteWorkDir, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("PASSWORD", out var password) || string.IsNullOrWhiteSpace(password))
        {
            parameters.Remove("PASSWORD_FILE");
            return;
        }

        if (host.OsType == OperatingSystemType.Windows)
        {
            var passwordFilePath = Path.Combine(remoteWorkDir, "mqtt-password.txt").Replace("/", "\\");
            await _sshService.UploadTextAsync(password, passwordFilePath, host.OsType, cancellationToken);
            parameters["PASSWORD_FILE"] = passwordFilePath;
        }
        else
        {
            var passwordFilePath = $"{remoteWorkDir}/mqtt-password.txt";
            await _sshService.UploadTextAsync(password.Replace("\r\n", "\n").Replace("\r", "\n"), passwordFilePath, host.OsType, cancellationToken);
            await _sshService.ExecuteCommandAsync($"chmod 600 \"{passwordFilePath}\"", cancellationToken: cancellationToken);
            parameters["PASSWORD_FILE"] = passwordFilePath;
        }

        parameters.Remove("PASSWORD");
    }

    private async Task CleanupMosquittoSecretFilesAsync(RemoteHost host, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("PASSWORD_FILE", out var passwordFilePath) || string.IsNullOrWhiteSpace(passwordFilePath))
        {
            return;
        }

        try
        {
            if (host.OsType == OperatingSystemType.Windows)
            {
                await _sshService.ExecuteCommandAsync(
                    $"Remove-Item -Force -Path {EscapePowerShellSingleQuotedValue(passwordFilePath)} -ErrorAction SilentlyContinue",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _sshService.ExecuteCommandAsync(
                    $"rm -f \"{passwordFilePath.Replace("\"", "\\\"")}\"",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"清理 Mosquitto 临时密码文件失败: {ex.Message}");
        }
        finally
        {
            parameters.Remove("PASSWORD_FILE");
        }
    }

    private static string NormalizeWindowsParameterKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part.Substring(1).ToLowerInvariant() : string.Empty)));
    }

    private static Dictionary<string, bool> ExtractPowerShellScriptParameterMetadata(string? localScriptPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(localScriptPath) || !File.Exists(localScriptPath))
        {
            return result;
        }

        var content = File.ReadAllText(localScriptPath);
        var paramStart = content.IndexOf("param(", StringComparison.OrdinalIgnoreCase);
        if (paramStart < 0)
        {
            return result;
        }

        var bodyStart = paramStart + "param(".Length;
        var bodyEnd = content.IndexOf(")", bodyStart, StringComparison.Ordinal);
        if (bodyEnd <= bodyStart)
        {
            return result;
        }

        var paramBody = content.Substring(bodyStart, bodyEnd - bodyStart);
        var matches = System.Text.RegularExpressions.Regex.Matches(paramBody, @"\[(?<type>[^\]]+)\]\s*\$(?<name>[A-Za-z_][A-Za-z0-9_]*)");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[name] = type.Contains("switch", StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static string BuildWindowsPowerShellArguments(Dictionary<string, string> parameters, Dictionary<string, bool> parameterMetadata)
    {
        var arguments = new List<string>();

        foreach (var parameter in parameters)
        {
            if (string.Equals(parameter.Key, "version", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedName = NormalizeWindowsParameterKey(parameter.Key);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var candidateNames = new List<string> { normalizedName };
            if (string.Equals(parameter.Key, "INSTALL_DIR", StringComparison.OrdinalIgnoreCase))
            {
                candidateNames.Add("InstallPath");
            }

            var matchedName = candidateNames.FirstOrDefault(parameterMetadata.ContainsKey);
            if (string.IsNullOrWhiteSpace(matchedName) || !parameterMetadata.TryGetValue(matchedName, out var isSwitch))
            {
                continue;
            }

            if (isSwitch)
            {
                var switchValue = string.Equals(parameter.Value, "true", StringComparison.OrdinalIgnoreCase)
                    ? "$true"
                    : "$false";
                arguments.Add($"-{matchedName}:{switchValue}");
                continue;
            }

            arguments.Add($"-{matchedName} {EscapePowerShellSingleQuotedValue(parameter.Value)}");
        }

        return string.Join(" ", arguments);
    }

    private static string ReplacePowerShellPlaceholders(string script, Dictionary<string, string> parameters)
    {
        var resolvedScript = script;
        foreach (var parameter in parameters)
        {
            var aliases = new List<string>
            {
                parameter.Key.ToLowerInvariant()
            };

            if (string.Equals(parameter.Key, "PACKAGE_PATH", StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add("package");
            }

            foreach (var alias in aliases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var placeholder = $"{{{alias}}}";
                resolvedScript = resolvedScript.Replace(placeholder, EscapePowerShellSingleQuotedValue(parameter.Value), StringComparison.OrdinalIgnoreCase);
            }
        }

        return resolvedScript;
    }

    private static bool IsJdkApplication(ApplicationInfo app)
    {
        return (app.Id?.StartsWith("jdk", StringComparison.OrdinalIgnoreCase) == true) ||
               (app.Name?.StartsWith("jdk", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string NormalizeRemoteDirectoryPath(OperatingSystemType osType, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmedPath = path.Trim();
        if (trimmedPath.Contains("..", StringComparison.Ordinal)
            || trimmedPath.IndexOfAny(['"', '\'', '`', ';', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("远端上传目录包含不允许的字符或路径跳转。请选择安全的绝对路径。");
        }

        var normalizedPath = osType == OperatingSystemType.Windows
            ? trimmedPath.Replace("/", "\\")
            : trimmedPath.Replace("\\", "/");

        if (osType == OperatingSystemType.Windows)
        {
            if (!Regex.IsMatch(normalizedPath, @"^[a-zA-Z]:\\"))
            {
                throw new InvalidOperationException("Windows 远端上传目录必须使用绝对路径。请选择类似 C:\\Windows\\Temp\\jdk-upload 的目录。");
            }

            return normalizedPath.EndsWith(":\\", StringComparison.Ordinal)
                ? normalizedPath
                : normalizedPath.TrimEnd('\\');
        }

        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Linux 远端上传目录必须使用绝对路径。请选择类似 /tmp/jdk-upload 的目录。");
        }

        return normalizedPath == "/"
            ? normalizedPath
            : normalizedPath.TrimEnd('/');
    }

    private static string CombineRemotePath(OperatingSystemType osType, string basePath, string childPath)
    {
        if (osType == OperatingSystemType.Windows)
        {
            return Path.Combine(basePath, childPath).Replace("/", "\\");
        }

        var normalizedBasePath = NormalizeRemoteDirectoryPath(osType, basePath);
        return normalizedBasePath == "/"
            ? $"/{childPath.TrimStart('/')}"
            : $"{normalizedBasePath}/{childPath.TrimStart('/')}";
    }

    private async Task StartServiceAsync(RemoteHost host, ApplicationInfo app, CancellationToken cancellationToken)
    {
        var serviceName = app.Name.ToLower() switch
        {
            "mysql" => "mysql",
            "mariadb" => "mariadb",
            "redis" => host.OsType == OperatingSystemType.Windows ? "redis-server" : "redis",
            "elasticsearch" => "elasticsearch",
            "rabbitmq" => "rabbitmq-server",
            "mosquitto" => "mosquitto",
            "nginx" => "nginx",
            "consul" => "consul",
            "traefik" => "traefik",
            _ => app.Name.ToLower()
        };
        
                
        if (host.OsType == OperatingSystemType.Windows)
        {
            await _sshService.ExecuteCommandAsync(
                $"Start-Service {serviceName} -ErrorAction SilentlyContinue",
                cancellationToken: cancellationToken);
        }
        else
        {
            var startCommand = app.Name.Equals("MySQL", StringComparison.OrdinalIgnoreCase)
                ? "systemctl restart mysql || systemctl start mysql || systemctl restart mysqld || systemctl start mysqld"
                : app.Name.Equals("MariaDB", StringComparison.OrdinalIgnoreCase)
                    ? "systemctl restart mariadb || systemctl start mariadb || systemctl restart mysql || systemctl start mysql || systemctl restart mysqld || systemctl start mysqld"
                    : $"systemctl restart {serviceName} || systemctl start {serviceName}";

            await _sshService.ExecuteCommandAsync(
                startCommand,
                cancellationToken: cancellationToken);
        }
    }

    private string GetExecutableName(ApplicationInfo app)
    {
        return app.Name.ToLower() switch
        {
            "mysql" => "mysql",
            "mariadb" => "mariadb",
            "redis" => "redis-server",
            "elasticsearch" => "elasticsearch",
            "rabbitmq" => "rabbitmq-server",
            "mosquitto" => "mosquitto",
            "nginx" => "nginx",
            "consul" => "consul",
            "traefik" => "traefik",
            _ => app.Name.ToLower()
        };
    }

    private string GetProcessName(ApplicationInfo app)
    {
        return app.Name.ToLower() switch
        {
            "mysql" => "mysql",
            "mariadb" => "mariadbd",
            "redis" => "redis-server",
            "elasticsearch" => "elasticsearch",
            "rabbitmq" => "beam.smp",
            "mosquitto" => "mosquitto",
            "nginx" => "nginx",
            "consul" => "consul",
            "traefik" => "traefik",
            _ => app.Name.ToLower()
        };
    }

    private string GetServiceName(string appName)
    {
        appName = appName.ToLower();
        
        if (appName.Contains("elasticsearch"))
        {
            return "elasticsearch";
        }
        
        return appName switch
        {
            "mysql" => "mysql",
            "mariadb" => "mariadb",
            "redis" => "redis",
            "elasticsearch" => "elasticsearch",
            "rabbitmq" => "rabbitmq-server",
            "mosquitto" => "mosquitto",
            "nginx" => "nginx",
            "consul" => "consul",
            "traefik" => "traefik",
            _ => appName
        };
    }

    private string ExtractVersion(string output)
    {
        var versionPattern = System.Text.RegularExpressions.Regex.Match(output, @"\d+\.\d+\.\d+");
        return versionPattern.Success ? versionPattern.Value : "未知";
    }

    private bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var cleanValue = value.Trim().ToLower();
        return cleanValue == "true" || cleanValue == "1" || cleanValue == "running" || cleanValue == "active" || cleanValue == "installed" || cleanValue == "yes";
    }

    private void ParseCheckOutput(string output, ApplicationStatus status)
    {
        if (string.IsNullOrEmpty(output)) return;

        var events = ScriptProtocolParser.Parse(output).ToList();
        ApplicationStatusNormalizer.ApplyStatusEvents(status, events);
        var evidence = ApplicationStatusNormalizer.BuildEvidence(events);
        ApplicationStatusNormalizer.Normalize(status, evidence);
    }
}
