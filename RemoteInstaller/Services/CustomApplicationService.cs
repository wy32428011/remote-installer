using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// Handles custom application upload, start, and config loading.
/// </summary>
public class CustomApplicationService
{
    private readonly SshService _sshService;
    private readonly ConfigurationService _configurationService;

    public CustomApplicationService(SshService sshService, ConfigurationService configurationService)
    {
        _sshService = sshService;
        _configurationService = configurationService;
    }

    public async Task<string> UploadApplicationAsync(
        RemoteHost host,
        string localSourcePath,
        string remoteDirectory,
        bool preserveTopLevelDirectory,
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localSourcePath))
        {
            throw new ArgumentException("Select a local file or directory first.", nameof(localSourcePath));
        }

        if (string.IsNullOrWhiteSpace(remoteDirectory))
        {
            throw new ArgumentException("Enter a remote target directory first.", nameof(remoteDirectory));
        }

        await _sshService.ConnectAsync(host, cancellationToken);

        if (File.Exists(localSourcePath))
        {
            var fileName = Path.GetFileName(localSourcePath);
            var remoteFilePath = CombineRemotePath(remoteDirectory, fileName);
            await _sshService.UploadFileAsync(localSourcePath, remoteFilePath, onProgress, cancellationToken);
            return remoteFilePath;
        }

        if (Directory.Exists(localSourcePath))
        {
            var localDirectory = Path.GetFullPath(localSourcePath);
            var directoryInfo = new DirectoryInfo(localDirectory);
            var remoteTargetDirectory = preserveTopLevelDirectory
                ? CombineRemotePath(remoteDirectory, directoryInfo.Name)
                : remoteDirectory;

            await _sshService.UploadDirectoryAsync(localDirectory, remoteTargetDirectory, onProgress, cancellationToken);
            return remoteTargetDirectory;
        }

        throw new FileNotFoundException($"Local path was not found: {localSourcePath}");
    }

    public async Task<string> StartApplicationAsync(RemoteHost host, string startCommand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(startCommand))
        {
            throw new ArgumentException("Enter a start command first.", nameof(startCommand));
        }

        await _sshService.ConnectAsync(host, cancellationToken);
        return await _sshService.ExecuteCommandAsync(
            startCommand,
            cancellationToken: cancellationToken,
            throwOnError: false);
    }

    public async Task<string> LoadConfigContentAsync(RemoteHost host, string configFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            throw new ArgumentException("Enter a config file path first.", nameof(configFilePath));
        }

        await _sshService.ConnectAsync(host, cancellationToken);
        if (!await _sshService.FileExistsAsync(configFilePath, cancellationToken))
        {
            throw new FileNotFoundException($"Remote config file was not found: {configFilePath}");
        }

        return await _configurationService.ReadConfigAsync(configFilePath, cancellationToken);
    }

    public async Task SaveConfigContentAsync(RemoteHost host, string configFilePath, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            throw new ArgumentException("Enter a config file path first.", nameof(configFilePath));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty.", nameof(content));
        }

        await _sshService.ConnectAsync(host, cancellationToken);

        // Create the parent directory if needed
        var parentDir = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            var mkdirCmd = host.OsType == OperatingSystemType.Windows
                ? $"if not exist \"{parentDir}\" mkdir \"{parentDir}\""
                : $"mkdir -p \"{parentDir}\"";
            await _sshService.ExecuteCommandAsync(mkdirCmd, cancellationToken: cancellationToken, throwOnError: false);
        }

        // Write content via SFTP
        await _sshService.UploadTextAsync(content, configFilePath, host.OsType, cancellationToken);
    }

    public async Task UploadFileAsync(
        RemoteHost host,
        string localFilePath,
        string remoteFilePath,
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException("Enter a local file path first.", nameof(localFilePath));
        }

        if (string.IsNullOrWhiteSpace(remoteFilePath))
        {
            throw new ArgumentException("Enter a remote file path first.", nameof(remoteFilePath));
        }

        await _sshService.ConnectAsync(host, cancellationToken);
        await _sshService.UploadFileAsync(localFilePath, remoteFilePath, onProgress, cancellationToken);
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

        return normalizedBasePath.EndsWith("/", StringComparison.Ordinal)
            ? normalizedBasePath + normalizedChildPath
            : $"{normalizedBasePath}/{normalizedChildPath}";
    }

    private static string NormalizeRemotePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }
}
