using System;
using System.IO;

namespace RemoteInstaller.Models
{
    /// <summary>
    /// 系统设置模型
    /// 用于存储和持久化系统级别的配置参数
    /// </summary>
    public class SystemSettings
    {
        #region 远程仓库配置

        /// <summary>
        /// 远程安装包仓库地址
        /// 用于从中央仓库下载应用安装包
        /// </summary>
        public string RepositoryUrl { get; set; } = string.Empty;

        /// <summary>
        /// 仓库认证 Token（可选）
        /// 用于访问私有仓库时的身份验证
        /// </summary>
        public string RepositoryToken { get; set; } = string.Empty;

        /// <summary>
        /// 客户端更新检测地址（可选）
        /// 为空时默认使用仓库地址下的 /api/version。
        /// </summary>
        public string UpdateCheckUrl { get; set; } = string.Empty;

        #endregion

        #region 网络代理设置

        /// <summary>
        /// 是否启用代理
        /// </summary>
        public bool UseProxy { get; set; } = false;

        /// <summary>
        /// 代理类型：0=None, 1=HTTP, 2=HTTPS, 3=SOCKS4, 4=SOCKS5
        /// </summary>
        public ProxyType ProxyType { get; set; } = ProxyType.None;

        /// <summary>
        /// 代理服务器地址
        /// </summary>
        public string ProxyHost { get; set; } = string.Empty;

        /// <summary>
        /// 代理服务器端口
        /// </summary>
        public int ProxyPort { get; set; } = 0;

        /// <summary>
        /// 代理用户名（可选）
        /// </summary>
        public string ProxyUsername { get; set; } = string.Empty;

        /// <summary>
        /// 代理密码（可选）
        /// </summary>
        public string ProxyPassword { get; set; } = string.Empty;

        #endregion

        #region 连接设置

        /// <summary>
        /// SSH 连接超时时间（秒）
        /// 默认 60 秒
        /// </summary>
        public int ConnectionTimeout { get; set; } = 60;

        /// <summary>
        /// 失败重试次数
        /// 默认 3 次
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 重试间隔时间（秒）
        /// 默认 5 秒
        /// </summary>
        public int RetryInterval { get; set; } = 5;

        #endregion

        #region 缓存设置

        /// <summary>
        /// 本地缓存目录
        /// 用于存储下载的安装包和临时文件
        /// </summary>
        public string CacheDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 缓存最大大小（MB）
        /// 0 表示无限制，默认 5000MB
        /// </summary>
        public long MaxCacheSizeMB { get; set; } = 5000;

        #endregion

        #region 并发设置

        /// <summary>
        /// 最大并发任务数
        /// 默认 3 个
        /// </summary>
        public int MaxConcurrentTasks { get; set; } = 3;

        #endregion

        #region 主题设置

        /// <summary>
        /// 当前主题：0=Dark, 1=Light
        /// </summary>
        public ThemeType CurrentTheme { get; set; } = ThemeType.Dark;

        #endregion

        #region 其他设置

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        #endregion

        /// <summary>
        /// 创建默认设置实例
        /// </summary>
        public static SystemSettings CreateDefault()
        {
            return new SystemSettings
            {
                RepositoryUrl = string.Empty,
                RepositoryToken = string.Empty,
                UpdateCheckUrl = string.Empty,
                UseProxy = false,
                ProxyType = ProxyType.None,
                ProxyHost = string.Empty,
                ProxyPort = 0,
                ProxyUsername = string.Empty,
                ProxyPassword = string.Empty,
                ConnectionTimeout = 60,
                RetryCount = 3,
                RetryInterval = 5,
                CacheDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteInstaller", "Cache"),
                MaxCacheSizeMB = 5000,
                MaxConcurrentTasks = 3,
                CurrentTheme = ThemeType.Dark,
                LastUpdated = DateTime.Now
            };
        }

        /// <summary>
        /// 深拷贝设置
        /// </summary>
        public SystemSettings Clone()
        {
            return (SystemSettings)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// 代理类型枚举
    /// </summary>
    public enum ProxyType
    {
        /// <summary>
        /// 不使用代理
        /// </summary>
        None = 0,

        /// <summary>
        /// HTTP 代理
        /// </summary>
        Http = 1,

        /// <summary>
        /// HTTPS 代理
        /// </summary>
        Https = 2,

        /// <summary>
        /// SOCKS4 代理
        /// </summary>
        Socks4 = 3,

        /// <summary>
        /// SOCKS5 代理
        /// </summary>
        Socks5 = 4
    }

    /// <summary>
    /// 主题类型枚举
    /// </summary>
    public enum ThemeType
    {
        /// <summary>
        /// 深色主题
        /// </summary>
        Dark = 0,

        /// <summary>
        /// 浅色主题
        /// </summary>
        Light = 1
    }
}
