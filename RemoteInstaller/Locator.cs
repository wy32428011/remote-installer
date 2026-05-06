using RemoteInstaller.Services;
using RemoteInstaller.ViewModels;

namespace RemoteInstaller
{
    /// <summary>
    /// ViewModel Locator
    /// </summary>
    public static class Locator
    {
        // 使用单例模式，确保只创建一个 MainViewModel 实例
        private static MainViewModel _instance = null!;

        // 服务单例
        private static DatabaseService _databaseService = null!;
        private static SshService _sshService = null!;
        private static ILogger _logger = null!;
        private static AppConfigurationService _appConfigurationService = null!;
        private static ConfigurationService _configurationService = null!;
        private static HostStatusRefreshCoordinator _hostStatusRefreshCoordinator = null!;

        public static MainViewModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _logger = new LoggerService();
                    _databaseService = new DatabaseService();
                    _sshService = new SshService();
                    _configurationService = new ConfigurationService(_sshService, _logger);
                    _hostStatusRefreshCoordinator = new HostStatusRefreshCoordinator();
                    _appConfigurationService = new AppConfigurationService();
                    _instance = new MainViewModel(_sshService, _databaseService, _logger, _configurationService, _hostStatusRefreshCoordinator, _appConfigurationService);
                }
                return _instance;
            }
        }

        public static DatabaseService DatabaseService => _databaseService;
        public static SshService SshService => _sshService;
        public static ILogger Logger => _logger;
        public static AppConfigurationService AppConfigurationService => _appConfigurationService;
        public static ConfigurationService ConfigurationService => _configurationService;
    }
}
