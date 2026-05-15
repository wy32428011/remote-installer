using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RemoteInstaller.Tests;

public class WpfAutomationIdCoverageTests
{
    public static TheoryData<string, string[]> CriticalAutomationIds => new()
    {
        {
            Path.Combine("RemoteInstaller", "MainWindow.xaml"),
            new[]
            {
                "{Binding Id, StringFormat=ApplicationInstallButton_{0}}",
                "{Binding Id, StringFormat=ApplicationConfigureButton_{0}}",
                "ScriptManagementTab",
                "ScriptManagementContentHost"
            }
        },
        {
            Path.Combine("RemoteInstaller", "Views", "Controls", "ScriptManagementControl.xaml"),
            new[]
            {
                "ScriptManagementRoot",
                "LocalScriptFolderTextBox",
                "BrowseScriptFolderButton",
                "RemoteScriptDirectoryTextBox",
                "LocalScriptFileTextBox",
                "BrowseScriptFileButton",
                "UploadSingleScriptButton",
                "LoadRemoteDirectoryButton",
                "RemoteDirectoryTreeView",
                "RefreshScriptListButton",
                "ScriptFilesListBox",
                "UploadScriptFolderButton",
                "ScriptContentTextBox",
                "SaveScriptButton"
            }
        },
        {
            Path.Combine("RemoteInstaller", "Views", "InstallConfigDialog.xaml"),
            new[]
            {
                "InstallConfigContentScrollViewer",
                "InstallVersionComboBox",
                "LocalResourceRadioButton",
                "OnlineInstallRadioButton",
                "LocalPackagePathTextBox",
                "MySqlLocalPackageFolderButton",
                "RefreshPackageDetectionButton",
                "JdkLocalPackagePathTextBox",
                "BrowseJdkLocalFolderButton",
                "RefreshJdkDetectionButton",
                "RemoteUploadPathTextBox",
                "InstallParameterItemsControl",
                "InstallConfigCancelButton",
                "InstallConfigConfirmButton"
            }
        },
        {
            Path.Combine("RemoteInstaller", "Views", "Dialogs", "ConfigEditorDialog.xaml"),
            new[]
            {
                "ConfigEditorToolbarSaveButton",
                "ConfigEditorToolbarSaveAndRestartButton",
                "ConfigEditorToolbarCloseButton",
                "ConfigEditorFileSelectorComboBox",
                "StructuredModeButton",
                "TextModeButton",
                "AddConfigItemButton",
                "RemoveSelectedConfigItemButton",
                "MemoryLimitTextBox",
                "ElasticsearchMemoryAdvancedModeCheckBox",
                "JvmXmsTextBox",
                "JvmXmxTextBox",
                "KeyValueConfigDataGrid",
                "YamlConfigTreeView",
                "RawConfigTextBox",
                "ConfigEditorSaveButton",
                "ConfigEditorSaveAndRestartButton",
                "ConfigEditorCloseButton"
            }
        }
    };

    [Theory]
    [MemberData(nameof(CriticalAutomationIds))]
    public void CriticalWpfControlsExposeStableAutomationIds(string relativePath, string[] automationIds)
    {
        var xaml = File.ReadAllText(Path.Combine(GetProjectRoot(), relativePath));

        foreach (var automationId in automationIds)
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"{automationId}\"", xaml);
        }
    }

    [Fact]
    public void CriticalWpfAutomationIdsAreUniqueAcrossCoveredViews()
    {
        var duplicateIds = CriticalAutomationIds
            .SelectMany(entry => (string[])entry[1])
            .GroupBy(automationId => automationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicateIds);
    }

    [Fact]
    public void CoveredWpfViewsDoNotReuseStaticAutomationIds()
    {
        var duplicateIds = CriticalAutomationIds
            .Select(entry => (string)entry[0])
            .Distinct(StringComparer.Ordinal)
            .SelectMany(relativePath => ExtractStaticAutomationIds(File.ReadAllText(Path.Combine(GetProjectRoot(), relativePath))))
            .GroupBy(automationId => automationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicateIds);
    }

    private static IEnumerable<string> ExtractStaticAutomationIds(string xaml)
    {
        const string prefix = "AutomationProperties.AutomationId=\"";
        var offset = 0;

        while (true)
        {
            var start = xaml.IndexOf(prefix, offset, StringComparison.Ordinal);
            if (start < 0)
            {
                yield break;
            }

            start += prefix.Length;
            var end = xaml.IndexOf('"', start);
            if (end < 0)
            {
                yield break;
            }

            var automationId = xaml[start..end];
            if (!automationId.StartsWith('{'))
            {
                yield return automationId;
            }

            offset = end + 1;
        }
    }

    private static string GetProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

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
