// ============================================================================
// MultiFlash TOOL - Donate Dialog
// 赞赏对话框 | 寄付ダイアログ | 기부 대화 상자
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace tools.Dialogs
{
    /// <summary>
    /// Donate Dialog / 赞赏对话框 / 寄付ダイアログ
    /// </summary>
    public partial class DonateDialog : Window
    {
        // 加密货币地址
        private const string USDT_TRC20 = "TS5Q3e8dXmYGuwrdc8KcWTxksj91WRN1Fx";
        private const string USDC_ERC20 = "0x5eaa81f7bd55c6108ceecd6deef4984c5c86daa4";

        public DonateDialog()
        {
            InitializeComponent();
            LoadQRImages();
        }

        /// <summary>
        /// 加载二维码图片
        /// </summary>
        private void LoadQRImages()
        {
            try
            {
                // 获取程序运行目录
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                
                // 微信二维码 - 尝试多个可能的位置和格式
                string[] wechatPaths = new[]
                {
                    Path.Combine(baseDir, "Assets", "wechat_donate.jpg"),
                    Path.Combine(baseDir, "Assets", "wechat_donate.png"),
                    Path.Combine(baseDir, "Assets", "wechat.jpg"),
                    Path.Combine(baseDir, "Assets", "wechat.png"),
                    @"c:\Users\Administrator\Desktop\debg\tools\Assets\wechat_donate.jpg",
                    @"c:\Users\Administrator\Desktop\debg\tools\Assets\wechat_donate.png"
                };
                
                foreach (var path in wechatPaths)
                {
                    if (File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ImgWeChat.Source = bitmap;
                        break;
                    }
                }
                
                // 支付宝二维码
                string[] alipayPaths = new[]
                {
                    Path.Combine(baseDir, "Assets", "alipay_donate.jpg"),
                    Path.Combine(baseDir, "Assets", "alipay_donate.png"),
                    Path.Combine(baseDir, "Assets", "alipay.jpg"),
                    Path.Combine(baseDir, "Assets", "alipay.png"),
                    @"c:\Users\Administrator\Desktop\debg\tools\Assets\alipay_donate.jpg",
                    @"c:\Users\Administrator\Desktop\debg\tools\Assets\alipay_donate.png"
                };
                
                foreach (var path in alipayPaths)
                {
                    if (File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ImgAlipay.Source = bitmap;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载二维码图片失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
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
        /// Tab 切换
        /// </summary>
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelWeChat == null) return; // 防止初始化时调用
            
            // 隐藏所有面板
            PanelWeChat.Visibility = Visibility.Collapsed;
            PanelAlipay.Visibility = Visibility.Collapsed;
            PanelUSDT.Visibility = Visibility.Collapsed;
            PanelUSDC.Visibility = Visibility.Collapsed;
            
            // 显示选中的面板
            if (TabWeChat.IsChecked == true)
                PanelWeChat.Visibility = Visibility.Visible;
            else if (TabAlipay.IsChecked == true)
                PanelAlipay.Visibility = Visibility.Visible;
            else if (TabUSDT.IsChecked == true)
                PanelUSDT.Visibility = Visibility.Visible;
            else if (TabUSDC.IsChecked == true)
                PanelUSDC.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 复制 USDT 地址
        /// </summary>
        private void CopyUSDT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(USDT_TRC20);
                MessageBox.Show($"USDT (TRC20) 地址已复制！\n\n{USDT_TRC20}\n\n⚠️ 请确认网络为 TRC20", 
                    "✅ 复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        /// <summary>
        /// 复制 USDC 地址
        /// </summary>
        private void CopyUSDC_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(USDC_ERC20);
                MessageBox.Show($"USDC (ERC20) 地址已复制！\n\n{USDC_ERC20}\n\n⚠️ 请确认网络为 ERC20 (Ethereum)", 
                    "✅ 复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }
    }
}
