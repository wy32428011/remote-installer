using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

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

                    client.Disconnect();

                    var result = new TestResult
                    {
                        Success = true,
                        Message = "连接成功",
                        DetectedOsType = detectedOsType
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
    /// 连接到远程主机
    /// </summary>
    public async Task ConnectAsync(RemoteHost host, CancellationToken cancellationToken = default)
    {
        var requestedHostKey = GetHostKey(host);

        // 如果已经连接，直接返回
        if (_sshClient != null && _sshClient.IsConnected &&
            _sftpClient != null && _sftpClient.IsConnected &&
            string.Equals(_connectedHostKey, requestedHostKey, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.Info($"连接到 {host.Name}");

        // 解密密码
        string? password = null;
        if (host.AuthType == AuthType.Password && !string.IsNullOrEmpty(host.EncryptedPassword))
        {
            password = EncryptionService.Decrypt(host.EncryptedPassword);
        }

        // 创建连接信息
        ConnectionInfo connectionInfo = CreateConnectionInfo(host, password);

        // 如果已有客户端但断开，先断开
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

        // 创建并连接 SSH 客户端
        _sshClient = new SshClient(connectionInfo);
        await Task.Run(() => _sshClient.Connect(), cancellationToken);

        // 创建 SFTP 客户端
        _sftpClient = new SftpClient(connectionInfo);
        await Task.Run(() => _sftpClient.Connect(), cancellationToken);
        _connectedHostKey = requestedHostKey;

        _logger?.Success($"连接到 {host.Name} 成功");

        // 触发连接建立事件
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
    /// 执行远程命令
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, Action<string>? onOutput = null, CancellationToken cancellationToken = default, bool throwOnError = false)
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
            using var cmd = _sshClient!.CreateCommand(cleanCommand);
            cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);

            var asyncResult = cmd.BeginExecute();
            var fullOutput = new StringBuilder();

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
                        }
                        while (errorReader.Peek() != -1)
                        {
                            int readCount = errorReader.Read(buffer, 0, buffer.Length);
                            if (readCount <= 0) break;
                            var chunk = new string(buffer, 0, readCount);
                            onOutput?.Invoke(chunk);
                            fullOutput.Append(chunk);
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

                var result = fullOutput.ToString();

                if (cmd.ExitStatus != 0)
                {
                    var error = cmd.Error;

                    if (throwOnError)
                    {
                        throw new Exception($"远程命令执行失败 (ExitCode: {cmd.ExitStatus}): {error ?? result}");
                    }
                }

                return result ?? string.Empty;
            }
            catch (ObjectDisposedException)
            {
                // 如果整个 cmd 都被释放了
                return fullOutput.ToString();
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
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 未连接，无法上传文件");
        }

        var normalizedRemotePath = NormalizeRemotePath(remotePath);
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
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 未连接，无法读取文件");
        }

        return await Task.Run(() =>
        {
            using (var remoteStream = _sftpClient.OpenRead(remotePath))
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
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 未连接，无法检查文件");
        }

        return await Task.Run(() => _sftpClient.Exists(remotePath), cancellationToken);
    }

    /// <summary>
    /// 创建远程目录
    /// </summary>
    public async Task MakeDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 未连接，无法创建目录");
        }

        await EnsureRemoteDirectoryExistsAsync(remotePath, cancellationToken);
    }

    private async Task EnsureRemoteDirectoryExistsAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            throw new InvalidOperationException("SFTP 未连接，无法创建目录");
        }

        var normalizedPath = NormalizeRemotePath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
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




















