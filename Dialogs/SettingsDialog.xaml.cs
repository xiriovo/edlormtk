// ============================================================================
// MultiFlash TOOL - Settings Dialog
// 设置对话框 | 設定ダイアログ | 설정 대화상자
// ============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using tools.Localization;

namespace tools.Dialogs
{
    /// <summary>
    /// Settings Dialog - Language and preferences
    /// 设置对话框 - 语言和偏好设置
    /// </summary>
    public partial class SettingsDialog : Window
    {
        private bool _isInitializing = true;

        public SettingsDialog()
        {
            InitializeComponent();
            LoadSettings();
            _isInitializing = false;
        }

        /// <summary>
        /// Load current settings
        /// 加载当前设置
        /// </summary>
        private void LoadSettings()
        {
            // 显示系统语言信息
            TxtSystemLanguage.Text = LocalizationManager.GetSystemLanguageInfo();
            TxtCurrentLanguage.Text = $"{LocalizationManager.CurrentLanguage} ({LocalizationManager.GetLanguageDisplayName(LocalizationManager.CurrentLanguage)})";

            // 设置当前语言选中状态
            var currentLang = LocalizationManager.CurrentLanguage;
            switch (currentLang)
            {
                case "zh-CN":
                case "zh-TW":
                    LangZhCN.IsChecked = true;
                    break;
                case "en-US":
                    LangEnUS.IsChecked = true;
                    break;
                case "ja-JP":
                    LangJaJP.IsChecked = true;
                    break;
                case "ko-KR":
                    LangKoKR.IsChecked = true;
                    break;
                case "ru-RU":
                    LangRuRU.IsChecked = true;
                    break;
                case "es-ES":
                    LangEsES.IsChecked = true;
                    break;
                default:
                    LangEnUS.IsChecked = true;
                    break;
            }
        }

        /// <summary>
        /// Language selection changed
        /// 语言选择改变
        /// </summary>
        private void Language_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var radio = sender as RadioButton;
            if (radio == null) return;

            string langCode = "en-US";
            if (radio == LangZhCN) langCode = "zh-CN";
            else if (radio == LangEnUS) langCode = "en-US";
            else if (radio == LangJaJP) langCode = "ja-JP";
            else if (radio == LangKoKR) langCode = "ko-KR";
            else if (radio == LangRuRU) langCode = "ru-RU";
            else if (radio == LangEsES) langCode = "es-ES";

            // 切换语言
            LocalizationManager.SetLanguage(langCode);
        }

        /// <summary>
        /// Title bar drag
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// Close button click
        /// 关闭按钮点击
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// OK button click
        /// 确定按钮点击
        /// </summary>
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
