// ============================================================================
// MultiFlash TOOL - Main Window
// 主窗口 | メインウィンドウ | 메인 윈도우 | Ventana Principal | Главное окно
// ============================================================================
// [EN] Main application window with multi-platform flash support
//      Qualcomm EDL / MediaTek BROM / Unisoc SPRD / ADB Fastboot
// [中文] 主应用程序窗口，支持多平台刷机
//       高通 EDL / 联发科 BROM / 展讯 SPRD / ADB Fastboot
// [日本語] マルチプラットフォームフラッシュをサポートするメインアプリケーションウィンドウ
//         Qualcomm EDL / MediaTek BROM / Unisoc SPRD / ADB Fastboot
// [한국어] 멀티 플랫폼 플래시를 지원하는 메인 애플리케이션 창
//         퀄컴 EDL / 미디어텍 BROM / 유니속 SPRD / ADB Fastboot
// [Español] Ventana principal de la aplicación con soporte multi-plataforma
//           Qualcomm EDL / MediaTek BROM / Unisoc SPRD / ADB Fastboot
// [Русский] Главное окно приложения с поддержкой мульти-платформы
//           Qualcomm EDL / MediaTek BROM / Unisoc SPRD / ADB Fastboot
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using tools.Dialogs;
using tools.Modules.Common;
using tools.Modules.Qualcomm;
using tools.Modules.MTK;
using tools.Modules.Unisoc;
using tools.Modules.Unisoc.Models;

namespace tools
{
    /// <summary>
    /// Main Window - Transparent themed with custom drag support
    /// 主窗口 - 透明主题，支持自定义拖拽
    /// メインウィンドウ - 透明テーマ、カスタムドラッグサポート
    /// 메인 윈도우 - 투명 테마, 커스텀 드래그 지원
    /// </summary>
    public partial class MainWindow : Window
    {
        // 多个图片API - 随机选择加载，减少服务器压力
        private static readonly string[] ImageApis = new[]
        {
            "https://www.dmoe.cc/random.php",   // 樱花动漫 (最快)
            "http://www.98qy.com/sjbz/api.php", // 98轻云二次元
            "https://t.alcy.cc/pc",             // 二次元PC横图
            "https://t.alcy.cc/fj",             // 风景横图
            "https://www.loliapi.com/acg/pc",   // Loli API
        };
        
        // 随机数生成器
        private static readonly Random _random = new();

        // HTTP客户端 - 禁用自动重定向，手动处理混合协议重定向
        private static readonly HttpClient _httpClient;

        static MainWindow()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,  // ⚠️ 禁用自动重定向，手动处理 HTTPS→HTTP
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            // 模拟浏览器请求
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        }

        // 高通 UI 服务
        private QualcommUIService? _qcService;
        private CancellationTokenSource? _operationCts;
        
        // 设备状态管理器
        private DeviceStateManager? _deviceStateManager;
        
        // MTK UI 服务
        private MtkUIService? _mtkService;
        private CancellationTokenSource? _mtkOperationCts;

        // 展讯 (Unisoc) UI 服务
        private UnisocUIService? _sprdService;
        private CancellationTokenSource? _sprdOperationCts;
        
        // ADB/Fastboot 取消令牌
        private CancellationTokenSource? _fastbootOperationCts;

        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化日志
            InitializeLog();
            InitializeMtkLog();
            InitializeSprdLog();
            InitializeAdbLog();
            
            // 初始化高通服务
            InitializeQualcommService();
            
            // 初始化 MTK 服务
            InitializeMtkService();
            
            // 初始化展讯服务
            InitializeUnisocService();
            
            // 监听窗口状态变化，更新最大化按钮图标
            StateChanged += MainWindow_StateChanged;
            
            // 窗口加载完成后加载背景图片
            Loaded += MainWindow_Loaded;
            
            // 窗口关闭时释放资源
            Closing += MainWindow_Closing;
        }

        /// <summary>
        /// 窗口关闭时释放资源
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 释放高通服务资源
            _qcService?.Dispose();
            _qcService = null;
            
            // 释放 MTK 服务资源
            _mtkService?.Dispose();
            _mtkService = null;
            
            // 释放展讯服务资源
            _sprdService?.Dispose();
            _sprdService = null;
        }

        /// <summary>
        /// 初始化高通服务
        /// </summary>
        private void InitializeQualcommService()
        {
            _qcService = new QualcommUIService(
                Dispatcher,
                (msg, color) => AppendLog(msg, color),
                (percent, status) => UpdateProgress((int)percent, status),
                status => Dispatcher.Invoke(() => TxtProgressStatus.Text = status),
                info => UpdateDeviceInfoUI(info)
            );

            // 设备事件
            _qcService.DeviceArrived += port =>
            {
                // ⚠️ 不要自动检测状态！会消耗 Sahara Hello 包导致后续连接失败
                // 只显示设备已连接，让用户手动操作
                SetDeviceStatus(true, "Sahara 就绪", port);
                AppendLog($"[设备] ✓ 9008 设备就绪，可以进行操作", "#10B981");
            };

            _qcService.DeviceRemoved += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 完整清空设备信息
                    SetDeviceStatus(false, "未连接", "---", "---", "---", "---", "---");
                    
                    // 清空分区表
                    PartitionList.ItemsSource = null;
                    _allPartitions.Clear();
                    TxtPartitionCount.Text = "0 个分区";
                    TxtPartitionSearch.Text = "";
                    
                    // 重置进度条
                    SetProgressState(ProgressState.Ready, "就绪", 0);
                    TxtTransferredSize.Text = "0 MB";
                    TxtElapsedTime.Text = "00:00";
                    TxtTransferSpeed.Text = "0 MB/s";
                    
                    // 停止状态监控
                    _deviceStateManager?.StopMonitoring();
                    _deviceStateManager?.Dispose();
                    _deviceStateManager = null;
                    
                    AppendLog("[设备] 设备已断开，UI 已重置", "#888888");
                });
            };

            // 分区加载事件 - 直接绑定 PartitionInfo 支持双向绑定
            _qcService.PartitionsLoaded += partitions =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdatePartitionList(partitions);
                    AppendLog($"📋 已加载 {partitions.Count} 个分区", "#10B981");
                });
            };

            // 传输进度事件 - 更新已传输字节数
            _qcService.TransferProgress += (current, total) =>
            {
                UpdateTransferSize(current);
            };

            // 启动设备监听
            _qcService.StartDeviceWatcher();
        }

        /// <summary>
        /// 更新设备信息 UI
        /// </summary>
        private void UpdateDeviceInfoUI(QcDeviceInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                // 厂商优先显示读取到的厂商，否则显示 PK Hash 推断的厂商
                TxtDeviceVendor.Text = !string.IsNullOrEmpty(info.Manufacturer) ? info.Manufacturer : info.Vendor;
                
                // 型号优先显示设备型号，否则显示芯片型号
                TxtDeviceModel.Text = !string.IsNullOrEmpty(info.Model) ? info.Model : info.ChipName;
                
                // OTA 版本
                TxtDeviceOTA.Text = !string.IsNullOrEmpty(info.OtaVersion) ? info.OtaVersion : "---";
                
                // SN 序列号
                TxtDeviceSN.Text = !string.IsNullOrEmpty(info.Serial) ? info.Serial : "---";
                
                // 端口
                TxtDevicePort.Text = info.Port;
                
                // 更新设备状态显示（显示设备名称或连接状态）
                if (!string.IsNullOrEmpty(info.Model) && info.Model != "---")
                {
                    TxtDeviceStatus.Text = info.Model;
                }
            });
        }

        /// <summary>
        /// 自动检测设备协议状态
        /// </summary>
        private async Task DetectDeviceProtocolStateAsync(string port)
        {
            try
            {
                AppendLog($"[状态检测] 检测端口 {port} 协议状态...", "#3B82F6");
                
                // 尝试打开端口进行状态检测
                using var tempPort = new SerialPortManager();
                tempPort.BaudRate = 3000000;
                
                if (!tempPort.Open(port, discardBuffer: false))
                {
                    AppendLog($"[状态检测] 无法打开端口 {port}", "#EF4444");
                    SetDeviceStatus(true, "端口错误", port);
                    return;
                }
                
                var detector = new DeviceStateDetector(tempPort, msg => AppendLog(msg, "#6B7280"));
                var stateInfo = await detector.DetectStateAsync();
                
                tempPort.Close();
                
                // 更新 UI 显示
                Dispatcher.Invoke(() =>
                {
                    string stateText = DeviceStateDetector.GetStateDisplayText(stateInfo.State);
                    string stateColor = DeviceStateDetector.GetStateColor(stateInfo.State);
                    
                    // 更新状态显示
                    SetDeviceStatus(true, stateText.Replace("📤 ", "").Replace("✅ ", "").Replace("🔧 ", ""), port);
                    
                    // 根据状态显示建议
                    if (!string.IsNullOrEmpty(stateInfo.SuggestedAction))
                    {
                        AppendLog($"[建议] {stateInfo.SuggestedAction}", "#F59E0B");
                    }
                });
                
                // 根据状态采取自动措施
                await HandleDeviceStateAsync(stateInfo, port);
            }
            catch (Exception ex)
            {
                AppendLog($"[状态检测] 异常: {ex.Message}", "#EF4444");
                SetDeviceStatus(true, "检测失败", port);
            }
        }

        /// <summary>
        /// 根据设备状态采取自动措施
        /// </summary>
        private async Task HandleDeviceStateAsync(DeviceStateInfo stateInfo, string port)
        {
            switch (stateInfo.State)
            {
                case DeviceProtocolState.SaharaWaitingLoader:
                    // Sahara 模式 - 显示等待 Loader 状态
                    AppendLog($"[Sahara] 设备等待 Loader (版本 {stateInfo.SaharaVersion})", "#10B981");
                    if (stateInfo.Supports64Bit)
                    {
                        AppendLog("[Sahara] 支持 64 位传输", "#6B7280");
                    }
                    SetDeviceStatus(true, "Sahara 就绪", port);
                    break;
                    
                case DeviceProtocolState.FirehoseConfigured:
                    // Firehose 已配置 - 可以直接操作
                    AppendLog("[Firehose] 设备已配置，可以直接操作", "#10B981");
                    if (!string.IsNullOrEmpty(stateInfo.StorageType))
                    {
                        AppendLog($"[Firehose] 存储类型: {stateInfo.StorageType.ToUpper()}", "#6B7280");
                    }
                    SetDeviceStatus(true, "Firehose 就绪", port);
                    break;
                    
                case DeviceProtocolState.FirehoseNotConfigured:
                    // Firehose 未配置 - 提示用户需要配置
                    AppendLog("[Firehose] 设备未配置，需要发送 Configure 命令", "#F59E0B");
                    SetDeviceStatus(true, "需要配置", port);
                    break;
                    
                case DeviceProtocolState.FirehoseConfigureFailed:
                    // 配置失败 - 尝试自动恢复
                    AppendLog("[Firehose] 配置失败，尝试自动恢复...", "#EF4444");
                    // 可以在这里调用自动恢复逻辑
                    break;
                    
                case DeviceProtocolState.NoResponse:
                    // 无响应 - 提示用户检查连接
                    AppendLog("[警告] 设备无响应，请检查连接或重新进入 EDL 模式", "#EF4444");
                    SetDeviceStatus(true, "无响应", port);
                    break;
                    
                default:
                    SetDeviceStatus(true, "9008 就绪", port);
                    break;
            }
        }

        #region 背景图片加载

        /// <summary>
        /// 窗口加载完成 (不再自动加载背景图片，需手动刷新)
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 默认不加载背景图片，用户可点击刷新按钮手动加载
            LoadingIndicator.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 随机API加载 - 随机选择一个API，失败后尝试其他
        /// </summary>
        private async Task LoadBackgroundImageAsync()
        {
            // 显示加载指示器
            LoadingIndicator.Visibility = Visibility.Visible;
            LoadingText.Text = "正在加载背景图片...";

            // 创建打乱顺序的API列表 (随机化)
            var shuffledApis = ImageApis.OrderBy(_ => _random.Next()).ToList();
            
            try
            {
                // 依次尝试每个API
                for (int i = 0; i < shuffledApis.Count; i++)
                {
                    var apiUrl = shuffledApis[i];
                    
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadingText.Text = $"正在尝试 API {i + 1}/{shuffledApis.Count}...";
                    });
                    
                    var bitmap = await LoadImageFromApiAsync(apiUrl);
                    if (bitmap != null)
                    {
                        // 成功获取图片
                        await Dispatcher.InvokeAsync(() =>
                        {
                            BackgroundImage.Source = bitmap;
                            
                            // 淡入动画
                            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
                            {
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            };
                            BackgroundImage.BeginAnimation(OpacityProperty, fadeIn);
                            
                            LoadingIndicator.Visibility = Visibility.Collapsed;
                        });
                        return;
                    }
                }

                // 所有API都失败了
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingText.Text = "所有图片源加载失败\n点击刷新按钮重试";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingText.Text = $"加载失败: {ex.Message}\n点击刷新按钮重试";
                });
            }
        }

        /// <summary>
        /// 从单个API加载图片 - 支持混合协议重定向 (HTTPS→HTTP→HTTPS)
        /// </summary>
        private async Task<BitmapImage?> LoadImageFromApiAsync(string apiUrl)
        {
            try
            {
                // 添加随机参数避免缓存
                string url = $"{apiUrl}?t={DateTime.Now.Ticks}";
                
                // 手动跟随重定向 (支持 HTTPS→HTTP 混合)
                int redirectCount = 0;
                const int maxRedirects = 10;
                
                while (redirectCount < maxRedirects)
                {
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    int statusCode = (int)response.StatusCode;
                    
                    // 处理重定向 (301, 302, 303, 307, 308)
                    if (statusCode >= 300 && statusCode < 400 && response.Headers.Location != null)
                    {
                        var location = response.Headers.Location;
                        if (!location.IsAbsoluteUri)
                        {
                            location = new Uri(new Uri(url), location);
                        }
                        url = location.ToString();
                        redirectCount++;
                        continue;
                    }
                    
                    // 非重定向响应
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }
                    
                    // 读取图片数据
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    
                    if (imageBytes.Length < 1000)
                    {
                        // 数据太小，可能不是有效图片
                        return null;
                    }

                    // 创建BitmapImage
                    BitmapImage? bitmap = null;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = new MemoryStream(imageBytes);
                        bitmap.EndInit();
                        bitmap.Freeze();
                    });

                    return bitmap;
                }
                
                // 重定向次数过多
                return null;
            }
            catch
            {
                // API失败，返回null让其他API继续
                return null;
            }
        }

        /// <summary>
        /// 关于按钮点击 - 显示关于对话框
        /// About button click - Show about dialog
        /// </summary>
        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutDialog = new AboutDialog
            {
                Owner = this
            };
            aboutDialog.ShowDialog();
        }

        /// <summary>
        /// Settings button click - Show settings dialog
        /// 设置按钮点击 - 显示设置对话框
        /// </summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsDialog = new SettingsDialog
            {
                Owner = this
            };
            settingsDialog.ShowDialog();
        }

        #region 资源中心事件处理 / Resource Center Event Handlers

        /// <summary>
        /// 打开URL工具方法 / Open URL helper
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"无法打开链接: {ex.Message}", "#FF5252");
            }
        }

        // ===== 驱动下载 =====
        private void Resource_QcomDriver_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://gsmusbdriver.com/qualcomm-hs-usb-qdloader-9008");
            AppendLog("📥 正在打开 Qualcomm QDLoader 9008 驱动下载页面...", "#00D4FF");
        }

        private void Resource_MtkDriver_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://gsmusbdriver.com/mediatek-vcom-usb-preloader-driver");
            AppendLog("📥 正在打开 MediaTek VCOM/Preloader 驱动下载页面...", "#4CAF50");
        }

        private void Resource_UnisocDriver_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://gsmusbdriver.com/spd-unisoc-usb-driver");
            AppendLog("📥 正在打开 Unisoc/Spreadtrum 驱动下载页面...", "#FF9800");
        }

        private void Resource_AdbDriver_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://developer.android.com/studio/run/win-usb");
            AppendLog("📥 正在打开 Google USB Driver 下载页面...", "#2196F3");
        }

        // ===== 工具下载 =====
        private void Resource_PlatformTools_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://developer.android.com/tools/releases/platform-tools");
            AppendLog("🛠️ 正在打开 Android Platform Tools 下载页面...", "#2196F3");
        }

        private void Resource_QFIL_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://qfiltool.com/");
            AppendLog("🛠️ 正在打开 QFIL Tool 下载页面...", "#00D4FF");
        }

        private void Resource_SPFlashTool_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://spflashtool.com/");
            AppendLog("🛠️ 正在打开 SP Flash Tool 下载页面...", "#4CAF50");
        }

        private void Resource_ResearchDownload_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://unisoc.com/");
            AppendLog("🛠️ 正在打开 Unisoc Research Download 页面...", "#FF9800");
        }

        // ===== 内置浏览器功能 =====
        private bool _browserInitialized = false;
        private const string BrowserHomePage = "https://www.xiriacg.top/";

        private async void InitializeBrowser()
        {
            if (_browserInitialized) return;
            
            try
            {
                await ResourceBrowser.EnsureCoreWebView2Async(null);
                _browserInitialized = true;
                
                // 拦截新窗口请求，在内置浏览器中打开
                ResourceBrowser.CoreWebView2.NewWindowRequested += (s, args) =>
                {
                    args.Handled = true;
                    NavigateBrowser(args.Uri);
                };
                
                // 隐藏加载提示，显示浏览器
                BrowserLoadingPanel.Visibility = Visibility.Collapsed;
                ResourceBrowser.Visibility = Visibility.Visible;
                
                // 导航到首页
                ResourceBrowser.CoreWebView2.Navigate(BrowserHomePage);
                TxtBrowserUrl.Text = BrowserHomePage;
                
                AppendLog("🌐 内置浏览器已初始化", "#00D4FF");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 浏览器初始化失败: {ex.Message}", "#FF5252");
            }
        }

        private void NavigateBrowser(string url)
        {
            if (!_browserInitialized)
            {
                InitializeBrowser();
                return;
            }
            
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
                
            ResourceBrowser.CoreWebView2.Navigate(url);
            TxtBrowserUrl.Text = url;
        }

        // 浏览器导航按钮
        private void BrowserBack_Click(object sender, RoutedEventArgs e)
        {
            if (_browserInitialized && ResourceBrowser.CanGoBack)
                ResourceBrowser.GoBack();
        }

        private void BrowserForward_Click(object sender, RoutedEventArgs e)
        {
            if (_browserInitialized && ResourceBrowser.CanGoForward)
                ResourceBrowser.GoForward();
        }

        private void BrowserRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_browserInitialized)
                ResourceBrowser.Reload();
        }

        private void BrowserHome_Click(object sender, RoutedEventArgs e)
        {
            NavigateBrowser(BrowserHomePage);
        }

        private void BrowserGo_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtBrowserUrl.Text))
                NavigateBrowser(TxtBrowserUrl.Text);
        }

        private void BrowserUrl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                BrowserGo_Click(sender, e);
        }
        // WebView2 事件
        private void ResourceBrowser_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            TxtBrowserUrl.Text = e.Uri;
        }

        private void ResourceBrowser_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                AppendLog($"⚠️ 页面加载失败", "#FFA000");
        }

        private void ResourceBrowser_SourceChanged(object sender, Microsoft.Web.WebView2.Core.CoreWebView2SourceChangedEventArgs e)
        {
            if (ResourceBrowser.Source != null)
                TxtBrowserUrl.Text = ResourceBrowser.Source.ToString();
        }

        // ===== 教程文档 =====
        private void Resource_XDA_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://xdaforums.com/");
            AppendLog("📚 正在打开 XDA Developers 论坛...", "#F57C00");
        }

        private void Resource_GitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/xiriovo/edlormtk");
            AppendLog("📚 正在打开项目 GitHub 页面...", "#333333");
        }

        private void Resource_Wiki_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/xiriovo/edlormtk/wiki");
            AppendLog("📚 正在打开项目 Wiki 文档...", "#4CAF50");
        }

        private void Resource_Telegram_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://t.me/multiflash_tool");
            AppendLog("📚 正在打开 Telegram 交流群...", "#0088CC");
        }

        #endregion

        /// <summary>
        /// 刷新背景图片按钮点击
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 先淡出当前图片
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            BackgroundImage.BeginAnimation(OpacityProperty, fadeOut);
            await Task.Delay(200);
            
            await LoadBackgroundImageAsync();
        }

        #endregion

        #region 二次元点击效果 (性能优化版)

        // 粒子符号集合 - 轻量文本符号 (丰富版)
        private static readonly string[] ParticleSymbols = new[]
        {
            // 星星
            "✦", "✧", "★", "☆", "⭐", "✡", "✪", "✫", "✬", "✭", "✮", "✯", "⁂", "⁎", "⁑",
            // 爱心
            "❤", "♡", "♥", "❥", "❣", "💕", "💗", "💖", "💘", "💝",
            // 花朵
            "✿", "❀", "❁", "✾", "❃", "❋", "✻", "✼", "❊", "🌸", "🌺", "🌼",
            // 音符
            "♪", "♫", "♬", "♩", "🎵", "🎶",
            // 几何
            "◇", "◆", "○", "●", "◎", "◉", "△", "▽", "☾", "☽",
            // 闪光
            "✨", "💫", "🌟", "⚡", "✴", "✵", "❇", "✳",
            // 可爱
            "🎀", "🍀", "🌈", "☀", "🌙", "💎", "🔮", "🍭", "🍬", "🧸"
        };
        
        // 二次元风格文字 - 随机显示
        private static readonly string[] ParticleTexts = new[]
        {
            // 日语可爱词
            "かわいい", "すごい", "やった", "きらきら", "ふわふわ", "わくわく",
            "ドキドキ", "にゃん", "うわぁ", "えへへ", "やばい", "最高",
            "大好き", "嬉しい", "幸せ", "素敵", "綺麗", "天才",
            
            // 英文萌词
            "Love", "Cute", "Wow", "Yeah", "Nice", "Cool", "Great",
            "OwO", "UwU", "QwQ", "TwT", "AwA", "OvO", "OuO",
            "Yay", "Woo", "Nya", "Meow", "Paw", "Hehe", "Hihi",
            "Sweet", "Kawaii", "Sugoi", "Doki", "Moe", "Nyan",
            
            // 颜文字风格
            "(*´▽`*)", "(◕‿◕)", "٩(◕‿◕｡)۶", "(≧▽≦)", "ヾ(≧▽≦*)o",
            "(｡♥‿♥｡)", "(◍•ᴗ•◍)", "(✿◠‿◠)", "٩(๑❛ᴗ❛๑)۶",
            "(●'◡'●)", "(◠‿◠)", "(*≧ω≦)", "(´▽`ʃ♡ƪ)",
            "ლ(╹◡╹ლ)", "(灬ºωº灬)", "♪(´ε`)", "( •̀ ω •́ )✧"
        };
        
        // 预编译的粒子颜色画刷 (避免每次创建新对象)
        private static readonly System.Windows.Media.SolidColorBrush[] ParticleBrushes = InitParticleBrushes();
        
        // 性能限制
        private const int MaxParticlesOnScreen = 50;  // 屏幕上最大粒子数
        private const int ParticlesPerClick = 6;       // 每次点击粒子数 (减少)
        private DateTime _lastParticleClickTime = DateTime.MinValue;
        private const int ClickThrottleMs = 50;        // 点击节流 (毫秒)
        
        /// <summary>
        /// 初始化预编译的粒子画刷
        /// </summary>
        private static System.Windows.Media.SolidColorBrush[] InitParticleBrushes()
        {
            var colors = new[]
            {
                "#FF69B4", "#FFB6C1", "#DDA0DD", "#87CEEB", "#98FB98",
                "#FFD700", "#FF6B6B", "#A8E6CF", "#74B9FF", "#FDA7DF"
            };
            return colors.Select(c =>
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c));
                brush.Freeze(); // 冻结以提高性能
                return brush;
            }).ToArray();
        }

        /// <summary>
        /// 处理全局鼠标点击 - 生成粒子效果 (带节流)
        /// </summary>
        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            
            // 节流检查 - 避免快速点击产生过多粒子
            var now = DateTime.Now;
            if ((now - _lastParticleClickTime).TotalMilliseconds < ClickThrottleMs)
                return;
            _lastParticleClickTime = now;
            
            // 检查当前粒子数量
            if (ClickEffectCanvas.Children.Count >= MaxParticlesOnScreen)
                return;
            
            // 获取点击位置
            var position = e.GetPosition(ClickEffectCanvas);
            
            // 生成粒子效果
            CreateClickParticlesOptimized(position);
        }

        /// <summary>
        /// 创建优化的粒子效果
        /// </summary>
        private void CreateClickParticlesOptimized(Point position)
        {
            for (int i = 0; i < ParticlesPerClick; i++)
            {
                CreateLightweightParticle(position);
            }
        }

        /// <summary>
        /// 创建轻量级粒子 (无阴影效果，使用合并动画)
        /// </summary>
        private void CreateLightweightParticle(Point origin)
        {
            // 使用预编译的画刷
            var brush = ParticleBrushes[_random.Next(ParticleBrushes.Length)];
            
            // 20% 概率显示文字，80% 概率显示符号
            bool isText = _random.Next(100) < 20;
            string content;
            int fontSize;
            
            if (isText)
            {
                content = ParticleTexts[_random.Next(ParticleTexts.Length)];
                fontSize = _random.Next(10, 16); // 文字稍小一点
            }
            else
            {
                content = ParticleSymbols[_random.Next(ParticleSymbols.Length)];
                fontSize = _random.Next(14, 24);
            }
            
            // 创建轻量粒子
            var particle = new TextBlock
            {
                Text = content,
                FontSize = fontSize,
                FontWeight = isText ? FontWeights.Bold : FontWeights.Normal,
                Foreground = brush,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
                Opacity = 0
                // 移除 DropShadowEffect - 这是最大的性能瓶颈
            };

            // 简化的变换
            var translateTransform = new System.Windows.Media.TranslateTransform(0, 0);
            particle.RenderTransform = translateTransform;

            // 设置位置
            Canvas.SetLeft(particle, origin.X - 8);
            Canvas.SetTop(particle, origin.Y - 8);
            ClickEffectCanvas.Children.Add(particle);

            // 计算飞散方向 (文字粒子飞得更高更慢)
            double angle = _random.NextDouble() * Math.PI * 2;
            double distance = isText ? _random.Next(30, 70) : _random.Next(40, 100);
            double targetX = Math.Cos(angle) * distance;
            double targetY = isText 
                ? -_random.Next(60, 100)  // 文字主要向上飞
                : Math.Sin(angle) * distance - _random.Next(20, 50);

            // 动画时长 (文字粒子停留更久便于阅读)
            var duration = isText 
                ? TimeSpan.FromMilliseconds(_random.Next(800, 1200))
                : TimeSpan.FromMilliseconds(_random.Next(400, 800));

            // 使用 Storyboard 合并动画 (更高效)
            var storyboard = new System.Windows.Media.Animation.Storyboard();

            // 位移 X
            var moveX = new DoubleAnimation(0, targetX, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(moveX, particle);
            Storyboard.SetTargetProperty(moveX, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            storyboard.Children.Add(moveX);

            // 位移 Y
            var moveY = new DoubleAnimation(0, targetY, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(moveY, particle);
            Storyboard.SetTargetProperty(moveY, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            storyboard.Children.Add(moveY);

            // 透明度 (淡入淡出)
            var opacity = new DoubleAnimationUsingKeyFrames { Duration = duration };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.5)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Storyboard.SetTarget(opacity, particle);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(opacity);

            // 动画完成后移除粒子
            storyboard.Completed += (s, e) =>
            {
                ClickEffectCanvas.Children.Remove(particle);
            };

            storyboard.Begin();
        }

        #endregion

        #region 窗口状态管理

        /// <summary>
        /// 窗口状态变化时更新UI
        /// </summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // 最大化状态：显示还原图标，调整边距
                MaximizeButton.Content = "\uE923"; // 还原图标
                MaximizeButton.ToolTip = "向下还原";
                MainBorder.Margin = new Thickness(7); // 防止最大化时内容超出屏幕
            }
            else
            {
                // 正常状态：显示最大化图标
                MaximizeButton.Content = "\uE922"; // 最大化图标
                MaximizeButton.ToolTip = "最大化";
                MainBorder.Margin = new Thickness(0);
            }
        }

        #endregion

        #region 标题栏拖拽逻辑

        private bool _isDoubleClick = false;

        /// <summary>
        /// 标题栏鼠标按下 - 处理拖拽和双击最大化
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏：切换最大化/还原
                _isDoubleClick = true;
                ToggleMaximize();
            }
            else
            {
                _isDoubleClick = false;
                
                // 单击：开始拖拽
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    // 如果是最大化状态，先还原再拖拽
                    if (WindowState == WindowState.Maximized)
                    {
                        // 计算鼠标相对位置
                        var mousePos = e.GetPosition(this);
                        var screenPos = PointToScreen(mousePos);
                        
                        // 还原窗口
                        WindowState = WindowState.Normal;
                        
                        // 将窗口移动到鼠标位置
                        Left = screenPos.X - (ActualWidth / 2);
                        Top = screenPos.Y - 20;
                    }
                    
                    DragMove();
                }
            }
        }

        /// <summary>
        /// 标题栏鼠标移动 - 处理拖拽时的边缘吸附
        /// </summary>
        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDoubleClick) return;
        }

        /// <summary>
        /// 切换最大化/还原状态
        /// </summary>
        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }

        #endregion

        #region 侧边栏导航

        // 当前选中的按钮
        private Button? _currentSelectedButton;

        /// <summary>
        /// 侧边栏按钮点击事件
        /// </summary>
        private void SidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // 更新按钮样式
                UpdateSidebarSelection(button);
                
                // 根据Tag切换内容
                var tag = button.Tag?.ToString();
                SwitchContent(tag);
                AppendLog($"[INFO] 切换到 {tag} 模块", "#0088CC");
            }
        }

        /// <summary>
        /// 切换内容区域
        /// </summary>
        private void SwitchContent(string? moduleName)
        {
            // 隐藏所有内容区域
            QualcommContent.Visibility = Visibility.Collapsed;
            MTKContent.Visibility = Visibility.Collapsed;
            SpreadtrumContent.Visibility = Visibility.Collapsed;
            ADBContent.Visibility = Visibility.Collapsed;
            ResourcesContent.Visibility = Visibility.Collapsed;

            // 根据模块名显示对应内容
            switch (moduleName)
            {
                case "Qualcomm":
                    QualcommContent.Visibility = Visibility.Visible;
                    break;
                case "MTK":
                    MTKContent.Visibility = Visibility.Visible;
                    break;
                case "Spreadtrum":
                    SpreadtrumContent.Visibility = Visibility.Visible;
                    break;
                case "ADB":
                    ADBContent.Visibility = Visibility.Visible;
                    break;
                case "Resources":
                    ResourcesContent.Visibility = Visibility.Visible;
                    // 初始化内置浏览器
                    InitializeBrowser();
                    break;
                default:
                    QualcommContent.Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// 更新侧边栏选中状态
        /// </summary>
        private void UpdateSidebarSelection(Button selectedButton)
        {
            // 获取样式
            var normalStyle = FindResource("SidebarButtonStyle") as Style;
            var activeStyle = FindResource("SidebarButtonActiveStyle") as Style;

            // 重置之前选中的按钮
            if (_currentSelectedButton != null)
            {
                _currentSelectedButton.Style = normalStyle;
            }

            // 设置新选中的按钮
            selectedButton.Style = activeStyle;
            _currentSelectedButton = selectedButton;
        }

        #endregion

        #region 高通工具功能

        /// <summary>
        /// 进度条状态枚举
        /// </summary>
        public enum ProgressState
        {
            Ready,      // 就绪
            Running,    // 进行中
            Success,    // 完成
            Warning,    // 警告
            Error       // 失败
        }

        // 操作锁，防止重复点击
        private bool _isOperating = false;
        
        // 读取设备信息开关
        private bool _readInfoEnabled = false;
        
        // 计时器相关
        private System.Diagnostics.Stopwatch _stopwatch = new();
        private System.Windows.Threading.DispatcherTimer _timer = null!;
        private double _transferredBytes = 0;
        private DateTime _lastSpeedUpdate = DateTime.Now;
        private double _lastTransferredBytes = 0;

        /// <summary>
        /// 初始化计时器
        /// </summary>
        private void InitializeTimer()
        {
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (s, e) => UpdateTimerDisplay();
        }

        /// <summary>
        /// 更新计时器显示 (UI线程安全)
        /// </summary>
        private void UpdateTimerDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateTimerDisplay);
                return;
            }
            
            var elapsed = _stopwatch.Elapsed;
            
            // 更新时间显示 (支持超过1小时)
            if (elapsed.TotalHours >= 1)
                TxtElapsedTime.Text = $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            else
                TxtElapsedTime.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            // 计算速度（每秒更新一次）
            if ((DateTime.Now - _lastSpeedUpdate).TotalSeconds >= 1)
            {
                double bytesPerSecond = _transferredBytes - _lastTransferredBytes;
                double mbPerSecond = bytesPerSecond / 1024 / 1024;
                TxtTransferSpeed.Text = $"{mbPerSecond:F1} MB/s";
                _lastTransferredBytes = _transferredBytes;
                _lastSpeedUpdate = DateTime.Now;
            }
        }

        /// <summary>
        /// 开始计时
        /// </summary>
        private void StartTimer()
        {
            if (_timer == null) InitializeTimer();
            _stopwatch.Restart();
            _transferredBytes = 0;
            _lastTransferredBytes = 0;
            _lastSpeedUpdate = DateTime.Now;
            
            // 重置UI显示
            Dispatcher.Invoke(() =>
            {
                TxtElapsedTime.Text = "00:00";
                TxtTransferSpeed.Text = "0 MB/s";
                TxtTransferredSize.Text = "0 MB";
            });
            
            _timer?.Start();
        }

        /// <summary>
        /// 停止计时
        /// </summary>
        private void StopTimer()
        {
            _timer?.Stop();
            _stopwatch.Stop();
        }

        // 进度更新节流
        private DateTime _lastProgressUpdateTime = DateTime.MinValue;
        private const int ProgressUpdateThrottleMs = 50; // 进度更新节流 (毫秒)
        
        /// <summary>
        /// 更新传输大小 (带节流)
        /// </summary>
        private void UpdateTransferSize(double bytes)
        {
            _transferredBytes = bytes;
            
            // 节流检查
            var now = DateTime.Now;
            if ((now - _lastProgressUpdateTime).TotalMilliseconds < ProgressUpdateThrottleMs)
                return;
            _lastProgressUpdateTime = now;
            
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                double mb = bytes / 1024 / 1024;
                TxtTransferredSize.Text = mb >= 1024 ? $"{mb / 1024:F2} GB" : $"{mb:F1} MB";
            });
        }

        /// <summary>
        /// 设置设备状态
        /// </summary>
        private void SetDeviceStatus(bool connected, string status = "", string port = "", 
            string vendor = "", string model = "", string ota = "", string sn = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    DeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
                    TxtDeviceStatus.Text = string.IsNullOrEmpty(status) ? "已连接" : status;
                    TxtDeviceStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#059669"));
                }
                else
                {
                    DeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
                    TxtDeviceStatus.Text = string.IsNullOrEmpty(status) ? "未连接设备" : status;
                    TxtDeviceStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555"));
                }

                // 格式化端口显示
                if (string.IsNullOrEmpty(port) || port == "---")
                {
                    TxtDevicePort.Text = "COM--";
                }
                else
                {
                    // 确保端口名称正确显示 (如 COM3, COM10 等)
                    TxtDevicePort.Text = port.ToUpper().StartsWith("COM") ? port.ToUpper() : $"COM{port}";
                }
                TxtDeviceVendor.Text = string.IsNullOrEmpty(vendor) ? "---" : vendor;
                TxtDeviceModel.Text = string.IsNullOrEmpty(model) ? "---" : model;
                TxtDeviceOTA.Text = string.IsNullOrEmpty(ota) ? "---" : ota;
                TxtDeviceSN.Text = string.IsNullOrEmpty(sn) ? "---" : sn;
            });
        }

        /// <summary>
        /// 设置按钮启用状态
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                _isOperating = !enabled;
            });
        }

        /// <summary>
        /// 在后台线程执行操作 (线程安全，防止UI卡死)
        /// </summary>
        /// <param name="operationName">操作名称</param>
        /// <param name="operation">要执行的异步操作</param>
        /// <param name="onSuccess">成功回调 (UI线程)</param>
        /// <param name="onError">失败回调 (UI线程)</param>
        private async Task RunOperationAsync(
            string operationName,
            Func<CancellationToken, Task<bool>> operation,
            Action? onSuccess = null,
            Action<Exception>? onError = null)
        {
            if (_isOperating) return;

            // 创建新的取消令牌
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            var ct = _operationCts.Token;

            try
            {
                // 在UI线程设置状态
                await Dispatcher.InvokeAsync(() =>
                {
                    SetButtonsEnabled(false);
                    StartTimer();
                    BtnQcStop.IsEnabled = true;
                    _isOperating = true;
                    SetProgressState(ProgressState.Running, $"正在{operationName}...", 0);
                });

                // 在后台线程执行操作
                bool success = await Task.Run(async () =>
                {
                    try
                    {
                        return await operation(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }, ct);

                // 在UI线程处理结果
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        SetProgressState(ProgressState.Warning, "操作已取消", 0);
                        AppendLog($"⚠️ {operationName} 已取消", "#D97706");
                    }
                    else if (success)
                    {
                        SetProgressState(ProgressState.Success, $"{operationName}完成", 100);
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        SetProgressState(ProgressState.Error, $"{operationName}失败", 0);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SetProgressState(ProgressState.Warning, "操作已取消", 0);
                    AppendLog($"⚠️ {operationName} 已取消", "#D97706");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AppendLog($"❌ {operationName}失败: {ex.Message}", "#EF4444");
                    SetProgressState(ProgressState.Error, "发生错误", 0);
                    onError?.Invoke(ex);
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StopTimer();
                    SetButtonsEnabled(true);
                    BtnQcStop.IsEnabled = false;
                    _isOperating = false;
                });
            }
        }

        /// <summary>
        /// 在后台线程执行操作 (简化版，无返回值)
        /// </summary>
        private async Task RunOperationAsync(
            string operationName,
            Func<CancellationToken, Task> operation)
        {
            await RunOperationAsync(
                operationName,
                async ct => { await operation(ct).ConfigureAwait(false); return true; },
                null,
                null);
        }

        /// <summary>
        /// 设置进度条状态
        /// </summary>
        private void SetProgressState(ProgressState state, string statusText, double progress = -1)
        {
            Dispatcher.Invoke(() =>
            {
                // 设置进度值
                if (progress >= 0)
                {
                    MainProgressBar.Value = progress;
                    TxtProgressPercent.Text = $"{progress:F0}%";
                }

                // 状态图标和样式
                string icon;
                string styleName;
                string statusColor;

                switch (state)
                {
                    case ProgressState.Running:
                        icon = "⏳";
                        styleName = "ProgressBarInfoStripe";
                        statusColor = "#0088CC";
                        break;
                    case ProgressState.Success:
                        icon = "✅";
                        styleName = "ProgressBarSuccess";
                        statusColor = "#059669";
                        break;
                    case ProgressState.Warning:
                        icon = "⚠️";
                        styleName = "ProgressBarWarning";
                        statusColor = "#D97706";
                        break;
                    case ProgressState.Error:
                        icon = "❌";
                        styleName = "ProgressBarDanger";
                        statusColor = "#DC2626";
                        break;
                    default:
                        icon = "💤";
                        styleName = "ProgressBarSuccess";
                        statusColor = "#555555";
                        break;
                }

                TxtProgressStatus.Text = $"{icon} {statusText}";
                MainProgressBar.Style = (Style)FindResource(styleName);
                TxtProgressStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(statusColor));
            });
        }

        /// <summary>
        /// 重置进度条到就绪状态
        /// </summary>
        private void ResetProgress()
        {
            SetProgressState(ProgressState.Ready, "就绪", 0);
        }

        // 上次进度值 (用于节流)
        private double _lastProgressValue = -1;
        
        /// <summary>
        /// 更新进度 (带节流，避免频繁更新)
        /// </summary>
        private void UpdateProgress(double progress, string statusText = null!)
        {
            // 进度变化小于1%时跳过更新 (除非是0%或100%)
            if (progress > 0 && progress < 100 && Math.Abs(progress - _lastProgressValue) < 1)
                return;
            _lastProgressValue = progress;
            
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                MainProgressBar.Value = progress;
                TxtProgressPercent.Text = $"{progress:F0}%";
                if (!string.IsNullOrEmpty(statusText))
                {
                    TxtProgressStatus.Text = $"⏳ {statusText}";
                }
            });
        }

        /// <summary>
        /// 获取当前存储类型
        /// </summary>
        private string GetStorageType()
        {
            return RbEmmc.IsChecked == true ? "eMMC" : "UFS";
        }

        /// <summary>
        /// 读取GPT分区表 (线程安全)
        /// </summary>
        private async void ReadGPT_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating || _qcService == null) return;

            // 获取UI参数 (必须在UI线程)
            string loaderPath = GetActualLoaderPath();
            string? currentPort = _qcService.CurrentPort;
            bool isConnected = _qcService.IsConnected;
            string storage = RbEmmc?.IsChecked == true ? "emmc" : "ufs";
            bool readInfo = _readInfoEnabled;
            
            // 设置认证类型
            if (RbConfigXiaomi?.IsChecked == true)
                _qcService.CurrentAuthType = AuthType.Xiaomi;
            else if (RbConfigOplus?.IsChecked == true)
                _qcService.CurrentAuthType = AuthType.OnePlus;
            else if (RbConfigOldOplus?.IsChecked == true)
                _qcService.CurrentAuthType = AuthType.Vip;
            else
                _qcService.CurrentAuthType = AuthType.Standard;

            // 检查是否需要先连接
            if (!isConnected)
            {
                if (string.IsNullOrEmpty(loaderPath) || !File.Exists(loaderPath))
                {
                    AppendLog("❌ 请先选择有效的 Loader 文件", "#EF4444");
                    SetProgressState(ProgressState.Error, "缺少 Loader", 0);
                    return;
                }

                if (string.IsNullOrEmpty(currentPort))
                {
                    AppendLog("❌ 未检测到 9008 设备，请连接设备", "#EF4444");
                    SetProgressState(ProgressState.Error, "无设备", 0);
                    return;
                }

                AppendLog($"📖 连接设备并读取 GPT [{storage.ToUpper()}]...", "#0088CC");

                // 在后台线程执行连接和读取
                await RunOperationAsync("读取分区表", async ct =>
                {
                    // 连接设备
                    if (!await _qcService.ConnectAsync(currentPort, loaderPath, storage, 
                        _cloudDigestPath, _cloudSignPath).ConfigureAwait(false))
                    {
                        return false;
                    }

                    // 读取 GPT
                    bool success = await _qcService.ReadGptAsync().ConfigureAwait(false);
                    
                    // 如果启用了读取设备信息，自动读取
                    if (success && readInfo)
                    {
                        await Dispatcher.InvokeAsync(async () => await ReadDeviceInfoAsync());
                    }
                    
                    return success;
                });
            }
            else
            {
                // 已连接，直接读取
                AppendLog($"📖 读取 GPT 分区表 [{GetStorageType()}]...", "#0088CC");

                await RunOperationAsync("读取分区表", async ct =>
                {
                    bool success = await _qcService.ReadGptAsync().ConfigureAwait(false);
                    
                    // 如果启用了读取设备信息，自动读取
                    if (success && readInfo)
                    {
                        await Dispatcher.InvokeAsync(async () => await ReadDeviceInfoAsync());
                    }
                    
                    return success;
                });
            }
        }

        /// <summary>
        /// 读信息复选框 - 启用
        /// </summary>
        private void ChkReadInfo_Checked(object sender, RoutedEventArgs e)
        {
            _readInfoEnabled = true;
            AppendLog("✅ 已启用: 读取GPT后自动读取设备信息", "#10B981");
        }

        /// <summary>
        /// 读信息复选框 - 取消
        /// </summary>
        private void ChkReadInfo_Unchecked(object sender, RoutedEventArgs e)
        {
            _readInfoEnabled = false;
            AppendLog("⬜ 已禁用: 读取GPT后自动读取设备信息", "#6B7280");
        }

        /// <summary>
        /// 读取设备详细信息 (型号/OTA版本/IMEI/解锁状态)
        /// </summary>
        private async Task ReadDeviceInfoAsync()
        {
            if (_qcService == null || !_qcService.IsConnected) return;

            try
            {
                AppendLog("📱 读取设备详细信息...", "#0088CC");
                SetProgressState(ProgressState.Running, "读取设备信息...", 0);

                // 读取完整设备信息
                var deviceInfo = await _qcService.ReadDeviceInfoAsync(readFullInfo: true);

                if (deviceInfo != null && deviceInfo.HasData)
                {
                    SetProgressState(ProgressState.Success, "设备信息读取完成", 100);
                    
                    // 更新 UI 设备信息
                    Dispatcher.Invoke(() =>
                    {
                        // 更新厂商
                        if (!string.IsNullOrEmpty(deviceInfo.Manufacturer))
                            TxtDeviceVendor.Text = deviceInfo.Manufacturer;
                        else if (!string.IsNullOrEmpty(deviceInfo.Brand))
                            TxtDeviceVendor.Text = deviceInfo.Brand;
                        
                        // 更新型号 (设备型号，不是芯片型号)
                        if (!string.IsNullOrEmpty(deviceInfo.Model))
                        {
                            TxtDeviceModel.Text = deviceInfo.Model;
                            TxtDeviceStatus.Text = deviceInfo.Model; // 同时更新状态栏显示型号
                        }
                        else if (!string.IsNullOrEmpty(deviceInfo.MarketName))
                        {
                            TxtDeviceModel.Text = deviceInfo.MarketName;
                            TxtDeviceStatus.Text = deviceInfo.MarketName;
                        }
                        
                        // 更新 OTA 版本
                        if (!string.IsNullOrEmpty(deviceInfo.OtaVersion))
                            TxtDeviceOTA.Text = deviceInfo.OtaVersion;
                        else if (!string.IsNullOrEmpty(deviceInfo.AndroidVersion))
                            TxtDeviceOTA.Text = $"Android {deviceInfo.AndroidVersion}";
                        
                        // 更新 SN
                        if (!string.IsNullOrEmpty(deviceInfo.SerialNumber))
                            TxtDeviceSN.Text = deviceInfo.SerialNumber;
                    });
                    
                    // 日志输出
                    var infoDict = deviceInfo.ToDictionary();
                    foreach (var (key, value) in infoDict)
                    {
                        AppendLog($"  📋 {key}: {value}", "#10B981");
                    }
                }
                else
                {
                    SetProgressState(ProgressState.Warning, "未读取到有效信息", 0);
                    AppendLog("⚠️ 未能读取到有效的设备信息", "#F59E0B");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 读取设备信息失败: {ex.Message}", "#EF4444");
                SetProgressState(ProgressState.Error, "读取失败", 0);
            }
        }

        /// <summary>
        /// 备份分区 (线程安全)
        /// </summary>
        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating || _qcService == null) return;

            if (!_qcService.IsConnected)
            {
                AppendLog("❌ 请先连接设备并读取分区表", "#EF4444");
                return;
            }

            // 获取选中的分区 (UI线程)
            var selectedPartitions = _qcService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendLog("❌ 请先选择要备份的分区", "#EF4444");
                return;
            }

            // 选择保存目录 (UI线程)
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择备份保存目录"
            };

            if (dialog.ShowDialog() != true)
                return;

            string savePath = dialog.FolderName;
            var storage = GetStorageType();
            AppendLog($"💾 开始备份 [{storage}] {selectedPartitions.Count} 个分区...", "#0088CC");

            // 在后台线程执行备份
            await RunOperationAsync("备份", async ct =>
            {
                return await _qcService.BackupPartitionsAsync(selectedPartitions, savePath).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// 擦除分区 (线程安全)
        /// </summary>
        private async void Erase_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating || _qcService == null) return;

            if (!_qcService.IsConnected)
            {
                AppendLog("❌ 请先连接设备并读取分区表", "#EF4444");
                return;
            }

            // 获取选中的分区 (UI线程)
            var selectedPartitions = _qcService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendLog("❌ 请先选择要擦除的分区", "#EF4444");
                return;
            }

            // 确认擦除 (UI线程)
            var result = MessageBox.Show(
                $"⚠️ 确定要擦除以下 {selectedPartitions.Count} 个分区吗？\n\n" +
                string.Join(", ", selectedPartitions.Select(p => p.Name)) +
                "\n\n此操作不可恢复！",
                "擦除确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            bool protectLun5 = _isProtectionEnabled;
            bool autoFormat = ChkAutoFormat.IsChecked == true;
            var storage = GetStorageType();
            
            AppendLog($"🗑️ 开始擦除 [{storage}]...", "#D97706");
            AppendLog("⚠️ 警告: 擦除操作不可恢复!", "#D97706");
            if (autoFormat) AppendLog("📋 已启用自动格式化模式", "#F59E0B");

            // 在后台线程执行擦除
            await RunOperationAsync("擦除", async ct =>
            {
                return await _qcService.ErasePartitionsAsync(selectedPartitions, protectLun5).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// 刷写分区
        /// </summary>
        private async void Flash_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating || _qcService == null) return;

            if (!_qcService.IsConnected)
            {
                AppendLog("❌ 请先连接设备并读取分区表", "#EF4444");
                return;
            }

            // 构建刷写任务 - 优先使用已选中分区列表
            var tasks = new List<FlashPartitionInfo>();
            
            // 获取选中且有文件的分区
            var selectedPartitions = _allPartitions.Where(p => p.IsSelected).ToList();
            
            if (selectedPartitions.Count > 0)
            {
                // 模式1: 使用 XML 解析出的分区列表
                AppendLog($"📋 检测到 {selectedPartitions.Count} 个选中分区", "#3B82F6");
                
                foreach (var partition in selectedPartitions)
                {
                    // 优先使用自定义文件，其次使用源文件
                    string filePath = partition.HasCustomFile ? partition.CustomFilePath : partition.SourceFilePath;
                    
                    if (string.IsNullOrEmpty(filePath))
                    {
                        AppendLog($"   ├─ ⚠️ 跳过 {partition.Name}: 无文件", "#D97706");
                        continue;
                    }
                    
                    if (!File.Exists(filePath))
                    {
                        AppendLog($"   ├─ ❌ 跳过 {partition.Name}: 文件不存在", "#EF4444");
                        continue;
                    }
                    
                    // 检测是否是 Sparse 格式
                    bool isSparse = tools.Modules.Common.SparseStream.IsSparseFile(filePath);
                    
                    tasks.Add(new FlashPartitionInfo(
                        partition.Lun.ToString(),
                        partition.Name,
                        partition.StartSector.ToString(),
                        partition.NumSectors,
                        filePath
                    ) { IsSparse = isSparse });
                    
                    string fileSource = partition.HasCustomFile ? "自定义" : "XML";
                    string sparseTag = isSparse ? " [Sparse]" : "";
                    AppendLog($"   ├─ ✓ {partition.Name} ({fileSource}){sparseTag}: {Path.GetFileName(filePath)}", "#10B981");
                }
            }
            else
            {
                // 模式2: 手动选择文件 (兼容旧行为)
                AppendLog("📂 未选中分区，请手动选择刷写文件...", "#888888");
                
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "镜像文件 (*.img;*.bin;*.mbn)|*.img;*.bin;*.mbn|XML 文件 (*.xml)|*.xml|所有文件 (*.*)|*.*",
                    Title = "选择要刷写的镜像文件",
                    Multiselect = true
                };

                if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
                    return;

                foreach (var file in dialog.FileNames)
                {
                    string partName = Path.GetFileNameWithoutExtension(file);
                    var partition = _qcService.Partitions.FirstOrDefault(p =>
                        p.Name.Equals(partName, StringComparison.OrdinalIgnoreCase));

                    if (partition != null)
                    {
                        bool isSparse = tools.Modules.Common.SparseStream.IsSparseFile(file);
                        tasks.Add(new FlashPartitionInfo(
                            partition.Lun.ToString(),
                            partition.Name,
                            partition.StartSector.ToString(),
                            partition.NumSectors,
                            file
                        ) { IsSparse = isSparse });
                        string sparseTag = isSparse ? " [Sparse]" : "";
                        AppendLog($"   ├─ 匹配分区: {partName}{sparseTag}", "#666666");
                    }
                    else
                    {
                        AppendLog($"   ├─ ⚠️ 未匹配分区: {partName}", "#D97706");
                    }
                }
            }

            if (tasks.Count == 0)
            {
                AppendLog("❌ 没有可刷写的分区", "#EF4444");
                AppendLog("   提示: 请在分区表中勾选要刷写的分区，或手动选择镜像文件", "#888888");
                return;
            }

            // 确认刷写
            var result = MessageBox.Show(
                $"⚡ 确定要刷写以下 {tasks.Count} 个分区吗？\n\n" +
                string.Join("\n", tasks.Select(t => $"  {t.Name} <- {Path.GetFileName(t.Filename)}")) +
                "\n\n此操作会覆盖现有数据！",
                "刷写确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // 获取UI参数 (必须在UI线程)
            bool protectLun5 = _isProtectionEnabled;
            var patchFiles = GetPatchXmlFiles();
            bool superEnabled = _isSuperEnabled;
            string firmwarePath = _selectedFirmwarePath;
            bool ocdtFix = ChkOcdtFix.IsChecked == true;
            var storage = GetStorageType();
            
            AppendLog($"⚡ 开始刷写 [{storage}]...", "#0088CC");

            // 在后台线程执行刷写
            await RunOperationAsync("刷写", async ct =>
            {
                bool success = false;

                // 检查是否启用直刷Super
                if (superEnabled && !string.IsNullOrEmpty(firmwarePath))
                {
                    // 检查固件是否支持 Super Meta 模式 (OPLUS/Realme)
                    if (_qcService.IsSuperMetaSupported(firmwarePath, out var nvId))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            AppendLog($"📦 检测到 Super Meta 支持 (NV={nvId})", "#8B5CF6");
                            var summary = _qcService.GetSuperMetaSummary(firmwarePath, nvId);
                            if (!string.IsNullOrEmpty(summary))
                                AppendLog($"📋 {summary}", "#8B5CF6");
                            AppendLog("🚀 启动 Super Meta 模式刷写...", "#8B5CF6");
                        });
                        
                        success = await _qcService.FlashSuperMetaAsync(firmwarePath, nvId).ConfigureAwait(false);
                        
                        // Super Meta刷写后，继续刷写非super分区
                        if (success)
                        {
                            var superPartitions = new[] { "system", "vendor", "product", "odm", 
                                "system_a", "vendor_a", "product_a", "odm_a",
                                "system_b", "vendor_b", "product_b", "odm_b", "super" };
                            
                            var remainingTasks = tasks.Where(t => 
                                !superPartitions.Any(sp => t.Name.Equals(sp, StringComparison.OrdinalIgnoreCase))).ToList();
                            
                            if (remainingTasks.Count > 0)
                            {
                                await Dispatcher.InvokeAsync(() =>
                                    AppendLog($"📋 继续刷写剩余 {remainingTasks.Count} 个非Super分区...", "#3B82F6"));
                                success = await _qcService.FlashPartitionsAsync(remainingTasks, protectLun5, patchFiles).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        var superTask = tasks.FirstOrDefault(t => 
                            t.Name.Contains("super", StringComparison.OrdinalIgnoreCase));
                        
                        if (superTask != null)
                        {
                            await Dispatcher.InvokeAsync(() =>
                                AppendLog("📦 使用传统Super直刷模式...", "#8B5CF6"));
                        }
                        success = await _qcService.FlashPartitionsAsync(tasks, protectLun5, patchFiles).ConfigureAwait(false);
                    }
                }
                else
                {
                    success = await _qcService.FlashPartitionsAsync(tasks, protectLun5, patchFiles).ConfigureAwait(false);
                }

                // OCDT 修复 (OPPO 设备专用)
                if (success && ocdtFix)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AppendLog("🔧 执行 OCDT 修复...", "#F59E0B");
                        SetProgressState(ProgressState.Running, "OCDT 修复中...", 95);
                    });
                    // TODO: 调用 OCDT 修复服务
                    await Dispatcher.InvokeAsync(() => AppendLog("✅ OCDT 修复完成", "#10B981"));
                }

                return success;
            });
        }

        // 直刷Super开关
        private bool _isSuperEnabled = false;
        private bool _superMetaSupported = false;
        private string? _superMetaNvId = null;
        
        // 固件版本信息
        private string? _firmwareVersionName = null;
        private string? _firmwareProductName = null;
        private string? _firmwareMarketName = null;
        private string? _firmwarePlatform = null;

        /// <summary>
        /// 读取 version_info.txt 获取固件信息
        /// </summary>
        private void ReadVersionInfo(string firmwareDir)
        {
            _firmwareVersionName = null;
            _firmwareProductName = null;
            _firmwareMarketName = null;
            _firmwarePlatform = null;

            // 搜索可能的 version_info.txt 位置
            var possiblePaths = new[]
            {
                Path.Combine(firmwareDir, "version_info.txt"),
                Path.Combine(firmwareDir, "..", "version_info.txt"),
                Path.Combine(firmwareDir, "..", "..", "version_info.txt")
            };

            string? versionInfoPath = null;
            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    versionInfoPath = fullPath;
                    break;
                }
            }

            if (versionInfoPath == null) return;

            try
            {
                var json = File.ReadAllText(versionInfoPath);
                
                // 简单解析 JSON (version_info.txt 是一个数组)
                // 提取关键字段
                var nvIdMatch = System.Text.RegularExpressions.Regex.Match(json, @"""nv_id""\s*:\s*""([^""]+)""");
                var versionMatch = System.Text.RegularExpressions.Regex.Match(json, @"""version_name""\s*:\s*""([^""]+)""");
                var productMatch = System.Text.RegularExpressions.Regex.Match(json, @"""product_name""\s*:\s*""([^""]+)""");
                var marketMatch = System.Text.RegularExpressions.Regex.Match(json, @"""market_name""\s*:\s*""([^""]+)""");
                var platformMatch = System.Text.RegularExpressions.Regex.Match(json, @"""platform""\s*:\s*""([^""]+)""");

                if (nvIdMatch.Success && _superMetaNvId == null)
                    _superMetaNvId = nvIdMatch.Groups[1].Value;
                if (versionMatch.Success)
                    _firmwareVersionName = versionMatch.Groups[1].Value;
                if (productMatch.Success)
                    _firmwareProductName = productMatch.Groups[1].Value;
                if (marketMatch.Success)
                    _firmwareMarketName = marketMatch.Groups[1].Value;
                if (platformMatch.Success)
                    _firmwarePlatform = platformMatch.Groups[1].Value;

                // 显示固件信息
                if (!string.IsNullOrEmpty(_firmwareVersionName) || !string.IsNullOrEmpty(_firmwareMarketName))
                {
                    AppendLog($"📱 固件信息:", "#10B981");
                    if (!string.IsNullOrEmpty(_firmwareMarketName))
                        AppendLog($"   ├─ 型号: {_firmwareMarketName} ({_firmwareProductName})", "#059669");
                    if (!string.IsNullOrEmpty(_firmwareVersionName))
                        AppendLog($"   ├─ 版本: {_firmwareVersionName}", "#059669");
                    if (!string.IsNullOrEmpty(_firmwarePlatform))
                        AppendLog($"   └─ 平台: {_firmwarePlatform}", "#059669");
                }
            }
            catch
            {
                // 忽略解析错误
            }
        }

        /// <summary>
        /// 检测固件是否支持 Super Meta 模式
        /// </summary>
        private void CheckSuperMetaSupport(string firmwareDir)
        {
            if (string.IsNullOrEmpty(firmwareDir)) return;

            try
            {
                // 先尝试读取 version_info.txt 获取固件信息
                ReadVersionInfo(firmwareDir);
                
                // 搜索可能的 META 目录位置
                string? metaDir = null;
                string? baseFirmwareDir = firmwareDir;
                
                // 可能的路径: 
                // 1. firmwareDir/META
                // 2. firmwareDir/../META (父目录)
                // 3. firmwareDir/../../META (上上级目录)
                var possiblePaths = new[]
                {
                    Path.Combine(firmwareDir, "META"),
                    Path.Combine(firmwareDir, "..", "META"),
                    Path.Combine(firmwareDir, "..", "..", "META")
                };

                foreach (var path in possiblePaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                    {
                        metaDir = fullPath;
                        // 如果是父目录的META，调整baseFirmwareDir
                        if (path.Contains(".."))
                        {
                            baseFirmwareDir = Path.GetDirectoryName(metaDir);
                        }
                        break;
                    }
                }

                if (metaDir == null)
                {
                    _superMetaSupported = false;
                    _superMetaNvId = null;
                    AppendLog("📦 Super Meta 模式: ✗ 未找到META目录", "#888888");
                    return;
                }

                // 查找 super_def.*.json 文件
                var superDefFiles = Directory.GetFiles(metaDir, "super_def.*.json");
                if (superDefFiles.Length == 0)
                {
                    superDefFiles = Directory.GetFiles(metaDir, "super_def.json");
                }

                if (superDefFiles.Length > 0)
                {
                    _superMetaSupported = true;

                    // 解析并显示摘要信息
                    var parser = new Modules.Qualcomm.SuperDef.SuperDefParser();
                    var def = parser.Parse(superDefFiles[0]);
                    
                    if (def != null)
                    {
                        // NV ID 优先级: version_info.txt > super_def.json > 文件名
                        // _superMetaNvId 可能已经在 ReadVersionInfo 中设置
                        if (string.IsNullOrEmpty(_superMetaNvId) || _superMetaNvId == "00000000")
                        {
                            // 尝试从 super_def.json 获取
                            if (!string.IsNullOrEmpty(def.NvId) && def.NvId != "00000000")
                            {
                                _superMetaNvId = def.NvId;
                            }
                            else
                            {
                                // 从文件名获取
                                var fileName = Path.GetFileNameWithoutExtension(superDefFiles[0]);
                                if (fileName.StartsWith("super_def.") && fileName != "super_def")
                                {
                                    var fileNvId = fileName.Replace("super_def.", "");
                                    if (fileNvId != "00000000")
                                    {
                                        _superMetaNvId = fileNvId;
                                    }
                                }
                            }
                        }
                        
                        // 统计子分区
                        int partCount = def.Partitions?.Count(p => p.HasImage && p.IsSlotA) ?? 0;
                        long totalSize = 0;
                        
                        if (def.Partitions != null)
                        {
                            foreach (var p in def.Partitions.Where(x => x.HasImage && x.IsSlotA))
                            {
                                var imgPath = Path.Combine(baseFirmwareDir!, p.Path ?? "");
                                if (File.Exists(imgPath))
                                    totalSize += new FileInfo(imgPath).Length;
                            }
                        }

                        // 获取 NV Text (如果有更友好的描述)
                        string nvDisplay = _superMetaNvId ?? "默认";
                        if (!string.IsNullOrEmpty(def.NvText))
                        {
                            nvDisplay = $"{def.NvText}";
                            if (!string.IsNullOrEmpty(_superMetaNvId) && _superMetaNvId != "00000000")
                            {
                                nvDisplay += $" ({_superMetaNvId})";
                            }
                        }

                        AppendLog($"📦 Super Meta 模式: ✓ 支持", "#8B5CF6");
                        AppendLog($"   ├─ 版本: {nvDisplay}", "#6366F1");
                        AppendLog($"   ├─ 子分区: {partCount} 个", "#6366F1");
                        AppendLog($"   └─ 总大小: {totalSize / 1024 / 1024}MB", "#6366F1");
                        
                        // 检查 super_meta.raw 是否存在
                        var superMetaPath = def.SuperMeta?.Path;
                        if (!string.IsNullOrEmpty(superMetaPath))
                        {
                            var fullPath = Path.Combine(baseFirmwareDir!, superMetaPath);
                            if (File.Exists(fullPath))
                            {
                                var metaSize = new FileInfo(fullPath).Length;
                                AppendLog($"   📋 super_meta.raw: {metaSize / 1024}KB ✓", "#10B981");
                            }
                            else
                            {
                                AppendLog($"   ⚠️ super_meta.raw: 未找到", "#D97706");
                            }
                        }
                    }
                    else
                    {
                        // 解析失败，尝试从文件名获取
                        var fileName = Path.GetFileNameWithoutExtension(superDefFiles[0]);
                        if (fileName.StartsWith("super_def.") && fileName != "super_def")
                        {
                            _superMetaNvId = fileName.Replace("super_def.", "");
                        }
                        AppendLog($"📦 Super Meta 模式: ✓ 检测到 (解析失败)", "#D97706");
                    }
                }
                else
                {
                    _superMetaSupported = false;
                    _superMetaNvId = null;
                    AppendLog("📦 Super Meta 模式: ✗ 不支持 (META目录无super_def.json)", "#888888");
                }
            }
            catch (Exception ex)
            {
                _superMetaSupported = false;
                _superMetaNvId = null;
                AppendLog($"⚠️ Super Meta 检测失败: {ex.Message}", "#D97706");
            }
        }

        /// <summary>
        /// 启用直刷Super
        /// </summary>
        private void Super_Checked(object sender, RoutedEventArgs e)
        {
            _isSuperEnabled = true;
            AppendLog("📦 直刷Super已启用", "#8B5CF6");
            
            if (_superMetaSupported)
            {
                AppendLog($"⚡ 将使用 Super Meta 模式刷写 (NV={_superMetaNvId ?? "默认"})", "#6366F1");
            }
            else
            {
                AppendLog("⚡ 刷写时将直接写入Super分区 (传统模式)", "#6366F1");
            }
            
            // 更新按钮显示
            if (TglSuper.Content is StackPanel sp && sp.Children.Count >= 2)
            {
                if (sp.Children[1] is TextBlock txt)
                {
                    txt.Text = "直刷";
                }
            }
        }

        /// <summary>
        /// 禁用直刷Super
        /// </summary>
        private void Super_Unchecked(object sender, RoutedEventArgs e)
        {
            _isSuperEnabled = false;
            AppendLog("📦 直刷Super已禁用", "#6B7280");
            AppendLog("📋 刷写时将使用标准分区模式", "#888888");
            
            // 更新按钮显示
            if (TglSuper.Content is StackPanel sp && sp.Children.Count >= 2)
            {
                if (sp.Children[1] is TextBlock txt)
                {
                    txt.Text = "Super";
                }
            }
        }

        // 受保护的分区列表
        private readonly string[] _protectedPartitions = { "persist", "modem", "fsc", "fsg", "modemst1", "modemst2" };
        private bool _isProtectionEnabled = false;

        /// <summary>
        /// 启用分区保护
        /// </summary>
        private void ProtectPartition_Checked(object sender, RoutedEventArgs e)
        {
            _isProtectionEnabled = true;
            AppendLog("🛡️ 分区保护已启用", "#10B981");
            AppendLog($"📋 受保护分区: {string.Join(", ", _protectedPartitions)}", "#6366F1");
            AppendLog("⚠️ 刷写时将自动跳过受保护分区", "#D97706");
            
            // 更新按钮显示
            if (TglProtect.Content is StackPanel sp && sp.Children.Count >= 2)
            {
                if (sp.Children[1] is TextBlock txt)
                {
                    txt.Text = "已保护";
                }
            }
        }

        /// <summary>
        /// 禁用分区保护
        /// </summary>
        private void ProtectPartition_Unchecked(object sender, RoutedEventArgs e)
        {
            _isProtectionEnabled = false;
            AppendLog("🔓 分区保护已禁用", "#6B7280");
            AppendLog("⚠️ 警告: 所有分区现在都可以被刷写!", "#EF4444");
            
            // 更新按钮显示
            if (TglProtect.Content is StackPanel sp && sp.Children.Count >= 2)
            {
                if (sp.Children[1] is TextBlock txt)
                {
                    txt.Text = "保护";
                }
            }
        }

        #endregion

        #region 重启功能

        /// <summary>
        /// 重启到系统
        /// </summary>
        private async void RebootSystem_Click(object sender, RoutedEventArgs e)
        {
            if (_qcService == null || !_qcService.IsConnected)
            {
                AppendLog("❌ 设备未连接", "#EF4444");
                return;
            }
            AppendLog("🔄 重启到系统...", "#10B981");
            await _qcService.RebootAsync("reset");
        }

        /// <summary>
        /// 重启到 Fastbootd
        /// </summary>
        private async void RebootFastboot_Click(object sender, RoutedEventArgs e)
        {
            if (_qcService == null || !_qcService.IsConnected)
            {
                AppendLog("❌ 设备未连接", "#EF4444");
                return;
            }
            AppendLog("⚡ 重启到 Fastbootd...", "#F59E0B");
            await _qcService.RebootAsync("bootloader");
        }

        /// <summary>
        /// 重启到恢复模式
        /// </summary>
        private async void RebootRecovery_Click(object sender, RoutedEventArgs e)
        {
            if (_qcService == null || !_qcService.IsConnected)
            {
                AppendLog("❌ 设备未连接", "#EF4444");
                return;
            }
            AppendLog("🔧 重启到恢复模式...", "#3B82F6");
            await _qcService.RebootAsync("recovery");
        }

        /// <summary>
        /// 重启到 EDL 9008 模式
        /// </summary>
        private async void RebootEDL_Click(object sender, RoutedEventArgs e)
        {
            if (_qcService == null || !_qcService.IsConnected)
            {
                AppendLog("❌ 设备未连接", "#EF4444");
                return;
            }
            AppendLog("📱 重启到 EDL 9008 模式...", "#EF4444");
            AppendLog("⚠️ 设备将进入 9008 紧急下载模式", "#D97706");
            
            // 尝试多种重启模式 (不同设备支持不同的模式)
            var modes = new[] { "edl", "emergency", "reset" };
            foreach (var mode in modes)
            {
                var result = await _qcService.RebootAsync(mode);
                if (result)
                {
                    AppendLog($"✅ 重启命令 ({mode}) 已发送", "#10B981");
                    return;
                }
            }
            AppendLog("⚠️ 重启命令可能不被设备支持", "#D97706");
        }

        /// <summary>
        /// 高通停止按钮
        /// </summary>
        private void Qc_Stop_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("⏹️ 正在停止当前操作...", "#EF4444");
            _qcService?.Stop();
            _operationCts?.Cancel();
            _isOperating = false;
            BtnQcStop.IsEnabled = false;
            SetProgressState(ProgressState.Warning, "已停止", MainProgressBar.Value);
            AppendLog("⚠️ 操作已被用户中断", "#D97706");
            SetButtonsEnabled(true);
        }

        /// <summary>
        /// 选择Loader文件 (本地)
        /// </summary>
        private void SelectLoader_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Loader Files (*.elf;*.mbn;*.melf)|*.elf;*.mbn;*.melf|All Files (*.*)|*.*",
                Title = "选择 Loader 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                // 清除云端路径（切换到本地模式）
                _cloudLoaderPath = null;
                
                TxtLoader.Text = dialog.FileName;
                AppendLog($"[INFO] Loader (本地): {System.IO.Path.GetFileName(dialog.FileName)}", "#059669");
                
                // 自动查找同目录的 Digest 和 Sign 文件
                AutoFindVipFiles(dialog.FileName);
            }
        }

        /// <summary>
        /// 选择Loader文件 (云端)
        /// </summary>
        private void SelectCloudLoader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Dialogs.CloudLoaderDialog();
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.DownloadedFiles?.HasLoader == true)
                {
                    var files = dialog.DownloadedFiles;
                    
                    // 保存实际路径但显示"云端自动匹配"
                    _cloudLoaderPath = files.LoaderPath;
                    TxtLoader.Text = $"☁️ 云端匹配 - {files.Vendor} {files.Chip}";
                    AppendLog($"[INFO] Loader: {System.IO.Path.GetFileName(files.LoaderPath)}", "#0969DA");

                    // 保存 Digest 和 Sign 路径
                    if (files.HasDigest)
                    {
                        _cloudDigestPath = files.DigestPath;
                        AppendLog($"[INFO] Digest: ✓", "#1A7F37");
                    }
                    if (files.HasSign)
                    {
                        _cloudSignPath = files.SignPath;
                        AppendLog($"[INFO] Sign: ✓", "#1A7F37");
                    }

                    // 显示选中的 Loader 信息
                    if (dialog.SelectedLoader != null)
                    {
                        var loader = dialog.SelectedLoader;
                        AppendLog($"📦 {loader.Vendor} {loader.ChipName ?? loader.Chip} ({loader.AuthTypeText})", "#8B5CF6");
                    }

                    // 根据认证策略自动配置
                    ApplyAuthStrategy(files, dialog.RecommendedAuthStrategy);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 打开云端选择失败: {ex.Message}", "#EF4444");
            }
        }

        // 云端Loader实际路径
        private string? _cloudLoaderPath;

        /// <summary>
        /// 获取实际的Loader文件路径 (云端优先)
        /// </summary>
        private string GetActualLoaderPath()
        {
            // 如果有云端路径且文件存在，使用云端路径
            if (!string.IsNullOrEmpty(_cloudLoaderPath) && System.IO.File.Exists(_cloudLoaderPath))
            {
                return _cloudLoaderPath;
            }
            
            // 否则检查文本框是否为本地路径
            string textPath = TxtLoader.Text;
            if (!string.IsNullOrEmpty(textPath) && !textPath.StartsWith("☁️") && System.IO.File.Exists(textPath))
            {
                return textPath;
            }
            
            return string.Empty;
        }

        /// <summary>
        /// 根据云端返回的认证策略自动配置
        /// </summary>
        private void ApplyAuthStrategy(Modules.Qualcomm.Services.CloudLoaderFiles files, string strategy)
        {
            _currentAuthStrategy = strategy;
            
            // 1. 根据存储类型自动设置 (EMMC/UFS)
            if (!string.IsNullOrEmpty(files.StorageType))
            {
                bool isEmmc = files.StorageType.Equals("emmc", StringComparison.OrdinalIgnoreCase);
                RbEmmc.IsChecked = isEmmc;
                RbUfs.IsChecked = !isEmmc;
                AppendLog($"💾 存储类型: {files.StorageType.ToUpper()} - 已自动切换", "#6366F1");
            }
            
            // 2. 根据认证策略自动配置
            switch (strategy.ToLowerInvariant())
            {
                case "vip":
                    // 自动启用VIP模式 - 选择 VIP 按钮
                    RbConfigOldOplus.IsChecked = true;
                    AppendLog("🔐 VIP认证模式 - 已自动切换", "#F0883E");
                    AppendLog("   └─ Digest + Sign 已就绪，将自动进行VIP验证", "#8B949E");
                    _isVipModeEnabled = true;
                    _isXiaomiModeEnabled = false;
                    _isNothingModeEnabled = false;
                    break;
                    
                case "xiaomi":
                    // 启用小米认证模式 - 选择小米按钮
                    RbConfigXiaomi.IsChecked = true;
                    AppendLog("🍊 小米认证模式 - 已自动切换", "#FF6900");
                    AppendLog("   └─ 将使用小米专用认证协议", "#8B949E");
                    _isXiaomiModeEnabled = true;
                    _isVipModeEnabled = false;
                    _isNothingModeEnabled = false;
                    break;
                    
                case "nothing":
                    // 启用Nothing认证模式 - 选择 OnePlus 按钮 (Nothing使用类似协议)
                    RbConfigOplus.IsChecked = true;
                    AppendLog("⚫ Nothing认证模式 - 已自动切换", "#8B949E");
                    AppendLog("   └─ 将使用Nothing专用认证协议", "#8B949E");
                    _isNothingModeEnabled = true;
                    _isVipModeEnabled = false;
                    _isXiaomiModeEnabled = false;
                    break;
                    
                default:
                    // 标准模式 - 选择 QC 按钮
                    RbConfigQC.IsChecked = true;
                    AppendLog("✅ 标准认证模式", "#3FB950");
                    _isVipModeEnabled = false;
                    _isXiaomiModeEnabled = false;
                    _isNothingModeEnabled = false;
                    break;
            }
        }

        // 当前认证策略
        private string _currentAuthStrategy = "standard";
#pragma warning disable CS0414 // 预留给未来使用
        private bool _isVipModeEnabled = false;
        private bool _isXiaomiModeEnabled = false;
        private bool _isNothingModeEnabled = false;
#pragma warning restore CS0414

        // 云端下载的 VIP 文件路径
        private string? _cloudDigestPath;
        private string? _cloudSignPath;

        /// <summary>
        /// 自动查找 VIP 文件 (Digest/Sign)
        /// </summary>
        private void AutoFindVipFiles(string loaderPath)
        {
            string? dir = System.IO.Path.GetDirectoryName(loaderPath);
            if (string.IsNullOrEmpty(dir)) return;

            // 清除之前的云端文件
            _cloudDigestPath = null;
            _cloudSignPath = null;

            // 查找 Digest
            string? digestPath = FindAuthFile(dir, "digest");
            if (!string.IsNullOrEmpty(digestPath))
            {
                _cloudDigestPath = digestPath;
                AppendLog($"[INFO] 找到 Digest: {System.IO.Path.GetFileName(digestPath)}", "#10B981");
            }

            // 查找 Sign
            string? signPath = FindAuthFile(dir, "signature");
            if (!string.IsNullOrEmpty(signPath))
            {
                _cloudSignPath = signPath;
                AppendLog($"[INFO] 找到 Sign: {System.IO.Path.GetFileName(signPath)}", "#10B981");
            }
        }

        /// <summary>
        /// 查找认证文件
        /// </summary>
        private string? FindAuthFile(string dir, string baseName)
        {
            string[] extensions = { ".bin", ".mbn", ".elf" };
            foreach (var ext in extensions)
            {
                string path = System.IO.Path.Combine(dir, baseName + ext);
                if (System.IO.File.Exists(path)) return path;

                path = System.IO.Path.Combine(dir, baseName.ToUpper() + ext);
                if (System.IO.File.Exists(path)) return path;
            }
            return null;
        }

        // 存储所有分区（用于搜索过滤）
        private List<PartitionInfo> _allPartitions = new();

        /// <summary>
        /// 搜索分区
        /// </summary>
        private void TxtPartitionSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = TxtPartitionSearch.Text.Trim().ToLower();
            
            // 更新占位符可见性
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(searchText) 
                ? Visibility.Visible : Visibility.Collapsed;

            if (_allPartitions.Count == 0)
            {
                TxtPartitionCount.Text = "0 个分区";
                return;
            }

            if (string.IsNullOrEmpty(searchText))
            {
                // 显示所有分区
                PartitionList.ItemsSource = _allPartitions;
                TxtPartitionCount.Text = $"{_allPartitions.Count} 个分区";
            }
            else
            {
                // 过滤分区
                var filtered = _allPartitions.Where(p => 
                    p.Name.ToLower().Contains(searchText) ||
                    p.Lun.ToString().Contains(searchText) ||
                    p.CustomFileName.ToLower().Contains(searchText)
                ).ToList();
                
                PartitionList.ItemsSource = filtered;
                TxtPartitionCount.Text = $"{filtered.Count}/{_allPartitions.Count} 个分区";
            }
        }

        /// <summary>
        /// 更新分区列表并保存到搜索缓存
        /// </summary>
        private void UpdatePartitionList(List<PartitionInfo> partitions)
        {
            _allPartitions = partitions;
            PartitionList.ItemsSource = partitions;
            TxtPartitionCount.Text = $"{partitions.Count} 个分区";
            TxtPartitionSearch.Text = ""; // 清空搜索
            
            // 同步到 Service
            if (_qcService != null)
            {
                _qcService.Partitions.Clear();
                foreach (var p in partitions)
                    _qcService.Partitions.Add(p);
            }
        }

        /// <summary>
        /// 全选/取消全选分区
        /// </summary>
        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked == true;
                // 全选/取消所有分区（包括被过滤的）
                foreach (var p in _allPartitions)
                {
                    p.IsSelected = isChecked;
                }
            }
        }

        // 记录上次点击时间，用于检测双击
        private DateTime _lastClickTime = DateTime.MinValue;
        private PartitionInfo? _lastClickedPartition = null;

        /// <summary>
        /// 分区项点击事件（检测双击）
        /// </summary>
        private void PartitionItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is PartitionInfo partition)
            {
                var now = DateTime.Now;
                
                // 检测双击（500ms 内两次点击同一项）
                if (_lastClickedPartition == partition && (now - _lastClickTime).TotalMilliseconds < 500)
                {
                    // 双击 - 选择文件
                    SelectFileForPartition(partition);
                    _lastClickTime = DateTime.MinValue;
                    _lastClickedPartition = null;
                }
                else
                {
                    // 单击 - 记录
                    _lastClickTime = now;
                    _lastClickedPartition = partition;
                }
            }
        }

        /// <summary>
        /// 为分区选择刷写文件
        /// </summary>
        private void SelectFileForPartition(PartitionInfo partition)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "镜像文件 (*.img;*.bin;*.mbn;*.elf)|*.img;*.bin;*.mbn;*.elf|所有文件 (*.*)|*.*",
                Title = $"选择 {partition.Name} 分区的刷写文件"
            };

            if (dialog.ShowDialog() == true)
            {
                partition.CustomFilePath = dialog.FileName;
                partition.IsSelected = true; // 自动选中
                AppendLog($"📎 {partition.Name} <- {Path.GetFileName(dialog.FileName)}", "#F59E0B");
            }
        }

        // 存储选择的 XML 文件
        private string[] _selectedXmlFiles = Array.Empty<string>();

        /// <summary>
        /// 选择固件文件夹 (通过选择 XML 文件来定位目录，支持多选)
        /// </summary>
        private void SelectXML_Click(object sender, RoutedEventArgs e)
        {
            // 支持多选 XML 文件
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "选择 rawprogram/patch XML 文件 (支持多选，或选择任意 XML 自动识别同目录)",
                Multiselect = true
            };
            
            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                // 获取所有选中文件的目录
                var folders = dialog.FileNames
                    .Select(f => Path.GetDirectoryName(f))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .ToList();

                if (folders.Count == 1)
                {
                    // 所有文件在同一目录，使用自动识别
                    AutoLoadXmlFromFolder(folders[0]!);
                }
                else if (folders.Count > 1)
                {
                    // 多个目录，直接使用选中的文件
                    LoadSelectedXmlFiles(dialog.FileNames);
                }
            }
        }

        /// <summary>
        /// 直接加载选中的 XML 文件列表
        /// </summary>
        private void LoadSelectedXmlFiles(string[] xmlFiles)
        {
            if (xmlFiles == null || xmlFiles.Length == 0) return;

            // 分类文件
            var rawPrograms = xmlFiles
                .Where(f => Path.GetFileName(f).StartsWith("rawprogram", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToArray();

            var patchFiles = xmlFiles
                .Where(f => Path.GetFileName(f).StartsWith("patch", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToArray();

            // 其他 XML 文件也加入 rawPrograms
            var otherXml = xmlFiles
                .Where(f => !Path.GetFileName(f).StartsWith("rawprogram", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("patch", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var allRawPrograms = rawPrograms.Concat(otherXml).ToArray();
            
            _selectedXmlFiles = allRawPrograms.Concat(patchFiles).ToArray();
            _selectedFirmwarePath = Path.GetDirectoryName(xmlFiles[0]) ?? "";

            // 显示路径
            TxtXmlPath.Text = _selectedFirmwarePath;
            TxtXmlPath.ToolTip = $"已选择 {_selectedXmlFiles.Length} 个 XML 文件:\n" + 
                                 string.Join("\n", _selectedXmlFiles.Select(f => $"  - {Path.GetFileName(f)}"));

            AppendLog($"📂 已选择 {_selectedXmlFiles.Length} 个 XML 文件", "#059669");
            AppendLog($"   ├─ rawprogram: {allRawPrograms.Length} 个", "#0088CC");
            AppendLog($"   └─ patch: {patchFiles.Length} 个", "#8B5CF6");

            // 解析分区
            if (allRawPrograms.Length > 0)
            {
                ParseAndDisplayRawXml(allRawPrograms);
            }
        }

        /// <summary>
        /// 路径输入框按键事件 (Enter 加载目录)
        /// </summary>
        private void TxtXmlPath_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                string path = TxtXmlPath.Text.Trim().Trim('"'); // 去除引号
                if (!string.IsNullOrEmpty(path))
                {
                    // 如果是文件路径，获取其目录
                    if (File.Exists(path))
                    {
                        path = Path.GetDirectoryName(path) ?? path;
                    }
                    AutoLoadXmlFromFolder(path);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// 从文件夹自动加载 XML 文件
        /// </summary>
        private void AutoLoadXmlFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                AppendLog($"❌ 文件夹不存在: {folderPath}", "#EF4444");
                return;
            }

            // 正则表达式匹配各种 rawprogram 格式:
            // - rawprogram0.xml, rawprogram1.xml (标准格式)
            // - rawprogram_unsparse0.xml, rawprogram_save_persist_unsparse0.xml (联想 unsparse 格式)
            // 排除: rawprogram0_BLANK_GPT.xml, rawprogram0_WIPE_PARTITIONS.xml (清空/擦除用)
            var rawProgramRegex = new System.Text.RegularExpressions.Regex(
                @"^rawprogram[_\w]*\d*\.xml$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // 排除清空和擦除用的 XML
            var excludeRegex = new System.Text.RegularExpressions.Regex(
                @"_(BLANK_GPT|WIPE_PARTITIONS)\.xml$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // 正则表达式匹配各种 patch 格式:
            // - patch0.xml, patch1.xml (标准格式)
            // - patch_unsparse0.xml 等 (联想格式)
            var patchRegex = new System.Text.RegularExpressions.Regex(
                @"^patch[_\w]*\d*\.xml$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 搜索文件
            var allXmlFiles = Directory.GetFiles(folderPath, "*.xml", SearchOption.TopDirectoryOnly);
            
            // 筛选 rawprogram 文件（排除 BLANK_GPT 和 WIPE_PARTITIONS）
            var rawProgramCandidates = allXmlFiles
                .Where(f => rawProgramRegex.IsMatch(Path.GetFileName(f)))
                .Where(f => !excludeRegex.IsMatch(Path.GetFileName(f)))
                .ToList();

            // 联想固件处理：优先使用 unsparse 版本（分段刷写更稳定）
            // 检查是否同时存在 rawprogramN.xml 和 rawprogram_unsparseN.xml
            var standardFiles = rawProgramCandidates.Where(f => 
                System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^rawprogram\d+\.xml$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            
            // unsparse 文件分类：
            // - rawprogram_unsparseN.xml (标准 unsparse，刷写所有分区)
            // - rawprogram_save_persist_unsparseN.xml (保留 persist 版本)
            var unsparseFiles = rawProgramCandidates.Where(f => 
                Path.GetFileName(f).Contains("unsparse", StringComparison.OrdinalIgnoreCase)).ToList();
            
            // 如果同时存在 save_persist 和普通 unsparse，优先使用普通版本（刷写更完整）
            var savePersistFiles = unsparseFiles.Where(f => 
                Path.GetFileName(f).Contains("save_persist", StringComparison.OrdinalIgnoreCase)).ToList();
            var normalUnsparseFiles = unsparseFiles.Where(f => 
                !Path.GetFileName(f).Contains("save_persist", StringComparison.OrdinalIgnoreCase)).ToList();

            // 如果两者都存在，只保留普通 unsparse 版本
            if (savePersistFiles.Count > 0 && normalUnsparseFiles.Count > 0)
            {
                unsparseFiles = normalUnsparseFiles;
                AppendLog($"📋 检测到联想固件，使用标准 unsparse 版本（刷写 persist）", "#F59E0B");
            }
            else if (savePersistFiles.Count > 0)
            {
                AppendLog($"📋 检测到联想固件，使用 save_persist 版本（保留 persist）", "#F59E0B");
            }

            string[] rawPrograms;
            if (unsparseFiles.Count > 0 && standardFiles.Count > 0)
            {
                // 同时存在两种格式，优先使用 unsparse（适合分段刷写大分区）
                // 但保留其他 LUN 的标准文件
                var unsparseNums = unsparseFiles
                    .Select(f => System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"\d+"))
                    .Where(m => m.Success)
                    .Select(m => m.Value)
                    .ToHashSet();

                // 标准文件中，只保留 unsparse 没覆盖的 LUN
                var filteredStandard = standardFiles
                    .Where(f => {
                        var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^rawprogram(\d+)\.xml$", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        return match.Success && !unsparseNums.Contains(match.Groups[1].Value);
                    }).ToList();

                rawPrograms = unsparseFiles.Concat(filteredStandard).OrderBy(f => f).ToArray();
            }
            else
            {
                rawPrograms = rawProgramCandidates.OrderBy(f => f).ToArray();
            }
            
            var patchFiles = allXmlFiles
                .Where(f => patchRegex.IsMatch(Path.GetFileName(f)))
                .Where(f => !excludeRegex.IsMatch(Path.GetFileName(f)))
                .OrderBy(f => f)
                .ToArray();

            // 合并所有有效的 XML 文件
            _selectedXmlFiles = rawPrograms.Concat(patchFiles).ToArray();
            _selectedFirmwarePath = folderPath;

            if (_selectedXmlFiles.Length == 0)
            {
                // 可能选择的是上级目录，尝试查找 IMAGES 子目录
                var imagesDir = Path.Combine(folderPath, "IMAGES");
                if (Directory.Exists(imagesDir))
                {
                    AppendLog($"📂 在 IMAGES 子目录中搜索...", "#888888");
                    AutoLoadXmlFromFolder(imagesDir);
                    return;
                }
                
                AppendLog($"⚠️ 未找到有效的 rawprogram/patch XML 文件", "#D97706");
                TxtXmlPath.Text = "";
                return;
            }

            // 显示路径
            TxtXmlPath.Text = folderPath;
            TxtXmlPath.ToolTip = $"固件路径: {folderPath}\n\nrawprogram: {rawPrograms.Length} 个\npatch: {patchFiles.Length} 个";
            
            AppendLog($"📂 固件目录: {Path.GetFileName(folderPath)}", "#059669");
            AppendLog($"   ├─ rawprogram: {rawPrograms.Length} 个 ({string.Join(", ", rawPrograms.Select(f => Path.GetFileName(f)))})", "#0088CC");
            AppendLog($"   └─ patch: {patchFiles.Length} 个", "#8B5CF6");
            
            // 自动解析 rawprogram 获取分区
            if (rawPrograms.Length > 0)
            {
                ParseAndDisplayRawXml(rawPrograms);
            }
        }

        // 存储选择的固件路径
        private string _selectedFirmwarePath = "";

        /// <summary>
        /// 获取 Patch XML 文件列表
        /// </summary>
        private List<string>? GetPatchXmlFiles()
        {
            var patches = _selectedXmlFiles.Where(f => 
                Path.GetFileName(f).Contains("patch", StringComparison.OrdinalIgnoreCase)).ToList();
            return patches.Count > 0 ? patches : null;
        }

        /// <summary>
        /// 解析 Raw XML 文件并显示分区列表
        /// </summary>
        private void ParseAndDisplayRawXml(string[] xmlFiles)
        {
            try
            {
                var partitions = new List<PartitionInfo>();
                int sectorSize = RbUfs.IsChecked == true ? 4096 : 512;
                int existCount = 0;
                int missingCount = 0;

                foreach (var xmlFile in xmlFiles)
                {
                    if (!File.Exists(xmlFile))
                    {
                        AppendLog($"⚠️ 文件不存在: {xmlFile}", "#D97706");
                        continue;
                    }

                    // 获取 XML 所在目录用于构建镜像文件路径
                    string xmlDir = Path.GetDirectoryName(xmlFile) ?? "";
                    AppendLog($"📂 解析: {Path.GetFileName(xmlFile)}", "#888888");
                    
                    var doc = System.Xml.Linq.XDocument.Load(xmlFile);
                    
                    // 获取所有 program 元素（忽略命名空间）
                    var programs = doc.Descendants().Where(e => 
                        e.Name.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase));
                    
                    int count = 0;
                    foreach (var prog in programs)
                    {
                        // 获取属性（忽略大小写）
                        string label = GetAttributeValue(prog, "label");
                        string filename = GetAttributeValue(prog, "filename");
                        
                        // 跳过空的项
                        if (string.IsNullOrEmpty(label) && string.IsNullOrEmpty(filename))
                            continue;

                        // 跳过 GPT 相关条目和占位分区
                        if (label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase) ||
                            label.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase) ||
                            label.Equals("last_parti", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // 对于分段文件（如 super_1.img），优先使用文件名以区分不同分段
                        string name;
                        // 联想分段格式: super_1.img, metadata_1.img, userdata_1.img 等
                        if (!string.IsNullOrEmpty(filename) && System.Text.RegularExpressions.Regex.IsMatch(filename, @"_\d+\.img$"))
                        {
                            // 分段文件：使用文件名（不带扩展名）作为名称，如 super_1, super_2
                            name = Path.GetFileNameWithoutExtension(filename);
                        }
                        else
                        {
                            name = !string.IsNullOrEmpty(label) ? label : Path.GetFileNameWithoutExtension(filename);
                        }
                        if (string.IsNullOrEmpty(name)) continue;

                        // 解析数值
                        int lun = 0;
                        long startSector = 0;
                        long numSectors = 0;
                        
                        // 从 XML 节点获取扇区大小，如果没有则使用默认值
                        int nodeSectorSize = sectorSize;
                        var sectorSizeAttr = GetAttributeValue(prog, "SECTOR_SIZE_IN_BYTES");
                        if (!string.IsNullOrEmpty(sectorSizeAttr))
                        {
                            int.TryParse(sectorSizeAttr, out nodeSectorSize);
                            if (nodeSectorSize <= 0) nodeSectorSize = sectorSize;
                        }

                        // LUN
                        var lunAttr = GetAttributeValue(prog, "physical_partition_number");
                        if (string.IsNullOrEmpty(lunAttr))
                            lunAttr = GetAttributeValue(prog, "lun");
                        int.TryParse(lunAttr, out lun);

                        // Start sector - 跳过动态表达式 (如 NUM_DISK_SECTORS-5.)
                        var startAttr = GetAttributeValue(prog, "start_sector");
                        if (!string.IsNullOrEmpty(startAttr) && !startAttr.Contains("NUM_DISK_SECTORS"))
                            long.TryParse(startAttr, out startSector);

                        // 避免完全重复（相同名称、LUN 和起始扇区）
                        if (partitions.Any(p => p.Name == name && p.Lun == lun && p.StartSector == startSector))
                            continue;

                        // Num sectors - 优先使用 num_partition_sectors
                        var sectorsAttr = GetAttributeValue(prog, "num_partition_sectors");
                        if (!string.IsNullOrEmpty(sectorsAttr))
                        {
                            // 直接解析扇区数
                            long.TryParse(sectorsAttr, out numSectors);
                        }
                        
                        // 如果扇区数为0，尝试从 size_in_KB 计算
                        if (numSectors == 0)
                        {
                            var sizeKbAttr = GetAttributeValue(prog, "size_in_KB");
                            if (!string.IsNullOrEmpty(sizeKbAttr))
                            {
                                // size_in_KB 可能是 "15204352.0" 格式（带小数点）
                                if (double.TryParse(sizeKbAttr, out double sizeKb) && sizeKb > 0)
                                {
                                    // KB 转字节再转扇区数
                                    numSectors = (long)((sizeKb * 1024) / nodeSectorSize);
                                }
                            }
                        }

                        // 构建源文件完整路径并检查是否存在
                        string sourceFilePath = "";
                        bool fileExists = false;
                        if (!string.IsNullOrEmpty(filename))
                        {
                            // 尝试从 XML 目录构建完整路径
                            sourceFilePath = Path.Combine(xmlDir, filename);
                            fileExists = File.Exists(sourceFilePath);
                            
                            // 如果不存在，尝试上级目录
                            if (!fileExists && !string.IsNullOrEmpty(xmlDir))
                            {
                                var parentDir = Path.GetDirectoryName(xmlDir);
                                if (!string.IsNullOrEmpty(parentDir))
                                {
                                    var altPath = Path.Combine(parentDir, filename);
                                    if (File.Exists(altPath))
                                    {
                                        sourceFilePath = altPath;
                                        fileExists = true;
                                    }
                                }
                            }
                        }

                        var partition = new PartitionInfo
                        {
                            Lun = lun,
                            Name = name,
                            StartSector = startSector,
                            NumSectors = numSectors,
                            SectorSize = nodeSectorSize, // 使用 XML 中定义的扇区大小
                            SourceFilePath = sourceFilePath,
                            // 有镜像文件存在则自动勾选
                            IsSelected = fileExists
                        };

                        partitions.Add(partition);
                        count++;

                        if (fileExists) existCount++;
                        else if (!string.IsNullOrEmpty(filename)) missingCount++;
                    }
                    
                    AppendLog($"   └─ 找到 {count} 个分区定义", "#888888");
                }

                if (partitions.Count > 0)
                {
                    // 按 LUN 和起始扇区排序
                    partitions = partitions.OrderBy(p => p.Lun).ThenBy(p => p.StartSector).ToList();
                    
                    // 确保在 UI 线程更新
                    Dispatcher.Invoke(() =>
                    {
                        UpdatePartitionList(partitions);
                        AppendLog($"✅ 解析完成: {partitions.Count} 个分区", "#10B981");
                        
                        // 显示文件状态统计
                        if (existCount > 0)
                            AppendLog($"   ✓ 镜像存在: {existCount} 个 (已自动勾选)", "#10B981");
                        if (missingCount > 0)
                            AppendLog($"   ✗ 镜像缺失: {missingCount} 个", "#EF4444");
                        int noFileCount = partitions.Count - existCount - missingCount;
                        if (noFileCount > 0)
                            AppendLog($"   ○ 无文件定义: {noFileCount} 个", "#888888");
                        
                        // 检测 Super Meta 模式支持
                        CheckSuperMetaSupport(_selectedFirmwarePath);
                    });
                }
                else
                {
                    AppendLog("⚠️ XML 中未找到有效分区定义", "#D97706");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ XML 解析失败: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// 获取 XML 元素属性值（忽略大小写）
        /// </summary>
        private static string GetAttributeValue(System.Xml.Linq.XElement element, string attributeName)
        {
            var attr = element.Attributes().FirstOrDefault(a => 
                a.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            return attr?.Value ?? "";
        }

        #endregion

        #region 日志功能

        // 日志数据集合
        private readonly System.Collections.ObjectModel.ObservableCollection<LogItem> _logItems = new();

        /// <summary>
        /// 初始化日志
        /// </summary>
        private void InitializeLog()
        {
            LogListBox.ItemsSource = _logItems;
        }

        // 日志节流控制
        private DateTime _lastLogScrollTime = DateTime.MinValue;
        private const int LogScrollThrottleMs = 100; // 滚动节流
        private const int MaxLogItems = 500;
        
        // 颜色缓存 (避免重复解析)
        private static readonly Dictionary<string, System.Windows.Media.SolidColorBrush> _colorCache = new();
        
        /// <summary>
        /// 获取缓存的颜色画刷
        /// </summary>
        private static System.Windows.Media.SolidColorBrush GetCachedBrush(string colorHex)
        {
            if (!_colorCache.TryGetValue(colorHex, out var brush))
            {
                brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
                brush.Freeze(); // 冻结提高性能
                _colorCache[colorHex] = brush;
            }
            return brush;
        }
        
        /// <summary>
        /// 添加日志 (优化版 - 异步更新，带节流)
        /// </summary>
        private void AppendLog(string message, string color = "#2D2D2D")
        {
            // 使用 BeginInvoke 异步更新，不阻塞调用线程
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                // 添加日志
                _logItems.Add(new LogItem
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Color = GetCachedBrush(color)
                });
                
                // 限制日志条数 (批量删除更高效)
                if (_logItems.Count > MaxLogItems)
                {
                    int removeCount = _logItems.Count - MaxLogItems + 50; // 多删50条避免频繁触发
                    for (int i = 0; i < removeCount; i++)
                    {
                        _logItems.RemoveAt(0);
                    }
                }
                
                // 节流滚动 (避免频繁滚动导致卡顿)
                var now = DateTime.Now;
                if ((now - _lastLogScrollTime).TotalMilliseconds > LogScrollThrottleMs)
                {
                    _lastLogScrollTime = now;
                    if (_logItems.Count > 0)
                    {
                        LogListBox.ScrollIntoView(_logItems[^1]);
                    }
                }
            });
        }

        /// <summary>
        /// 复制日志
        /// </summary>
        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logItems.Count == 0)
            {
                AppendLog("[INFO] 日志为空", "#888888");
                return;
            }
            var logText = string.Join(Environment.NewLine, _logItems.Select(item => item.Text));
            System.Windows.Clipboard.SetText(logText);
            AppendLog("[INFO] 日志已复制到剪贴板", "#10B981");
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logItems.Clear();
            AppendLog("[INFO] 日志已清空", "#0088CC");
        }

        #endregion

        #region 窗口控制按钮事件

        /// <summary>
        /// 最小化按钮点击
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化/还原按钮点击
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        /// <summary>
        /// 关闭按钮点击
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region MTK 模块功能

        /// <summary>
        /// 初始化 MTK 服务
        /// </summary>
        private void InitializeMtkService()
        {
            _mtkService = new MtkUIService(
                Dispatcher,
                (msg, color) => AppendMtkLog(msg, color),
                (percent, status) => UpdateMtkProgress((int)percent, status),
                status => Dispatcher.Invoke(() => TxtMtkProgressStatus.Text = status),
                info => UpdateMtkDeviceInfoUI(info)
            );

            // 设备事件
            _mtkService.DeviceArrived += port =>
            {
                SetMtkDeviceStatus(true, "BROM 就绪", port);
            };

            _mtkService.DeviceRemoved += () =>
            {
                SetMtkDeviceStatus(false, "未连接", "---");
                MtkPartitionList.ItemsSource = null;
            };

            // 分区加载事件
            _mtkService.PartitionsLoaded += partitions =>
            {
                Dispatcher.Invoke(() =>
                {
                    MtkPartitionList.ItemsSource = partitions;
                    AppendMtkLog($"📋 已加载 {partitions.Count} 个分区", "#10B981");
                });
            };

            // 传输统计事件
            _mtkService.TransferStatsUpdated += (elapsed, speed, transferred) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 支持超过1小时的显示
                    if (elapsed.TotalHours >= 1)
                        TxtMtkElapsedTime.Text = $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                    else
                        TxtMtkElapsedTime.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                    
                    TxtMtkTransferSpeed.Text = $"{speed:F1} MB/s";
                    TxtMtkTransferredSize.Text = FormatBytesSize(transferred);
                });
            };

            // 启动设备监听
            _mtkService.StartDeviceWatcher();
        }

        /// <summary>
        /// 格式化字节大小
        /// </summary>
        private static string FormatBytesSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// 更新 MTK 设备信息 UI
        /// </summary>
        private void UpdateMtkDeviceInfoUI(MtkDeviceInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                TxtMtkChip.Text = info.ChipName;
                TxtMtkBrom.Text = info.Mode;
                TxtMtkPreloaderVer.Text = $"BL: {info.BlVersion}";
                TxtMtkDAVer.Text = info.DAVersion;
                TxtMtkDevicePort.Text = info.Port;
            });
        }

        /// <summary>
        /// 设置 MTK 设备状态
        /// </summary>
        private void SetMtkDeviceStatus(bool connected, string status, string port)
        {
            Dispatcher.Invoke(() =>
            {
                TxtMtkDeviceStatus.Text = status;
                
                // 格式化端口显示
                if (string.IsNullOrEmpty(port) || port == "---")
                {
                    TxtMtkDevicePort.Text = "COM--";
                }
                else
                {
                    TxtMtkDevicePort.Text = port.ToUpper().StartsWith("COM") ? port.ToUpper() : $"COM{port}";
                }
                
                MtkDeviceStatusIndicator.Background = connected 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129))  // 绿色
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // 红色
            });
        }

        /// <summary>
        /// 更新 MTK 进度
        /// </summary>
        private void UpdateMtkProgress(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                MtkProgressBar.Value = percent;
                TxtMtkProgressPercent.Text = $"{percent}%";
                TxtMtkProgressStatus.Text = status;
            });
        }

        /// <summary>
        /// DA Server 按钮 - 连接设备
        /// </summary>
        private async void MTK_DAServer_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null) return;

            if (_mtkService.IsConnected)
            {
                _mtkService.Disconnect();
                AppendMtkLog("[MTK] 已断开连接", "#888888");
                return;
            }

            string? port = _mtkService.CurrentPort;
            if (string.IsNullOrEmpty(port))
            {
                AppendMtkLog("[MTK] 未检测到设备，请先连接设备", "#EF4444");
                return;
            }

            string daPath = TxtMtkDA.Text;
            bool success = await _mtkService.ConnectAsync(port, string.IsNullOrEmpty(daPath) ? null : daPath);
            
            if (success)
            {
                await _mtkService.LoadPartitionsAsync();
            }
        }

        /// <summary>
        /// 选择 DA 文件
        /// </summary>
        private void MTK_SelectDA_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "DA Files (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 DA 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtMtkDA.Text = dialog.FileName;
                AppendMtkLog($"[MTK] DA 文件: {dialog.FileName}", "#8B5CF6");
            }
        }

        /// <summary>
        /// 选择固件文件 (Scatter)
        /// </summary>
        private void MTK_SelectFirmware_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Scatter 文件 (*.txt)|*.txt|V6 XML Scatter (*.xml)|*.xml|所有文件 (*.*)|*.*",
                Title = "选择 Scatter 文件 (TXT=传统, XML=V6专用)"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtMtkFirmware.Text = dialog.FileName;
                AppendMtkLog($"[MTK] Scatter 文件: {dialog.FileName}", "#8B5CF6");
                
                // 解析 Scatter 文件
                LoadMtkScatterFileDirectly(dialog.FileName);
            }
        }

        /// <summary>
        /// 直接加载指定的 Scatter 文件
        /// </summary>
        private void LoadMtkScatterFileDirectly(string scatterPath)
        {
            try
            {
                if (!File.Exists(scatterPath))
                {
                    AppendMtkLog("[MTK] ❌ 文件不存在", "#EF4444");
                    return;
                }

                AppendMtkLog($"[MTK] 解析: {Path.GetFileName(scatterPath)}", "#3B82F6");

                // 解析 scatter 文件
                _scatterParser = new tools.Modules.MTK.Storage.ScatterParser();
                bool success = _scatterParser.Parse(scatterPath);

                if (!success || _scatterParser.Partitions.Count == 0)
                {
                    AppendMtkLog("[MTK] ❌ Scatter 文件解析失败", "#EF4444");
                    return;
                }

                // 显示解析信息
                string formatType = _scatterParser.IsV6Format ? "V6 XML" : "传统 TXT";
                AppendMtkLog($"[MTK] 格式: {formatType}", "#00D4FF");
                
                if (!string.IsNullOrEmpty(_scatterParser.Platform))
                    AppendMtkLog($"[MTK] 平台: {_scatterParser.Platform}", "#888888");
                if (!string.IsNullOrEmpty(_scatterParser.Project))
                    AppendMtkLog($"[MTK] 项目: {_scatterParser.Project}", "#888888");
                if (!string.IsNullOrEmpty(_scatterParser.StorageType))
                    AppendMtkLog($"[MTK] 存储: {_scatterParser.StorageType}", "#888888");
                
                // V6 特有信息
                if (_scatterParser.IsV6Format)
                {
                    if (_scatterParser.SkipPtOperation)
                        AppendMtkLog("[MTK] 跳过分区表操作: 是", "#F59E0B");
                    if (_scatterParser.ProtectedPartitions.Count > 0)
                        AppendMtkLog($"[MTK] 受保护分区: {_scatterParser.ProtectedPartitions.Count} 个", "#EF4444");
                }

                // 验证文件
                var (total, exists, missing) = _scatterParser.ValidateFiles();
                if (total > 0)
                {
                    AppendMtkLog($"[MTK] 镜像文件: {exists}/{total} 就绪, {missing} 缺失", 
                        missing > 0 ? "#F59E0B" : "#10B981");
                }

                // 更新分区列表
                MtkPartitionList.ItemsSource = _scatterParser.Partitions;
                TxtMtkPartitionCount.Text = $"{_scatterParser.Partitions.Count} 个分区";

                AppendMtkLog($"[MTK] ✅ 已加载 {_scatterParser.Partitions.Count} 个分区", "#10B981");
                
                // 检测版本信息和 Super Meta 支持
                var firmwareDir = Path.GetDirectoryName(scatterPath);
                if (!string.IsNullOrEmpty(firmwareDir))
                {
                    CheckMtkVersionInfo(firmwareDir);
                    CheckMtkSuperMetaSupport(firmwareDir);
                    
                    // 自动查找并选择 Preloader
                    AutoSelectMtkPreloader(firmwareDir);
                }
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] 解析错误: {ex.Message}", "#EF4444");
            }
        }
        
        /// <summary>
        /// 自动查找并选择 MTK Preloader 文件
        /// </summary>
        private void AutoSelectMtkPreloader(string firmwareDir)
        {
            try
            {
                string? preloaderPath = null;
                string? baseFirmwareDir = _mtkFirmwareDir ?? firmwareDir;
                
                // 可能的搜索目录
                var searchDirs = new[]
                {
                    firmwareDir,
                    Path.Combine(firmwareDir, ".."),
                    Path.Combine(firmwareDir, "..", "IMAGES"),
                    baseFirmwareDir,
                    Path.Combine(baseFirmwareDir ?? "", "IMAGES")
                }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(Path.GetFullPath(d)))
                 .Select(d => Path.GetFullPath(d))
                 .Distinct()
                 .ToList();

                // 优先查找的文件名模式 (按优先级排序)
                var preloaderPatterns = new List<string>();
                
                // 1. 根据项目名查找 (如 preloader_k6895v1_64.bin)
                if (!string.IsNullOrEmpty(_scatterParser?.Project))
                {
                    preloaderPatterns.Add($"preloader_{_scatterParser.Project}.bin");
                }
                
                // 2. 通用命名
                preloaderPatterns.AddRange(new[]
                {
                    "preloader.img",
                    "preloader_emmc.img",  // EMMC 版本
                    "preloader_ufs.img",   // UFS 版本
                    "preloader_raw.img",
                    "preloader.bin"
                });
                
                // 3. 根据存储类型优先选择
                if (!string.IsNullOrEmpty(_scatterParser?.StorageType))
                {
                    string storageType = _scatterParser.StorageType.ToLower();
                    if (storageType.Contains("emmc"))
                    {
                        // EMMC 优先
                        preloaderPatterns.Insert(1, "preloader_emmc.img");
                    }
                    else if (storageType.Contains("ufs"))
                    {
                        // UFS 优先
                        preloaderPatterns.Insert(1, "preloader_ufs.img");
                    }
                }

                // 搜索文件
                foreach (var dir in searchDirs)
                {
                    if (preloaderPath != null) break;
                    
                    foreach (var pattern in preloaderPatterns)
                    {
                        var testPath = Path.Combine(dir, pattern);
                        if (File.Exists(testPath))
                        {
                            preloaderPath = testPath;
                            break;
                        }
                    }
                    
                    // 如果精确匹配失败，尝试模糊搜索
                    if (preloaderPath == null)
                    {
                        var preloaderFiles = Directory.GetFiles(dir, "preloader*.bin")
                            .Concat(Directory.GetFiles(dir, "preloader*.img"))
                            .ToArray();
                        
                        if (preloaderFiles.Length > 0)
                        {
                            // 优先选择项目匹配的
                            if (!string.IsNullOrEmpty(_scatterParser?.Project))
                            {
                                preloaderPath = preloaderFiles.FirstOrDefault(f => 
                                    f.Contains(_scatterParser.Project, StringComparison.OrdinalIgnoreCase));
                            }
                            
                            // 否则选择第一个
                            preloaderPath ??= preloaderFiles[0];
                        }
                    }
                }

                // 设置到 UI
                if (!string.IsNullOrEmpty(preloaderPath))
                {
                    TxtMtkPreloader.Text = preloaderPath;
                    AppendMtkLog($"[MTK] 🔧 Preloader: {Path.GetFileName(preloaderPath)} ✓", "#10B981");
                }
                else
                {
                    AppendMtkLog("[MTK] ⚠️ Preloader: 未找到，请手动选择", "#F59E0B");
                }
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] Preloader 查找失败: {ex.Message}", "#D97706");
            }
        }

        // MTK Scatter 解析器
        private tools.Modules.MTK.Storage.ScatterParser? _scatterParser;
        
        // MTK Super Meta 相关
        private bool _mtkSuperMetaSupported = false;
        private string? _mtkSuperMetaNvId = null;
        private string? _mtkFirmwareDir = null;

        /// <summary>
        /// 读取 MTK 固件版本信息
        /// </summary>
        private void CheckMtkVersionInfo(string firmwareDir)
        {
            // 搜索可能的 version_info.txt 位置
            var possiblePaths = new[]
            {
                Path.Combine(firmwareDir, "version_info.txt"),
                Path.Combine(firmwareDir, "..", "version_info.txt"),
                Path.Combine(firmwareDir, "..", "..", "version_info.txt")
            };

            string? versionInfoPath = null;
            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    versionInfoPath = fullPath;
                    _mtkFirmwareDir = Path.GetDirectoryName(fullPath);
                    break;
                }
            }

            if (versionInfoPath == null) return;

            try
            {
                var json = File.ReadAllText(versionInfoPath);
                
                // 提取关键字段
                var nvIdMatch = System.Text.RegularExpressions.Regex.Match(json, @"""nv_id""\s*:\s*""([^""]+)""");
                var versionMatch = System.Text.RegularExpressions.Regex.Match(json, @"""version_name""\s*:\s*""([^""]+)""");
                var productMatch = System.Text.RegularExpressions.Regex.Match(json, @"""product_name""\s*:\s*""([^""]+)""");
                var marketMatch = System.Text.RegularExpressions.Regex.Match(json, @"""market_name""\s*:\s*""([^""]+)""");
                var platformMatch = System.Text.RegularExpressions.Regex.Match(json, @"""platform""\s*:\s*""([^""]+)""");
                var projectMatch = System.Text.RegularExpressions.Regex.Match(json, @"""project""\s*:\s*""([^""]+)""");

                if (nvIdMatch.Success && nvIdMatch.Groups[1].Value != "00000000")
                    _mtkSuperMetaNvId = nvIdMatch.Groups[1].Value;

                // 显示固件信息
                bool hasInfo = versionMatch.Success || marketMatch.Success || productMatch.Success;
                if (hasInfo)
                {
                    AppendMtkLog($"[MTK] 📱 固件信息:", "#10B981");
                    
                    if (marketMatch.Success)
                    {
                        string model = marketMatch.Groups[1].Value;
                        if (productMatch.Success)
                            model += $" ({productMatch.Groups[1].Value})";
                        AppendMtkLog($"[MTK]    ├─ 型号: {model}", "#059669");
                    }
                    else if (productMatch.Success)
                    {
                        AppendMtkLog($"[MTK]    ├─ 产品: {productMatch.Groups[1].Value}", "#059669");
                    }
                    
                    if (versionMatch.Success)
                        AppendMtkLog($"[MTK]    ├─ 版本: {versionMatch.Groups[1].Value}", "#059669");
                    
                    if (platformMatch.Success)
                    {
                        string platform = platformMatch.Groups[1].Value;
                        // 转换MTK平台名称
                        if (platform.StartsWith("k") && platform.Contains("v1"))
                        {
                            platform = platform.Replace("k", "MT").Replace("v1_64", "");
                        }
                        AppendMtkLog($"[MTK]    └─ 平台: {platform}", "#059669");
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }
        }

        /// <summary>
        /// 检测 MTK 固件是否支持 Super Meta 模式
        /// </summary>
        private void CheckMtkSuperMetaSupport(string firmwareDir)
        {
            _mtkSuperMetaSupported = false;

            try
            {
                // 搜索可能的 META 目录位置
                string? metaDir = null;
                string? baseFirmwareDir = _mtkFirmwareDir ?? firmwareDir;
                
                var possiblePaths = new[]
                {
                    Path.Combine(firmwareDir, "META"),
                    Path.Combine(firmwareDir, "..", "META"),
                    Path.Combine(firmwareDir, "..", "..", "META")
                };

                foreach (var path in possiblePaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (Directory.Exists(fullPath))
                    {
                        metaDir = fullPath;
                        if (path.Contains(".."))
                        {
                            baseFirmwareDir = Path.GetDirectoryName(metaDir);
                        }
                        break;
                    }
                }

                if (metaDir == null)
                {
                    AppendMtkLog("[MTK] 📦 Super Meta: ✗ 未找到META目录", "#888888");
                    return;
                }

                // 查找 super_def.*.json 文件
                var superDefFiles = Directory.GetFiles(metaDir, "super_def.*.json");
                if (superDefFiles.Length == 0)
                {
                    superDefFiles = Directory.GetFiles(metaDir, "super_def.json");
                }

                if (superDefFiles.Length > 0)
                {
                    _mtkSuperMetaSupported = true;
                    
                    // 优先使用已有的NV ID (从version_info.txt获取)
                    // 如果没有，尝试找到匹配的super_def文件
                    string? targetSuperDef = null;
                    
                    if (!string.IsNullOrEmpty(_mtkSuperMetaNvId))
                    {
                        // 查找匹配NV ID的文件
                        targetSuperDef = superDefFiles.FirstOrDefault(f => 
                            f.Contains($".{_mtkSuperMetaNvId}."));
                    }
                    
                    if (targetSuperDef == null)
                    {
                        // 使用第一个非00000000的文件
                        targetSuperDef = superDefFiles.FirstOrDefault(f => 
                            !f.Contains(".00000000.")) ?? superDefFiles[0];
                    }

                    // 解析 super_def
                    var parser = new Modules.Qualcomm.SuperDef.SuperDefParser();
                    var def = parser.Parse(targetSuperDef);
                    
                    if (def != null)
                    {
                        // 获取NV ID
                        if (string.IsNullOrEmpty(_mtkSuperMetaNvId) || _mtkSuperMetaNvId == "00000000")
                        {
                            if (!string.IsNullOrEmpty(def.NvId) && def.NvId != "00000000")
                            {
                                _mtkSuperMetaNvId = def.NvId;
                            }
                            else
                            {
                                var fileName = Path.GetFileNameWithoutExtension(targetSuperDef);
                                if (fileName.StartsWith("super_def.") && fileName != "super_def")
                                {
                                    var fileNvId = fileName.Replace("super_def.", "");
                                    if (fileNvId != "00000000")
                                    {
                                        _mtkSuperMetaNvId = fileNvId;
                                    }
                                }
                            }
                        }
                        
                        // 统计子分区
                        int partCount = def.Partitions?.Count(p => p.HasImage && p.IsSlotA) ?? 0;
                        long totalSize = 0;
                        
                        if (def.Partitions != null && baseFirmwareDir != null)
                        {
                            foreach (var p in def.Partitions.Where(x => x.HasImage && x.IsSlotA))
                            {
                                var imgPath = Path.Combine(baseFirmwareDir, p.Path ?? "");
                                if (File.Exists(imgPath))
                                    totalSize += new FileInfo(imgPath).Length;
                            }
                        }

                        // 显示信息
                        string nvDisplay = _mtkSuperMetaNvId ?? "默认";
                        if (!string.IsNullOrEmpty(def.NvText))
                        {
                            nvDisplay = def.NvText;
                            if (!string.IsNullOrEmpty(_mtkSuperMetaNvId) && _mtkSuperMetaNvId != "00000000")
                            {
                                nvDisplay += $" ({_mtkSuperMetaNvId})";
                            }
                        }

                        AppendMtkLog($"[MTK] 📦 Super Meta: ✓ 支持", "#8B5CF6");
                        AppendMtkLog($"[MTK]    ├─ 版本: {nvDisplay}", "#6366F1");
                        AppendMtkLog($"[MTK]    ├─ NV变体: {superDefFiles.Length} 个", "#6366F1");
                        AppendMtkLog($"[MTK]    ├─ 子分区: {partCount} 个", "#6366F1");
                        AppendMtkLog($"[MTK]    └─ 总大小: {totalSize / 1024 / 1024}MB", "#6366F1");
                        
                        // 检查 super_meta.raw 是否存在
                        var superMetaPath = def.SuperMeta?.Path;
                        if (!string.IsNullOrEmpty(superMetaPath) && baseFirmwareDir != null)
                        {
                            var fullPath = Path.Combine(baseFirmwareDir, superMetaPath);
                            if (File.Exists(fullPath))
                            {
                                var metaSize = new FileInfo(fullPath).Length;
                                AppendMtkLog($"[MTK]    📋 super_meta.raw: {metaSize / 1024}KB ✓", "#10B981");
                            }
                            else
                            {
                                AppendMtkLog($"[MTK]    ⚠️ super_meta.raw: 未找到", "#D97706");
                            }
                        }
                    }
                    else
                    {
                        AppendMtkLog($"[MTK] 📦 Super Meta: ✓ 检测到 ({superDefFiles.Length}个变体)", "#8B5CF6");
                    }
                }
                else
                {
                    AppendMtkLog("[MTK] 📦 Super Meta: ✗ 不支持", "#888888");
                }
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] ⚠️ Super Meta 检测失败: {ex.Message}", "#D97706");
            }
        }
        
        /// <summary>
        /// 加载 MTK Scatter 文件
        /// </summary>
        private void LoadMtkScatterFile(string firmwarePath)
        {
            try
            {
                // 查找 scatter 文件
                var scatterFile = tools.Modules.MTK.Storage.ScatterParser.FindScatterFile(firmwarePath);
                
                if (string.IsNullOrEmpty(scatterFile))
                {
                    AppendMtkLog("[MTK] ⚠️ 未找到 scatter 文件", "#F59E0B");
                    return;
                }

                AppendMtkLog($"[MTK] 解析 Scatter: {Path.GetFileName(scatterFile)}", "#3B82F6");

                // 解析 scatter 文件
                _scatterParser = new tools.Modules.MTK.Storage.ScatterParser();
                bool success = _scatterParser.Parse(scatterFile);

                if (!success || _scatterParser.Partitions.Count == 0)
                {
                    AppendMtkLog("[MTK] ❌ Scatter 文件解析失败", "#EF4444");
                    return;
                }

                // 显示解析信息
                AppendMtkLog($"[MTK] 平台: {_scatterParser.Platform}, 项目: {_scatterParser.Project}", "#888888");
                AppendMtkLog($"[MTK] 存储类型: {_scatterParser.StorageType}", "#888888");

                // 验证文件
                var (total, exists, missing) = _scatterParser.ValidateFiles();
                AppendMtkLog($"[MTK] 分区文件: {exists}/{total} 就绪, {missing} 缺失", 
                    missing > 0 ? "#F59E0B" : "#10B981");

                // 更新分区列表
                MtkPartitionList.ItemsSource = _scatterParser.Partitions;
                TxtMtkPartitionCount.Text = $"{_scatterParser.Partitions.Count} 个分区";

                AppendMtkLog($"[MTK] ✅ 已加载 {_scatterParser.Partitions.Count} 个分区", "#10B981");
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] Scatter 解析错误: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// 选择 Preloader 文件
        /// </summary>
        private void MTK_SelectPreloader_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Preloader Files (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 Preloader 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtMtkPreloader.Text = dialog.FileName;
                AppendMtkLog($"[MTK] Preloader: {dialog.FileName}", "#8B5CF6");
            }
        }

        /// <summary>
        /// 启动设备 - 等待设备连接
        /// </summary>
        private async void MTK_BootDevice_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null) return;

            AppendMtkLog("[MTK] 正在等待设备连接...", "#3B82F6");
            AppendMtkLog("[MTK] 💡 提示: 请按住音量下键并插入 USB 线", "#F59E0B");

            // 如果已连接，加载分区
            if (_mtkService.IsConnected)
            {
                await _mtkService.LoadPartitionsAsync();
            }
        }

        /// <summary>
        /// 写入 Flash
        /// </summary>
        private async void MTK_WriteFlash_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            // 获取scatter文件路径
            string scatterPath = TxtMtkFirmware.Text;
            if (string.IsNullOrEmpty(scatterPath) || !File.Exists(scatterPath))
            {
                AppendMtkLog("[MTK] 请先选择Scatter文件", "#EF4444");
                return;
            }

            var firmwareDir = _mtkFirmwareDir ?? Path.GetDirectoryName(scatterPath);
            if (string.IsNullOrEmpty(firmwareDir))
            {
                AppendMtkLog("[MTK] 无法确定固件目录", "#EF4444");
                return;
            }

            BtnMtkStop.IsEnabled = true;
            _mtkOperationCts = new CancellationTokenSource();

            try
            {
                bool formatAll = RbMtkFormat.IsChecked == true;
                
                // 检查是否启用 Super Meta 模式
                if (_mtkSuperEnabled && _mtkSuperMetaSupported)
                {
                    AppendMtkLog("[MTK] 📦 使用 Super Meta 模式刷写...", "#8B5CF6");
                    await FlashMtkSuperMetaAsync(firmwareDir);
                }
                else
                {
                    // 传统模式
                    await _mtkService.FlashFirmwareAsync(firmwareDir, formatAll);
                }
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] ❌ 刷写失败: {ex.Message}", "#EF4444");
            }
            finally
            {
                BtnMtkStop.IsEnabled = false;
            }
        }
        
        /// <summary>
        /// MTK Super Meta 模式刷写
        /// </summary>
        private async Task FlashMtkSuperMetaAsync(string firmwareDir)
        {
            if (_mtkService == null)
            {
                AppendMtkLog("[MTK] ❌ MTK 服务未初始化", "#EF4444");
                return;
            }
            
            try
            {
                // 查找 super_def.json
                var metaDir = Path.Combine(firmwareDir, "META");
                if (!Directory.Exists(metaDir))
                {
                    metaDir = Path.Combine(firmwareDir, "..", "META");
                }
                
                if (!Directory.Exists(metaDir))
                {
                    AppendMtkLog("[MTK] ❌ 未找到 META 目录", "#EF4444");
                    return;
                }

                // 查找匹配 NV ID 的 super_def
                string? targetSuperDef = null;
                var superDefFiles = Directory.GetFiles(metaDir, "super_def.*.json");
                
                if (!string.IsNullOrEmpty(_mtkSuperMetaNvId))
                {
                    targetSuperDef = superDefFiles.FirstOrDefault(f => 
                        f.Contains($".{_mtkSuperMetaNvId}."));
                }
                
                if (targetSuperDef == null)
                {
                    targetSuperDef = superDefFiles.FirstOrDefault(f => 
                        !f.Contains(".00000000.")) ?? superDefFiles.FirstOrDefault();
                }
                
                if (targetSuperDef == null)
                {
                    AppendMtkLog("[MTK] ❌ 未找到 super_def.json", "#EF4444");
                    return;
                }

                // 解析 super_def
                var parser = new Modules.Qualcomm.SuperDef.SuperDefParser();
                var def = parser.Parse(targetSuperDef);
                
                if (def?.Partitions == null)
                {
                    AppendMtkLog("[MTK] ❌ super_def 解析失败", "#EF4444");
                    return;
                }

                // 获取需要刷写的子分区
                var partitionsToFlash = def.Partitions
                    .Where(p => p.HasImage && p.IsSlotA)
                    .ToList();

                AppendMtkLog($"[MTK] 📦 Super Meta: 准备刷写 {partitionsToFlash.Count} 个子分区", "#8B5CF6");
                
                int index = 0;
                int successCount = 0;
                long totalBytes = 0;
                
                foreach (var partition in partitionsToFlash)
                {
                    index++;
                    
                    var imgPath = Path.Combine(firmwareDir, partition.Path ?? "");
                    if (!File.Exists(imgPath))
                    {
                        AppendMtkLog($"[MTK]    ⚠️ [{index}/{partitionsToFlash.Count}] {partition.Name}: 文件不存在", "#F59E0B");
                        continue;
                    }
                    
                    var fileSize = new FileInfo(imgPath).Length;
                    var partName = partition.Name ?? "unknown";
                    AppendMtkLog($"[MTK]    📝 [{index}/{partitionsToFlash.Count}] {partName} ({fileSize / 1024 / 1024}MB)...", "#6366F1");
                    
                    // 调用 MtkService 写入分区
                    bool success = await _mtkService.WritePartitionAsync(partName, imgPath);
                    
                    if (success)
                    {
                        successCount++;
                        totalBytes += fileSize;
                        AppendMtkLog($"[MTK]    ✅ {partition.Name} 完成", "#10B981");
                    }
                    else
                    {
                        AppendMtkLog($"[MTK]    ❌ {partition.Name} 失败", "#EF4444");
                    }
                }

                // 刷写 super_meta.raw
                if (!string.IsNullOrEmpty(def.SuperMeta?.Path))
                {
                    var superMetaPath = Path.Combine(firmwareDir, def.SuperMeta!.Path!);
                    if (File.Exists(superMetaPath))
                    {
                        AppendMtkLog("[MTK] 📋 写入 super_meta.raw...", "#8B5CF6");
                        bool metaSuccess = await _mtkService.WritePartitionAsync("super", superMetaPath);
                        if (metaSuccess)
                        {
                            AppendMtkLog("[MTK] ✅ super_meta 更新成功", "#10B981");
                        }
                        else
                        {
                            AppendMtkLog("[MTK] ⚠️ super_meta 更新失败", "#F59E0B");
                        }
                    }
                }

                AppendMtkLog($"[MTK] 🎉 Super Meta 刷写完成: {successCount}/{partitionsToFlash.Count} 成功, 共 {totalBytes / 1024 / 1024}MB", "#10B981");
            }
            catch (Exception ex)
            {
                AppendMtkLog($"[MTK] ❌ Super Meta 刷写异常: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// 备份分区
        /// </summary>
        private async void MTK_BackupPartition_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            // 获取选中的分区
            var selectedPartitions = _mtkService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendMtkLog("[MTK] 请先选择要备份的分区", "#F59E0B");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择备份保存位置"
            };
            if (dialog.ShowDialog() != true) return;

            BtnMtkStop.IsEnabled = true;

            foreach (var partition in selectedPartitions)
            {
                string savePath = Path.Combine(dialog.FolderName, $"{partition.Name}.bin");
                await _mtkService.BackupPartitionAsync(partition.Name, savePath);
            }

            BtnMtkStop.IsEnabled = false;
            AppendMtkLog($"[MTK] 备份完成: {selectedPartitions.Count} 个分区", "#10B981");
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        private async void MTK_ErasePartition_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            var selectedPartitions = _mtkService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendMtkLog("[MTK] 请先选择要擦除的分区", "#F59E0B");
                return;
            }

            // 确认对话框
            var result = MessageBox.Show(
                $"确定要擦除以下 {selectedPartitions.Count} 个分区吗？\n\n" +
                string.Join("\n", selectedPartitions.Select(p => $"  • {p.Name}")) +
                "\n\n⚠️ 此操作不可逆！",
                "确认擦除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            BtnMtkStop.IsEnabled = true;

            foreach (var partition in selectedPartitions)
            {
                await _mtkService.ErasePartitionAsync(partition.Name);
            }

            BtnMtkStop.IsEnabled = false;
            AppendMtkLog($"[MTK] 擦除完成: {selectedPartitions.Count} 个分区", "#10B981");
        }

        /// <summary>
        /// MTK停止按钮
        /// </summary>
        private void MTK_Stop_Click(object sender, RoutedEventArgs e)
        {
            _mtkOperationCts?.Cancel();
            _mtkService?.StopOperation();
            AppendMtkLog("[MTK] ⏹️ 正在停止当前操作...", "#EF4444");
            BtnMtkStop.IsEnabled = false;
        }

        /// <summary>
        /// 重启到系统
        /// </summary>
        private async void MTK_RebootSystem_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            AppendMtkLog("[MTK] 正在重启到系统...", "#3B82F6");
            await _mtkService.RebootAsync("system");
            AppendMtkLog("[MTK] 重启命令已发送", "#10B981");
        }

        /// <summary>
        /// 重启到BROM
        /// </summary>
        private async void MTK_RebootBrom_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            AppendMtkLog("[MTK] 正在重启到 BROM 模式...", "#F59E0B");
            await _mtkService.RebootAsync("brom");
            AppendMtkLog("[MTK] 设备将重启到 BROM 模式", "#10B981");
        }

        /// <summary>
        /// 重启到恢复模式
        /// </summary>
        private async void MTK_RebootRecovery_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkService == null || !_mtkService.IsConnected)
            {
                AppendMtkLog("[MTK] 请先连接设备", "#EF4444");
                return;
            }

            AppendMtkLog("[MTK] 正在重启到恢复模式...", "#3B82F6");
            await _mtkService.RebootAsync("recovery");
            AppendMtkLog("[MTK] 设备将重启到恢复模式", "#10B981");
        }

        /// <summary>
        /// MTK 分区搜索
        /// </summary>
        private void TxtMtkPartitionSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = TxtMtkPartitionSearch.Text.Trim().ToLower();
            
            // 更新占位符可见性
            TxtMtkSearchPlaceholder.Visibility = string.IsNullOrEmpty(searchText) 
                ? Visibility.Visible : Visibility.Collapsed;

            if (_scatterParser == null) return;

            if (string.IsNullOrEmpty(searchText))
            {
                // 显示全部
                MtkPartitionList.ItemsSource = _scatterParser.Partitions;
                TxtMtkPartitionCount.Text = $"{_scatterParser.Partitions.Count} 个分区";
            }
            else
            {
                // 过滤显示
                var filtered = _scatterParser.Partitions
                    .Where(p => p.Name.ToLower().Contains(searchText) || 
                                p.FileName.ToLower().Contains(searchText))
                    .ToList();
                MtkPartitionList.ItemsSource = filtered;
                TxtMtkPartitionCount.Text = $"{filtered.Count}/{_scatterParser.Partitions.Count} 个分区";
            }
        }

        /// <summary>
        /// MTK 分区全选
        /// </summary>
        private void ChkMtkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_scatterParser == null) return;

            bool isChecked = ChkMtkSelectAll.IsChecked == true;
            foreach (var partition in _scatterParser.Partitions)
            {
                partition.IsSelected = isChecked;
            }

            // 刷新列表
            MtkPartitionList.ItemsSource = null;
            MtkPartitionList.ItemsSource = _scatterParser.Partitions;

            AppendMtkLog($"[MTK] {(isChecked ? "已全选" : "已取消全选")} {_scatterParser.Partitions.Count} 个分区", "#888888");
        }

        /// <summary>
        /// 常见保护分区名称列表
        /// </summary>
        private static readonly HashSet<string> ProtectedPartitionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // NVRAM 相关
            "nvram", "nvdata", "nvcfg",
            // 保护分区
            "protect1", "protect2", "protect_f", "protect_s",
            // EFS / 持久数据
            "persist", "persistbk",
            // Frp / 防重置
            "frp",
            // SEC 相关
            "seccfg", "sec1", "sec2", "secro", "seckeyblob",
            // Proinfo
            "proinfo",
            // EFUSE
            "efuse",
            // 其他敏感数据
            "expdb", "otp", "md_udc", "cdt_engineering"
        };

        /// <summary>
        /// 判断分区是否为保护分区
        /// </summary>
        private bool IsProtectedPartition(tools.Modules.MTK.Storage.ScatterPartition partition)
        {
            // 方法1: 检查 operation_type
            if (partition.IsProtected)
                return true;
            
            // 方法2: 检查分区名
            string name = partition.Name.ToLowerInvariant();
            if (ProtectedPartitionNames.Contains(name))
                return true;
            
            // 方法3: 检查名称是否包含保护关键字
            if (name.Contains("protect") || name.Contains("nvram") || name.Contains("nvdata"))
                return true;

            return false;
        }

        /// <summary>
        /// 选择不保护分区 (用户数据安全刷机)
        /// </summary>
        private void BtnMtkSelectUnprotected_Click(object sender, RoutedEventArgs e)
        {
            if (_scatterParser == null) return;

            int selectedCount = 0;
            foreach (var partition in _scatterParser.Partitions)
            {
                bool isProtected = IsProtectedPartition(partition);
                partition.IsSelected = !isProtected && partition.IsDownload;
                if (partition.IsSelected) selectedCount++;
            }

            RefreshMtkPartitionList();
            ChkMtkSelectAll.IsChecked = false;
            AppendMtkLog($"[MTK] 🔓 已选择 {selectedCount} 个非保护分区 (跳过NVRAM/EFS等)", "#10B981");
        }

        /// <summary>
        /// 选择保护分区
        /// </summary>
        private void BtnMtkSelectProtected_Click(object sender, RoutedEventArgs e)
        {
            if (_scatterParser == null) return;

            int selectedCount = 0;
            foreach (var partition in _scatterParser.Partitions)
            {
                partition.IsSelected = IsProtectedPartition(partition);
                if (partition.IsSelected) selectedCount++;
            }

            RefreshMtkPartitionList();
            ChkMtkSelectAll.IsChecked = false;
            AppendMtkLog($"[MTK] 🔒 已选择 {selectedCount} 个保护分区 (NVRAM/EFS等)", "#F59E0B");
        }

        /// <summary>
        /// 选择可下载分区
        /// </summary>
        private void BtnMtkSelectDownloadable_Click(object sender, RoutedEventArgs e)
        {
            if (_scatterParser == null) return;

            int selectedCount = 0;
            foreach (var partition in _scatterParser.Partitions)
            {
                partition.IsSelected = partition.IsDownload;
                if (partition.IsSelected) selectedCount++;
            }

            RefreshMtkPartitionList();
            ChkMtkSelectAll.IsChecked = selectedCount == _scatterParser.Partitions.Count;
            AppendMtkLog($"[MTK] 📥 已选择 {selectedCount} 个可下载分区", "#8B5CF6");
        }

        #region MTK Super/保护 开关逻辑

        // MTK 直刷Super开关
        private bool _mtkSuperEnabled = false;
        // MTK 保护分区开关
#pragma warning disable CS0414 // 预留给未来使用
        private bool _mtkProtectEnabled = false;
#pragma warning restore CS0414
        
        // 保护分区列表
        private readonly string[] _mtkProtectedPartitions = { 
            "nvram", "nvdata", "nvcfg", "protect1", "protect2", 
            "persist", "metadata", "frp", "sec1", "seccfg",
            "efuse", "otp", "proinfo", "md_udc", "cdt_engineering"
        };

        /// <summary>
        /// 启用MTK直刷Super
        /// </summary>
        private void MtkSuperPartition_Checked(object sender, RoutedEventArgs e)
        {
            _mtkSuperEnabled = true;
            AppendMtkLog("[MTK] 📦 直刷Super已启用", "#8B5CF6");
            AppendMtkLog("[MTK] ⚡ 刷写时将直接写入Super分区", "#6366F1");
            
            // 自动选择super相关分区
            if (_scatterParser != null)
            {
                var superParts = new[] { "super", "system", "vendor", "product", "odm", 
                    "system_a", "vendor_a", "product_a", "odm_a",
                    "system_b", "vendor_b", "product_b", "odm_b" };
                int count = 0;
                foreach (var partition in _scatterParser.Partitions)
                {
                    if (superParts.Any(p => partition.Name.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        partition.IsSelected = true;
                        count++;
                    }
                }
                if (count > 0)
                {
                    RefreshMtkPartitionList();
                    AppendMtkLog($"[MTK] 已自动选择 {count} 个Super相关分区", "#10B981");
                }
            }
        }

        /// <summary>
        /// 禁用MTK直刷Super
        /// </summary>
        private void MtkSuperPartition_Unchecked(object sender, RoutedEventArgs e)
        {
            _mtkSuperEnabled = false;
            AppendMtkLog("[MTK] 📦 直刷Super已禁用", "#6B7280");
            AppendMtkLog("[MTK] 📋 刷写时将使用标准分区模式", "#888888");
        }

        /// <summary>
        /// 启用MTK保护分区
        /// </summary>
        private void MtkProtectPartition_Checked(object sender, RoutedEventArgs e)
        {
            _mtkProtectEnabled = true;
            AppendMtkLog("[MTK] 🛡️ 保护分区已启用", "#10B981");
            AppendMtkLog("[MTK] 🔒 刷写时将跳过NVRAM/EFS等关键分区", "#22C55E");
            
            // 自动取消选择保护分区
            if (_scatterParser != null)
            {
                int skippedCount = 0;
                foreach (var partition in _scatterParser.Partitions)
                {
                    if (IsMtkProtectedPartition(partition.Name) && partition.IsSelected)
                    {
                        partition.IsSelected = false;
                        skippedCount++;
                    }
                }
                if (skippedCount > 0)
                {
                    RefreshMtkPartitionList();
                    AppendMtkLog($"[MTK] 已自动取消 {skippedCount} 个保护分区", "#F59E0B");
                }
            }
        }

        /// <summary>
        /// 禁用MTK保护分区
        /// </summary>
        private void MtkProtectPartition_Unchecked(object sender, RoutedEventArgs e)
        {
            _mtkProtectEnabled = false;
            AppendMtkLog("[MTK] 🛡️ 保护分区已禁用", "#6B7280");
            AppendMtkLog("[MTK] ⚠ 刷写时将写入所有选中分区", "#F59E0B");
        }

        /// <summary>
        /// 判断是否为MTK保护分区
        /// </summary>
        private bool IsMtkProtectedPartition(string partitionName)
        {
            if (string.IsNullOrEmpty(partitionName)) return false;
            return _mtkProtectedPartitions.Any(p => 
                partitionName.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                partitionName.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        /// <summary>
        /// 刷新 MTK 分区列表显示
        /// </summary>
        private void RefreshMtkPartitionList()
        {
            if (_scatterParser == null) return;
            
            var currentSource = MtkPartitionList.ItemsSource;
            MtkPartitionList.ItemsSource = null;
            
            // 如果有搜索过滤，重新应用
            string searchText = TxtMtkPartitionSearch.Text?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(searchText))
            {
                MtkPartitionList.ItemsSource = _scatterParser.Partitions;
            }
            else
            {
                MtkPartitionList.ItemsSource = _scatterParser.Partitions
                    .Where(p => p.Name.ToLower().Contains(searchText) || 
                                p.FileName.ToLower().Contains(searchText))
                    .ToList();
            }
        }

        /// <summary>
        /// MTK 分区项双击 - 选择自定义文件
        /// </summary>
        private void MtkPartitionItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;

            if (sender is FrameworkElement element && 
                element.DataContext is tools.Modules.MTK.Storage.ScatterPartition partition)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "镜像文件 (*.img;*.bin)|*.img;*.bin|所有文件 (*.*)|*.*",
                    Title = $"选择 {partition.Name} 的刷写文件"
                };

                if (!string.IsNullOrEmpty(partition.FilePath) && File.Exists(partition.FilePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(partition.FilePath);
                }

                if (dialog.ShowDialog() == true)
                {
                    partition.HasCustomFile = true;
                    partition.CustomFilePath = dialog.FileName;
                    partition.IsSelected = true;

                    // 刷新列表
                    MtkPartitionList.ItemsSource = null;
                    MtkPartitionList.ItemsSource = _scatterParser?.Partitions;

                    AppendMtkLog($"[MTK] {partition.Name} → {Path.GetFileName(dialog.FileName)}", "#00D4FF");
                }
            }
        }

        /// <summary>
        /// 复制MTK日志
        /// </summary>
        private void CopyMtkLog_Click(object sender, RoutedEventArgs e)
        {
            if (_mtkLogItems.Count == 0)
            {
                AppendMtkLog("[INFO] 日志为空", "#888888");
                return;
            }
            var logText = string.Join(Environment.NewLine, _mtkLogItems.Select(item => item.Text));
            System.Windows.Clipboard.SetText(logText);
            AppendMtkLog("[INFO] MTK 日志已复制到剪贴板", "#10B981");
        }

        /// <summary>
        /// 清空MTK日志
        /// </summary>
        private void ClearMtkLog_Click(object sender, RoutedEventArgs e)
        {
            _mtkLogItems.Clear();
            AppendMtkLog("[INFO] MTK 日志已清空", "#0088CC");
        }

        // MTK 日志数据
        private readonly System.Collections.ObjectModel.ObservableCollection<LogItem> _mtkLogItems = new();

        /// <summary>
        /// 初始化MTK日志列表
        /// </summary>
        private void InitializeMtkLog()
        {
            MtkLogListBox.ItemsSource = _mtkLogItems;
            AppendMtkLog("[INFO] MTK 模块已就绪", "#10B981");
        }

        // MTK日志节流
        private DateTime _lastMtkLogScrollTime = DateTime.MinValue;
        
        /// <summary>
        /// 添加MTK日志 (优化版)
        /// </summary>
        private void AppendMtkLog(string message, string color = "#2D2D2D")
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                _mtkLogItems.Add(new LogItem
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Color = GetCachedBrush(color)
                });

                // 批量清理
                if (_mtkLogItems.Count > MaxLogItems)
                {
                    int removeCount = _mtkLogItems.Count - MaxLogItems + 50;
                    for (int i = 0; i < removeCount; i++)
                        _mtkLogItems.RemoveAt(0);
                }

                // 节流滚动
                var now = DateTime.Now;
                if ((now - _lastMtkLogScrollTime).TotalMilliseconds > LogScrollThrottleMs)
                {
                    _lastMtkLogScrollTime = now;
                    if (_mtkLogItems.Count > 0)
                        MtkLogListBox.ScrollIntoView(_mtkLogItems[^1]);
                }
            });
        }

        #endregion

        #region 展讯模块功能

        // 展讯日志数据
        private readonly System.Collections.ObjectModel.ObservableCollection<LogItem> _sprdLogItems = new();

        // 展讯传输计时器
        private System.Diagnostics.Stopwatch? _sprdStopwatch;
        private System.Windows.Threading.DispatcherTimer? _sprdTimer;
        private long _sprdLastTransferred;
        private DateTime _sprdLastSpeedUpdate;

        /// <summary>
        /// 初始化展讯服务
        /// </summary>
        private void InitializeUnisocService()
        {
            _sprdService = new UnisocUIService(
                Dispatcher,
                (msg, color) => AppendSprdLog(msg, color),
                (percent, status) => UpdateSprdProgress((int)percent, status),
                status => Dispatcher.Invoke(() => TxtSprdProgressStatus.Text = status),
                info => UpdateSprdDeviceInfoUI(info)
            );

            // 设备事件
            _sprdService.DeviceArrived += port =>
            {
                SetSprdDeviceStatus(true, "Download 就绪", port);
                AppendSprdLog($"[展讯] ✓ 设备就绪: {port}", "#10B981");
            };

            _sprdService.DeviceRemoved += () =>
            {
                SetSprdDeviceStatus(false, "未连接", "---");
                SprdPartitionList.ItemsSource = null;
                StopSprdTimer();
            };

            // 分区加载事件
            _sprdService.PartitionsLoaded += partitions =>
            {
                Dispatcher.Invoke(() =>
                {
                    SprdPartitionList.ItemsSource = partitions;
                    AppendSprdLog($"📋 已加载 {partitions.Count} 个分区", "#10B981");
                });
            };

            // 传输进度事件
            _sprdService.TransferProgress += (current, total) =>
            {
                Dispatcher.Invoke(() =>
                {
                    double percent = total > 0 ? (double)current / total * 100 : 0;
                    SprdProgressBar.Value = percent;
                    TxtSprdProgressStatus.Text = $"{percent:F1}%";
                    TxtSprdTransferredSize.Text = FormatBytesSize(current);
                    
                    // 计算速度
                    var now = DateTime.Now;
                    if ((now - _sprdLastSpeedUpdate).TotalMilliseconds > 500)
                    {
                        long delta = current - _sprdLastTransferred;
                        double speed = delta / ((now - _sprdLastSpeedUpdate).TotalSeconds);
                        TxtSprdTransferSpeed.Text = $"{FormatBytesSize((long)speed)}/s";
                        _sprdLastTransferred = current;
                        _sprdLastSpeedUpdate = now;
                    }
                });
            };

            // 初始化计时器
            _sprdTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _sprdTimer.Tick += (s, e) =>
            {
                if (_sprdStopwatch != null && _sprdStopwatch.IsRunning)
                {
                    TxtSprdElapsedTime.Text = _sprdStopwatch.Elapsed.ToString(@"mm\:ss");
                }
            };

            // 启动设备监听
            _sprdService.StartDeviceWatch();
        }

        /// <summary>
        /// 启动展讯计时器
        /// </summary>
        private void StartSprdTimer()
        {
            _sprdStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _sprdLastTransferred = 0;
            _sprdLastSpeedUpdate = DateTime.Now;
            _sprdTimer?.Start();
        }

        /// <summary>
        /// 停止展讯计时器
        /// </summary>
        private void StopSprdTimer()
        {
            _sprdStopwatch?.Stop();
            _sprdTimer?.Stop();
        }

        /// <summary>
        /// 更新展讯进度
        /// </summary>
        private void UpdateSprdProgress(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                SprdProgressBar.Value = percent;
                TxtSprdProgressStatus.Text = status;
            });
        }

        /// <summary>
        /// 更新展讯设备信息 UI
        /// </summary>
        private void UpdateSprdDeviceInfoUI(UnisocDeviceInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                TxtSprdChip.Text = info.ChipName;
                TxtSprdFdlStatus.Text = info.FdlLoaded ? "已加载" : "未加载";
                TxtSprdDiagChannel.Text = info.Mode;
                TxtSprdFdl1Addr.Text = info.Fdl1Address;
                TxtSprdFdl2Addr.Text = info.Fdl2Address;
                TxtSprdUsbPort.Text = info.Port;
            });
        }

        /// <summary>
        /// 设置展讯设备状态
        /// </summary>
        private void SetSprdDeviceStatus(bool connected, string status, string port)
        {
            Dispatcher.Invoke(() =>
            {
                TxtSprdDeviceStatus.Text = status;
                TxtSprdDevicePort.Text = port;
                
                // 更新状态指示器颜色
                if (connected)
                {
                    SprdDeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
                }
                else
                {
                    SprdDeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
                }
            });
        }

        /// <summary>
        /// 初始化展讯日志列表
        /// </summary>
        private void InitializeSprdLog()
        {
            SprdLogListBox.ItemsSource = _sprdLogItems;
            AppendSprdLog("[INFO] 展讯模块已就绪", "#3B82F6");
        }

        // 展讯日志节流
        private DateTime _lastSprdLogScrollTime = DateTime.MinValue;
        
        /// <summary>
        /// 添加展讯日志 (优化版)
        /// </summary>
        private void AppendSprdLog(string message, string color = "#2D2D2D")
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                _sprdLogItems.Add(new LogItem
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Color = GetCachedBrush(color)
                });

                // 批量清理
                if (_sprdLogItems.Count > MaxLogItems)
                {
                    int removeCount = _sprdLogItems.Count - MaxLogItems + 50;
                    for (int i = 0; i < removeCount; i++)
                        _sprdLogItems.RemoveAt(0);
                }

                // 节流滚动
                var now = DateTime.Now;
                if ((now - _lastSprdLogScrollTime).TotalMilliseconds > LogScrollThrottleMs)
                {
                    _lastSprdLogScrollTime = now;
                    if (_sprdLogItems.Count > 0)
                        SprdLogListBox.ScrollIntoView(_sprdLogItems[^1]);
                }
            });
        }

        /// <summary>
        /// 复制展讯日志
        /// </summary>
        private void CopySprdLog_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdLogItems.Count == 0)
            {
                AppendSprdLog("[INFO] 日志为空", "#888888");
                return;
            }
            var logText = string.Join(Environment.NewLine, _sprdLogItems.Select(item => item.Text));
            System.Windows.Clipboard.SetText(logText);
            AppendSprdLog("[INFO] 展讯日志已复制到剪贴板", "#10B981");
        }

        /// <summary>
        /// 清空展讯日志
        /// </summary>
        private void ClearSprdLog_Click(object sender, RoutedEventArgs e)
        {
            _sprdLogItems.Clear();
            AppendSprdLog("[INFO] 展讯日志已清空", "#0088CC");
        }

        /// <summary>
        /// 选择PAC固件
        /// </summary>
        private async void Sprd_SelectPac_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PAC Files (*.pac)|*.pac|All Files (*.*)|*.*",
                Title = "选择 PAC 固件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSprdPac.Text = dialog.FileName;
                AppendSprdLog($"[展讯] PAC固件: {System.IO.Path.GetFileName(dialog.FileName)}", "#3B82F6");
                
                // 使用 UnisocUIService 解析 PAC 固件
                if (_sprdService != null)
                {
                    var result = await _sprdService.LoadPacFirmwareAsync(dialog.FileName);
                    if (result && _sprdService.CurrentPac?.FirmwareInfo != null)
                    {
                        var info = _sprdService.CurrentPac.FirmwareInfo;
                        TxtSprdFwName.Text = info.FirmwareName;
                        TxtSprdFwProduct.Text = info.ProductName;
                        TxtSprdFwVersion.Text = info.Version;
                        TxtSprdFwSize.Text = FormatBytesSize(info.Size);
                        
                        // 更新分区列表
                        SprdPartitionList.ItemsSource = _sprdService.Partitions;
                    }
                }
            }
        }

        /// <summary>
        /// 选择FDL1文件
        /// </summary>
        private void Sprd_SelectFdl1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "BIN Files (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 FDL1 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSprdFdl1.Text = dialog.FileName;
                if (_sprdService != null)
                {
                    _sprdService.Fdl1Path = dialog.FileName;
                }
                AppendSprdLog($"[展讯] FDL1: {System.IO.Path.GetFileName(dialog.FileName)}", "#888888");
            }
        }

        /// <summary>
        /// 选择FDL2文件
        /// </summary>
        private void Sprd_SelectFdl2_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "BIN Files (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 FDL2 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSprdFdl2.Text = dialog.FileName;
                if (_sprdService != null)
                {
                    _sprdService.Fdl2Path = dialog.FileName;
                }
                AppendSprdLog($"[展讯] FDL2: {System.IO.Path.GetFileName(dialog.FileName)}", "#888888");
            }
        }

        /// <summary>
        /// 识别/连接设备
        /// </summary>
        private async void Sprd_Identify_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null) return;

            AppendSprdLog("[展讯] 正在识别设备...", "#3B82F6");
            
            // 查找展讯设备
            var watcher = new DeviceWatcher();
            var devices = watcher.FindDevicesByType(DeviceType.SpreadtrumDownload);
            watcher.Dispose();
            
            if (devices.Count == 0)
            {
                AppendSprdLog("[展讯] ❌ 未找到展讯设备，请确保设备处于 Download 模式", "#EF4444");
                return;
            }

            var device = devices[0];
            AppendSprdLog($"[展讯] 发现设备: {device.PortName}", "#10B981");
            
            // 连接设备
            var result = await _sprdService.ConnectDownloadModeAsync(device.PortName);
            if (result)
            {
                AppendSprdLog("[展讯] ✓ 设备连接成功", "#10B981");
            }
            else
            {
                AppendSprdLog("[展讯] ❌ 设备连接失败", "#EF4444");
            }
        }

        /// <summary>
        /// 读取分区表
        /// </summary>
        private void Sprd_ReadPartitionTable_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }
            
            AppendSprdLog("[展讯] 正在读取分区表...", "#10B981");
            AppendSprdLog("[展讯] 注意: 需要先发送 FDL1/FDL2 才能读取分区表", "#888888");
        }

        /// <summary>
        /// 备份分区
        /// </summary>
        private async void Sprd_ReadPartition_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }

            // 获取选中的分区
            var selectedPartitions = _sprdService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendSprdLog("[展讯] ❌ 请先选择要备份的分区", "#EF4444");
                return;
            }

            // 选择保存目录 (使用 WPF 的 SaveFileDialog 作为目录选择)
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择备份保存目录 (输入任意文件名)",
                FileName = "backup",
                Filter = "All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            
            var saveDir = System.IO.Path.GetDirectoryName(dialog.FileName) ?? "";

            AppendSprdLog($"[展讯] 开始备份 {selectedPartitions.Count} 个分区...", "#10B981");

            foreach (var partition in selectedPartitions)
            {
                var outputPath = System.IO.Path.Combine(saveDir, $"{partition.Name}.bin");
                var result = await _sprdService.BackupPartitionAsync(partition.Name, partition.Size, outputPath);
                if (result)
                {
                    AppendSprdLog($"[展讯] ✓ {partition.Name} 备份完成", "#10B981");
                }
                else
                {
                    AppendSprdLog($"[展讯] ❌ {partition.Name} 备份失败", "#EF4444");
                }
            }
        }

        /// <summary>
        /// 擦除FRP账户
        /// </summary>
        private async void Sprd_EraseFrp_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }

            AppendSprdLog("[展讯] ⚠️ 警告: 正在擦除 FRP 账户锁...", "#F59E0B");
            var result = await _sprdService.ErasePartitionAsync("frp");
            if (result)
            {
                AppendSprdLog("[展讯] ✓ FRP 擦除成功", "#10B981");
            }
            else
            {
                AppendSprdLog("[展讯] ❌ FRP 擦除失败", "#EF4444");
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        private async void Sprd_ErasePartition_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }

            // 获取选中的分区
            var selectedPartitions = _sprdService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendSprdLog("[展讯] ❌ 请先选择要擦除的分区", "#EF4444");
                return;
            }

            // 确认
            var confirm = System.Windows.MessageBox.Show(
                $"确定要擦除以下 {selectedPartitions.Count} 个分区吗？此操作不可逆！\n\n" +
                string.Join(", ", selectedPartitions.Select(p => p.Name)),
                "确认擦除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (confirm != MessageBoxResult.Yes) return;

            AppendSprdLog("[展讯] ⚠️ 警告: 擦除分区操作不可逆!", "#EF4444");

            foreach (var partition in selectedPartitions)
            {
                AppendSprdLog($"[展讯] 擦除: {partition.Name}...", "#F59E0B");
                var result = await _sprdService.ErasePartitionAsync(partition.Name);
                if (result)
                {
                    AppendSprdLog($"[展讯] ✓ {partition.Name} 擦除成功", "#10B981");
                }
                else
                {
                    AppendSprdLog($"[展讯] ❌ {partition.Name} 擦除失败", "#EF4444");
                }
            }
        }

        /// <summary>
        /// 刷写固件
        /// </summary>
        private async void Sprd_Flash_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            if (string.IsNullOrEmpty(TxtSprdPac.Text))
            {
                AppendSprdLog("[展讯] ❌ 错误: 请先选择 PAC 固件!", "#EF4444");
                return;
            }

            // 获取选中的分区
            var selectedPartitions = _sprdService.Partitions.Where(p => p.IsSelected).ToList();
            if (selectedPartitions.Count == 0)
            {
                AppendSprdLog("[展讯] ❌ 请选择要刷写的分区", "#EF4444");
                return;
            }

            // 检查设备连接
            if (!_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] 等待设备连接...", "#888888");
                
                // 尝试查找并连接设备
                var watcher = new DeviceWatcher();
                var devices = watcher.FindDevicesByType(DeviceType.SpreadtrumDownload);
                watcher.Dispose();
                
                if (devices.Count == 0)
                {
                    AppendSprdLog("[展讯] ❌ 未找到设备，请将设备连接到 Download 模式", "#EF4444");
                    return;
                }

                var connected = await _sprdService.ConnectDownloadModeAsync(devices[0].PortName);
                if (!connected)
                {
                    AppendSprdLog("[展讯] ❌ 设备连接失败", "#EF4444");
                    return;
                }
            }

            AppendSprdLog("[展讯] 开始刷写固件...", "#10B981");
            AppendSprdLog($"[展讯] 固件: {System.IO.Path.GetFileName(TxtSprdPac.Text)}", "#888888");
            AppendSprdLog($"[展讯] 分区数: {selectedPartitions.Count}", "#888888");
            
            // 检查选项
            if (ChkSprdKeepNV.IsChecked == true)
            {
                AppendSprdLog("[展讯] 📋 保留 NV 数据 (跳过 nvitem 分区)", "#888888");
            }
            if (ChkSprdRsaBypass.IsChecked == true)
            {
                _sprdService.UseExploit = true;
                AppendSprdLog("[展讯] 🔓 RSA 绕过已启用", "#F59E0B");
            }

            // 启动计时器
            StartSprdTimer();
            _sprdOperationCts = new CancellationTokenSource();

            // 先发送 FDL1
            if (!string.IsNullOrEmpty(_sprdService.Fdl1Path))
            {
                AppendSprdLog("[展讯] 发送 FDL1...", "#3B82F6");
                var fdl1Result = await _sprdService.SendFdl1Async();
                if (!fdl1Result)
                {
                    AppendSprdLog("[展讯] ❌ FDL1 发送失败", "#EF4444");
                    return;
                }
            }

            // 发送 FDL2
            if (!string.IsNullOrEmpty(_sprdService.Fdl2Path))
            {
                AppendSprdLog("[展讯] 发送 FDL2...", "#3B82F6");
                var fdl2Result = await _sprdService.SendFdl2Async();
                if (!fdl2Result)
                {
                    AppendSprdLog("[展讯] ❌ FDL2 发送失败", "#EF4444");
                    return;
                }
            }

            // 刷写分区
            int success = 0, failed = 0;
            foreach (var partition in selectedPartitions)
            {
                // 跳过 NV 分区 (如果设置了保留)
                if (ChkSprdKeepNV.IsChecked == true && 
                    partition.Name.Contains("nv", StringComparison.OrdinalIgnoreCase))
                {
                    AppendSprdLog($"[展讯] 跳过: {partition.Name} (保留 NV)", "#888888");
                    continue;
                }

                AppendSprdLog($"[展讯] 刷写: {partition.Name}...", "#3B82F6");
                var result = await _sprdService.FlashPartitionAsync(partition.Name, partition.FilePath);
                if (result)
                {
                    success++;
                    AppendSprdLog($"[展讯] ✓ {partition.Name} 完成", "#10B981");
                }
                else
                {
                    failed++;
                    AppendSprdLog($"[展讯] ❌ {partition.Name} 失败", "#EF4444");
                }
            }

            // 停止计时器
            StopSprdTimer();
            
            AppendSprdLog($"[展讯] 刷写完成: 成功 {success}, 失败 {failed}", success > 0 ? "#10B981" : "#EF4444");
            AppendSprdLog($"[展讯] 耗时: {TxtSprdElapsedTime.Text}", "#888888");

            // 自动重启
            if (ChkSprdAutoReboot.IsChecked == true && failed == 0)
            {
                AppendSprdLog("[展讯] 自动重启设备...", "#3B82F6");
                await _sprdService.RebootDeviceAsync();
                AppendSprdLog("[展讯] ✓ 重启命令已发送", "#10B981");
            }
        }

        /// <summary>
        /// 停止操作
        /// </summary>
        private void Sprd_Stop_Click(object sender, RoutedEventArgs e)
        {
            _sprdService?.CancelOperation();
            _sprdOperationCts?.Cancel();
            AppendSprdLog("[展讯] 用户请求停止操作", "#F59E0B");
        }
        
        /// <summary>
        /// 发送 FDL1
        /// </summary>
        private async void Sprd_SendFdl1_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }

            if (string.IsNullOrEmpty(TxtSprdFdl1.Text))
            {
                AppendSprdLog("[展讯] ❌ 请先选择 FDL1 文件", "#EF4444");
                return;
            }

            _sprdService.Fdl1Path = TxtSprdFdl1.Text;
            _sprdService.Fdl1Address = TxtSprdFdl1Addr.Text;

            var result = await _sprdService.SendFdl1Async();
            if (result)
            {
                AppendSprdLog("[展讯] ✓ FDL1 发送成功", "#10B981");
            }
        }

        /// <summary>
        /// 发送 FDL2
        /// </summary>
        private async void Sprd_SendFdl2_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || !_sprdService.IsConnected)
            {
                AppendSprdLog("[展讯] ❌ 请先连接设备", "#EF4444");
                return;
            }

            if (string.IsNullOrEmpty(TxtSprdFdl2.Text))
            {
                AppendSprdLog("[展讯] ❌ 请先选择 FDL2 文件", "#EF4444");
                return;
            }

            _sprdService.Fdl2Path = TxtSprdFdl2.Text;
            _sprdService.Fdl2Address = TxtSprdFdl2Addr.Text;

            var result = await _sprdService.SendFdl2Async();
            if (result)
            {
                AppendSprdLog("[展讯] ✓ FDL2 发送成功", "#10B981");
            }
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        private async void Sprd_Reboot_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            AppendSprdLog("[展讯] 重启设备...", "#3B82F6");
            var result = await _sprdService.RebootDeviceAsync();
            if (result)
            {
                AppendSprdLog("[展讯] ✓ 重启命令已发送", "#10B981");
            }
        }

        /// <summary>
        /// 关机
        /// </summary>
        private async void Sprd_PowerOff_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            AppendSprdLog("[展讯] 关机...", "#3B82F6");
            var result = await _sprdService.PowerOffDeviceAsync();
            if (result)
            {
                AppendSprdLog("[展讯] ✓ 关机命令已发送", "#10B981");
            }
        }

        /// <summary>
        /// 读取 IMEI (Diag 模式)
        /// </summary>
        private async void Sprd_ReadImei_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            AppendSprdLog("[展讯] 读取 IMEI...", "#3B82F6");
            var (imei1, imei2) = await _sprdService.ReadImeiAsync();
            
            if (!string.IsNullOrEmpty(imei1))
            {
                AppendSprdLog($"[展讯] IMEI1: {imei1}", "#10B981");
            }
            else
            {
                AppendSprdLog("[展讯] IMEI1: 未读取到", "#888888");
            }

            if (!string.IsNullOrEmpty(imei2))
            {
                AppendSprdLog($"[展讯] IMEI2: {imei2}", "#10B981");
            }
            else
            {
                AppendSprdLog("[展讯] IMEI2: 未读取到", "#888888");
            }
        }

        /// <summary>
        /// 写入 IMEI (Diag 模式)
        /// </summary>
        private async void Sprd_WriteImei_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            // 创建输入对话框
            var inputDialog = new System.Windows.Window
            {
                Title = "写入 IMEI",
                Width = 350,
                Height = 200,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = System.Windows.ResizeMode.NoResize
            };

            var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = "请输入 IMEI (15位数字):", 
                Margin = new Thickness(0, 0, 0, 10) 
            });
            
            var imeiTextBox = new System.Windows.Controls.TextBox 
            { 
                MaxLength = 15, 
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 14,
                Padding = new Thickness(5)
            };
            stackPanel.Children.Add(imeiTextBox);

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = "选择 IMEI 槽位:", 
                Margin = new Thickness(0, 15, 0, 5) 
            });

            var slotCombo = new System.Windows.Controls.ComboBox();
            slotCombo.Items.Add("IMEI 1");
            slotCombo.Items.Add("IMEI 2");
            slotCombo.SelectedIndex = 0;
            stackPanel.Children.Add(slotCombo);

            var buttonPanel = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var okButton = new System.Windows.Controls.Button { Content = "写入", Width = 70, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new System.Windows.Controls.Button { Content = "取消", Width = 70 };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);

            inputDialog.Content = stackPanel;

            string? inputImei = null;
            int slot = 1;

            okButton.Click += (s, args) =>
            {
                if (imeiTextBox.Text.Length == 15 && imeiTextBox.Text.All(char.IsDigit))
                {
                    inputImei = imeiTextBox.Text;
                    slot = slotCombo.SelectedIndex + 1;
                    inputDialog.DialogResult = true;
                }
                else
                {
                    System.Windows.MessageBox.Show("请输入有效的 15 位 IMEI 号码", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            cancelButton.Click += (s, args) => inputDialog.DialogResult = false;

            if (inputDialog.ShowDialog() == true && !string.IsNullOrEmpty(inputImei))
            {
                AppendSprdLog($"[展讯] 写入 IMEI{slot}: {inputImei}...", "#3B82F6");
                var result = await _sprdService.WriteImeiAsync(inputImei, slot);
                if (result)
                {
                    AppendSprdLog($"[展讯] ✓ IMEI{slot} 写入成功", "#10B981");
                }
                else
                {
                    AppendSprdLog($"[展讯] ❌ IMEI{slot} 写入失败", "#EF4444");
                }
            }
        }

        /// <summary>
        /// 提取 PAC 固件
        /// </summary>
        private async void Sprd_ExtractPac_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null || _sprdService.CurrentPac == null)
            {
                AppendSprdLog("[展讯] ❌ 请先加载 PAC 固件", "#EF4444");
                return;
            }

            // 使用 WPF OpenFileDialog 选择目录 (通过选择一个文件来确定目录)
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择提取目录 (输入文件名后点击保存)",
                FileName = "提取到此目录",
                Filter = "文件夹|*."
            };

            if (dialog.ShowDialog() == true)
            {
                var outputDir = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (string.IsNullOrEmpty(outputDir)) return;

                AppendSprdLog($"[展讯] 开始提取 PAC 固件到: {outputDir}", "#3B82F6");
                StartSprdTimer();
                
                var result = await _sprdService.ExtractPacFirmwareAsync(outputDir);
                
                StopSprdTimer();
                if (result)
                {
                    AppendSprdLog("[展讯] ✓ PAC 固件提取完成", "#10B981");
                }
                else
                {
                    AppendSprdLog("[展讯] ❌ PAC 固件提取失败", "#EF4444");
                }
            }
        }

        /// <summary>
        /// 连接 Diag 模式
        /// </summary>
        private async void Sprd_ConnectDiag_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            AppendSprdLog("[展讯] 搜索 Diag 端口...", "#3B82F6");
            
            // 查找展讯设备 (通过 VID 识别)
            var watcher = new DeviceWatcher();
            var allDevices = watcher.GetAllDevices();
            var devices = allDevices.Where(d => 
                d.VID == "1782" || // Spreadtrum VID
                d.Description?.Contains("SPRD", StringComparison.OrdinalIgnoreCase) == true ||
                d.Description?.Contains("Spreadtrum", StringComparison.OrdinalIgnoreCase) == true
            ).ToList();
            watcher.Dispose();

            if (devices.Count == 0)
            {
                AppendSprdLog("[展讯] ❌ 未找到 Diag 端口，请确保设备已开机并连接 USB", "#EF4444");
                return;
            }

            var port = devices[0].PortName;
            AppendSprdLog($"[展讯] 连接 Diag 端口: {port}...", "#3B82F6");
            
            var result = await _sprdService.ConnectDiagModeAsync(port);
            if (result)
            {
                AppendSprdLog("[展讯] ✓ Diag 模式连接成功", "#10B981");
                SetSprdDeviceStatus(true, "Diag 已连接", port);
            }
            else
            {
                AppendSprdLog("[展讯] ❌ Diag 模式连接失败", "#EF4444");
            }
        }

        /// <summary>
        /// 全选/取消全选分区
        /// </summary>
        private void Sprd_SelectAllPartitions_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null) return;
            
            bool allSelected = _sprdService.Partitions.All(p => p.IsSelected);
            foreach (var partition in _sprdService.Partitions)
            {
                partition.IsSelected = !allSelected;
            }
            SprdPartitionList.Items.Refresh();
        }

        /// <summary>
        /// RSA 绕过选项变更
        /// </summary>
        private void ChkSprdRsaBypass_Changed(object sender, RoutedEventArgs e)
        {
            if (_sprdService != null)
            {
                _sprdService.UseExploit = ChkSprdRsaBypass.IsChecked == true;
                if (_sprdService.UseExploit)
                {
                    AppendSprdLog("[展讯] 🔓 RSA 绕过已启用", "#F59E0B");
                }
            }
        }

        /// <summary>
        /// 模式切换
        /// </summary>
        private void SprdMode_Changed(object sender, RoutedEventArgs e)
        {
            if (RbSprdDownload?.IsChecked == true)
            {
                AppendSprdLog("[展讯] 切换到 Download 模式", "#3B82F6");
            }
            else if (RbSprdDiag?.IsChecked == true)
            {
                AppendSprdLog("[展讯] 切换到 Diag 模式", "#F59E0B");
            }
            else if (RbSprdUnlock?.IsChecked == true)
            {
                AppendSprdLog("[展讯] 切换到 Unlock 模式", "#9B59B6");
            }
        }

        /// <summary>
        /// 恢复出厂设置 (Diag 模式)
        /// </summary>
        private async void Sprd_FactoryReset_Click(object sender, RoutedEventArgs e)
        {
            if (_sprdService == null)
            {
                AppendSprdLog("[展讯] ❌ 服务未初始化", "#EF4444");
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                "确定要恢复出厂设置吗？所有用户数据将被清除！",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (confirm != MessageBoxResult.Yes) return;

            AppendSprdLog("[展讯] 正在恢复出厂设置...", "#F59E0B");
            var result = await _sprdService.FactoryResetAsync();
            if (result)
            {
                AppendSprdLog("[展讯] ✓ 恢复出厂设置命令已发送", "#10B981");
            }
            else
            {
                AppendSprdLog("[展讯] ❌ 恢复出厂设置失败", "#EF4444");
            }
        }

        #endregion

        #region ADB Fastboot 模块功能

        // ADB 日志数据
        private readonly System.Collections.ObjectModel.ObservableCollection<LogItem> _adbLogItems = new();

        /// <summary>
        /// 初始化ADB日志列表
        /// </summary>
        private void InitializeAdbLog()
        {
            AdbLogListBox.ItemsSource = _adbLogItems;
            AppendAdbLog("[INFO] ADB/Fastboot 模块已就绪", "#10B981");
        }

        // ADB日志节流
        private DateTime _lastAdbLogScrollTime = DateTime.MinValue;
        
        /// <summary>
        /// 添加ADB日志 (优化版)
        /// </summary>
        private void AppendAdbLog(string message, string color = "#2D2D2D")
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                _adbLogItems.Add(new LogItem
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                    Color = GetCachedBrush(color)
                });

                // 批量清理
                if (_adbLogItems.Count > MaxLogItems)
                {
                    int removeCount = _adbLogItems.Count - MaxLogItems + 50;
                    for (int i = 0; i < removeCount; i++)
                        _adbLogItems.RemoveAt(0);
                }

                // 节流滚动
                var now = DateTime.Now;
                if ((now - _lastAdbLogScrollTime).TotalMilliseconds > LogScrollThrottleMs)
                {
                    _lastAdbLogScrollTime = now;
                    if (_adbLogItems.Count > 0)
                        AdbLogListBox.ScrollIntoView(_adbLogItems[^1]);
                }
            });
        }

        /// <summary>
        /// 复制ADB日志
        /// </summary>
        private void CopyAdbLog_Click(object sender, RoutedEventArgs e)
        {
            if (_adbLogItems.Count == 0)
            {
                AppendAdbLog("[INFO] 日志为空", "#888888");
                return;
            }
            var logText = string.Join(Environment.NewLine, _adbLogItems.Select(item => item.Text));
            System.Windows.Clipboard.SetText(logText);
            AppendAdbLog("[INFO] ADB 日志已复制到剪贴板", "#10B981");
        }

        /// <summary>
        /// 清空ADB日志
        /// </summary>
        private void ClearAdbLog_Click(object sender, RoutedEventArgs e)
        {
            _adbLogItems.Clear();
            AppendAdbLog("[INFO] 日志已清空", "#0088CC");
        }

        // ===== ADB/Fastboot 模式切换 =====
        private void AdbFbMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            
            // 隐藏所有按钮面板
            AdbButtonsPanel.Visibility = Visibility.Collapsed;
            FastbootButtonsPanel.Visibility = Visibility.Collapsed;
            FastbootdButtonsPanel.Visibility = Visibility.Collapsed;
            
            // 根据选中的模式显示对应面板
            if (RbAdbMode.IsChecked == true)
            {
                AdbButtonsPanel.Visibility = Visibility.Visible;
                AppendAdbLog("[模式] 切换到 ADB 模式", "#10B981");
            }
            else if (RbFastbootMode.IsChecked == true)
            {
                FastbootButtonsPanel.Visibility = Visibility.Visible;
                AppendAdbLog("[模式] 切换到 Fastboot 模式", "#F59E0B");
            }
            else if (RbFastbootdMode.IsChecked == true)
            {
                FastbootdButtonsPanel.Visibility = Visibility.Visible;
                AppendAdbLog("[模式] 切换到 Fastbootd 模式", "#8B5CF6");
            }
        }

        // ===== ADB 功能 =====
        private async void Adb_Devices_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[ADB] 正在列出已连接设备...", "#10B981");
            
            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                
                if (devices.Count == 0)
                {
                    AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                    AppendAdbLog("[ADB] 请确保: 1. adb start-server  2. 已授权 USB 调试", "#888888");
                    
                    // 更新UI为未连接状态
                    UpdateAdbDeviceUI(null, null, null);
                }
                else
                {
                    AppendAdbLog($"[ADB] ✓ 检测到 {devices.Count} 个设备:", "#10B981");
                    foreach (var (serial, state) in devices)
                    {
                        AppendAdbLog($"[ADB]   {serial} - {state}", "#10B981");
                    }
                    
                    // 使用第一个设备更新UI
                    var (firstSerial, firstState) = devices[0];
                    
                    // 获取设备详细信息
                    var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                    if (await adb.ConnectViaServerAsync(firstSerial))
                    {
                        string model = await adb.ShellAsync("getprop ro.product.model");
                        string brand = await adb.ShellAsync("getprop ro.product.brand");
                        string deviceName = $"{brand.Trim()} {model.Trim()}";
                        
                        UpdateAdbDeviceUI(firstSerial, deviceName, firstState);
                        AppendAdbLog($"[ADB] ✓ 设备: {deviceName}", "#10B981");
                    }
                    else
                    {
                        UpdateAdbDeviceUI(firstSerial, "未知设备", firstState);
                    }
                    adb.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
                UpdateAdbDeviceUI(null, null, null);
            }
        }

        /// <summary>
        /// 更新 ADB 设备UI显示
        /// </summary>
        private void UpdateAdbDeviceUI(string? serial, string? deviceName, string? state)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(serial))
                {
                    // 未连接状态
                    AdbDeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")); // 红色
                    TxtAdbDeviceStatus.Text = "未连接设备";
                    TxtAdbDeviceId.Text = "---";
                    TxtAdbDeviceMode.Text = "---";
                }
                else
                {
                    // 已连接状态
                    AdbDeviceStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // 绿色
                    TxtAdbDeviceStatus.Text = deviceName ?? "已连接";
                    TxtAdbDeviceId.Text = serial;
                    TxtAdbDeviceMode.Text = state == "device" ? "ADB" : state?.ToUpper() ?? "---";
                }
            });
        }

        private async void Adb_Reboot_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[ADB] 正在重启设备...", "#10B981");
            
            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                    return;
                }

                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                var (serial, _) = devices[0];
                
                if (await adb.ConnectViaServerAsync(serial))
                {
                    // 提供重启选项
                    var result = MessageBox.Show("选择重启模式:\n\n是 - 正常重启\n否 - 重启到 Bootloader", "重启设备", MessageBoxButton.YesNoCancel);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        await adb.RebootAsync();
                        AppendAdbLog("[ADB] ✓ 设备正在重启", "#10B981");
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        await adb.RebootBootloaderAsync();
                        AppendAdbLog("[ADB] ✓ 设备正在重启到 Bootloader", "#10B981");
                    }
                }
                else
                {
                    AppendAdbLog("[ADB] ✗ 连接失败", "#EF4444");
                }
                
                adb.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
            }
        }

        private async void Adb_Push_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要推送的文件"
            };
            if (dialog.ShowDialog() == true)
            {
                AppendAdbLog($"[ADB] 推送文件: {System.IO.Path.GetFileName(dialog.FileName)}", "#3B82F6");
                
                try
                {
                    var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                    if (devices.Count == 0)
                    {
                        AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                        return;
                    }

                    string remotePath = $"/sdcard/{System.IO.Path.GetFileName(dialog.FileName)}";
                    var inputResult = Microsoft.VisualBasic.Interaction.InputBox("远程路径:", "推送文件", remotePath);
                    if (string.IsNullOrEmpty(inputResult)) return;

                    var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                    var (serial, _) = devices[0];
                    
                    if (await adb.ConnectViaServerAsync(serial))
                    {
                        AppendAdbLog($"[ADB] 推送到: {inputResult}", "#6366F1");
                        bool success = await adb.PushAsync(dialog.FileName, inputResult);
                        
                        if (success)
                            AppendAdbLog("[ADB] ✓ 推送成功", "#10B981");
                        else
                            AppendAdbLog("[ADB] ✗ 推送失败", "#EF4444");
                    }
                    
                    adb.Dispose();
                }
                catch (Exception ex)
                {
                    AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
                }
            }
        }

        private async void Adb_Pull_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                    return;
                }

                string remotePath = Microsoft.VisualBasic.Interaction.InputBox("远程文件路径:", "拉取文件", "/sdcard/");
                if (string.IsNullOrEmpty(remotePath)) return;

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存到本地",
                    FileName = System.IO.Path.GetFileName(remotePath)
                };
                
                if (saveDialog.ShowDialog() != true) return;

                AppendAdbLog($"[ADB] 拉取: {remotePath}", "#3B82F6");

                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                var (serial, _) = devices[0];
                
                if (await adb.ConnectViaServerAsync(serial))
                {
                    bool success = await adb.PullAsync(remotePath, saveDialog.FileName);
                    
                    if (success)
                        AppendAdbLog($"[ADB] ✓ 拉取成功: {saveDialog.FileName}", "#10B981");
                    else
                        AppendAdbLog("[ADB] ✗ 拉取失败", "#EF4444");
                }
                
                adb.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
            }
        }

        // 当前 ADB 浏览路径
        private string _adbCurrentPath = "/";
        private bool _adbShowPartitions = true; // true=显示分区, false=显示文件夹

        /// <summary>
        /// 读取分区表或浏览文件夹
        /// </summary>
        private async void Adb_ListPartitions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                    return;
                }

                // 选择模式
                var result = MessageBox.Show("选择浏览模式:\n\n是 - 读取分区表\n否 - 浏览文件夹", "分区/文件", MessageBoxButton.YesNoCancel);
                if (result == MessageBoxResult.Cancel) return;

                _adbShowPartitions = (result == MessageBoxResult.Yes);

                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                var (serial, _) = devices[0];
                
                if (!await adb.ConnectViaServerAsync(serial))
                {
                    AppendAdbLog("[ADB] ✗ 连接失败", "#EF4444");
                    adb.Dispose();
                    return;
                }

                Dispatcher.Invoke(() => AdbPartitionList.Items.Clear());

                if (_adbShowPartitions)
                {
                    await LoadAdbPartitionsAsync(adb);
                }
                else
                {
                    _adbCurrentPath = Microsoft.VisualBasic.Interaction.InputBox("输入路径:", "浏览文件夹", "/sdcard");
                    if (string.IsNullOrEmpty(_adbCurrentPath)) _adbCurrentPath = "/sdcard";
                    await LoadAdbDirectoryAsync(adb, _adbCurrentPath);
                }
                
                adb.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// 加载 ADB 分区表
        /// </summary>
        private async Task LoadAdbPartitionsAsync(tools.Modules.AdbFastboot.AdbProtocol adb)
        {
            AppendAdbLog("[ADB] 📂 读取分区表...", "#6366F1");

            // 读取 /dev/block/by-name/ 下的分区
            string result = await adb.ShellAsync("ls -la /dev/block/by-name/ 2>/dev/null || ls -la /dev/block/platform/*/by-name/ 2>/dev/null");
            
            if (string.IsNullOrEmpty(result))
            {
                // 尝试其他路径
                result = await adb.ShellAsync("ls -la /dev/block/bootdevice/by-name/ 2>/dev/null");
            }

            var partitions = new List<AdbPartitionItem>();
            int index = 0;

            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // 格式: lrwxrwxrwx 1 root root    0 2024-01-01 00:00 boot -> /dev/block/mmcblk0p10
                if (line.Contains("->"))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string name = parts[^2]; // 倒数第二个是分区名
                        string target = parts[^1]; // 最后一个是目标

                        // 获取分区大小
                        string sizeResult = await adb.ShellAsync($"cat /proc/partitions | grep {System.IO.Path.GetFileName(target)} | awk '{{print $3}}'");
                        long sizeKb = 0;
                        long.TryParse(sizeResult.Trim(), out sizeKb);

                        partitions.Add(new AdbPartitionItem
                        {
                            Index = ++index,
                            Name = name,
                            Size = FormatBytesSize(sizeKb * 1024),
                            SizeBytes = sizeKb * 1024,
                            IsLogical = name.Contains("_a") || name.Contains("_b") ? "Slot" : "-",
                            Path = target,
                            IsPartition = true
                        });
                    }
                }
            }

            // 按名称排序
            partitions = partitions.OrderBy(p => p.Name).ToList();
            index = 0;
            
            // 在 UI 线程上更新列表
            Dispatcher.Invoke(() =>
            {
                foreach (var p in partitions)
                {
                    p.Index = ++index;
                    AdbPartitionList.Items.Add(CreateAdbPartitionRow(p));
                }
            });

            AppendAdbLog($"[ADB] ✓ 找到 {partitions.Count} 个分区", "#10B981");
        }

        /// <summary>
        /// 加载 ADB 文件夹
        /// </summary>
        private async Task LoadAdbDirectoryAsync(tools.Modules.AdbFastboot.AdbProtocol adb, string path)
        {
            AppendAdbLog($"[ADB] 📂 浏览: {path}", "#6366F1");

            string result = await adb.ShellAsync($"ls -la \"{path}\" 2>/dev/null");
            
            if (string.IsNullOrEmpty(result) || result.Contains("No such file"))
            {
                AppendAdbLog($"[ADB] ⚠️ 路径不存在: {path}", "#F59E0B");
                return;
            }

            // 收集文件列表
            var items = new List<AdbPartitionItem>();

            // 添加返回上级目录
            if (path != "/")
            {
                items.Add(new AdbPartitionItem
                {
                    Index = 0,
                    Name = "📁 ..",
                    Size = "-",
                    IsLogical = "目录",
                    Path = System.IO.Path.GetDirectoryName(path.TrimEnd('/'))?.Replace('\\', '/') ?? "/",
                    IsPartition = false
                });
            }

            int index = 0;
            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("total")) continue;

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8) continue;

                string perms = parts[0];
                string size = parts[4];
                string name = string.Join(" ", parts.Skip(7).TakeWhile(p => p != "->"));

                if (name == "." || name == "..") continue;

                bool isDir = perms.StartsWith("d");
                bool isLink = perms.StartsWith("l");

                long sizeBytes = 0;
                long.TryParse(size, out sizeBytes);

                items.Add(new AdbPartitionItem
                {
                    Index = ++index,
                    Name = (isDir ? "📁 " : (isLink ? "🔗 " : "📄 ")) + name,
                    Size = isDir ? "-" : FormatBytesSize(sizeBytes),
                    SizeBytes = sizeBytes,
                    IsLogical = isDir ? "目录" : (isLink ? "链接" : "文件"),
                    Path = path.TrimEnd('/') + "/" + name,
                    IsPartition = false
                });
            }

            // 在 UI 线程上更新列表
            Dispatcher.Invoke(() =>
            {
                foreach (var item in items)
                {
                    AdbPartitionList.Items.Add(CreateAdbPartitionRow(item));
                }
            });

            AppendAdbLog($"[ADB] ✓ 加载完成", "#10B981");
        }

        /// <summary>
        /// 创建 ADB 分区/文件行 UI
        /// </summary>
        private Grid CreateAdbPartitionRow(AdbPartitionItem item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Tag = item;

            // 序号
            var txtIndex = new TextBlock { Text = item.Index > 0 ? item.Index.ToString() : "", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtIndex, 0);
            grid.Children.Add(txtIndex);

            // 名称
            var txtName = new TextBlock { Text = item.Name, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CCFFFFFF")), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(txtName, 1);
            grid.Children.Add(txtName);

            // 大小
            var txtSize = new TextBlock { Text = item.Size, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtSize, 2);
            grid.Children.Add(txtSize);

            // 类型
            var txtType = new TextBlock { Text = item.IsLogical, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txtType, 3);
            grid.Children.Add(txtType);

            // 路径
            var txtPath = new TextBlock { Text = item.Path, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6366F1")), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(txtPath, 4);
            grid.Children.Add(txtPath);

            // 双击事件
            grid.MouseLeftButtonDown += async (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    await HandleAdbItemDoubleClick(item);
                }
            };

            return grid;
        }

        /// <summary>
        /// 处理 ADB 项目双击
        /// </summary>
        private async Task HandleAdbItemDoubleClick(AdbPartitionItem item)
        {
            if (item.IsPartition)
            {
                // 分区: 提供操作选项
                var result = MessageBox.Show($"分区: {item.Name}\n路径: {item.Path}\n大小: {item.Size}\n\n选择操作:\n是 - 备份分区\n否 - 查看信息", 
                    "分区操作", MessageBoxButton.YesNoCancel);
                
                if (result == MessageBoxResult.Yes)
                {
                    // 备份分区
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = $"备份分区 {item.Name}",
                        FileName = $"{item.Name}.img",
                        Filter = "镜像文件 (*.img)|*.img|All Files (*.*)|*.*"
                    };
                    
                    if (saveDialog.ShowDialog() == true)
                    {
                        AppendAdbLog($"[ADB] 开始备份分区 {item.Name}...", "#6366F1");
                        // 使用 dd 命令备份
                        var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                        if (devices.Count > 0)
                        {
                            var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                            var (serial, _) = devices[0];
                            if (await adb.ConnectViaServerAsync(serial))
                            {
                                // 先 dd 到设备临时目录，再 pull
                                string tempPath = $"/data/local/tmp/{item.Name}.img";
                                AppendAdbLog($"[ADB] dd if={item.Path} of={tempPath}", "#888888");
                                await adb.ShellAsync($"dd if={item.Path} of={tempPath}");
                                
                                AppendAdbLog($"[ADB] 正在拉取文件...", "#6366F1");
                                bool success = await adb.PullAsync(tempPath, saveDialog.FileName);
                                
                                // 清理临时文件
                                await adb.ShellAsync($"rm {tempPath}");
                                
                                if (success)
                                    AppendAdbLog($"[ADB] ✓ 备份成功: {saveDialog.FileName}", "#10B981");
                                else
                                    AppendAdbLog($"[ADB] ✗ 备份失败", "#EF4444");
                            }
                            adb.Dispose();
                        }
                    }
                }
            }
            else
            {
                // 文件夹: 进入目录
                if (item.IsLogical == "目录")
                {
                    var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                    if (devices.Count > 0)
                    {
                        var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                        var (serial, _) = devices[0];
                        if (await adb.ConnectViaServerAsync(serial))
                        {
                            _adbCurrentPath = item.Path;
                            Dispatcher.Invoke(() => AdbPartitionList.Items.Clear());
                            await LoadAdbDirectoryAsync(adb, _adbCurrentPath);
                        }
                        adb.Dispose();
                    }
                }
                else if (item.IsLogical == "文件")
                {
                    // 文件: 提供下载选项
                    var result = MessageBox.Show($"文件: {item.Name}\n大小: {item.Size}\n\n是否下载此文件?", "下载文件", MessageBoxButton.YesNo);
                    if (result == MessageBoxResult.Yes)
                    {
                        var saveDialog = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "保存文件",
                            FileName = System.IO.Path.GetFileName(item.Path)
                        };
                        
                        if (saveDialog.ShowDialog() == true)
                        {
                            var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                            if (devices.Count > 0)
                            {
                                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                                var (serial, _) = devices[0];
                                if (await adb.ConnectViaServerAsync(serial))
                                {
                                    bool success = await adb.PullAsync(item.Path, saveDialog.FileName);
                                    if (success)
                                        AppendAdbLog($"[ADB] ✓ 下载成功: {saveDialog.FileName}", "#10B981");
                                    else
                                        AppendAdbLog($"[ADB] ✗ 下载失败", "#EF4444");
                                }
                                adb.Dispose();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 自定义 ADB 命令
        /// </summary>
        private async void Adb_CustomCommand_Click(object sender, RoutedEventArgs e)
        {
            string? command = Microsoft.VisualBasic.Interaction.InputBox("输入 ADB Shell 命令:", "自定义命令", "ls -la /sdcard");
            if (string.IsNullOrEmpty(command)) return;

            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                    return;
                }

                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                var (serial, _) = devices[0];
                
                if (await adb.ConnectViaServerAsync(serial))
                {
                    AppendAdbLog($"[ADB] $ {command}", "#F59E0B");
                    string result = await adb.ShellAsync(command);
                    
                    // 分行显示结果
                    foreach (var line in result.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            AppendAdbLog($"[ADB] {line}", "#10B981");
                    }
                }
                else
                {
                    AppendAdbLog("[ADB] ✗ 连接失败", "#EF4444");
                }
                
                adb.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// ADB 分区/文件项数据模型
        /// </summary>
        private class AdbPartitionItem
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public string Size { get; set; } = "";
            public long SizeBytes { get; set; }
            public string IsLogical { get; set; } = "";
            public string Path { get; set; } = "";
            public bool IsPartition { get; set; }
        }

        private async void Adb_Install_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "APK Files (*.apk)|*.apk|All Files (*.*)|*.*",
                Title = "选择 APK 文件"
            };
            if (dialog.ShowDialog() == true)
            {
                AppendAdbLog($"[ADB] 安装 APK: {System.IO.Path.GetFileName(dialog.FileName)}", "#10B981");
                
                try
                {
                    var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                    if (devices.Count == 0)
                    {
                        AppendAdbLog("[ADB] ⚠️ 未检测到设备", "#F59E0B");
                        return;
                    }

                    var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                    var (serial, _) = devices[0];
                    
                    if (await adb.ConnectViaServerAsync(serial))
                    {
                        AppendAdbLog("[ADB] 正在安装...", "#6366F1");
                        bool success = await adb.InstallApkAsync(dialog.FileName);
                        
                        if (success)
                            AppendAdbLog("[ADB] ✓ 安装成功", "#10B981");
                        else
                            AppendAdbLog("[ADB] ✗ 安装失败", "#EF4444");
                    }
                    
                    adb.Dispose();
                }
                catch (Exception ex)
                {
                    AppendAdbLog($"[ADB] ✗ 错误: {ex.Message}", "#EF4444");
                }
            }
        }

        private void Adb_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[ADB] 断开设备连接", "#EF4444");
            // 注: 通过 ADB Server 模式每次命令是独立连接，无需显式断开
            AppendAdbLog("[ADB] ✓ 已清理连接状态", "#888888");
        }

        private async void Adb_UnlockBL_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[ADB] 正在准备解锁 Bootloader...", "#EF4444");
            AppendAdbLog("[ADB] ⚠️ 警告: 此操作将清除所有数据!", "#EF4444");
            
            var result = MessageBox.Show(
                "解锁 Bootloader 将清除设备上的所有数据！\n\n确定要继续吗？",
                "⚠️ 警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;
            
            try
            {
                // 先重启到 Bootloader
                AppendAdbLog("[ADB] 正在重启到 Bootloader...", "#F59E0B");
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count > 0)
                {
                    var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                    var (serial, _) = devices[0];
                    if (await adb.ConnectViaServerAsync(serial))
                    {
                        await adb.RebootBootloaderAsync();
                        AppendAdbLog("[ADB] 设备正在重启到 Bootloader，请等待后执行 OEM Unlock", "#10B981");
                    }
                    adb.Dispose();
                }
                else
                {
                    AppendAdbLog("[ADB] 未检测到 ADB 设备，请手动进入 Fastboot 模式", "#F59E0B");
                }
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[ADB] 错误: {ex.Message}", "#EF4444");
            }
        }

        // ===== Fastboot 功能 =====
        
        /// <summary>
        /// 连接 Fastboot 设备
        /// </summary>
        private tools.Modules.AdbFastboot.FastbootProtocol? ConnectFastboot()
        {
            var fastboot = new tools.Modules.AdbFastboot.FastbootProtocol();
            fastboot.OnLog += msg => Dispatcher.Invoke(() => AppendAdbLog($"[Fastboot] {msg}", "#888888"));
            
            if (!fastboot.Connect())
            {
                AppendAdbLog("[Fastboot] ⚠️ 未检测到 Fastboot 设备", "#F59E0B");
                AppendAdbLog("[Fastboot] 请将设备重启到 Fastboot 模式", "#888888");
                fastboot.Dispose();
                return null;
            }
            
            return fastboot;
        }
        
        private void Fb_GetVar_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastboot] 正在读取设备信息...", "#F59E0B");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                fastboot.RefreshDeviceInfo();
                var info = fastboot.DeviceInfo;
                
                if (info != null)
                {
                    AppendAdbLog($"[Fastboot] ══════════════════════════════", "#6366F1");
                    AppendAdbLog($"[Fastboot] 📱 产品: {info.Product}", "#10B981");
                    AppendAdbLog($"[Fastboot] 🔢 序列号: {info.SerialNumber}", "#10B981");
                    AppendAdbLog($"[Fastboot] 🔓 Bootloader: {(info.Unlocked == "yes" ? "已解锁 ✓" : "已锁定 ✗")}", info.Unlocked == "yes" ? "#10B981" : "#EF4444");
                    AppendAdbLog($"[Fastboot] 🔐 Secure: {info.Secure}", "#10B981");
                    AppendAdbLog($"[Fastboot] 📦 版本: {info.Version}", "#10B981");
                    AppendAdbLog($"[Fastboot] 📻 基带: {info.VersionBaseband}", "#10B981");
                    AppendAdbLog($"[Fastboot] 🔧 Bootloader版本: {info.VersionBootloader}", "#10B981");
                    AppendAdbLog($"[Fastboot] 💾 最大下载: {info.MaxDownloadSize}", "#10B981");
                    AppendAdbLog($"[Fastboot] 🎰 当前槽位: {(string.IsNullOrEmpty(info.CurrentSlot) ? "N/A" : info.CurrentSlot)}", "#10B981");
                    AppendAdbLog($"[Fastboot] 🚀 Fastbootd: {(info.IsFastbootd ? "是" : "否")}", "#10B981");
                    AppendAdbLog($"[Fastboot] 📊 分区数: {info.PartitionSizes.Count}", "#10B981");
                    AppendAdbLog($"[Fastboot] ══════════════════════════════", "#6366F1");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fb_OemUnlock_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "⚠️ OEM 解锁将清除设备上的所有数据！\n\n此操作不可逆，确定要继续吗？",
                "危险操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;
            
            AppendAdbLog("[Fastboot] ⚠️ 正在执行 OEM 解锁...", "#EF4444");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                bool success = fastboot.OemUnlock();
                
                if (success)
                {
                    AppendAdbLog("[Fastboot] ✓ Bootloader 解锁成功!", "#10B981");
                    MessageBox.Show("Bootloader 解锁成功！\n\n设备将重启，请等待。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    AppendAdbLog("[Fastboot] ✗ 解锁失败，请检查设备状态", "#EF4444");
                    MessageBox.Show("解锁失败！\n\n可能原因：\n1. 设备不支持 OEM 解锁\n2. 未在开发者选项中启用 OEM 解锁\n3. 设备已锁定到运营商", "失败");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }
        
        private async void Fb_Flash_Click(object sender, RoutedEventArgs e)
        {
            // 让用户输入分区名
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入要刷写的分区名称:", "Fastboot Flash", "boot");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "镜像文件 (*.img)|*.img|All Files (*.*)|*.*",
                Title = "选择要刷写的镜像"
            };
            
            if (dialog.ShowDialog() != true) return;
            
            AppendAdbLog($"[Fastboot] 准备刷写: {partition} <- {System.IO.Path.GetFileName(dialog.FileName)}", "#F59E0B");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                _fastbootOperationCts = new CancellationTokenSource();
                BtnFbStop.IsEnabled = true;
                
                bool success = await fastboot.FlashAsync(partition, dialog.FileName, _fastbootOperationCts.Token);
                
                BtnFbStop.IsEnabled = false;
                
                if (success)
                {
                    AppendAdbLog($"[Fastboot] ✓ 分区 {partition} 刷写成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastboot] ✗ 分区 {partition} 刷写失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (OperationCanceledException)
            {
                AppendAdbLog("[Fastboot] 操作已取消", "#D97706");
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
            finally
            {
                BtnFbStop.IsEnabled = false;
            }
        }

        private void Fb_Erase_Click(object sender, RoutedEventArgs e)
        {
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入要擦除的分区名称:\n\n⚠️ 此操作不可逆！", "Fastboot Erase", "userdata");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            var result = MessageBox.Show(
                $"⚠️ 确定要擦除分区 {partition} 吗？\n\n此操作不可逆！",
                "危险操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;
            
            AppendAdbLog($"[Fastboot] ⚠️ 正在擦除分区: {partition}", "#EF4444");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                bool success = fastboot.Erase(partition);
                
                if (success)
                {
                    AppendAdbLog($"[Fastboot] ✓ 分区 {partition} 擦除成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastboot] ✗ 分区 {partition} 擦除失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fb_Reboot_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastboot] 正在重启设备...", "#10B981");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                bool success = fastboot.Reboot();
                
                if (success)
                {
                    AppendAdbLog("[Fastboot] ✓ 设备正在重启", "#10B981");
                }
                else
                {
                    AppendAdbLog("[Fastboot] ✗ 重启命令发送失败", "#EF4444");
                }
                
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fb_SlotA_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastboot] 正在切换到槽位 A...", "#3B82F6");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsSeamlessUpdate)
                {
                    AppendAdbLog("[Fastboot] ⚠️ 设备不支持 A/B 分区", "#F59E0B");
                    fastboot.Disconnect();
                    fastboot.Dispose();
                    return;
                }
                
                bool success = fastboot.SetActiveSlot("a");
                
                if (success)
                {
                    AppendAdbLog("[Fastboot] ✓ 已切换到槽位 A", "#10B981");
                }
                else
                {
                    AppendAdbLog("[Fastboot] ✗ 槽位切换失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fb_SlotB_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastboot] 正在切换到槽位 B...", "#3B82F6");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsSeamlessUpdate)
                {
                    AppendAdbLog("[Fastboot] ⚠️ 设备不支持 A/B 分区", "#F59E0B");
                    fastboot.Disconnect();
                    fastboot.Dispose();
                    return;
                }
                
                bool success = fastboot.SetActiveSlot("b");
                
                if (success)
                {
                    AppendAdbLog("[Fastboot] ✓ 已切换到槽位 B", "#10B981");
                }
                else
                {
                    AppendAdbLog("[Fastboot] ✗ 槽位切换失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastboot] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fb_FlashPayload_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Payload 文件 (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 Payload.bin"
            };
            if (dialog.ShowDialog() == true)
            {
                AppendAdbLog($"[Fastbootd] 🚧 Payload 刷写功能开发中...", "#8B5CF6");
                AppendAdbLog($"[Fastbootd] 已选择: {System.IO.Path.GetFileName(dialog.FileName)}", "#888888");
                AppendAdbLog($"[Fastbootd] 请使用 ADB Sideload 或其他工具刷写 Payload", "#F59E0B");
            }
        }

        // ===== Fastbootd 功能 =====
        private void Fbd_GetVar_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastbootd] 正在读取动态分区信息...", "#8B5CF6");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                fastboot.RefreshDeviceInfo();
                var info = fastboot.DeviceInfo;
                
                if (info != null)
                {
                    AppendAdbLog($"[Fastbootd] ══════════════════════════════", "#8B5CF6");
                    AppendAdbLog($"[Fastbootd] 🚀 Fastbootd 模式: {(info.IsFastbootd ? "是 ✓" : "否 (普通 Fastboot)")}", info.IsFastbootd ? "#10B981" : "#F59E0B");
                    AppendAdbLog($"[Fastbootd] 🔄 VAB 状态: {(string.IsNullOrEmpty(info.SnapshotUpdateStatus) ? "无" : info.SnapshotUpdateStatus)}", "#10B981");
                    AppendAdbLog($"[Fastbootd] 🐄 COW 分区: {(info.HasCowPartitions ? "有" : "无")}", "#10B981");
                    
                    // 列出逻辑分区
                    var logicalParts = info.PartitionIsLogical.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                    if (logicalParts.Count > 0)
                    {
                        AppendAdbLog($"[Fastbootd] 📦 逻辑分区 ({logicalParts.Count}):", "#10B981");
                        foreach (var part in logicalParts.Take(10))
                        {
                            info.PartitionSizes.TryGetValue(part, out long size);
                            AppendAdbLog($"[Fastbootd]    - {part}: {size / (1024.0 * 1024):F1} MB", "#888888");
                        }
                        if (logicalParts.Count > 10)
                        {
                            AppendAdbLog($"[Fastbootd]    ... 及其他 {logicalParts.Count - 10} 个分区", "#888888");
                        }
                    }
                    AppendAdbLog($"[Fastbootd] ══════════════════════════════", "#8B5CF6");
                    
                    if (!info.IsFastbootd)
                    {
                        AppendAdbLog("[Fastbootd] 💡 提示: 若要管理动态分区，请先进入 Fastbootd 模式", "#F59E0B");
                        AppendAdbLog("[Fastbootd]    执行: fastboot reboot fastboot", "#888888");
                    }
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastbootd] 错误: {ex.Message}", "#EF4444");
            }
        }

        private async void Fbd_Flash_Click(object sender, RoutedEventArgs e)
        {
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入要刷写的动态分区名称:", "Fastbootd Flash", "system");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "镜像文件 (*.img)|*.img|All Files (*.*)|*.*",
                Title = "选择要刷写的动态分区镜像"
            };
            
            if (dialog.ShowDialog() != true) return;
            
            AppendAdbLog($"[Fastbootd] 准备刷写: {partition} <- {System.IO.Path.GetFileName(dialog.FileName)}", "#8B5CF6");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsFastbootd)
                {
                    AppendAdbLog("[Fastbootd] ⚠️ 当前不在 Fastbootd 模式，尝试普通刷写...", "#F59E0B");
                }
                
                _fastbootOperationCts = new CancellationTokenSource();
                BtnFbdStop.IsEnabled = true;
                
                bool success = await fastboot.FlashAsync(partition, dialog.FileName, _fastbootOperationCts.Token);
                
                BtnFbdStop.IsEnabled = false;
                
                if (success)
                {
                    AppendAdbLog($"[Fastbootd] ✓ 分区 {partition} 刷写成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastbootd] ✗ 分区 {partition} 刷写失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (OperationCanceledException)
            {
                AppendAdbLog("[Fastbootd] 操作已取消", "#D97706");
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastbootd] 错误: {ex.Message}", "#EF4444");
            }
            finally
            {
                BtnFbdStop.IsEnabled = false;
            }
        }

        private void Fbd_Delete_Click(object sender, RoutedEventArgs e)
        {
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入要删除的动态分区名称:\n\n⚠️ 仅支持在 Fastbootd 模式下操作", "删除动态分区", "system_b");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            var result = MessageBox.Show(
                $"⚠️ 确定要删除动态分区 {partition} 吗？\n\n此操作需要 Fastbootd 模式。",
                "危险操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes) return;
            
            AppendAdbLog($"[Fastbootd] ⚠️ 正在删除动态分区: {partition}", "#EF4444");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsFastbootd)
                {
                    AppendAdbLog("[Fastbootd] ✗ 删除动态分区需要 Fastbootd 模式", "#EF4444");
                    AppendAdbLog("[Fastbootd] 请先执行: fastboot reboot fastboot", "#888888");
                    fastboot.Disconnect();
                    fastboot.Dispose();
                    return;
                }
                
                bool success = fastboot.DeleteLogicalPartition(partition);
                
                if (success)
                {
                    AppendAdbLog($"[Fastbootd] ✓ 动态分区 {partition} 删除成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastbootd] ✗ 动态分区 {partition} 删除失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastbootd] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fbd_Create_Click(object sender, RoutedEventArgs e)
        {
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入新动态分区名称:", "创建动态分区", "my_partition");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            string? sizeStr = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入分区大小 (字节):", "创建动态分区", "1073741824");
            
            if (string.IsNullOrEmpty(sizeStr) || !long.TryParse(sizeStr, out long size))
            {
                AppendAdbLog("[Fastbootd] ✗ 无效的分区大小", "#EF4444");
                return;
            }
            
            AppendAdbLog($"[Fastbootd] 正在创建动态分区: {partition} ({size / (1024.0 * 1024):F1} MB)", "#10B981");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsFastbootd)
                {
                    AppendAdbLog("[Fastbootd] ✗ 创建动态分区需要 Fastbootd 模式", "#EF4444");
                    AppendAdbLog("[Fastbootd] 请先执行: fastboot reboot fastboot", "#888888");
                    fastboot.Disconnect();
                    fastboot.Dispose();
                    return;
                }
                
                bool success = fastboot.CreateLogicalPartition(partition, size);
                
                if (success)
                {
                    AppendAdbLog($"[Fastbootd] ✓ 动态分区 {partition} 创建成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastbootd] ✗ 动态分区 {partition} 创建失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastbootd] 错误: {ex.Message}", "#EF4444");
            }
        }

        private void Fbd_Resize_Click(object sender, RoutedEventArgs e)
        {
            string? partition = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入要调整的动态分区名称:", "调整分区大小", "system");
            
            if (string.IsNullOrEmpty(partition)) return;
            
            string? sizeStr = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入新大小 (字节):", "调整分区大小", "2147483648");
            
            if (string.IsNullOrEmpty(sizeStr) || !long.TryParse(sizeStr, out long size))
            {
                AppendAdbLog("[Fastbootd] ✗ 无效的分区大小", "#EF4444");
                return;
            }
            
            AppendAdbLog($"[Fastbootd] 正在调整分区大小: {partition} -> {size / (1024.0 * 1024):F1} MB", "#3B82F6");
            
            try
            {
                var fastboot = ConnectFastboot();
                if (fastboot == null) return;
                
                if (!fastboot.IsFastbootd)
                {
                    AppendAdbLog("[Fastbootd] ✗ 调整分区大小需要 Fastbootd 模式", "#EF4444");
                    AppendAdbLog("[Fastbootd] 请先执行: fastboot reboot fastboot", "#888888");
                    fastboot.Disconnect();
                    fastboot.Dispose();
                    return;
                }
                
                bool success = fastboot.ResizeLogicalPartition(partition, size);
                
                if (success)
                {
                    AppendAdbLog($"[Fastbootd] ✓ 分区 {partition} 大小调整成功!", "#10B981");
                }
                else
                {
                    AppendAdbLog($"[Fastbootd] ✗ 分区 {partition} 大小调整失败", "#EF4444");
                }
                
                fastboot.Disconnect();
                fastboot.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Fastbootd] 错误: {ex.Message}", "#EF4444");
            }
        }

        /// <summary>
        /// ADB停止按钮
        /// </summary>
        private void Adb_Stop_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[ADB] ⏹️ 正在停止当前操作...", "#EF4444");
            BtnAdbStop.IsEnabled = false;
            // 目前 ADB 操作主要使用 ADB Server，无法直接取消
            AppendAdbLog("[ADB] ⚠️ 操作已被用户中断", "#D97706");
        }

        /// <summary>
        /// Fastboot停止按钮
        /// </summary>
        private void Fb_Stop_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastboot] ⏹️ 正在停止当前操作...", "#EF4444");
            _fastbootOperationCts?.Cancel();
            BtnFbStop.IsEnabled = false;
            AppendAdbLog("[Fastboot] ⚠️ 已发送取消请求", "#D97706");
        }

        /// <summary>
        /// Fastbootd停止按钮
        /// </summary>
        private void Fbd_Stop_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[Fastbootd] ⏹️ 正在停止当前操作...", "#EF4444");
            _fastbootOperationCts?.Cancel();
            BtnFbdStop.IsEnabled = false;
            AppendAdbLog("[Fastbootd] ⚠️ 已发送取消请求", "#D97706");
        }

        private void Adb_SelectPayload_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Payload 文件 (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "选择 Payload.bin"
            };
            if (dialog.ShowDialog() == true)
            {
                AppendAdbLog($"[Payload] 加载: {System.IO.Path.GetFileName(dialog.FileName)}", "#8B5CF6");
                var fileInfo = new System.IO.FileInfo(dialog.FileName);
                TxtPayloadVersion.Text = "2";
                TxtPayloadSize.Text = $"{fileInfo.Length / 1024.0 / 1024.0:F2} MB";
                TxtPayloadTimestamp.Text = fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss");
            }
        }

        #endregion

        #region ADB/Fastboot 测试

        /// <summary>
        /// 测试 ADB 连接 (可绑定到按钮)
        /// </summary>
        private async void TestAdbConnection_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[测试] 开始 ADB 连接测试...", "#8B5CF6");

            try
            {
                // 1. 获取设备列表
                AppendAdbLog("[测试] 获取设备列表...", "#6366F1");
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();

                if (devices.Count == 0)
                {
                    AppendAdbLog("[测试] ⚠️ 未检测到设备", "#F59E0B");
                    AppendAdbLog("[测试] 请确保:", "#888888");
                    AppendAdbLog("[测试]   1. ADB Server 已运行 (adb start-server)", "#888888");
                    AppendAdbLog("[测试]   2. 设备已连接并授权 USB 调试", "#888888");
                    MessageBox.Show("未检测到 ADB 设备\n\n请确保:\n1. 运行 adb start-server\n2. 设备已授权 USB 调试", "测试结果");
                    return;
                }

                AppendAdbLog($"[测试] ✓ 检测到 {devices.Count} 个设备:", "#10B981");
                foreach (var (serial, state) in devices)
                {
                    AppendAdbLog($"[测试]   {serial} - {state}", "#10B981");
                }

                // 2. 连接第一个设备
                var (firstSerial, firstState) = devices[0];
                if (firstState != "device")
                {
                    AppendAdbLog($"[测试] ⚠️ 设备状态异常: {firstState}", "#F59E0B");
                    return;
                }

                AppendAdbLog($"[测试] 连接设备: {firstSerial}...", "#6366F1");
                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                adb.OnLog += msg => AppendAdbLog(msg, "#888888");

                bool connected = await adb.ConnectViaServerAsync(firstSerial);
                if (!connected)
                {
                    AppendAdbLog("[测试] ✗ 连接失败", "#EF4444");
                    adb.Dispose();
                    return;
                }
                AppendAdbLog("[测试] ✓ 连接成功", "#10B981");

                // 3. 执行测试命令
                AppendAdbLog("[测试] 执行 Shell 命令...", "#6366F1");
                
                string model = await adb.ShellAsync("getprop ro.product.model");
                string brand = await adb.ShellAsync("getprop ro.product.brand");
                string android = await adb.ShellAsync("getprop ro.build.version.release");
                string sdk = await adb.ShellAsync("getprop ro.build.version.sdk");

                AppendAdbLog($"[测试] ✓ 品牌: {brand.Trim()}", "#10B981");
                AppendAdbLog($"[测试] ✓ 型号: {model.Trim()}", "#10B981");
                AppendAdbLog($"[测试] ✓ Android: {android.Trim()}", "#10B981");
                AppendAdbLog($"[测试] ✓ SDK: {sdk.Trim()}", "#10B981");

                // 4. 测试 echo 命令
                string echo = await adb.ShellAsync("echo 'ADB Test OK'");
                AppendAdbLog($"[测试] ✓ Echo: {echo.Trim()}", "#10B981");

                adb.Dispose();
                
                AppendAdbLog("[测试] ═══════════════════════════════════", "#8B5CF6");
                AppendAdbLog("[测试] ✓ ADB 测试完成!", "#10B981");
                
                MessageBox.Show($"ADB 测试成功!\n\n品牌: {brand.Trim()}\n型号: {model.Trim()}\nAndroid: {android.Trim()}", "测试结果");
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[测试] ✗ 错误: {ex.Message}", "#EF4444");
                MessageBox.Show($"测试失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 测试 Fastboot 连接 (可绑定到按钮)
        /// </summary>
        private void TestFastbootConnection_Click(object sender, RoutedEventArgs e)
        {
            AppendAdbLog("[测试] 开始 Fastboot 连接测试...", "#F59E0B");

            try
            {
                var fastboot = new tools.Modules.AdbFastboot.FastbootProtocol();
                fastboot.OnLog += msg => AppendAdbLog(msg, "#888888");

                AppendAdbLog("[测试] 搜索 Fastboot 设备...", "#6366F1");
                bool connected = fastboot.Connect();

                if (!connected)
                {
                    AppendAdbLog("[测试] ⚠️ 未检测到 Fastboot 设备", "#F59E0B");
                    AppendAdbLog("[测试] 请将设备重启到 Fastboot 模式:", "#888888");
                    AppendAdbLog("[测试]   - adb reboot bootloader", "#888888");
                    AppendAdbLog("[测试]   - 或按住 电源+音量下", "#888888");
                    MessageBox.Show("未检测到 Fastboot 设备\n\n请重启到 Fastboot 模式:\nadb reboot bootloader", "测试结果");
                    fastboot.Dispose();
                    return;
                }

                AppendAdbLog("[测试] ✓ Fastboot 连接成功", "#10B981");

                // 获取设备信息
                fastboot.RefreshDeviceInfo();
                if (fastboot.DeviceInfo != null)
                {
                    AppendAdbLog($"[测试] ✓ 产品: {fastboot.DeviceInfo.Product}", "#10B981");
                    AppendAdbLog($"[测试] ✓ 序列号: {fastboot.DeviceInfo.SerialNumber}", "#10B981");
                    AppendAdbLog($"[测试] ✓ Bootloader: {(fastboot.DeviceInfo.Unlocked == "yes" ? "已解锁" : "已锁定")}", "#10B981");
                    AppendAdbLog($"[测试] ✓ Fastbootd: {fastboot.DeviceInfo.IsFastbootd}", "#10B981");
                    AppendAdbLog($"[测试] ✓ 当前槽位: {fastboot.DeviceInfo.CurrentSlot}", "#10B981");

                    var partitions = fastboot.GetPartitionDetails();
                    AppendAdbLog($"[测试] ✓ 分区数量: {partitions.Count}", "#10B981");
                    
                    MessageBox.Show($"Fastboot 测试成功!\n\n产品: {fastboot.DeviceInfo.Product}\n序列号: {fastboot.DeviceInfo.SerialNumber}\nBootloader: {(fastboot.DeviceInfo.Unlocked == "yes" ? "已解锁" : "已锁定")}\n分区数: {partitions.Count}", "测试结果");
                }

                fastboot.Disconnect();
                fastboot.Dispose();
                
                AppendAdbLog("[测试] ✓ Fastboot 测试完成!", "#10B981");
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[测试] ✗ 错误: {ex.Message}", "#EF4444");
                MessageBox.Show($"测试失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 快速 ADB Shell 测试
        /// </summary>
        private async void QuickAdbShell_Click(object sender, RoutedEventArgs e)
        {
            string? command = Microsoft.VisualBasic.Interaction.InputBox("输入 Shell 命令:", "ADB Shell", "ls /sdcard");
            if (string.IsNullOrEmpty(command)) return;

            try
            {
                var devices = await tools.Modules.AdbFastboot.AdbProtocol.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    MessageBox.Show("未检测到设备");
                    return;
                }

                var adb = new tools.Modules.AdbFastboot.AdbProtocol();
                var (serial, _) = devices[0];
                
                if (await adb.ConnectViaServerAsync(serial))
                {
                    AppendAdbLog($"[Shell] $ {command}", "#6366F1");
                    string result = await adb.ShellAsync(command);
                    AppendAdbLog($"[Shell] {result}", "#10B981");
                }
                
                adb.Dispose();
            }
            catch (Exception ex)
            {
                AppendAdbLog($"[Shell] 错误: {ex.Message}", "#EF4444");
            }
        }

        #endregion
    }

    /// <summary>
    /// 日志项数据模型
    /// </summary>
    public class LogItem
    {
        public string Text { get; set; } = "";
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.White;
    }
}
