using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace RemoteInstaller.Services;

/// <summary>
/// SSH 连接服务
/// 使用 Renci.SshNet (SSH.NET) 库实现
/// </summary>
public class SshService : IDisposable
{
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private bool _disposed;
    private ILogger? _logger;
    private string? _connectedHostKey;
    private ConnectionInfo? _connectionInfo;

    private ShellStream? _terminalShellStream;
    private CancellationTokenSource? _terminalReadCts;
    private Task? _terminalReadTask;
    private Action<string>? _terminalOutputHandler;
    private readonly object _terminalLock = new();
    private readonly SemaphoreSlim _terminalWriteLock = new(1, 1);
    
    // 连接超时时间（秒）
    private const int ConnectionTimeoutSeconds = 10;

    // 心跳检测相关
    private readonly Dictionary<string, CancellationTokenSource> _heartbeatTokens = new();
    private readonly object _heartbeatLock = new();
    private Timer? _heartbeatTimer;

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public event EventHandler<string>? ConnectionEstablished;
    public event EventHandler<string>? ConnectionLost;

    public class TestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public OperatingSystemType DetectedOsType { get; set; } = OperatingSystemType.CentOS;
        public string DetectedOsVersion { get; set; } = string.Empty;
        public string DetectedCpuArchitecture { get; set; } = string.Empty;
    }

    /// <summary>
    /// 启动心跳检测
    /// </summary>
    /// <param name="intervalMinutes">心跳间隔（分钟）</param>
    public void StartHeartbeat(int intervalMinutes = 5)
    {
        lock (_heartbeatLock)
        {
            if (_heartbeatTimer != null)
            {
                return;
            }

            _heartbeatTimer = new Timer(async _ => await HeartbeatCallback(), null,
                TimeSpan.FromMinutes(intervalMinutes), TimeSpan.FromMinutes(intervalMinutes));
        }
    }

    /// <summary>
    /// 心跳回调
    /// </summary>
    private async Task HeartbeatCallback()
    {
        lock (_heartbeatLock)
        {
            var hostIds = _heartbeatTokens.Keys.ToList();

            foreach (var hostId in hostIds)
            {
                try
                {
                    // 这里应该从数据库获取主机信息并检测
                    // 简化处理：只是记录心跳时间
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// 停止心跳检测
    /// </summary>
    public void StopHeartbeat()
    {
        lock (_heartbeatLock)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            foreach (var token in _heartbeatTokens.Values)
            {
                token.Cancel();
            }
            _heartbeatTokens.Clear();
        }
    }

    /// <summary>
    /// 测试 SSH 连接并检测操作系统类型
    /// </summary>
    public async Task<TestResult> TestConnectionAsync(RemoteHost host, CancellationToken cancellationToken = default)
    {
        try
        {
            // 解密密码
            string? password = null;
            if (host.AuthType == AuthType.Password && !string.IsNullOrEmpty(host.EncryptedPassword))
            {
                password = EncryptionService.Decrypt(host.EncryptedPassword);
                if (string.IsNullOrEmpty(password))
                {
                    return new TestResult
                    {
                        Success = false,
                        Message = "密码解密失败",
                        DetectedOsType = OperatingSystemType.CentOS
                    };
                }
            }

            // 创建连接信息
            ConnectionInfo connectionInfo = CreateConnectionInfo(host, password);

            // 创建 SSH 客户端
            using (var client = new SshClient(connectionInfo))
            {
                try
                {
                    // 异步连接（带超时）
                    await Task.Run(() => client.Connect(), cancellationToken);

                    // 检测操作系统类型
                    var detectedOsType = await DetectOperatingSystemAsync(client, cancellationToken);
                    var detectedOsVersion = await DetectOperatingSystemVersionAsync(client, cancellationToken);
                    var detectedCpuArchitecture = await DetectCpuArchitectureAsync(client, detectedOsType, cancellationToken);

                    client.Disconnect();

                    var result = new TestResult
                    {
                        Success = true,
                        Message = "连接成功",
                        DetectedOsType = detectedOsType,
                        DetectedOsVersion = detectedOsVersion,
                        DetectedCpuArchitecture = detectedCpuArchitecture
                    };

                    return result;
                }
                catch (Exception connectEx)
                {
                    // 提取更友好的错误消息
                    string errorMessage = GetFriendlyErrorMessage(connectEx);

                    return new TestResult
                    {
                        Success = false,
                        Message = errorMessage,
                        DetectedOsType = OperatingSystemType.CentOS
                    };
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new TestResult
            {
                Success = false,
                Message = "连接已被取消",
                DetectedOsType = OperatingSystemType.CentOS
            };
        }
        catch (Exception ex)
        {
            return new TestResult
            {
                Success = false,
                Message = $"连接失败：{ex.Message}",
                DetectedOsType = OperatingSystemType.CentOS
            };
        }
    }

    /// <summary>
    /// 创建 SSH 连接信息
    /// </summary>
    private ConnectionInfo CreateConnectionInfo(RemoteHost host, string? password)
    {
        List<AuthenticationMethod> authMethods = new();

        switch (host.AuthType)
        {
            case AuthType.Password:
                if (!string.IsNullOrEmpty(password))
                {
                    authMethods.Add(new PasswordAuthenticationMethod(host.Username, password));
                }
                else
                {
                    throw new ArgumentException("密码不能为空");
                }
                break;

            case AuthType.PrivateKey:
                if (!string.IsNullOrEmpty(host.KeyPath))
                {
                    // 解密密钥 passphrase（如果有）
                    string? keyPassphrase = null;
                    if (!string.IsNullOrEmpty(host.EncryptedKeyPassphrase))
                    {
                        keyPassphrase = EncryptionService.Decrypt(host.EncryptedKeyPassphrase);
                    }

                    if (File.Exists(host.KeyPath))
                    {
                        // 根据密钥文件类型选择适当的加载方式
                        PrivateKeyFile keyFile = LoadPrivateKeyFile(host.KeyPath, keyPassphrase);
                        authMethods.Add(new PrivateKeyAuthenticationMethod(host.Username, keyFile));
                    }
                    else
                    {
                        throw new FileNotFoundException($"密钥文件不存在：{host.KeyPath}");
                    }
                }
                else
                {
                    throw new ArgumentException("密钥路径不能为空");
                }
                break;

            default:
                throw new ArgumentException($"不支持的认证类型：{host.AuthType}");
        }

        return new ConnectionInfo(
            host.IpAddress,
            host.Port,
            host.Username,
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(ConnectionTimeoutSeconds)
        };
    }

    /// <summary>
    /// 加载私钥文件（自动检测密钥类型）
    /// </summary>
    private PrivateKeyFile LoadPrivateKeyFile(string keyPath, string? passphrase)
    {
        try
        {
            // 尝试作为 RSA/DSA/ECDSA 密钥加载
            return new PrivateKeyFile(keyPath, passphrase);
        }
        catch (Exception ex)
        {
            throw new Exception($"无法加载密钥文件：{ex.Message}");
        }
    }

    /// <summary>
    /// 检测远程主机的操作系统类型
    /// </summary>
    private async Task<OperatingSystemType> DetectOperatingSystemAsync(SshClient client, CancellationToken cancellationToken)
    {
        try
        {
            // 使用 RunCommand 执行命令，并设置超时
            var result = await Task.Run(() =>
            {
                using var cmd = client.CreateCommand("uname -a");
                cmd.CommandTimeout = TimeSpan.FromSeconds(5);
                return cmd.Execute();
            }, cancellationToken);

            string? output = result ?? string.Empty;

            if (!string.IsNullOrEmpty(output))
            {
                output = output.ToLower();

                // 检测 Windows (Cygwin/WSL 等)
                if (output.Contains("windows"))
                {
                    return OperatingSystemType.Windows;
                }

                // 检测具体 Linux 发行版
                return await DetectLinuxDistributionAsync(client, cancellationToken);
            }
        }
        catch
        {
        }

        // 默认返回 CentOS
        return OperatingSystemType.CentOS;
    }

    /// <summary>
    /// 检测 Linux 发行版
    /// </summary>
    private async Task<OperatingSystemType> DetectLinuxDistributionAsync(SshClient client, CancellationToken cancellationToken)
    {
        try
        {
            // 尝试读取 /etc/os-release 文件
            var commandResult = client.RunCommand("cat /etc/os-release 2>/dev/null");
            string? osRelease = commandResult?.Result ?? string.Empty;

            if (!string.IsNullOrEmpty(osRelease))
            {
                // 检查 ID 字段
                if (osRelease.Contains("ID=ubuntu") || osRelease.Contains("ID=\"ubuntu\"") || osRelease.Contains("ID='ubuntu'"))
                {
                    return OperatingSystemType.Ubuntu;
                }

                if (osRelease.Contains("ID=centos") || osRelease.Contains("ID=\"centos\"") || osRelease.Contains("ID='centos'") ||
                    osRelease.Contains("ID=rocky") || osRelease.Contains("ID=\"rocky\"") ||
                    osRelease.Contains("ID=almalinux") || osRelease.Contains("ID=\"almalinux\""))
                {
                    return OperatingSystemType.CentOS;
                }

                if (osRelease.Contains("ID=debian") || osRelease.Contains("ID=\"debian\"") || osRelease.Contains("ID='debian'"))
                {
                    return OperatingSystemType.Ubuntu;
                }

                if (osRelease.Contains("ID=redhat") || osRelease.Contains("ID=\"redhat\"") || osRelease.Contains("ID='redhat'"))
                {
                    return OperatingSystemType.CentOS;
                }
            }
        }
        catch
        {
        }

        // 默认返回 CentOS
        return OperatingSystemType.CentOS;
    }

    private async Task<string> DetectOperatingSystemVersionAsync(SshClient client, CancellationToken cancellationToken)
    {
        try
        {
            var commandResult = client.RunCommand("cat /etc/os-release 2>/dev/null");
            string? osRelease = commandResult?.Result ?? string.Empty;

            if (string.IsNullOrWhiteSpace(osRelease))
            {
                return string.Empty;
            }

            var versionLine = osRelease
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("VERSION_ID=", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(versionLine))
            {
                return string.Empty;
            }

            return versionLine.Substring("VERSION_ID=".Length).Trim().Trim('"', '\'');
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> DetectCpuArchitectureAsync(SshClient client, OperatingSystemType osType, CancellationToken cancellationToken)
    {
        try
        {
            var command = osType == OperatingSystemType.Windows
                ? "powershell -NoProfile -Command \"[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()\""
                : "uname -m 2>/dev/null";

            var result = await Task.Run(() =>
            {
                using var sshCommand = client.CreateCommand(command);
                sshCommand.CommandTimeout = TimeSpan.FromSeconds(5);
                return sshCommand.Execute();
            }, cancellationToken);

            return NormalizeCpuArchitecture(result);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeCpuArchitecture(string? architecture)
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

    /// <summary>
    /// 获取友好的错误消息
    /// </summary>
    private string GetFriendlyErrorMessage(Exception ex)
    {
        if (ex is SocketException socketEx)
        {
            return socketEx.SocketErrorCode.ToString() switch
            {
                "ConnectionRefused" => "连接被拒绝，请检查 SSH 服务是否运行",
                "ConnectionReset" => "连接被重置",
                "NetworkUnreachable" => "网络不可达",
                "HostUnreachable" => "主机不可达",
                "TimedOut" => "连接超时，请检查网络",
                _ => $"网络错误：{ex.Message}"
            };
        }

        if (ex is SshConnectionException connEx)
        {
            if (connEx.Message.Contains("Authentication failed") || connEx.Message.Contains("认证失败"))
            {
                return "认证失败，请检查用户名和密码/密钥";
            }
            return $"SSH 连接错误：{connEx.Message}";
        }

        if (ex.Message.Contains("Authentication failed") || ex.Message.Contains("认证失败"))
        {
            return "认证失败，请检查用户名和密码/密钥";
        }

        if (ex is FileNotFoundException fileEx)
        {
            return fileEx.Message;
        }

        return ex.Message;
    }

    /// <summary>
    /// 连接到远程主机。
    /// </summary>
    public async Task ConnectAsync(RemoteHost host, CancellationToken cancellationToken = default)
    {
        var requestedHostKey = GetHostKey(host);

        // 如果当前 SSH 会话已连接到目标主机，则直接复用。
        if (_sshClient != null && _sshClient.IsConnected &&
            string.Equals(_connectedHostKey, requestedHostKey, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.Info($"连接到 {host.Name}");

        // 解密密码。
        string? password = null;
        if (host.AuthType == AuthType.Password && !string.IsNullOrEmpty(host.EncryptedPassword))
        {
            password = EncryptionService.Decrypt(host.EncryptedPassword);
        }

        // 创建连接信息，并缓存起来供后续按需创建 SFTP 客户端复用。
        ConnectionInfo connectionInfo = CreateConnectionInfo(host, password);
        _connectionInfo = connectionInfo;

        // 如果已有客户端但目标主机不同或连接失效，则先完整清理。
        if (_sshClient != null)
        {
            CleanupTerminalSession();
            try { _sshClient.Disconnect(); } catch { }
            _sshClient.Dispose();
            _sshClient = null;
        }

        if (_sftpClient != null)
        {
            try { _sftpClient.Disconnect(); } catch { }
            _sftpClient.Dispose();
            _sftpClient = null;
        }

        // 先建立 SSH 主连接。
        _sshClient = new SshClient(connectionInfo);
        await Task.Run(() => _sshClient.Connect(), cancellationToken);
        _connectedHostKey = requestedHostKey;

        // SFTP 按需建立。状态检测、终端和普通命令不需要为 SFTP 握手付出额外等待。

        _logger?.Success($"连接到 {host.Name} 成功");

        // 触发连接建立事件。
        ConnectionEstablished?.Invoke(this, host.Id);
    }

    /// <summary>
    /// 检查连接是否有效（心跳检测）
    /// </summary>
    public async Task<bool> CheckConnectionAsync(RemoteHost host, CancellationToken cancellationToken = default)
    {
        try
        {
            // 如果已有连接，检查是否仍然有效
            if (_sshClient != null && _sshClient.IsConnected)
            {
                try
                {
                    // 执行一个简单的命令来测试连接
                    var result = await Task.Run(() => _sshClient.RunCommand("echo alive"), cancellationToken);
                    if (result?.ExitStatus == 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    // 连接已断开，重新连接
                    Disconnect();
                }
            }

            // 尝试重新连接
            var testResult = await TestConnectionAsync(host, cancellationToken);
            if (testResult.Success)
            {
                await ConnectAsync(host, cancellationToken);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 执行命令（带超时和重试）
    /// </summary>
    public async Task<string> ExecuteCommandWithRetryAsync(
        RemoteHost host,
        string command,
        int maxRetries = 3,
        int timeoutSeconds = 60,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // 确保连接有效
                if (attempt > 0 || !_sshClient?.IsConnected == true)
                {
                    if (!_sshClient?.IsConnected == true)
                    {
                        if (!await CheckConnectionAsync(host, cancellationToken))
                        {
                            throw new Exception("无法连接到远程主机");
                        }
                    }
                }

                // 执行命令
                return await ExecuteCommandAsync(command, onOutput, cancellationToken);
            }
            catch (TimeoutException) when (attempt < maxRetries)
            {
                await Task.Delay(2000 * (attempt + 1), cancellationToken); // 指数退避
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                await Task.Delay(2000 * (attempt + 1), cancellationToken);
            }
        }

        throw new Exception($"命令执行失败，已重试 {maxRetries} 次");
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect(string? hostId = null)
    {
        _logger?.Info($"断开连接");
        CleanupTerminalSession();

        try
        {
            _sftpClient?.Disconnect();
            _sftpClient?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _sftpClient = null;
        }

        try
        {
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _sshClient = null;
            _connectedHostKey = null;
            _connectionInfo = null;
        }

        // 触发连接断开事件
        if (!string.IsNullOrEmpty(hostId))
        {
            ConnectionLost?.Invoke(this, hostId);
        }
    }

    /// <summary>
    /// 设置日志服务
    /// </summary>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public async Task StartTerminalSessionAsync(RemoteHost host, Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        if (onOutput == null)
        {
            throw new ArgumentNullException(nameof(onOutput));
        }

        await ConnectAsync(host, cancellationToken);
        await StopTerminalSessionAsync();

        if (_sshClient == null || !_sshClient.IsConnected)
        {
            throw new InvalidOperationException("SSH 未连接，无法启动终端会话");
        }

        var shellStream = _sshClient.CreateShellStream("xterm-256color", 120, 40, 1280, 720, 4096);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_terminalLock)
        {
            _terminalShellStream = shellStream;
            _terminalReadCts = linkedCts;
            _terminalOutputHandler = onOutput;
            _terminalReadTask = Task.Run(() => ReadTerminalLoopAsync(linkedCts.Token), linkedCts.Token);
        }
    }

    public async Task SendTerminalInputAsync(string input, bool appendNewLine = true, CancellationToken cancellationToken = default)
    {
        ShellStream stream;
        lock (_terminalLock)
        {
            if (_terminalShellStream == null || _sshClient == null || !_sshClient.IsConnected)
            {
                throw new InvalidOperationException("终端会话未启动");
            }
            stream = _terminalShellStream;
        }

        await _terminalWriteLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                if (appendNewLine)
                {
                    stream.WriteLine(input ?? string.Empty);
                }
                else
                {
                    stream.Write(input ?? string.Empty);
                    stream.Flush();
                }
            }, cancellationToken);
        }
        finally
        {
            _terminalWriteLock.Release();
        }
    }

    public Task SendTerminalInterruptAsync(CancellationToken cancellationToken = default)
    {
        return SendTerminalInputAsync("\u0003", appendNewLine: false, cancellationToken);
    }

    public async Task StopTerminalSessionAsync()
    {
        var (readCts, readTask, shellStream) = DetachTerminalSession();

        try
        {
            readCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            shellStream?.Dispose();
        }
        catch
        {
        }

        if (readTask != null)
        {
            try
            {
                await readTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
            catch
            {
            }
        }

        readCts?.Dispose();
    }

    private async Task ReadTerminalLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var outputBatch = new StringBuilder(8192);

        while (!cancellationToken.IsCancellationRequested)
        {
            ShellStream? stream;
            Action<string>? handler;
            SshClient? client;

            lock (_terminalLock)
            {
                stream = _terminalShellStream;
                handler = _terminalOutputHandler;
                client = _sshClient;
            }

            if (stream == null || client == null || !client.IsConnected)
            {
                break;
            }

            try
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(40, cancellationToken);
                    continue;
                }

                outputBatch.Clear();

                do
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    outputBatch.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
                while (stream.DataAvailable && !cancellationToken.IsCancellationRequested);

                if (outputBatch.Length > 0)
                {
                    handler?.Invoke(outputBatch.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private void CleanupTerminalSession()
    {
        var (readCts, _, shellStream) = DetachTerminalSession();
        try
        {
            readCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            shellStream?.Dispose();
        }
        catch
        {
        }

        readCts?.Dispose();
    }

    private (CancellationTokenSource? readCts, Task? readTask, ShellStream? shellStream) DetachTerminalSession()
    {
        lock (_terminalLock)
        {
            var readCts = _terminalReadCts;
            var readTask = _terminalReadTask;
            var shellStream = _terminalShellStream;

            _terminalReadCts = null;
            _terminalReadTask = null;
            _terminalShellStream = null;
            _terminalOutputHandler = null;

            return (readCts, readTask, shellStream);
        }
    }

    private static string GetHostKey(RemoteHost host)
    {
        return $"{host.IpAddress}:{host.Port}:{host.Username}";
    }

    /// <summary>
    /// 确保 SFTP 客户端已连接。
    /// </summary>
    private async Task EnsureSftpConnectedAsync(CancellationToken cancellationToken = default)
    {
        // 如果已经连接，则直接复用。
        if (_sftpClient != null && _sftpClient.IsConnected)
        {
            return;
        }

        // SFTP 依赖 SSH 主连接和连接信息。
        if (_sshClient == null || !_sshClient.IsConnected || _connectionInfo == null)
        {
            throw new InvalidOperationException("SSH 未连接，无法建立 SFTP 客户端");
        }

        // 旧的 SFTP 客户端如果残留但已断开，先清理再重建。
        if (_sftpClient != null)
        {
            try { _sftpClient.Disconnect(); } catch { }
            _sftpClient.Dispose();
            _sftpClient = null;
        }

        _sftpClient = new SftpClient(_connectionInfo);
        await Task.Run(() => _sftpClient.Connect(), cancellationToken);
    }

    /// <summary>
    /// 判断当前连接是否提供可用的 SFTP 能力。
    /// </summary>
    public virtual async Task<bool> IsSftpAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureSftpConnectedAsync(cancellationToken);
            return _sftpClient != null && _sftpClient.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 列举远程目录内容。
    /// </summary>
    public virtual async Task<IReadOnlyList<RemoteFileEntry>> ListDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("远程目录路径不能为空", nameof(remotePath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedRemotePath = ResolveRemotePath(remotePath);
        return await Task.Run<IReadOnlyList<RemoteFileEntry>>(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            var entries = _sftpClient
                .ListDirectory(normalizedRemotePath, _ => { })
                .Where(file => file.Name != "." && file.Name != "..")
                .Select(MapRemoteFileEntry)
                .OrderByDescending(file => file.IsDirectory)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return entries;
        }, cancellationToken);
    }

    /// <summary>
    /// 获取单个远程条目。
    /// </summary>
    public virtual async Task<RemoteFileEntry?> GetEntryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("远程路径不能为空", nameof(remotePath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedRemotePath = ResolveRemotePath(remotePath);
        return await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            if (!_sftpClient.Exists(normalizedRemotePath))
            {
                return null;
            }

            var file = _sftpClient.Get(normalizedRemotePath);
            return MapRemoteFileEntry(file);
        }, cancellationToken);
    }

    /// <summary>
    /// 下载单个远程文件。
    /// </summary>
    public async Task DownloadFileAsync(string remotePath, string localPath, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("远程文件路径不能为空", nameof(remotePath));
        }

        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new ArgumentException("本地文件路径不能为空", nameof(localPath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedRemotePath = ResolveRemotePath(remotePath);
        var localDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }

        await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            var file = _sftpClient.Get(normalizedRemotePath);
            if (file.IsDirectory)
            {
                throw new InvalidOperationException("当前路径是目录，第一版仅支持下载单个文件");
            }

            using var localStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _sftpClient.DownloadFile(normalizedRemotePath, localStream, downloadedBytes =>
            {
                if (onProgress == null || file.Length <= 0)
                {
                    return;
                }

                var progress = (int)Math.Round(downloadedBytes * 100d / file.Length);
                onProgress(Math.Max(0, Math.Min(100, progress)));
            });
        }, cancellationToken);
    }

    /// <summary>
    /// 删除远程文件。
    /// </summary>
    public async Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("远程文件路径不能为空", nameof(remotePath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);
        var normalizedRemotePath = ResolveRemotePath(remotePath);

        await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            _sftpClient.DeleteFile(normalizedRemotePath);
        }, cancellationToken);
    }

    /// <summary>
    /// 删除远程目录。
    /// </summary>
    public async Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("远程目录路径不能为空", nameof(remotePath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);
        var normalizedRemotePath = ResolveRemotePath(remotePath);

        if (string.IsNullOrWhiteSpace(normalizedRemotePath) || normalizedRemotePath == "/")
        {
            throw new InvalidOperationException("不允许删除根目录");
        }

        await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            if (recursive)
            {
                DeleteDirectoryInternal(normalizedRemotePath);
                return;
            }

            _sftpClient.DeleteDirectory(normalizedRemotePath);
        }, cancellationToken);
    }

    /// <summary>
    /// 移动或重命名远程条目。
    /// </summary>
    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("源路径不能为空", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("目标路径不能为空", nameof(targetPath));
        }

        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedSourcePath = ResolveRemotePath(sourcePath);
        var normalizedTargetPath = ResolveRemotePath(targetPath);
        var targetDirectory = GetRemoteDirectoryPath(normalizedTargetPath);

        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            await EnsureRemoteDirectoryExistsAsync(targetDirectory, cancellationToken);
        }

        await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            _sftpClient.RenameFile(normalizedSourcePath, normalizedTargetPath);
        }, cancellationToken);
    }

    /// <summary>
    /// 执行远程命令
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, Action<string>? onOutput = null, CancellationToken cancellationToken = default, bool throwOnError = false)
    {
        var result = await ExecuteCommandResultAsync(command, onOutput, cancellationToken);

        if (throwOnError && result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            throw new Exception($"远程命令执行失败 (ExitCode: {result.ExitCode}): {error}");
        }

        return result.CombinedOutput;
    }

    /// <summary>
    /// 执行远程命令并返回结构化结果。
    /// </summary>
    public async Task<RemoteCommandResult> ExecuteCommandResultAsync(
        string command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        // 移除命令中的 \r 字符，防止在 Linux 环境下执行出错
        var cleanCommand = command.Replace("\r\n", "\n").Replace("\r", "");

        if (_sshClient == null || !_sshClient.IsConnected)
        {
            throw new InvalidOperationException("SSH 未连接");
        }

        // 安装/编译/卸载相关命令超时时间延长
        var isLongRunning = cleanCommand.ToLower().Contains("install") ||
                           cleanCommand.ToLower().Contains("uninstall") ||
                           cleanCommand.ToLower().Contains("remove") ||
                           cleanCommand.ToLower().Contains("purge") ||
                           cleanCommand.ToLower().Contains("apt") ||
                           cleanCommand.ToLower().Contains("yum") ||
                           cleanCommand.ToLower().Contains("dpkg") ||
                           cleanCommand.ToLower().Contains("rpm") ||
                           cleanCommand.ToLower().Contains("make") ||
                           cleanCommand.ToLower().Contains("setup");

        var timeoutMs = isLongRunning
            ? 600000  // 10 分钟 (增加到 10 分钟以支持较慢的编译过程)
            : 60000;  // 1 分钟

        return await Task.Run(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            using var cmd = _sshClient!.CreateCommand(cleanCommand);
            cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);

            var asyncResult = cmd.BeginExecute();
            var fullOutput = new StringBuilder();
            var stdoutOutput = new StringBuilder();
            var stderrOutput = new StringBuilder();

            try
            {
                // 异步读取标准输出和错误流
                using (var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8))
                using (var errorReader = new StreamReader(cmd.ExtendedOutputStream, Encoding.UTF8))
                {
                    char[] buffer = new char[4096];
                    while (!asyncResult.IsCompleted)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        bool readAnything = false;

                        try
                        {
                            // 读取标准输出
                            if (reader.Peek() != -1)
                            {
                                int readCount = reader.Read(buffer, 0, buffer.Length);
                                if (readCount > 0)
                                {
                                    var chunk = new string(buffer, 0, readCount);
                                    onOutput?.Invoke(chunk);
                                    fullOutput.Append(chunk);
                                    stdoutOutput.Append(chunk);
                                    readAnything = true;
                                }
                            }

                            // 读取错误输出
                            if (errorReader.Peek() != -1)
                            {
                                int readCount = errorReader.Read(buffer, 0, buffer.Length);
                                if (readCount > 0)
                                {
                                    var chunk = new string(buffer, 0, readCount);
                                    onOutput?.Invoke(chunk);
                                    fullOutput.Append(chunk);
                                    stderrOutput.Append(chunk);
                                    readAnything = true;
                                }
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // 流已释放，跳出循环
                            break;
                        }
                        catch (IOException)
                        {
                            // IO 错误（可能连接断开），跳出循环
                            break;
                        }

                        if (!readAnything)
                        {
                            // 等待命令完成或最多50ms，避免busy-wait
                            var waitHandle = asyncResult.AsyncWaitHandle;
                            await Task.Run(() => waitHandle.WaitOne(50), cancellationToken);
                        }
                    }

                    // 最后再读一次剩余的数据
                    try
                    {
                        while (reader.Peek() != -1)
                        {
                            int readCount = reader.Read(buffer, 0, buffer.Length);
                            if (readCount <= 0) break;
                            var chunk = new string(buffer, 0, readCount);
                            onOutput?.Invoke(chunk);
                            fullOutput.Append(chunk);
                            stdoutOutput.Append(chunk);
                        }
                        while (errorReader.Peek() != -1)
                        {
                            int readCount = errorReader.Read(buffer, 0, buffer.Length);
                            if (readCount <= 0) break;
                            var chunk = new string(buffer, 0, readCount);
                            onOutput?.Invoke(chunk);
                            fullOutput.Append(chunk);
                            stderrOutput.Append(chunk);
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException)
            {
            }

            try
            {
                // 等待完成并处理最终状态
                if (!asyncResult.IsCompleted)
                {
                    // 如果还没完成（比如被取消了），等待一小会儿让它尝试结束
                    asyncResult.AsyncWaitHandle.WaitOne(1000);
                }

                cmd.EndExecute(asyncResult);
                stopwatch.Stop();

                var stderr = stderrOutput.ToString();
                if (string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(cmd.Error))
                {
                    stderr = cmd.Error;
                }

                return new RemoteCommandResult
                {
                    Command = cleanCommand,
                    ExitCode = cmd.ExitStatus,
                    Stdout = stdoutOutput.ToString(),
                    Stderr = stderr,
                    TimedOut = false,
                    Duration = stopwatch.Elapsed
                };
            }
            catch (ObjectDisposedException)
            {
                // 如果整个 cmd 都被释放了
                stopwatch.Stop();
                return new RemoteCommandResult
                {
                    Command = cleanCommand,
                    ExitCode = -1,
                    Stdout = fullOutput.ToString(),
                    Stderr = string.Empty,
                    TimedOut = false,
                    Duration = stopwatch.Elapsed
                };
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 上传文件
    /// </summary>
    public async Task UploadFileAsync(string localPath, string remotePath, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(localPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"本地文件不存在：{localPath}");
        }

        using (var localStream = new FileStream(localPath, FileMode.Open, FileAccess.Read))
        {
            await UploadStreamAsync(localStream, remotePath, onProgress, cancellationToken);
        }
    }

    /// <summary>
    /// 上传整个目录，保持子目录结构
    /// </summary>
    public async Task UploadDirectoryAsync(string localDirectoryPath, string remoteDirectoryPath, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var directoryInfo = new DirectoryInfo(localDirectoryPath);
        if (!directoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"本地目录不存在：{localDirectoryPath}");
        }

        var directories = Directory.GetDirectories(localDirectoryPath, "*", SearchOption.AllDirectories);
        var files = Directory.GetFiles(localDirectoryPath, "*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        long uploadedBytes = 0;

        await EnsureRemoteDirectoryExistsAsync(remoteDirectoryPath, cancellationToken);

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeDirectory = Path.GetRelativePath(localDirectoryPath, directory);
            var remoteDirectory = CombineRemotePath(remoteDirectoryPath, relativeDirectory);
            await EnsureRemoteDirectoryExistsAsync(remoteDirectory, cancellationToken);
        }

        if (files.Length == 0)
        {
            onProgress?.Invoke(100);
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(localDirectoryPath, file);
            var remoteFilePath = CombineRemotePath(remoteDirectoryPath, relativePath);

            await UploadFileAsync(
                file,
                remoteFilePath,
                fileProgress =>
                {
                    if (onProgress == null)
                    {
                        return;
                    }

                    var currentFileBytes = (long)(fileInfo.Length * (fileProgress / 100d));
                    var currentUploadedBytes = Math.Min(totalBytes, uploadedBytes + currentFileBytes);
                    var overallProgress = totalBytes <= 0
                        ? 100
                        : (int)Math.Round(currentUploadedBytes * 100d / totalBytes);

                    onProgress(Math.Max(0, Math.Min(100, overallProgress)));
                },
                cancellationToken);

            uploadedBytes += fileInfo.Length;
            onProgress?.Invoke(totalBytes <= 0
                ? 100
                : (int)Math.Round(uploadedBytes * 100d / totalBytes));
        }
    }

    /// <summary>
    /// 上传流内容
    /// </summary>
    public async Task UploadStreamAsync(Stream localStream, string remotePath, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedRemotePath = ResolveRemotePath(remotePath);
        var fileSize = localStream.Length;
        var remoteDirectory = GetRemoteDirectoryPath(normalizedRemotePath);

        await EnsureRemoteDirectoryExistsAsync(remoteDirectory, cancellationToken);

        if (await Task.Run(() => _sftpClient.Exists(normalizedRemotePath), cancellationToken))
        {
            await Task.Run(() =>
            {
                if (_sftpClient.Exists(normalizedRemotePath))
                {
                    _sftpClient.DeleteFile(normalizedRemotePath);
                }
            }, cancellationToken);
        }

        await Task.Run(() =>
        {
            var buffer = new byte[64 * 1024];
            long totalBytes = 0;

            using var remoteStream = _sftpClient.OpenWrite(normalizedRemotePath);
            int bytesRead;
            while ((bytesRead = localStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                remoteStream.Write(buffer, 0, bytesRead);
                totalBytes += bytesRead;

                if (onProgress != null && fileSize > 0)
                {
                    var progress = (int)Math.Round(totalBytes * 100d / fileSize);
                    onProgress(Math.Max(0, Math.Min(100, progress)));
                }
            }

            remoteStream.Flush();
        }, cancellationToken);

        var exists = await Task.Run(() => _sftpClient.Exists(normalizedRemotePath), cancellationToken);
        if (!exists)
        {
            throw new Exception("文件上传后验证失败，未找到远程文件");
        }
    }

    /// <summary>
    /// 上传文本内容（自动处理换行符）
    /// </summary>
    public async Task UploadTextAsync(string content, string remotePath, OperatingSystemType targetOs, CancellationToken cancellationToken = default)
    {
        if (targetOs != OperatingSystemType.Windows)
        {
            // Linux 需要 LF 换行符
            content = content.Replace("\r\n", "\n");
        }

        var bytes = new UTF8Encoding(false).GetBytes(content);
        using (var ms = new MemoryStream(bytes))
        {
            await UploadStreamAsync(ms, remotePath, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// 读取远程文本文件内容
    /// </summary>
    public async Task<string> ReadTextFileAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        await EnsureSftpConnectedAsync(cancellationToken);

        return await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            var normalizedRemotePath = ResolveRemotePath(remotePath);
            using (var remoteStream = _sftpClient.OpenRead(normalizedRemotePath))
            using (var reader = new StreamReader(remoteStream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 检查远程文件是否存在
    /// </summary>
    public async Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        await EnsureSftpConnectedAsync(cancellationToken);

        return await Task.Run(() =>
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
            {
                throw new InvalidOperationException("SFTP 客户端未连接");
            }

            var normalizedRemotePath = ResolveRemotePath(remotePath);
            return _sftpClient.Exists(normalizedRemotePath);
        }, cancellationToken);
    }

    /// <summary>
    /// 创建远程目录
    /// </summary>
    public async Task MakeDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        await EnsureRemoteDirectoryExistsAsync(remotePath, cancellationToken);
    }

    private async Task EnsureRemoteDirectoryExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        await EnsureSftpConnectedAsync(cancellationToken);

        var normalizedInputPath = NormalizeRemotePath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedInputPath))
        {
            return;
        }

        var normalizedPath = ResolveRemotePath(remotePath);
        if (normalizedPath == "/")
        {
            return;
        }

        await Task.Run(() =>
        {
            foreach (var segment in EnumerateRemoteDirectorySegments(normalizedPath))
            {
                if (!_sftpClient.Exists(segment))
                {
                    _sftpClient.CreateDirectory(segment);
                }
            }
        }, cancellationToken);
    }

    private static IEnumerable<string> EnumerateRemoteDirectorySegments(string remotePath)
    {
        var normalizedPath = NormalizeRemotePath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath == "/")
        {
            yield break;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            yield break;
        }

        var current = normalizedPath.StartsWith("/", StringComparison.Ordinal) ? "/" : string.Empty;
        var startIndex = 0;

        if (segments[0].EndsWith(":", StringComparison.Ordinal))
        {
            current = segments[0];
            startIndex = 1;
        }

        for (var i = startIndex; i < segments.Length; i++)
        {
            current = CombineRemotePath(current, segments[i]);
            yield return current;
        }
    }

    /// <summary>
    /// 将用户输入路径解析为 SFTP 可用的实际路径。
    /// </summary>
    private string ResolveRemotePath(string remotePath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 客户端未连接");
        }

        var normalizedPath = NormalizeRemotePath(remotePath);
        var workingDirectory = NormalizeRemotePath(_sftpClient.WorkingDirectory);

        // 空路径或波浪线路径统一落到当前 SFTP 工作目录。
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath == "~")
        {
            return workingDirectory;
        }

        // ~/xxx 解析为当前工作目录下的相对路径。
        if (normalizedPath.StartsWith("~/", StringComparison.Ordinal))
        {
            return CombineRemotePath(workingDirectory, normalizedPath[2..]);
        }

        // 绝对路径直接使用；相对路径则挂到当前工作目录之下。
        return IsAbsoluteRemotePath(normalizedPath)
            ? normalizedPath
            : CombineRemotePath(workingDirectory, normalizedPath);
    }

    /// <summary>
    /// 判断远程路径是否已是绝对路径。
    /// </summary>
    private static bool IsAbsoluteRemotePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return false;
        }

        return remotePath.StartsWith("/", StringComparison.Ordinal)
            || (remotePath.Length >= 2 && char.IsLetter(remotePath[0]) && remotePath[1] == ':');
    }

    private static string GetRemoteDirectoryPath(string remotePath)
    {
        var normalizedPath = NormalizeRemotePath(remotePath);
        var lastSlashIndex = normalizedPath.LastIndexOf('/');
        if (lastSlashIndex < 0)
        {
            return string.Empty;
        }

        if (lastSlashIndex == 0)
        {
            return "/";
        }

        return normalizedPath[..lastSlashIndex];
    }

    private static string CombineRemotePath(string basePath, string childPath)
    {
        var normalizedBasePath = NormalizeRemotePath(basePath);
        var normalizedChildPath = NormalizeRemotePath(childPath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedBasePath))
        {
            return normalizedChildPath;
        }

        if (string.IsNullOrWhiteSpace(normalizedChildPath))
        {
            return normalizedBasePath;
        }

        if (normalizedBasePath.EndsWith("/", StringComparison.Ordinal))
        {
            return normalizedBasePath + normalizedChildPath;
        }

        return $"{normalizedBasePath}/{normalizedChildPath}";
    }

    private static string NormalizeRemotePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return string.Empty;
        }

        return remotePath.Trim().Replace('\\', '/');
    }

    /// <summary>
    /// 递归删除远程目录。
    /// </summary>
    private void DeleteDirectoryInternal(string remotePath)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 客户端未连接");
        }

        foreach (var entry in _sftpClient.ListDirectory(remotePath, _ => { }))
        {
            if (entry.Name == "." || entry.Name == "..")
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                DeleteDirectoryInternal(entry.FullName);
            }
            else
            {
                _sftpClient.DeleteFile(entry.FullName);
            }
        }

        _sftpClient.DeleteDirectory(remotePath);
    }

    /// <summary>
    /// 将 SSH.NET 的远程文件对象映射为仓库内通用模型。
    /// </summary>
    private static RemoteFileEntry MapRemoteFileEntry(SftpFile file)
    {
        return new RemoteFileEntry
        {
            Name = file.Name,
            RemotePath = NormalizeRemotePath(file.FullName),
            IsDirectory = file.IsDirectory,
            Size = file.IsDirectory ? 0 : file.Length,
            ModifiedTime = file.LastWriteTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)),
            Permissions = FormatPermissions(file.Attributes)
        };
    }

    /// <summary>
    /// 将 SFTP 权限位格式化为类似 rwxr-xr-x 的字符串。
    /// </summary>
    private static string FormatPermissions(SftpFileAttributes attributes)
    {
        var chars = new[]
        {
            attributes.IsDirectory ? 'd' : '-',
            attributes.OwnerCanRead ? 'r' : '-',
            attributes.OwnerCanWrite ? 'w' : '-',
            attributes.OwnerCanExecute ? 'x' : '-',
            attributes.GroupCanRead ? 'r' : '-',
            attributes.GroupCanWrite ? 'w' : '-',
            attributes.GroupCanExecute ? 'x' : '-',
            attributes.OthersCanRead ? 'r' : '-',
            attributes.OthersCanWrite ? 'w' : '-',
            attributes.OthersCanExecute ? 'x' : '-'
        };

        return new string(chars);
    }
    #region IDisposable 支持

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CleanupTerminalSession();
                _sshClient?.Disconnect();
                _sshClient?.Dispose();
                _sftpClient?.Disconnect();
                _sftpClient?.Dispose();
                _terminalWriteLock.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

}




















