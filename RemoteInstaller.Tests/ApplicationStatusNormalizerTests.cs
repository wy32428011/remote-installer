using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ApplicationStatusNormalizerTests
{
    [Fact]
    public void Normalize_RuntimeEvidenceMarksInstalledAndRunning()
    {
        var status = new ApplicationStatus
        {
            IsInstalled = false,
            IsRunning = false,
            InstalledVersion = string.Empty
        };
        var evidence = new ApplicationStatusEvidence { PortListening = true };

        ApplicationStatusNormalizer.Normalize(status, evidence);

        Assert.True(status.IsInstalled);
        Assert.True(status.IsRunning);
        Assert.Equal("未知", status.InstalledVersion);
    }

    [Fact]
    public void Normalize_ServiceResidueDoesNotMarkInstalled()
    {
        var status = new ApplicationStatus
        {
            IsInstalled = false,
            IsRunning = false,
            InstalledVersion = string.Empty
        };
        var evidence = new ApplicationStatusEvidence { ServiceOnlyResidue = true };

        ApplicationStatusNormalizer.Normalize(status, evidence);

        Assert.False(status.IsInstalled);
        Assert.False(status.IsRunning);
    }

    [Fact]
    public void Normalize_BinaryEvidenceMarksInstalledButNotRunning()
    {
        var status = new ApplicationStatus
        {
            IsInstalled = false,
            IsRunning = false,
            InstalledVersion = string.Empty
        };
        var evidence = new ApplicationStatusEvidence { BinaryFound = true };

        ApplicationStatusNormalizer.Normalize(status, evidence);

        Assert.True(status.IsInstalled);
        Assert.False(status.IsRunning);
        Assert.Equal("未知", status.InstalledVersion);
    }

    [Fact]
    public void BuildEvidenceFromProtocolEvents_ReadsResidueAndRunningFlags()
    {
        var events = ScriptProtocolParser.Parse(string.Join('\n', new[]
        {
            "RUNNING:true",
            "PORT:6379",
            "SERVICE_ONLY_STALE:false",
            "CONFIG_ONLY_RESIDUE:false"
        }));

        var evidence = ApplicationStatusNormalizer.BuildEvidence(events);

        Assert.True(evidence.ProcessFound);
        Assert.True(evidence.PortListening);
        Assert.False(evidence.ServiceOnlyResidue);
        Assert.False(evidence.ConfigOnlyResidue);
    }

    [Fact]
    public void InstallerService_DelegatesCheckOutputParsingToProtocolNormalizer()
    {
        var installerService = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");

        var parseCheckOutput = ExtractMethod(installerService, "private void ParseCheckOutput");
        var parseCombinedCheckOutput = ExtractMethod(installerService, "private void ParseCombinedCheckOutput");

        Assert.Contains("ScriptProtocolParser.Parse(output).ToList()", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.ApplyStatusEvents(status, events)", parseCheckOutput);
        Assert.Contains("ApplicationStatusNormalizer.Normalize(status, evidence)", parseCheckOutput);
        Assert.Contains("ParseCheckOutput(output, status);", parseCombinedCheckOutput);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到方法签名：{signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"未找到方法体起始：{signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        throw new InvalidOperationException($"未找到方法体结束：{signature}");
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var path = Path.Combine(GetProjectRoot(), Path.Combine(relativeParts));
        return File.ReadAllText(path);
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
