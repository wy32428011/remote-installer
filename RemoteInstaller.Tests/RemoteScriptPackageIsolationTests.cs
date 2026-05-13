using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RemoteInstaller.Tests;

public class RemoteScriptPackageIsolationTests
{
    private static readonly Regex[] ProhibitedGlobalPackageCommands =
    {
        new(@"\bdpkg\b[^\r\n;|&]*--configure\s+-a", RegexOptions.IgnoreCase),
        new(@"\bapt(?:-get)?\b[^\r\n;|&]*(?:--fix-broken|-f\s+install|install\s+-f)", RegexOptions.IgnoreCase),
        new(@"\bapt(?:-get)?\b[^\r\n;|&]*\bautoremove\b", RegexOptions.IgnoreCase),
        new(@"\bapt(?:-get)?\b[^\r\n;|&]*\bautoclean\b", RegexOptions.IgnoreCase)
    };

    [Fact]
    public void LinuxScripts_DoNotRunGlobalPackageStateRepairOrCleanupCommands()
    {
        var scriptsRoot = Path.Combine(GetProjectRoot(), "RemoteInstaller", "Scripts");
        var offenders = Directory
            .EnumerateFiles(scriptsRoot, "*.sh", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindProhibitedCommands)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Scripts must not repair or clean global package-manager state because that can configure/remove unrelated middleware packages."
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ProductionCode_DoesNotRunGlobalPackageStateRepairCommands()
    {
        var sourceRoot = Path.Combine(GetProjectRoot(), "RemoteInstaller");
        var excludedDirectories = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}"
        };
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !excludedDirectories.Any(excluded => path.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(FindProhibitedCommands)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Production code must not execute package-manager repair commands because they can configure unrelated middleware packages."
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> FindProhibitedCommands(string path)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (ProhibitedGlobalPackageCommands.Any(regex => regex.IsMatch(line)))
            {
                yield return $"{Path.GetRelativePath(GetProjectRoot(), path)}:{i + 1}: {line}";
            }
        }
    }

    private static string GetProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RemoteInstaller.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 RemoteInstaller.sln，无法定位项目根目录。");
    }
}
