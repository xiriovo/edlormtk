// ============================================================================
// MultiFlash TOOL - Application Entry Point
// 应用程序入口 | アプリケーションエントリ | 애플리케이션 진입점
// ============================================================================
// [EN] Application startup with multi-language support initialization
//      Detects system language and loads appropriate resources
// [中文] 应用程序启动，初始化多语言支持
//       检测系统语言并加载相应资源
// [日本語] 多言語サポートの初期化を伴うアプリケーション起動
//         システム言語を検出し、適切なリソースをロード
// [한국어] 다국어 지원 초기화와 함께 애플리케이션 시작
//         시스템 언어 감지 및 적절한 리소스 로드
// [Español] Inicio de aplicación con inicialización de soporte multiidioma
//           Detecta el idioma del sistema y carga los recursos apropiados
// [Русский] Запуск приложения с инициализацией многоязычной поддержки
//           Определяет язык системы и загружает соответствующие ресурсы
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using tools.Localization;

namespace tools
{
    /// <summary>
    /// Application Entry - Multi-language support initialization
    /// 应用程序入口 - 多语言支持初始化
    /// アプリケーションエントリ - 多言語サポート初期化
    /// 애플리케이션 진입점 - 다국어 지원 초기화
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
