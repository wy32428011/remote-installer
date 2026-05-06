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

    public TerminalDialog(TerminalViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.OutputAppended += OnOutputAppended;
        _viewModel.OutputReloadRequested += OnOutputReloadRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += TerminalDialog_Loaded;
        FileTreeView.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FileTreeViewItem_Expanded));
    }

    private void TerminalDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadVisibleOutput();
        CommandInput.Focus();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.CurrentCommand) &&
            CommandInput.Text != _viewModel.CurrentCommand)
        {
            CommandInput.Text = _viewModel.CurrentCommand;
            CommandInput.CaretIndex = CommandInput.Text.Length;
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

        var currentLength = GetVisibleDocumentLength();
        var incomingLength = GetSpanTextLength(spans);
        var visibleLimit = _viewModel.AutoScroll ? _viewModel.VisibleOutputLimit : VisibleOutputHardLimit;
        if (currentLength + incomingLength > visibleLimit)
        {
            ReloadVisibleOutput();
            return;
        }

        AppendSpans(spans);

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
        var document = CreateDocument(_viewModel.VisibleRenderSpans);
        OutputRichTextBox.Document = document;

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

        return new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent
        };
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

    private int GetVisibleDocumentLength()
    {
        if (OutputRichTextBox.Document == null)
        {
            return 0;
        }

        var range = new TextRange(OutputRichTextBox.Document.ContentStart, OutputRichTextBox.Document.ContentEnd);
        return range.Text.Length;
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
            _viewModel.CancelCommandCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && !CommandInput.IsKeyboardFocusWithin)
        {
            CommandInput.Focus();
            CommandInput.CaretIndex = CommandInput.Text.Length;
            e.Handled = true;
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DisconnectAsync();
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        _viewModel.OutputAppended -= OnOutputAppended;
        _viewModel.OutputReloadRequested -= OnOutputReloadRequested;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        await _viewModel.DisconnectAsync();
        base.OnClosing(e);
    }
}
