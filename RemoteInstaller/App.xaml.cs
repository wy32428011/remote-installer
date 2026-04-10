using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using MaterialDesignColors;
using RemoteInstaller.Models;

namespace RemoteInstaller
{
    /// <summary>
    /// App.xaml 的后台逻辑
    /// 包含应用启动级初始化和全局功能实现
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 设置 DPI 感知模式（必须在创建任何窗口之前设置）
            // 使用 PerMonitorV2 DPI 模式，支持每显示器 DPI
            try
            {
                // 设置 WPF 渲染模式为硬件加速
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            }
            catch
            {
                // 忽略渲染模式设置异常
            }

            // 设置控制台输出编码为 UTF-8，防止日志乱码
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // 忽略设置编码时的异常（可能在没有控制台的环境下运行）
            }

            base.OnStartup(e);
        }

        /// <summary>
        /// 获取当前应用程序实例
        /// </summary>
        public static App CurrentApp => Current as App;

        /// <summary>
        /// 当前应用的主题类型
        /// </summary>
        public static ThemeType CurrentThemeType { get; private set; } = ThemeType.Dark;

        /// <summary>
        /// 主题切换事件
        /// </summary>
        public static event EventHandler<ThemeType>? ThemeChanged;

        /// <summary>
        /// 切换主题
        /// </summary>
        /// <param name="theme">要切换到的主题类型</param>
        public static void SwitchTheme(ThemeType theme)
        {
            if (CurrentThemeType == theme) return;

            CurrentThemeType = theme;

            try
            {
                // 使用 MaterialDesign 的 PaletteHelper 切换主题
                var paletteHelper = new PaletteHelper();
                var themeManager = paletteHelper.GetTheme();

                // 设置 MaterialDesign BaseTheme
                if (theme == ThemeType.Dark)
                {
                    themeManager.SetBaseTheme(BaseTheme.Dark);
                }
                else
                {
                    themeManager.SetBaseTheme(BaseTheme.Light);
                }

                paletteHelper.SetTheme(themeManager);

                // 切换自定义主题资源字典
                var resources = Current?.Resources;
                if (resources != null && resources.MergedDictionaries != null)
                {
                    // 移除现有的自定义主题资源字典
                    for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
                    {
                        var dict = resources.MergedDictionaries[i];
                        var source = dict.Source?.ToString();
                        // 移除主题资源字典（DarkTheme、LightTheme、LightCustomColors）
                        if (source?.Contains("DarkTheme.xaml") == true ||
                            source?.Contains("LightCustomColors.xaml") == true ||
                            source?.Contains("LightTheme.xaml") == true)
                        {
                            resources.MergedDictionaries.RemoveAt(i);
                        }
                    }

                    // 添加新的主题资源到 Application.Resources
                    // 将主题字典插入到共享样式之前，确保共享样式可以统一覆盖控件外观。
                    string themePath = theme == ThemeType.Dark
                        ? "Themes/DarkTheme.xaml"
                        : "Themes/LightTheme.xaml";

                    var themeUri = new Uri($"pack://application:,,,/RemoteInstaller;component/{themePath}");
                    var themeDictionary = new ResourceDictionary { Source = themeUri };
                    var insertIndex = resources.MergedDictionaries.Count;

                    for (int i = 0; i < resources.MergedDictionaries.Count; i++)
                    {
                        var source = resources.MergedDictionaries[i].Source?.ToString();
                        if (source?.Contains("SharedTheme.xaml") == true ||
                            source?.Contains("CustomStyles.xaml") == true)
                        {
                            insertIndex = i;
                            break;
                        }
                    }

                    resources.MergedDictionaries.Insert(insertIndex, themeDictionary);
                }

                // 强制刷新所有窗口
                var mainWindow = Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.InvalidateVisual();
                    mainWindow.UpdateLayout();
                    RefreshAllChildren(mainWindow);
                }

                // 触发主题切换事件
                ThemeChanged?.Invoke(Current, theme);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"主题切换失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 递归刷新所有子元素的视觉效果
        /// </summary>
        /// <param name="dependencyObject">要刷新的依赖对象</param>
        private static void RefreshAllChildren(DependencyObject dependencyObject)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(dependencyObject);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(dependencyObject, i);
                if (child is FrameworkElement frameworkElement)
                {
                    frameworkElement.InvalidateVisual();
                    frameworkElement.UpdateLayout();
                }
                RefreshAllChildren(child);
            }
        }
    }
}
