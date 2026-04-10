using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteInstaller.ViewModels
{
    /// <summary>
    /// 应用卡片 ViewModel
    /// </summary>
    public partial class ApplicationCardViewModel : ObservableObject
    {
        private readonly MainViewModel _mainViewModel;

        // 命令缓存，避免每次访问都创建新实例
        private ICommand? _installCommand;
        private ICommand? _uninstallCommand;
        private ICommand? _checkStatusCommand;
        private ICommand? _configureCommand;
        private ICommand? _selectCommand;

        public ApplicationCardViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _version = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _icon = string.Empty;

        [ObservableProperty]
        private string _category = string.Empty;

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isSupported;

        [ObservableProperty]
        private string _osType = string.Empty;

        [ObservableProperty]
        private string _installedVersion;

        [ObservableProperty]
        private System.Collections.Generic.List<string> _versions = new();

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// 安装命令（缓存实例避免重复创建）
        /// </summary>
        public ICommand InstallCommand =>
            _installCommand ??= new RelayCommand(
                () => _mainViewModel.InstallApplicationCommand?.Execute(this),
                () => _mainViewModel.InstallApplicationCommand?.CanExecute(this) ?? false);

        /// <summary>
        /// 卸载命令（缓存实例避免重复创建）
        /// </summary>
        public ICommand UninstallCommand =>
            _uninstallCommand ??= new RelayCommand(
                () => _mainViewModel.UninstallApplicationCommand?.Execute(this),
                () => _mainViewModel.UninstallApplicationCommand?.CanExecute(this) ?? false);

        /// <summary>
        /// 检测状态命令（缓存实例避免重复创建）
        /// </summary>
        public ICommand CheckStatusCommand =>
            _checkStatusCommand ??= new RelayCommand(
                () => _mainViewModel.CheckApplicationStatusCommand?.Execute(this),
                () => _mainViewModel.CheckApplicationStatusCommand?.CanExecute(this) ?? false);

        /// <summary>
        /// 配置命令（缓存实例避免重复创建）
        /// </summary>
        public ICommand ConfigureCommand =>
            _configureCommand ??= new RelayCommand(
                () => _mainViewModel.ConfigureApplicationCommand?.Execute(this),
                () => _mainViewModel.ConfigureApplicationCommand?.CanExecute(this) ?? false);

        /// <summary>
        /// 选中命令，用于批量安装目标多选
        /// </summary>
        public ICommand SelectCommand =>
            _selectCommand ??= new RelayCommand(
                () => _mainViewModel.ToggleApplicationSelectionCommand?.Execute(this));
    }
}
