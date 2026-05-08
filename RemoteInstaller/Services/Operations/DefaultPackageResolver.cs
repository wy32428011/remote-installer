using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class DefaultPackageResolver : IPackageResolver
{
    private readonly Func<IEnumerable<string>> _scriptRootsFactory;

    public DefaultPackageResolver(Func<IEnumerable<string>>? scriptRootsFactory = null)
    {
        _scriptRootsFactory = scriptRootsFactory ?? DefaultScriptRoots;
    }

    public PackageResolution Resolve(ApplicationInfo application, RemoteHost host)
    {
        if (IsRabbitMq(application))
        {
            return ResolveRabbitMq(application, host);
        }

        return PackageResolution.NotFound($"当前应用未配置自动本地资源解析：{application.Name}");
    }

    private static bool IsRabbitMq(ApplicationInfo application)
    {
        return string.Equals(application.Id, "rabbitmq", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(application.Name, "RabbitMQ", StringComparison.OrdinalIgnoreCase);
    }

    private PackageResolution ResolveRabbitMq(ApplicationInfo application, RemoteHost host)
    {
        var offlineFolder = host.OsType switch
        {
            OperatingSystemType.CentOS => "rabbitmq-centos7",
            OperatingSystemType.Ubuntu => "rabbitmq-ubuntu",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(offlineFolder))
        {
            return PackageResolution.NotFound("当前系统未配置 RabbitMQ 本地资源目录。");
        }

        var packagePattern = host.OsType == OperatingSystemType.CentOS
            ? "rabbitmq-server-*.el7*.rpm"
            : "rabbitmq-server*.deb";

        foreach (var root in ResolveRabbitMqRoots(offlineFolder))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                if (host.OsType == OperatingSystemType.Ubuntu)
                {
                    var missingDependencies = FindMissingRabbitMqUbuntuDependencies(root);
                    if (missingDependencies.Length > 0)
                    {
                        return PackageResolution.NotFound(
                            $"RabbitMQ Ubuntu 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。",
                            missingDependencies);
                    }
                }

                return PackageResolution.FoundPackage(
                    root,
                    application.Version,
                    $"已从 Scripts 目录自动匹配 RabbitMQ 本地资源目录：{root}");
            }
            catch
            {
                // Ignore one invalid candidate and keep trying other configured roots.
            }
        }

        return PackageResolution.NotFound($"未在 Scripts/RabbitMQ/{offlineFolder} 中找到可用本地资源。");
    }

    private IEnumerable<string> ResolveRabbitMqRoots(string offlineFolder)
    {
        return _scriptRootsFactory()
            .Select(root => Path.Combine(root, "RabbitMQ", offlineFolder))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string[] FindMissingRabbitMqUbuntuDependencies(string root)
    {
        var dependencyFileNames = new[] { Path.Combine(root, "deps"), root }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.deb", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var requiredDebPrefixes = new[] { "erlang-base", "logrotate" };
        return requiredDebPrefixes
            .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IEnumerable<string> DefaultScriptRoots()
    {
        return new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts")
        }
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
