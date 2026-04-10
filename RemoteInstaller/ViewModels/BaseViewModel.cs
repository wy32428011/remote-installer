using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 基础 ViewModel
/// </summary>
public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    protected void SetBusy(bool busy)
    {
        IsBusy = busy;
        StatusMessage = busy ? "处理中..." : string.Empty;
    }

    protected void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    protected void ClearError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }
}
