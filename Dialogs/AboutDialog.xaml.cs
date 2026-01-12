// ============================================================================
// MultiFlash TOOL - About Dialog
// 关于对话框 | Aboutダイアログ | 정보 대화 상자
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace tools.Dialogs
{
    /// <summary>
    /// About Dialog / 关于对话框 / Aboutダイアログ
    /// </summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            
            // 设置版本号
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = $"Version {version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
        }

        /// <summary>
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击关闭
                Close();
            }
            else
            {
                DragMove();
            }
        }

        /// <summary>
        /// 关闭按钮
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 打开 GitHub
        /// </summary>
        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/xiriovo/edlormtk");
        }

        /// <summary>
        /// 复制 QQ 号
        /// </summary>
        private void QQ_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("1708298587");
                MessageBox.Show("QQ 号已复制到剪贴板：1708298587\n\nQQ number copied: 1708298587", 
                    "✅ 复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        /// <summary>
        /// 发送邮件
        /// </summary>
        private void Email_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("1708298587@qq.com");
                MessageBox.Show("邮箱已复制到剪贴板：1708298587@qq.com\n\nEmail copied: 1708298587@qq.com", 
                    "✅ 复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        /// <summary>
        /// 赞赏支持 - 打开赞赏对话框
        /// </summary>
        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            var donateDialog = new DonateDialog
            {
                Owner = this
            };
            donateDialog.ShowDialog();
        }

        /// <summary>
        /// 打开 URL
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
