using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class NginxOfflineInstallScriptTests
{
    [Fact]
    public void InstallLinuxScript_DoesNotRunGlobalDpkgConfigureAllDuringOfflineDebInstall()
    {
        var script = ReadProjectFile("RemoteInstaller", "Scripts", "Nginx", "install_linux.sh");

        Assert.DoesNotContain("--configure -a", script);
        Assert.Contains("dpkg \"${dpkg_opts[@]}\" -i \"${common_debs[@]}\"", script);
        Assert.Contains("dpkg \"${dpkg_opts[@]}\" -i \"${main_debs[@]}\"", script);
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
