using System.Windows;
using System.Windows.Controls;

namespace RemoteInstaller.Views.Controls;

public partial class ScriptManagementControl : UserControl
{
    private const double CompactLayoutThreshold = 1000;
    private bool _isCompactLayout;

    public ScriptManagementControl()
    {
        InitializeComponent();
        Loaded += ScriptManagementControl_Loaded;
        SizeChanged += ScriptManagementControl_SizeChanged;
    }

    private void ScriptManagementControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void ScriptManagementControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    // 根据可用宽度在左右双栏和上下堆叠之间切换，避免文件列表持续挤压脚本编辑区。
    private void UpdateResponsiveLayout()
    {
        var shouldUseCompactLayout = ActualWidth < CompactLayoutThreshold;
        if (shouldUseCompactLayout == _isCompactLayout)
        {
            return;
        }

        _isCompactLayout = shouldUseCompactLayout;

        if (shouldUseCompactLayout)
        {
            LeftColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            RightColumnDefinition.Width = new GridLength(0);
            PrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
            SecondaryRowDefinition.Height = new GridLength(1.25, GridUnitType.Star);

            Grid.SetRow(ScriptFilesPanel, 0);
            Grid.SetColumn(ScriptFilesPanel, 0);
            Grid.SetColumnSpan(ScriptFilesPanel, 2);
            ScriptFilesPanel.Margin = new Thickness(0, 0, 0, 12);

            Grid.SetRow(ScriptEditorPanel, 1);
            Grid.SetColumn(ScriptEditorPanel, 0);
            Grid.SetColumnSpan(ScriptEditorPanel, 2);
            ScriptEditorPanel.Margin = new Thickness(0);

            return;
        }

        LeftColumnDefinition.Width = new GridLength(268);
        RightColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        PrimaryRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        SecondaryRowDefinition.Height = new GridLength(0);

        Grid.SetRow(ScriptFilesPanel, 0);
        Grid.SetColumn(ScriptFilesPanel, 0);
        Grid.SetColumnSpan(ScriptFilesPanel, 1);
        ScriptFilesPanel.Margin = new Thickness(0, 0, 6, 0);

        Grid.SetRow(ScriptEditorPanel, 0);
        Grid.SetColumn(ScriptEditorPanel, 1);
        Grid.SetColumnSpan(ScriptEditorPanel, 1);
        ScriptEditorPanel.Margin = new Thickness(6, 0, 0, 0);
    }
}
