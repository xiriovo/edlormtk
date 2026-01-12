using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// 云端 Loader 服务 - 对接 MultiFlash Cloud API v2.0
    /// </summary>
    public class CloudLoaderService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _downloadClient; // 专用于下载的客户端 (更长超时)
        private readonly string _apiBaseUrl;
        private readonly Action<string>? _log;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // 云端服务器地址 (内部使用)
        private const string _endpointBase = "aHR0cHM6Ly93d3cueGlyaWFjZy50b3AvYXBp";
        public static string DEFAULT_API_URL => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_endpointBase));

        // 本地缓存目录
        public string CacheDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebugTools", "LoaderCache");

        public bool IsEnabled { get; set; } = true;
        public bool IsConnected { get; private set; }

        public CloudLoaderService(string? apiBaseUrl = null, Action<string>? log = null)
        {
            _apiBaseUrl = apiBaseUrl ?? DEFAULT_API_URL;
            _log = log;
            
            // API 客户端 (短超时)
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DebugTools/1.0");

            // 下载客户端 - 禁用自动重定向，手动处理 HTTPS→HTTP→HTTPS 重定向链
            var downloadHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,  // ⚠️ 禁用自动重定向，手动处理混合协议重定向
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _downloadClient = new HttpClient(downloadHandler)
            {
                Timeout = TimeSpan.FromMinutes(10) // 10分钟超时，支持大文件下载
            };
            _downloadClient.DefaultRequestHeaders.Add("User-Agent", "DebugTools/1.0 (Compatible; GitHub)");
            _downloadClient.DefaultRequestHeaders.Add("Accept", "*/*");

            if (!Directory.Exists(CacheDirectory))
                Directory.CreateDirectory(CacheDirectory);
        }

        #region 连接测试

        /// <summary>
        /// 测试连接 - GET /api/health
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(_apiBaseUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    IsConnected = true;
                    _log?.Invoke("[Cloud] ✅ 已连接到云端服务器");
                    return true;
                }
                IsConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Cloud] ❌ 连接失败: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        #endregion

        #region 厂商列表 - GET /api/vendors

        /// <summary>
        /// 获取所有厂商列表
        /// </summary>
        public async Task<List<CloudVendorInfo>> GetVendorsAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/vendors", ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                
                _log?.Invoke($"[Cloud] 厂商API响应: {json.Substring(0, Math.Min(200, json.Length))}...");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<CloudApiResponse<List<CloudVendorInfo>>>(json, _jsonOptions);
                    _log?.Invoke($"[Cloud] 解析到 {result?.Data?.Count ?? 0} 个厂商");
                    return result?.Data ?? new List<CloudVendorInfo>();
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Cloud] 获取厂商列表失败: {ex.Message}");
            }
            return new List<CloudVendorInfo>();
        }

        #endregion

        #region 芯片列表 - GET /api/chipsets

        /// <summary>
        /// 获取所有芯片列表
        /// </summary>
        public async Task<List<CloudChipInfo>> GetChipsAsync(string? vendor = null, CancellationToken ct = default)
        {
            try
            {
                string url = $"{_apiBaseUrl}/chipsets";
                
                var response = await _httpClient.GetAsync(url, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                
                _log?.Invoke($"[Cloud] 芯片API响应: {json.Substring(0, Math.Min(200, json.Length))}...");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<CloudApiResponse<List<CloudChipInfo>>>(json, _jsonOptions);
                    _log?.Invoke($"[Cloud] 解析到 {result?.Data?.Count ?? 0} 个芯片");
                    return result?.Data ?? new List<CloudChipInfo>();
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Cloud] 获取芯片列表失败: {ex.Message}");
            }
            return new List<CloudChipInfo>();
        }

        #endregion

        #region Loader 列表 - GET /api/loaders

        /// <summary>
        /// 搜索 Loader (支持分页和多条件筛选)
        /// GET /api/loaders?vendor=xxx&chip=xxx&search=xxx&page=1&limit=50
        /// </summary>
        public async Task<CloudLoaderListResult> SearchLoadersAsync(
            CloudLoaderQuery query,
            CancellationToken ct = default)
        {
            var result = new CloudLoaderListResult();

            try
            {
                var queryParams = new List<string>();
                
                // 厂商
                if (!string.IsNullOrEmpty(query.Vendor))
                    queryParams.Add($"vendor={Uri.EscapeDataString(query.Vendor)}");
                
                // 芯片
                if (!string.IsNullOrEmpty(query.Chip))
                    queryParams.Add($"chip={Uri.EscapeDataString(query.Chip)}");
                
                // 搜索关键词
                if (!string.IsNullOrEmpty(query.Keyword))
                    queryParams.Add($"search={Uri.EscapeDataString(query.Keyword)}");
                
                // VIP 筛选
                if (query.RequiresVip == true)
                    queryParams.Add("requires_vip=1");
                
                // 分页
                queryParams.Add($"page={query.Page}");
                queryParams.Add($"limit={query.PageSize}");

                string url = $"{_apiBaseUrl}/loaders";
                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                _log?.Invoke($"[Cloud] 搜索 Loader: {url}");

                var response = await _httpClient.GetAsync(url, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                
                _log?.Invoke($"[Cloud] Loader API响应: {json.Substring(0, Math.Min(300, json.Length))}...");

                if (response.IsSuccessStatusCode)
                {
                    var apiResult = JsonSerializer.Deserialize<CloudPaginatedResponse<CloudLoaderInfo>>(json, _jsonOptions);
                    _log?.Invoke($"[Cloud] 解析结果: Success={apiResult?.Success}, Data={apiResult?.Data != null}");
                    if (apiResult?.Success == true && apiResult.Data != null)
                    {
                        result.Loaders = apiResult.Data.Items ?? new List<CloudLoaderInfo>();
                        
                        var pagination = apiResult.Data.Pagination;
                        if (pagination != null)
                        {
                            result.TotalCount = pagination.Total;
                            result.Page = pagination.Page;
                            result.PageSize = pagination.Limit;
                            result.TotalPages = pagination.Pages;
                        }
                        
                        // 补充筛选 (has_digest, has_sign)
                        if (query.HasDigest == true)
                            result.Loaders = result.Loaders.Where(l => l.HasDigest).ToList();
                        if (query.HasSign == true)
                            result.Loaders = result.Loaders.Where(l => l.HasSign).ToList();
                    }
                }
                else
                {
                    result.Error = $"HTTP {(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log?.Invoke($"[Cloud] 搜索失败: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 获取客户端 Loader 列表 (简化版)
        /// GET /api/client/loaders?vendor=xxx&chipset=xxx
        /// </summary>
        public async Task<List<CloudLoaderInfo>> GetLoadersAsync(
            string? vendor = null,
            string? chip = null,
            string? keyword = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var query = new CloudLoaderQuery
            {
                Vendor = vendor,
                Chip = chip,
                Keyword = keyword,
                Page = page,
                PageSize = pageSize
            };

            var result = await SearchLoadersAsync(query, ct);
            return result.Loaders;
        }

        #endregion

        #region Loader 匹配 - GET/POST /api/loaders/match

        /// <summary>
        /// 根据设备信息匹配云端 Loader
        /// </summary>
        public async Task<CloudMatchResult> MatchLoaderAsync(
            string? pkHash = null, 
            string? msmId = null, 
            string? oemId = null,
            string? vendor = null,
            CancellationToken ct = default)
        {
            var result = new CloudMatchResult();

            if (!IsEnabled)
            {
                result.Error = "云端匹配已禁用";
                return result;
            }

            try
            {
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(pkHash))
                    queryParams.Add($"pk_hash={Uri.EscapeDataString(pkHash)}");
                if (!string.IsNullOrEmpty(msmId))
                    queryParams.Add($"msm_id={Uri.EscapeDataString(msmId)}");
                if (!string.IsNullOrEmpty(oemId))
                    queryParams.Add($"oem_id={Uri.EscapeDataString(oemId)}");
                if (!string.IsNullOrEmpty(vendor))
                    queryParams.Add($"vendor={Uri.EscapeDataString(vendor)}");

                string url = $"{_apiBaseUrl}/loaders/match";
                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                _log?.Invoke($"[Cloud] 正在匹配 Loader...");

                var response = await _httpClient.GetAsync(url, ct);
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    result.Error = $"服务器返回错误: {response.StatusCode}";
                    return result;
                }

                var apiResponse = JsonSerializer.Deserialize<CloudApiResponse<CloudMatchData>>(json, _jsonOptions);

                if (apiResponse?.Success == true && apiResponse.Data != null)
                {
                    result.Matched = apiResponse.Data.Matched;
                    result.Loaders = apiResponse.Data.Loaders ?? new List<CloudLoaderInfo>();
                    
                    if (apiResponse.Data.BestMatch != null)
                    {
                        result.MatchTypeLabel = apiResponse.Data.BestMatch.MatchTypeLabel;
                    }
                    
                    _log?.Invoke($"[Cloud] 匹配到 {result.Loaders.Count} 个 Loader");
                }
                else
                {
                    result.Error = apiResponse?.Message ?? "服务器返回格式错误";
                }
            }
            catch (TaskCanceledException)
            {
                result.Error = "请求超时";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        #endregion

        #region 下载 - GET /api/loaders/download/{id}

        /// <summary>
        /// 下载 Loader 文件
        /// </summary>
        public async Task<string?> DownloadLoaderAsync(
            int loaderId, 
            string type = "loader",
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            string cacheFileName = $"{loaderId}_{type}.dat";
            string cachePath = Path.Combine(CacheDirectory, cacheFileName);

            // 检查缓存
            if (File.Exists(cachePath))
            {
                var cacheInfo = new FileInfo(cachePath);
                if (cacheInfo.LastWriteTime > DateTime.Now.AddDays(-7))
                {
                    _log?.Invoke($"[Cloud] 使用缓存: {cacheFileName}");
                    return cachePath;
                }
            }

            // 根据类型选择不同的 API 端点
            string url = type switch
            {
                "digest" => $"{_apiBaseUrl}/loaders/digest/{loaderId}",
                "sign" => $"{_apiBaseUrl}/loaders/sign/{loaderId}",
                _ => $"{_apiBaseUrl}/loaders/download/{loaderId}"
            };
            
            _log?.Invoke($"[Cloud] 正在下载 {type}...");

            try
            {
                // 手动跟随重定向链 (支持 HTTPS→HTTP→HTTPS 混合重定向)
                var response = await FollowRedirectsAsync(url, ct);
                if (response == null)
                {
                    _log?.Invoke($"[Cloud] ❌ 无法获取文件 (重定向失败)");
                    return null;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    // 检查具体错误
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _log?.Invoke($"[Cloud] ❌ 文件不存在 (404)");
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _log?.Invoke($"[Cloud] ❌ 访问被拒绝 (403) - 可能是 GitHub 速率限制");
                    }
                    else
                    {
                        _log?.Invoke($"[Cloud] ❌ 下载失败: HTTP {(int)response.StatusCode}");
                    }
                    response.Dispose();
                    return null;
                }

                var result = await SaveResponseToFileAsync(response, cachePath, cacheFileName, type, progress, ct);
                response.Dispose();
                return result;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _log?.Invoke("[Cloud] ❌ 下载超时 - 网络可能不稳定或文件过大");
                return null;
            }
            catch (TaskCanceledException)
            {
                _log?.Invoke("[Cloud] 下载被取消");
                return null;
            }
            catch (HttpRequestException ex)
            {
                _log?.Invoke($"[Cloud] ❌ 网络错误: {ex.Message}");
                if (ex.InnerException != null)
                    _log?.Invoke($"   └─ 详情: {ex.InnerException.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Cloud] ❌ 下载异常: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 手动跟随重定向链 - 支持 HTTPS→HTTP→HTTPS 混合重定向
        /// .NET HttpClient 默认不跟随 HTTPS→HTTP 的重定向 (安全策略)
        /// </summary>
        private async Task<HttpResponseMessage?> FollowRedirectsAsync(string url, CancellationToken ct)
        {
            const int maxRedirects = 10;
            int redirectCount = 0;
            
            while (redirectCount < maxRedirects)
            {
                var response = await _downloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                int statusCode = (int)response.StatusCode;
                
                // 检查是否是重定向 (301, 302, 303, 307, 308)
                if (statusCode >= 300 && statusCode < 400 && response.Headers.Location != null)
                {
                    var location = response.Headers.Location;
                    
                    // 处理相对 URL
                    if (!location.IsAbsoluteUri)
                    {
                        location = new Uri(new Uri(url), location);
                    }
                    
                    _log?.Invoke($"[Cloud] 重定向 {redirectCount + 1}: {statusCode} → {location.Host}");
                    url = location.ToString();
                    response.Dispose(); // 释放重定向响应
                    redirectCount++;
                    continue;
                }
                
                // 非重定向响应 - 返回
                return response;
            }
            
            _log?.Invoke($"[Cloud] ❌ 重定向次数过多 ({maxRedirects})");
            return null;
        }

        private async Task<string?> SaveResponseToFileAsync(
            HttpResponseMessage response,
            string cachePath,
            string cacheFileName,
            string type,
            IProgress<int>? progress,
            CancellationToken ct)
        {
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var receivedBytes = 0L;

            // 获取原始文件名
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                string originalFileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
                string ext = Path.GetExtension(originalFileName);
                if (!string.IsNullOrEmpty(ext))
                {
                    cacheFileName = $"{Path.GetFileNameWithoutExtension(cacheFileName)}{ext}";
                    cachePath = Path.Combine(CacheDirectory, cacheFileName);
                }
            }

            using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            
            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                receivedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    int percent = (int)(receivedBytes * 100 / totalBytes);
                    progress?.Report(percent);
                }
            }

            _log?.Invoke($"[Cloud] ✅ 下载完成: {Path.GetFileName(cachePath)} ({receivedBytes / 1024} KB)");
            return cachePath;
        }

        /// <summary>
        /// 下载完整的 Loader 套件 (loader + digest + sign)
        /// </summary>
        public async Task<CloudLoaderFiles?> DownloadLoaderKitAsync(
            CloudLoaderInfo loader,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            var files = new CloudLoaderFiles
            {
                Vendor = loader.Vendor,
                Chip = loader.ChipName ?? loader.Chip,
                StorageType = loader.StorageType ?? "ufs" // 默认 UFS
            };

            // 确定认证策略
            if (loader.IsXiaomiAuth)
            {
                files.AuthStrategy = "xiaomi";
            }
            else if (loader.IsNothingAuth)
            {
                files.AuthStrategy = "nothing";
            }
            else if (loader.IsVipAuth)
            {
                files.AuthStrategy = "vip";
            }
            else
            {
                files.AuthStrategy = "standard";
            }

            _log?.Invoke($"[Cloud] 认证策略: {files.AuthStrategy} (厂商: {files.Vendor}, 存储: {files.StorageType.ToUpper()})");

            // 下载 Loader
            files.LoaderPath = await DownloadLoaderAsync(loader.Id, "loader", progress, ct);
            if (string.IsNullOrEmpty(files.LoaderPath))
                return null;

            // 下载 Digest (如果有)
            if (loader.HasDigest)
            {
                _log?.Invoke("[Cloud] 正在下载 Digest...");
                files.DigestPath = await DownloadLoaderAsync(loader.Id, "digest", null, ct);
            }

            // 下载 Sign (如果有)
            if (loader.HasSign)
            {
                _log?.Invoke("[Cloud] 正在下载 Sign...");
                files.SignPath = await DownloadLoaderAsync(loader.Id, "sign", null, ct);
            }

            _log?.Invoke($"[Cloud] 下载完成 - Loader: ✓, Digest: {(files.HasDigest ? "✓" : "✗")}, Sign: {(files.HasSign ? "✓" : "✗")}");

            return files;
        }

        #endregion

        #region 缓存管理

        public void CleanupCache(int maxAgeDays = 30)
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;

                var cutoff = DateTime.Now.AddDays(-maxAgeDays);
                int cleaned = 0;

                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }

                if (cleaned > 0)
                    _log?.Invoke($"[Cloud] 清理缓存: {cleaned} 个文件");
            }
            catch { }
        }

        public long GetCacheSize()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return 0;
                return Directory.GetFiles(CacheDirectory).Sum(f => new FileInfo(f).Length);
            }
            catch { return 0; }
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
            _downloadClient?.Dispose();
        }
    }

    #region 数据模型

    /// <summary>
    /// Loader 查询参数
    /// </summary>
    public class CloudLoaderQuery
    {
        public string? Keyword { get; set; }
        public string? Vendor { get; set; }
        public string? Chip { get; set; }
        public bool? RequiresVip { get; set; }
        public bool? HasDigest { get; set; }
        public bool? HasSign { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    /// <summary>
    /// Loader 列表结果
    /// </summary>
    public class CloudLoaderListResult
    {
        public List<CloudLoaderInfo> Loaders { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public string? Error { get; set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// 下载的 Loader 文件路径
    /// </summary>
    public class CloudLoaderFiles
    {
        public string? LoaderPath { get; set; }
        public string? DigestPath { get; set; }
        public string? SignPath { get; set; }
        
        /// <summary>
        /// 推荐的认证策略: standard, vip, xiaomi, nothing
        /// </summary>
        public string AuthStrategy { get; set; } = "standard";
        
        /// <summary>
        /// 存储类型: ufs, emmc
        /// </summary>
        public string? StorageType { get; set; }
        
        /// <summary>
        /// 厂商名称
        /// </summary>
        public string? Vendor { get; set; }
        
        /// <summary>
        /// 芯片名称
        /// </summary>
        public string? Chip { get; set; }
        
        public bool HasLoader => !string.IsNullOrEmpty(LoaderPath);
        public bool HasDigest => !string.IsNullOrEmpty(DigestPath);
        public bool HasSign => !string.IsNullOrEmpty(SignPath);
        
        /// <summary>
        /// 是否为VIP认证
        /// </summary>
        public bool IsVipAuth => AuthStrategy == "vip" || (HasDigest && HasSign);
        
        /// <summary>
        /// 是否为小米认证
        /// </summary>
        public bool IsXiaomiAuth => AuthStrategy == "xiaomi";
        
        /// <summary>
        /// 是否为Nothing认证
        /// </summary>
        public bool IsNothingAuth => AuthStrategy == "nothing";
    }

    /// <summary>
    /// 云端 API 通用响应
    /// </summary>
    public class CloudApiResponse<T>
    {
        public bool Success { get; set; }
        public int Code { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    /// <summary>
    /// 云端 API 分页响应
    /// </summary>
    public class CloudPaginatedResponse<T>
    {
        public bool Success { get; set; }
        public int Code { get; set; }
        public string? Message { get; set; }
        public CloudPaginatedData<T>? Data { get; set; }
    }

    /// <summary>
    /// 分页数据
    /// </summary>
    public class CloudPaginatedData<T>
    {
        public List<T>? Items { get; set; }
        public CloudPaginationInfo? Pagination { get; set; }
    }

    /// <summary>
    /// 分页信息
    /// </summary>
    public class CloudPaginationInfo
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int Limit { get; set; }
        public int Pages { get; set; }
        [JsonPropertyName("has_next")]
        public bool HasNext { get; set; }
        [JsonPropertyName("has_prev")]
        public bool HasPrev { get; set; }
    }

    /// <summary>
    /// 厂商信息
    /// </summary>
    public class CloudVendorInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("oem_id")]
        public string? OemId { get; set; }
        [JsonPropertyName("auth_type")]
        public string? AuthType { get; set; }
        [JsonPropertyName("is_active")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public int Status { get; set; }
        [JsonPropertyName("loader_count")]
        public int LoaderCount { get; set; }
        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }
        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
        
        // 计算属性
        public string Display => DisplayName ?? Name ?? "Unknown";
    }
    
    /// <summary>
    /// 布尔/整数转换器 (API可能返回 true/false 或 1/0)
    /// </summary>
    public class BoolOrIntConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number => reader.GetInt32() != 0,
                JsonTokenType.String => reader.GetString()?.ToLower() is "true" or "1",
                _ => false
            };
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }

    /// <summary>
    /// 芯片信息
    /// </summary>
    public class CloudChipInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("code_name")]
        public string? CodeName { get; set; }
        [JsonPropertyName("chip_name")]
        public string? ChipName { get; set; }
        [JsonPropertyName("msm_id")]
        public string? MsmId { get; set; }
        [JsonPropertyName("hwid")]
        public string? HwId { get; set; }
        [JsonPropertyName("marketing_name")]
        public string? MarketingName { get; set; }
        public string? Series { get; set; }
        [JsonPropertyName("storage_type")]
        public string? StorageType { get; set; }
        [JsonPropertyName("sahara_version")]
        public int SaharaVersion { get; set; }
        [JsonPropertyName("loader_count")]
        public int LoaderCount { get; set; }
        public string? Manufacturer { get; set; }
        public string? Description { get; set; }
        public int Status { get; set; }
        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
        
        // 计算属性
        public string Display => !string.IsNullOrEmpty(MarketingName) 
            ? $"{ChipName ?? Name} ({MarketingName})" 
            : ChipName ?? Name ?? "Unknown";
    }

    /// <summary>
    /// 匹配结果数据
    /// </summary>
    public class CloudMatchData
    {
        public bool Matched { get; set; }
        public List<CloudLoaderInfo>? Loaders { get; set; }
        [JsonPropertyName("best_match")]
        public CloudBestMatchInfo? BestMatch { get; set; }
        public int Count { get; set; }
    }
    
    /// <summary>
    /// 最佳匹配信息
    /// </summary>
    public class CloudBestMatchInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Vendor { get; set; }
        public string? Chip { get; set; }
        [JsonPropertyName("match_type")]
        public string? MatchType { get; set; }
        [JsonPropertyName("match_type_label")]
        public string? MatchTypeLabel { get; set; }
        [JsonPropertyName("match_score")]
        public int MatchScore { get; set; }
        [JsonPropertyName("requires_vip")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool RequiresVip { get; set; }
        [JsonPropertyName("has_digest")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool HasDigest { get; set; }
        [JsonPropertyName("has_sign")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool HasSign { get; set; }
    }

    /// <summary>
    /// 云端匹配结果
    /// </summary>
    public class CloudMatchResult
    {
        public bool Matched { get; set; }
        public List<CloudLoaderInfo> Loaders { get; set; } = new();
        public string? Error { get; set; }
        public string? MatchTypeLabel { get; set; }

        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool HasLoaders => Matched && Loaders.Count > 0;
        public CloudLoaderInfo? BestMatch => Loaders.Count > 0 ? Loaders[0] : null;
    }

    /// <summary>
    /// 云端 Loader 信息
    /// </summary>
    public class CloudLoaderInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Vendor { get; set; }
        [JsonPropertyName("chip_name")]
        public string? ChipName { get; set; }
        public string? Chip { get; set; }
        [JsonPropertyName("filename")]
        public string? FileName { get; set; }
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }
        [JsonPropertyName("file_size")]
        public long FileSize { get; set; }
        [JsonPropertyName("file_type")]
        public string? FileType { get; set; }
        [JsonPropertyName("requires_vip")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool RequiresVip { get; set; }
        [JsonPropertyName("is_encrypted")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool IsEncrypted { get; set; }
        [JsonPropertyName("is_active")]
        [JsonConverter(typeof(BoolOrIntConverter))]
        public bool IsActive { get; set; }
        [JsonPropertyName("digest_path")]
        public string? DigestPath { get; set; }
        [JsonPropertyName("sign_path")]
        public string? SignPath { get; set; }
        [JsonPropertyName("auth_type")]
        public string? AuthType { get; set; }
        [JsonPropertyName("oem_id")]
        public string? OemId { get; set; }
        [JsonPropertyName("pk_hash")]
        public string? PkHash { get; set; }
        [JsonPropertyName("match_type")]
        public string? MatchType { get; set; }
        [JsonPropertyName("match_score")]
        public int MatchScore { get; set; }
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }
        [JsonPropertyName("download_count")]
        public int DownloadCount { get; set; }
        public string? Description { get; set; }
        [JsonPropertyName("storage_type")]
        public string? StorageType { get; set; }
        [JsonPropertyName("vendor_name")]
        public string? VendorName { get; set; }
        [JsonPropertyName("vendor_display")]
        public string? VendorDisplay { get; set; }

        // 计算属性 - 根据 path 是否存在判断
        public bool HasDigest => !string.IsNullOrEmpty(DigestPath);
        public bool HasSign => !string.IsNullOrEmpty(SignPath);
        
        // 显示属性
        public string DisplayName => $"{Vendor} {ChipName ?? Chip} {Version}".Trim();
        public string FileSizeText => FileSize > 1024 * 1024 
            ? $"{FileSize / 1024.0 / 1024.0:F2} MB" 
            : $"{FileSize / 1024.0:F1} KB";
        public string VipText => RequiresVip ? "VIP" : "-";
        public string DigestText => HasDigest ? "✓" : "-";
        public string SignText => HasSign ? "✓" : "-";
        
        /// <summary>
        /// 认证类型显示文本
        /// </summary>
        public string AuthTypeText
        {
            get
            {
                var authType = AuthType?.ToLowerInvariant() ?? "";
                return authType switch
                {
                    "vip" => "🔐VIP",
                    "xiaomi" => "🍊Mi",
                    "nothing" => "⚫NT",
                    "standard" => "标准",
                    _ => HasDigest && HasSign ? "🔐VIP" : "标准"
                };
            }
        }

        /// <summary>
        /// 判断是否为VIP认证类型（需要digest+sign）
        /// </summary>
        public bool IsVipAuth => AuthType?.Equals("vip", StringComparison.OrdinalIgnoreCase) == true 
                                 || (HasDigest && HasSign);

        /// <summary>
        /// 判断是否为小米认证类型
        /// </summary>
        public bool IsXiaomiAuth => AuthType?.Equals("xiaomi", StringComparison.OrdinalIgnoreCase) == true
                                    || Vendor?.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// 判断是否为Nothing认证类型
        /// </summary>
        public bool IsNothingAuth => AuthType?.Equals("nothing", StringComparison.OrdinalIgnoreCase) == true
                                     || Vendor?.Equals("Nothing", StringComparison.OrdinalIgnoreCase) == true;
    }

    #endregion
}
