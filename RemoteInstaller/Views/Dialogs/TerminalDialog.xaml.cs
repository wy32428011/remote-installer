using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RemoteInstaller.Models;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller.Views.Dialogs;

public partial class TerminalDialog : Window
{
    private readonly TerminalViewModel _viewModel;
    private const int VisibleOutputHardLimit = 350_000;
    private const int ResizeDebounceMs = 200;
    private int _visibleDocumentLength;
    private System.Windows.Threading.DispatcherTimer? _resizeDebounceTimer;

    public TerminalDialog(TerminalViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.OutputAppended += OnOutputAppended;
        _viewModel.OutputReloadRequested += OnOutputReloadRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += TerminalDialog_Loaded;
        OutputRichTextBox.SizeChanged += OutputRichTextBox_SizeChanged;
        FileTreeView.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FileTreeViewItem_Expanded));
    }

    private void TerminalDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadVisibleOutput();
        ApplyOutputDocumentWidth();
        SyncTerminalSizeToViewModel();
        FocusCommandInput();
    }

    /// <summary>
    /// 输出区域尺寸变化时防抖同步 PTY 尺寸，避免拖拽缩放过程中高频发送 window-change。
    /// </summary>
    private void OutputRichTextBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_resizeDebounceTimer == null)
        {
            _resizeDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ResizeDebounceMs)
            };
            _resizeDebounceTimer.Tick += (_, _) =>
            {
                _resizeDebounceTimer!.Stop();
                ApplyOutputDocumentWidth();
                SyncTerminalSizeToViewModel();
            };
        }

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    /// <summary>
    /// 按当前输出区可视宽高和等宽字符尺寸计算终端列数/行数，并同步给 ViewModel。
    /// 远端 PTY 将按该列数排版，保证输出铺满整个可视区域而不是固定 120 列。
    /// </summary>
    private void SyncTerminalSizeToViewModel()
    {
        var cellSize = MeasureTerminalCellSize();
        if (cellSize.Width <= 0 || cellSize.Height <= 0)
        {
            return;
        }

        var viewportSize = GetEffectiveOutputViewportSize();

        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return;
        }

        var columns = (uint)Math.Max(1, Math.Floor(viewportSize.Width / cellSize.Width));
        var rows = (uint)Math.Max(1, Math.Floor(viewportSize.Height / cellSize.Height));
        _viewModel.UpdateTerminalSize(columns, rows);
    }

    /// <summary>
    /// 获取终端输出区的有效可视尺寸。
    /// 宽度优先使用控件实际宽度，避免 FlowDocument 旧页面宽度反向污染 ViewportWidth。
    /// </summary>
    private Size GetEffectiveOutputViewportSize()
    {
        var width = OutputRichTextBox.ActualWidth;
        if (double.IsNaN(width) || width <= 0)
        {
            width = OutputRichTextBox.ViewportWidth;
        }

        var height = OutputRichTextBox.ViewportHeight;
        if (double.IsNaN(height) || height <= 0)
        {
            height = OutputRichTextBox.ActualHeight;
        }

        width -= OutputRichTextBox.Padding.Left + OutputRichTextBox.Padding.Right;
        height -= OutputRichTextBox.Padding.Top + OutputRichTextBox.Padding.Bottom;

        if (double.IsNaN(width) || width <= 0)
        {
            width = 1;
        }

        if (double.IsNaN(height) || height <= 0)
        {
            height = 1;
        }

        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>
    /// 将当前文档宽度同步到终端输出区，保证 FlowDocument 按窗口宽度重排。
    /// </summary>
    private void ApplyOutputDocumentWidth()
    {
        if (OutputRichTextBox.Document == null)
        {
            return;
        }

        ApplyOutputDocumentWidth(OutputRichTextBox.Document);
        OutputRichTextBox.InvalidateMeasure();
    }

    /// <summary>
    /// 设置 FlowDocument 的页面宽度和列宽，避免 RichTextBox 使用默认窄页面宽度。
    /// </summary>
    private void ApplyOutputDocumentWidth(FlowDocument document)
    {
        var width = GetEffectiveOutputViewportSize().Width;

        if (document.MinPageWidth > width)
        {
            document.MinPageWidth = width;
        }

        if (document.MaxPageWidth < width)
        {
            document.MaxPageWidth = width;
        }

        document.PageWidth = width;
        document.ColumnWidth = width;
        document.ColumnGap = 0;
        document.IsColumnWidthFlexible = false;
        document.MinPageWidth = width;
        document.MaxPageWidth = width;
    }

    /// <summary>
    /// 测量终端等宽字体的单字符渲染尺寸。
    /// 行高取文档段落 LineHeight（18）与字体实际高度的较大值，与 CreateDocument 保持一致。
    /// </summary>
    private Size MeasureTerminalCellSize()
    {
        var typeface = new Typeface(
            OutputRichTextBox.FontFamily,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var formatted = new FormattedText(
            "W",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            OutputRichTextBox.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        const double documentLineHeight = 18d;
        return new Size(
            formatted.WidthIncludingTrailingWhitespace,
            Math.Max(formatted.Height, documentLineHeight));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ViewModel_PropertyChanged(sender, e)));
            return;
        }

        if (e.PropertyName == nameof(TerminalViewModel.CurrentCommand) &&
            CommandInput.Text != _viewModel.CurrentCommand)
        {
            CommandInput.Text = _viewModel.CurrentCommand;
            CommandInput.CaretIndex = CommandInput.Text.Length;
            return;
        }

        if (e.PropertyName == nameof(TerminalViewModel.IsExecuting) && !_viewModel.IsExecuting)
        {
            _ = Dispatcher.BeginInvoke(new Action(FocusCommandInput), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// 追加终端渲染片段。
    /// 这里只负责把 ViewModel 产出的样式片段映射到 RichTextBox，
    /// 不在视图层重复做 ANSI 解析。
    /// </summary>
    private void OnOutputAppended(IReadOnlyList<TerminalOutputSpan> spans)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnOutputAppended(spans)));
            return;
        }

        if (spans.Count == 0)
        {
            return;
        }

        var incomingLength = GetSpanTextLength(spans);
        var visibleLimit = _viewModel.AutoScroll ? _viewModel.VisibleOutputLimit : VisibleOutputHardLimit;
        if (_visibleDocumentLength + incomingLength > visibleLimit)
        {
            ReloadVisibleOutput();
            return;
        }

        AppendSpans(spans);
        _visibleDocumentLength += incomingLength;

        if (_viewModel.AutoScroll)
        {
            OutputRichTextBox.ScrollToEnd();
        }
    }

    private void OnOutputReloadRequested()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(OnOutputReloadRequested));
            return;
        }

        ReloadVisibleOutput();
    }

    private void ReloadVisibleOutput()
    {
        var spans = _viewModel.VisibleRenderSpans;
        var document = CreateDocument(spans);
        OutputRichTextBox.Document = document;
        _visibleDocumentLength = GetSpanTextLength(spans);

        if (_viewModel.AutoScroll)
        {
            OutputRichTextBox.ScrollToEnd();
        }
    }

    private void AppendSpans(IReadOnlyList<TerminalOutputSpan> spans)
    {
        var paragraph = EnsureOutputParagraph();
        OutputRichTextBox.BeginChange();
        try
        {
            foreach (var span in spans)
            {
                if (string.IsNullOrEmpty(span.Text))
                {
                    continue;
                }

                paragraph.Inlines.Add(CreateRun(span));
            }
        }
        finally
        {
            OutputRichTextBox.EndChange();
        }
    }

    private Paragraph EnsureOutputParagraph()
    {
        if (OutputRichTextBox.Document?.Blocks.FirstBlock is Paragraph paragraph)
        {
            return paragraph;
        }

        var document = CreateDocument([]);
        OutputRichTextBox.Document = document;
        return (Paragraph)document.Blocks.FirstBlock!;
    }

    private FlowDocument CreateDocument(IReadOnlyList<TerminalOutputSpan> spans)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 18
        };

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            paragraph.Inlines.Add(CreateRun(span));
        }

        var document = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent,
            ColumnGap = 0,
            IsColumnWidthFlexible = false
        };

        ApplyOutputDocumentWidth(document);
        return document;
    }

    private Run CreateRun(TerminalOutputSpan span)
    {
        var run = new Run(span.Text)
        {
            Foreground = ResolveBrush(span.ForegroundKey, OutputRichTextBox.Foreground),
            FontWeight = span.IsBold ? FontWeights.SemiBold : FontWeights.Normal
        };

        if (!string.IsNullOrWhiteSpace(span.BackgroundKey))
        {
            run.Background = ResolveBrush(span.BackgroundKey, null);
        }

        return run;
    }

    private Brush? ResolveBrush(string? resourceKey, Brush? fallbackBrush)
    {
        if (!string.IsNullOrWhiteSpace(resourceKey) && TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return fallbackBrush;
    }

    /// <summary>
    /// TreeView 展开时按需懒加载子目录。
    /// 这里保留少量 code-behind，仅做 WPF 事件到 ViewModel 命令的桥接。
    /// </summary>
    private async void FileTreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is DirectoryItem dirItem)
        {
            if (dirItem.IsDirectory && !dirItem.IsLoaded && _viewModel.FilePane != null)
            {
                await _viewModel.FilePane.LoadChildrenCommand.ExecuteAsync(dirItem);
            }

            e.Handled = true;
        }
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel.FilePane != null && e.NewValue is DirectoryItem selected)
        {
            _viewModel.FilePane.SelectedItem = selected;
        }
    }

    private async void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.FilePane?.OpenSelectedDirectoryCommand.CanExecute(null) == true)
        {
            await _viewModel.FilePane.OpenSelectedDirectoryCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private static int GetSpanTextLength(IReadOnlyList<TerminalOutputSpan> spans)
    {
        var totalLength = 0;
        foreach (var span in spans)
        {
            totalLength += span.Text.Length;
        }

        return totalLength;
    }

    private void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_viewModel.IsExecuting)
        {
            _viewModel.ExecuteCommandCommand.Execute(null);
            FocusCommandInput();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            _viewModel.NavigateHistoryUp();
            CommandInput.CaretIndex = CommandInput.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            _viewModel.NavigateHistoryDown();
            CommandInput.CaretIndex = CommandInput.Text.Length;
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            _viewModel.ReconnectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.ClearOutputCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.IsExecuting)
        {
            if (OutputRichTextBox.IsKeyboardFocusWithin && !OutputRichTextBox.Selection.IsEmpty)
            {
                return;
            }

            _viewModel.CancelCommandCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && !CommandInput.IsKeyboardFocusWithin)
        {
            FocusCommandInput();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 将键盘焦点放回终端命令行输入区域，并把光标移动到输入末尾。
    /// </summary>
    private void FocusCommandInput()
    {
        if (!CommandInput.IsVisible)
        {
            return;
        }

        CommandInput.Focus();
        Keyboard.Focus(CommandInput);
        CommandInput.CaretIndex = CommandInput.Text.Length;
    }

    private void TerminalCommandLine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FocusCommandInput();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DisconnectAsync();
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        _resizeDebounceTimer?.Stop();
        OutputRichTextBox.SizeChanged -= OutputRichTextBox_SizeChanged;
        _viewModel.OutputAppended -= OnOutputAppended;
        _viewModel.OutputReloadRequested -= OnOutputReloadRequested;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        await _viewModel.DisconnectAsync();
        base.OnClosing(e);
    }
}
