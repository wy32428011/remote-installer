using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
    }

    private void TerminalDialog_Loaded(object sender, RoutedEventArgs e)
    {
        OutputTextBox.BeginChange();
        try
        {
            OutputTextBox.Text = _viewModel.VisibleOutputSnapshot;
        }
        finally
        {
            OutputTextBox.EndChange();
        }

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

    private void OnOutputAppended(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var visibleLimit = _viewModel.AutoScroll ? _viewModel.VisibleOutputLimit : VisibleOutputHardLimit;
        var currentLength = OutputTextBox.Text?.Length ?? 0;
        if (currentLength + text.Length > visibleLimit)
        {
            ReloadVisibleOutput();
            return;
        }

        OutputTextBox.BeginChange();
        try
        {
            OutputTextBox.AppendText(text);
        }
        finally
        {
            OutputTextBox.EndChange();
        }

        if (_viewModel.AutoScroll)
        {
            OutputTextBox.ScrollToEnd();
        }
    }

    private void OnOutputReloadRequested()
    {
        ReloadVisibleOutput();
    }

    private void ReloadVisibleOutput()
    {
        OutputTextBox.BeginChange();
        try
        {
            OutputTextBox.Text = _viewModel.VisibleOutputSnapshot;
        }
        finally
        {
            OutputTextBox.EndChange();
        }

        if (_viewModel.AutoScroll)
        {
            OutputTextBox.ScrollToEnd();
        }
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
