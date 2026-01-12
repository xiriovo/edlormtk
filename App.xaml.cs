using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using tools.Localization;

namespace tools
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// 应用程序入口 - 初始化多语言支持
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 调试：显示系统语言信息
            var culture = CultureInfo.CurrentUICulture;
            System.Diagnostics.Debug.WriteLine($"[Localization] System Culture: {culture.Name} ({culture.DisplayName})");
            System.Diagnostics.Debug.WriteLine($"[Localization] Two Letter Code: {culture.TwoLetterISOLanguageName}");
            
            // 初始化本地化 - 自动检测系统语言
            // Initialize localization - auto detect system language
            LocalizationManager.Initialize();
            
            System.Diagnostics.Debug.WriteLine($"[Localization] Current Language: {LocalizationManager.CurrentLanguage}");
        }
    }
}
