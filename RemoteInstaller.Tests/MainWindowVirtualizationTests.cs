using System.IO;
using Xunit;

namespace RemoteInstaller.Tests;

public class MainWindowVirtualizationTests
{
    [Theory]
    [InlineData("x:Name=\"ServerListBox\"")]
    [InlineData("ItemsSource=\"{Binding FilteredTasks}\"")]
    public void LargeListBoxes_AreNotWrappedByOuterScrollViewer(string listMarker)
    {
        var source = ReadProjectFile("RemoteInstaller", "MainWindow.xaml");

        Assert.False(
            IsInsideScrollViewer(source, listMarker),
            $"{listMarker} is inside an outer ScrollViewer, which disables WPF item virtualization.");
    }

    private static bool IsInsideScrollViewer(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find marker: {marker}");

        var openingScrollViewer = source.LastIndexOf("<ScrollViewer", markerIndex, StringComparison.Ordinal);
        if (openingScrollViewer < 0)
        {
            return false;
        }

        var closingScrollViewer = source.LastIndexOf("</ScrollViewer>", markerIndex, StringComparison.Ordinal);
        return closingScrollViewer < openingScrollViewer;
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
