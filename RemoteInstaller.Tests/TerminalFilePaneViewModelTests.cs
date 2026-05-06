using RemoteInstaller.Models;
using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;
using Xunit;

namespace RemoteInstaller.Tests;

public class TerminalFilePaneViewModelTests
{
    [Fact]
    public async Task InitializeAsync_UsesResolvedHomePathForCurrentDirectory()
    {
        var sshService = new FakeSshService();
        var viewModel = CreateViewModel(sshService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsSftpAvailable);
        Assert.Equal("/home/app", viewModel.CurrentRemotePath);
        Assert.Equal("/home/app", viewModel.PathInput);
        Assert.Single(viewModel.DirectoryItems);
        Assert.Contains(viewModel.DirectoryItems[0].Children, item => item.Name == "logs");
    }

    [Fact]
    public async Task GoToPathCommand_LoadsTypedRemoteDirectory()
    {
        var sshService = new FakeSshService();
        var viewModel = CreateViewModel(sshService);
        await viewModel.InitializeAsync();

        viewModel.PathInput = "/var/log";
        await viewModel.GoToPathCommand.ExecuteAsync(null);

        Assert.Equal("/var/log", viewModel.CurrentRemotePath);
        Assert.Equal("/var/log", viewModel.PathInput);
        Assert.Contains(viewModel.DirectoryItems[0].Children, item => item.Name == "messages");
    }

    [Fact]
    public async Task OpenSelectedDirectoryCommand_EntersSelectedDirectory()
    {
        var sshService = new FakeSshService();
        var viewModel = CreateViewModel(sshService);
        await viewModel.InitializeAsync();

        var selectedDirectory = viewModel.DirectoryItems[0].Children.Single(item => item.Name == "logs");
        viewModel.SelectedItem = selectedDirectory;

        await viewModel.OpenSelectedDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/home/app/logs", viewModel.CurrentRemotePath);
        Assert.Equal("/home/app/logs", viewModel.PathInput);
        Assert.Contains(viewModel.DirectoryItems[0].Children, item => item.Name == "app.log");
    }

    private static TerminalFilePaneViewModel CreateViewModel(SshService sshService)
    {
        return new TerminalFilePaneViewModel(
            sshService,
            new RemoteHost
            {
                Name = "测试主机",
                IpAddress = "127.0.0.1",
                Username = "app"
            });
    }

    private sealed class FakeSshService : SshService
    {
        private readonly Dictionary<string, IReadOnlyList<RemoteFileEntry>> _directories = new(StringComparer.Ordinal)
        {
            ["/home/app"] =
            [
                Directory("logs", "/home/app/logs"),
                File("readme.txt", "/home/app/readme.txt", 128)
            ],
            ["/home/app/logs"] =
            [
                File("app.log", "/home/app/logs/app.log", 2048)
            ],
            ["/var/log"] =
            [
                File("messages", "/var/log/messages", 4096)
            ]
        };

        public override Task<bool> IsSftpAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public override Task<RemoteFileEntry?> GetEntryAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            var resolvedPath = Resolve(remotePath);
            var name = resolvedPath == "/" ? "/" : resolvedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? resolvedPath;

            return Task.FromResult<RemoteFileEntry?>(new RemoteFileEntry
            {
                Name = name,
                RemotePath = resolvedPath,
                IsDirectory = _directories.ContainsKey(resolvedPath)
            });
        }

        public override Task<IReadOnlyList<RemoteFileEntry>> ListDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            var resolvedPath = Resolve(remotePath);
            return Task.FromResult(_directories.TryGetValue(resolvedPath, out var entries)
                ? entries
                : Array.Empty<RemoteFileEntry>());
        }

        private static string Resolve(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath) || remotePath == "~")
            {
                return "/home/app";
            }

            var normalized = remotePath.Replace('\\', '/').Trim();
            if (normalized.Length > 1)
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        private static RemoteFileEntry Directory(string name, string remotePath)
        {
            return new RemoteFileEntry
            {
                Name = name,
                RemotePath = remotePath,
                IsDirectory = true
            };
        }

        private static RemoteFileEntry File(string name, string remotePath, long size)
        {
            return new RemoteFileEntry
            {
                Name = name,
                RemotePath = remotePath,
                IsDirectory = false,
                Size = size
            };
        }
    }
}
