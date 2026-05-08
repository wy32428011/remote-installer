using System;
using System.IO;
using System.Text.Json;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ManifestValidationGuardrailTests
{
    [Fact]
    public void AppConfiguration_DeclaredLinuxScriptsResolveToBundledFiles()
    {
        var projectRoot = GetProjectRoot();
        using var document = JsonDocument.Parse(ReadProjectFile("Scripts", "app-configuration.json"));
        var missing = new List<string>();

        foreach (var app in document.RootElement.GetProperty("applications").EnumerateArray())
        {
            var id = app.GetProperty("id").GetString() ?? string.Empty;
            var scripts = app.GetProperty("scripts");
            foreach (var operationName in new[] { "install", "uninstall", "detect" })
            {
                var linuxScript = scripts
                    .GetProperty(operationName)
                    .GetProperty("linux")
                    .GetString() ?? string.Empty;

                var token = ScriptResolver.ExtractScriptReferenceToken(linuxScript, ".sh");
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var expectedPath = Path.Combine(
                    projectRoot,
                    "RemoteInstaller",
                    token.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(expectedPath))
                {
                    missing.Add($"{id}:{operationName}:{token}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "配置声明的 Linux 脚本必须存在：" + Environment.NewLine + string.Join(Environment.NewLine, missing));
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
