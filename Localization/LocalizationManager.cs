// ============================================================================
// MultiFlash TOOL - Localization Manager
// 多语言管理器 | 多言語マネージャー | 다국어 관리자
// ============================================================================
// [EN] Manages application language based on system culture
// [中文] 根据系统区域设置管理应用程序语言
// [日本語] システムカルチャに基づいてアプリケーション言語を管理
// [한국어] 시스템 문화권에 따라 애플리케이션 언어를 관리
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace tools.Localization
{
    /// <summary>
    /// Language Manager - Auto-detect system language and switch UI
    /// 语言管理器 - 自动检测系统语言并切换界面
    /// </summary>
    public static class LocalizationManager
    {
        // 支持的语言列表 / Supported languages
        public static readonly Dictionary<string, string> SupportedLanguages = new Dictionary<string, string>
        {
            { "zh-CN", "简体中文" },
            { "zh-TW", "繁體中文" },
            { "en-US", "English" },
            { "ja-JP", "日本語" },
            { "ko-KR", "한국어" },
            { "ru-RU", "Русский" },
            { "es-ES", "Español" }
        };

        // 当前语言 / Current language
        private static string _currentLanguage = "zh-CN";
        public static string CurrentLanguage => _currentLanguage;

        // 语言改变事件 / Language changed event
        public static event EventHandler<string> LanguageChanged;

        /// <summary>
        /// Initialize localization - auto detect system language
        /// 初始化本地化 - 自动检测系统语言
        /// </summary>
        public static void Initialize()
        {
            var systemCulture = CultureInfo.CurrentUICulture;
            var languageCode = GetBestMatchLanguage(systemCulture);
            SetLanguage(languageCode);
        }

        /// <summary>
        /// Get best matching language for the system culture
        /// 获取与系统文化最匹配的语言
        /// </summary>
        private static string GetBestMatchLanguage(CultureInfo culture)
        {
            // 完全匹配 / Exact match
            var fullCode = culture.Name;
            if (SupportedLanguages.ContainsKey(fullCode))
                return fullCode;

            // 语言匹配（不含地区）/ Language match (without region)
            var langCode = culture.TwoLetterISOLanguageName;
            
            switch (langCode.ToLower())
            {
                case "zh":
                    // 根据地区选择简体或繁体
                    if (fullCode.Contains("TW") || fullCode.Contains("HK") || fullCode.Contains("MO"))
                        return "zh-TW";
                    return "zh-CN";
                case "en":
                    return "en-US";
                case "ja":
                    return "ja-JP";
                case "ko":
                    return "ko-KR";
                case "ru":
                    return "ru-RU";
                case "es":
                    return "es-ES";
                default:
                    return "en-US"; // 默认英文 / Default to English
            }
        }

        /// <summary>
        /// Set application language
        /// 设置应用程序语言
        /// </summary>
        public static void SetLanguage(string languageCode)
        {
            if (!SupportedLanguages.ContainsKey(languageCode))
                languageCode = "en-US";

            _currentLanguage = languageCode;

            try
            {
                // 构建资源字典路径 / Build resource dictionary path
                // 使用相对路径格式
                var resourcePath = $"/Localization/Strings.{languageCode}.xaml";
                
                var dict = new ResourceDictionary
                {
                    Source = new Uri(resourcePath, UriKind.Relative)
                };

                // 移除旧的语言资源 / Remove old language resources
                var toRemove = new List<ResourceDictionary>();
                foreach (var rd in Application.Current.Resources.MergedDictionaries)
                {
                    if (rd.Source != null && 
                        (rd.Source.OriginalString.Contains("Localization/Strings.") ||
                         rd.Source.OriginalString.Contains("/Localization/Strings.")))
                    {
                        toRemove.Add(rd);
                    }
                }
                foreach (var rd in toRemove)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(rd);
                }
                
                System.Diagnostics.Debug.WriteLine($"[Localization] Removed {toRemove.Count} old dictionaries, loading: {resourcePath}");

                // 添加新的语言资源 / Add new language resources
                Application.Current.Resources.MergedDictionaries.Add(dict);

                // 触发语言改变事件 / Fire language changed event
                LanguageChanged?.Invoke(null, languageCode);
                
                System.Diagnostics.Debug.WriteLine($"Language switched to: {languageCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load language resource [{languageCode}]: {ex.Message}");
                
                // 尝试加载默认语言 / Try to load default language
                if (languageCode != "zh-CN")
                {
                    SetLanguage("zh-CN");
                }
            }
        }

        /// <summary>
        /// Get localized string by key
        /// 通过键获取本地化字符串
        /// </summary>
        public static string GetString(string key)
        {
            try
            {
                var value = Application.Current.FindResource(key);
                return value?.ToString() ?? key;
            }
            catch
            {
                return key;
            }
        }

        /// <summary>
        /// Get language display name
        /// 获取语言显示名称
        /// </summary>
        public static string GetLanguageDisplayName(string languageCode)
        {
            return SupportedLanguages.TryGetValue(languageCode, out var name) ? name : languageCode;
        }

        /// <summary>
        /// Get system language info for display
        /// 获取系统语言信息用于显示
        /// </summary>
        public static string GetSystemLanguageInfo()
        {
            var culture = CultureInfo.CurrentUICulture;
            return $"{culture.DisplayName} ({culture.Name})";
        }
    }
}
