using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using tools.Modules.Qualcomm.Services;

namespace tools.Dialogs
{
    /// <summary>
    /// 云端 Loader 选择对话框
    /// 对接 MultiFlash Cloud API v2.0
    /// </summary>
    public partial class CloudLoaderDialog : Window
    {
        private readonly CloudLoaderService _cloudService;
        private List<CloudVendorInfo> _vendors = new();
        private List<CloudChipInfo> _chips = new();
        private CancellationTokenSource? _cts;
        
        // 分页状态
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalCount = 0;
        private const int PAGE_SIZE = 50;

        /// <summary>
        /// 选中的 Loader
        /// </summary>
        public CloudLoaderInfo? SelectedLoader { get; private set; }

        /// <summary>
        /// 下载的文件路径
        /// </summary>
        public CloudLoaderFiles? DownloadedFiles { get; private set; }

        /// <summary>
        /// 推荐的认证策略
        /// </summary>
        public string RecommendedAuthStrategy { get; private set; } = "standard";

        /// <summary>
        /// 是否需要启用VIP模式
        /// </summary>
        public bool ShouldEnableVip { get; private set; }

        public CloudLoaderDialog()
        {
            InitializeComponent();

            _cloudService = new CloudLoaderService(log: msg => 
                Dispatcher.Invoke(() => TxtStatus.Text = msg));

            // 初始化搜索框占位符
            TxtSearch.GotFocus += (s, e) => TxtSearchPlaceholder.Visibility = Visibility.Collapsed;
            TxtSearch.LostFocus += (s, e) => 
            {
                if (string.IsNullOrEmpty(TxtSearch.Text))
                    TxtSearchPlaceholder.Visibility = Visibility.Visible;
            };

            Loaded += CloudLoaderDialog_Loaded;
        }

        private async void CloudLoaderDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                TxtStatus.Text = "正在连接云端服务器...";

                // 测试连接
                bool connected = await _cloudService.TestConnectionAsync();
                if (!connected)
                {
                    TxtStatus.Text = "❌ 无法连接到云端服务器";
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                // 显示缓存信息
                var cacheSize = _cloudService.GetCacheSize();
                TxtCacheInfo.Text = cacheSize > 0 ? $"缓存: {cacheSize / 1024 / 1024} MB" : "";

                // 加载厂商列表
                await LoadVendorsAsync();

                // 加载芯片列表
                await LoadChipsAsync();

                // 加载 Loader 列表
                await LoadLoadersAsync();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"❌ 初始化失败: {ex.Message}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadVendorsAsync()
        {
            _vendors = await _cloudService.GetVendorsAsync();
            
            CmbVendor.Items.Clear();
            CmbVendor.Items.Add(new ComboBoxItem { Content = "全部厂商", Tag = "" });
            
            foreach (var vendor in _vendors.Where(v => v.IsActive).OrderBy(v => v.Name))
            {
                CmbVendor.Items.Add(new ComboBoxItem 
                { 
                    Content = vendor.Display,
                    Tag = vendor.Name
                });
            }
            
            CmbVendor.SelectedIndex = 0;
            TxtServerInfo.Text = $"📦 {_vendors.Count} 厂商";
        }

        private async Task LoadChipsAsync(string? vendor = null)
        {
            _chips = await _cloudService.GetChipsAsync(vendor);
            
            CmbChip.Items.Clear();
            CmbChip.Items.Add(new ComboBoxItem { Content = "全部芯片", Tag = "" });
            
            // 按 Loader 数量排序，显示有 Loader 的芯片
            foreach (var chip in _chips.Where(c => c.LoaderCount > 0).OrderByDescending(c => c.LoaderCount))
            {
                CmbChip.Items.Add(new ComboBoxItem 
                { 
                    Content = $"{chip.Display} ({chip.LoaderCount})",
                    Tag = chip.Name
                });
            }
            
            CmbChip.SelectedIndex = 0;
            
            // 更新服务器信息
            var totalLoaders = _chips.Sum(c => c.LoaderCount);
            TxtServerInfo.Text = $"📦 {_vendors.Count} 厂商 | {_chips.Count} 芯片 | {totalLoaders} Loader";
        }

        private async Task LoadLoadersAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LvLoaders.ItemsSource = null;

                // 构建查询
                var query = new CloudLoaderQuery
                {
                    Page = _currentPage,
                    PageSize = PAGE_SIZE,
                    Keyword = TxtSearch.Text?.Trim(),
                    Vendor = GetSelectedVendor(),
                    Chip = GetSelectedChip(),
                    RequiresVip = TglVipOnly.IsChecked == true ? true : null,
                    HasDigest = TglHasDigest.IsChecked == true ? true : null,
                    HasSign = TglHasSign.IsChecked == true ? true : null
                };

                var result = await _cloudService.SearchLoadersAsync(query);

                if (result.HasError)
                {
                    TxtStatus.Text = $"❌ 加载失败: {result.Error}";
                    return;
                }

                _totalCount = result.TotalCount;
                _totalPages = result.TotalPages;
                _currentPage = result.Page;

                LvLoaders.ItemsSource = result.Loaders;
                
                UpdatePagination();
                UpdateStatus(result.Loaders.Count, result.TotalCount);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"❌ 加载失败: {ex.Message}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private string? GetSelectedVendor()
        {
            if (CmbVendor.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                return string.IsNullOrEmpty(tag) ? null : tag;
            }
            return null;
        }

        private string? GetSelectedChip()
        {
            // 支持手动输入
            string? text = CmbChip.Text?.Trim();
            if (!string.IsNullOrEmpty(text) && !text.StartsWith("全部"))
            {
                // 如果包含括号，提取芯片名称
                int idx = text.IndexOf(" (");
                if (idx > 0)
                    return text.Substring(0, idx);
                return text;
            }

            if (CmbChip.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                return string.IsNullOrEmpty(tag) ? null : tag;
            }
            return null;
        }

        private void UpdatePagination()
        {
            BtnPrevPage.IsEnabled = _currentPage > 1;
            BtnNextPage.IsEnabled = _currentPage < _totalPages;
            TxtPageInfo.Text = _totalPages > 1 ? $"{_currentPage} / {_totalPages}" : "";
        }

        private void UpdateStatus(int displayed, int total)
        {
            string statusText;
            if (total == 0)
            {
                statusText = "未找到匹配的 Loader";
            }
            else if (displayed < total)
            {
                statusText = $"显示 {displayed} / {total} 个 Loader";
            }
            else
            {
                statusText = $"共 {total} 个 Loader";
            }

            // 添加筛选信息
            var filters = new List<string>();
            var vendor = GetSelectedVendor();
            var chip = GetSelectedChip();
            if (!string.IsNullOrEmpty(vendor))
                filters.Add(vendor);
            if (!string.IsNullOrEmpty(chip))
                filters.Add(chip);
            if (TglVipOnly.IsChecked == true)
                filters.Add("VIP");
            if (TglHasDigest.IsChecked == true)
                filters.Add("Digest");
            if (TglHasSign.IsChecked == true)
                filters.Add("Sign");

            if (filters.Count > 0)
                statusText += $" | 筛选: {string.Join(", ", filters)}";

            TxtStatus.Text = statusText;
        }

        #region 事件处理

        private async void CmbVendor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _currentPage = 1;
            await LoadLoadersAsync();
        }

        private async void CmbChip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _currentPage = 1;
            await LoadLoadersAsync();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) 
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _currentPage = 1;
                await LoadLoadersAsync();
            }
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadLoadersAsync();
        }

        private async void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _currentPage = 1;
            await LoadLoadersAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
        }

        private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadLoadersAsync();
            }
        }

        private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                await LoadLoadersAsync();
            }
        }

        private void LvLoaders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedLoader = LvLoaders.SelectedItem as CloudLoaderInfo;
            BtnDownload.IsEnabled = SelectedLoader != null;

            if (SelectedLoader != null)
            {
                // 更新状态
                var info = $"已选择: {SelectedLoader.FileName}";
                TxtStatus.Text = info;

                // 显示选中信息区域
                SelectedInfo.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;

                // 确定认证策略并显示提示
                DetermineAuthStrategy(SelectedLoader);
            }
            else
            {
                SelectedInfo.Visibility = Visibility.Collapsed;
                AuthHintPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 根据选中的Loader确定认证策略
        /// </summary>
        private void DetermineAuthStrategy(CloudLoaderInfo loader)
        {
            // 确定认证策略 (浅色主题配色)
            if (loader.IsXiaomiAuth)
            {
                RecommendedAuthStrategy = "xiaomi";
                ShouldEnableVip = false;
                
                TxtSelectedAuth.Text = "🍊 小米认证";
                TxtSelectedAuth.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCF, 0x22, 0x2E)); // 红色
                TxtSelectedHint.Text = "将启用小米认证模式";
                
                TxtAuthIcon.Text = "🍊";
                TxtAuthHint.Text = "此Loader需要小米认证，下载后将自动启用小米认证模式";
                TxtAuthHint.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCF, 0x22, 0x2E));
                AuthHintPanel.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xEB, 0xE9)); // 浅红色背景
                AuthHintPanel.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCF, 0x22, 0x2E));
            }
            else if (loader.IsNothingAuth)
            {
                RecommendedAuthStrategy = "nothing";
                ShouldEnableVip = false;
                
                TxtSelectedAuth.Text = "⚫ Nothing认证";
                TxtSelectedAuth.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x24, 0x29, 0x2F)); // 深灰
                TxtSelectedHint.Text = "将启用Nothing认证模式";
                
                TxtAuthIcon.Text = "⚫";
                TxtAuthHint.Text = "此Loader需要Nothing认证，下载后将自动启用Nothing认证模式";
                TxtAuthHint.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x24, 0x29, 0x2F));
                AuthHintPanel.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xEA, 0xEE, 0xF2)); // 浅灰背景
                AuthHintPanel.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6A));
            }
            else if (loader.IsVipAuth)
            {
                RecommendedAuthStrategy = "vip";
                ShouldEnableVip = true;
                
                TxtSelectedAuth.Text = "🔐 VIP认证";
                TxtSelectedAuth.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xBC, 0x4C, 0x00)); // 橙色
                TxtSelectedHint.Text = loader.HasDigest && loader.HasSign 
                    ? "Digest + Sign 已就绪" 
                    : "需要Digest和Sign文件";
                
                TxtAuthIcon.Text = "🔐";
                TxtAuthHint.Text = "此Loader需要VIP验证，下载后将自动启用VIP认证模式";
                TxtAuthHint.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xBC, 0x4C, 0x00));
                AuthHintPanel.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xF1, 0xE5)); // 浅橙色背景
                AuthHintPanel.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xBC, 0x4C, 0x00));
            }
            else
            {
                RecommendedAuthStrategy = "standard";
                ShouldEnableVip = false;
                
                TxtSelectedAuth.Text = "✅ 标准认证";
                TxtSelectedAuth.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1A, 0x7F, 0x37)); // 绿色
                TxtSelectedHint.Text = "无需额外验证";
                
                AuthHintPanel.Visibility = Visibility.Collapsed;
                return;
            }
            
            AuthHintPanel.Visibility = Visibility.Visible;
        }

        private async void LvLoaders_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedLoader != null)
            {
                await DownloadAndCloseAsync();
            }
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            await DownloadAndCloseAsync();
        }

        private async Task DownloadAndCloseAsync()
        {
            if (SelectedLoader == null) return;

            try
            {
                BtnDownload.IsEnabled = false;
                ProgressPanel.Visibility = Visibility.Visible;
                _cts = new CancellationTokenSource();

                var progress = new Progress<int>(p =>
                {
                    DownloadProgress.Value = p;
                    TxtProgress.Text = $"{p}%";
                });

                TxtStatus.Text = $"正在下载 {SelectedLoader.DisplayName}...";

                DownloadedFiles = await _cloudService.DownloadLoaderKitAsync(
                    SelectedLoader, progress, _cts.Token);

                if (DownloadedFiles?.HasLoader == true)
                {
                    TxtStatus.Text = "✅ 下载完成";
                    DialogResult = true;
                    Close();
                }
                else
                {
                    TxtStatus.Text = "❌ 下载失败";
                    BtnDownload.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "下载已取消";
                BtnDownload.IsEnabled = true;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"❌ 下载失败: {ex.Message}";
                BtnDownload.IsEnabled = true;
            }
            finally
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _cloudService.Dispose();
            base.OnClosed(e);
        }
    }
}
