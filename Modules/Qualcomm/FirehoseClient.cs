// ============================================================================
// MultiFlash TOOL - Qualcomm Firehose Client
// 高通 Firehose 客户端 | Qualcomm Firehoseクライアント | 퀄컴 Firehose 클라이언트
// ============================================================================
// [EN] Firehose is the XML-based flash protocol for Qualcomm EDL mode
//      Supports partition read/write/erase, GPT operations, etc.
// [中文] Firehose 是高通 EDL 模式的 XML 协议
//       支持分区读写擦除、GPT 操作等
// [日本語] FirehoseはQualcomm EDLモード用のXMLベースのフラッシュプロトコル
//         パーティションの読み書き消去、GPT操作などをサポート
// [한국어] Firehose는 퀄컴 EDL 모드용 XML 기반 플래시 프로토콜
//         파티션 읽기/쓰기/삭제, GPT 작업 등 지원
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm
{
    #region 错误处理 / Error Handling / エラー処理 / 오류 처리

    /// <summary>
    /// Firehose Error Helper / Firehose 错误码助手 / Firehoseエラーヘルパー / Firehose 오류 도우미
    /// </summary>
    public static class FirehoseErrorHelper
    {
        public static (string message, string suggestion, bool isFatal, bool canRetry) ParseNakError(string errorText)
        {
            if (string.IsNullOrEmpty(errorText))
                return ("未知错误", "请重试操作", false, true);

            string lower = errorText.ToLowerInvariant();

            // 认证/签名错误 (致命)
            if (lower.Contains("authentication") || lower.Contains("auth failed"))
                return ("认证失败", "设备需要特殊认证", true, false);

            if (lower.Contains("signature") || lower.Contains("sign"))
                return ("签名验证失败", "镜像签名不正确", true, false);

            if (lower.Contains("hash") && (lower.Contains("mismatch") || lower.Contains("fail")))
                return ("Hash 校验失败", "数据完整性验证失败", true, false);

            // 分区错误
            if (lower.Contains("partition not found"))
                return ("分区未找到", "设备上不存在此分区", true, false);

            if (lower.Contains("invalid lun"))
                return ("无效的 LUN", "指定的 LUN 不存在", true, false);

            // 参数错误
            if (lower.Contains("invalid value"))
                return ("参数值无效", "命令参数不正确", true, false);

            if (lower.Contains("invalid sector"))
                return ("扇区地址无效", "起始扇区或扇区数超出范围", true, false);

            // 存储错误
            if (lower.Contains("write protect"))
                return ("写保护", "存储设备处于写保护状态", true, false);

            if (lower.Contains("erase fail"))
                return ("擦除失败", "存储擦除操作失败", false, true);

            if (lower.Contains("program fail") || lower.Contains("write fail"))
                return ("写入失败", "数据写入存储失败", false, true);

            // 通信错误 (可重试)
            if (lower.Contains("timeout"))
                return ("超时", "操作超时，建议重试", false, true);

            if (lower.Contains("busy"))
                return ("设备忙", "设备正在处理其他操作", false, true);

            if (lower.Contains("crc") || lower.Contains("checksum"))
                return ("校验和错误", "数据传输错误，请检查USB", false, true);

            if (lower.Contains("not support") || lower.Contains("unsupported"))
                return ("不支持的操作", "设备不支持此功能", true, false);

            return ($"设备错误: {errorText}", "请查看完整错误信息", false, true);
        }

        public static string GetShortDescription(string errorText)
        {
            if (string.IsNullOrEmpty(errorText)) return "UNKNOWN";

            string lower = errorText.ToLowerInvariant();
            if (lower.Contains("auth")) return "AUTH_FAIL";
            if (lower.Contains("signature")) return "SIGN_FAIL";
            if (lower.Contains("hash")) return "HASH_FAIL";
            if (lower.Contains("partition not found")) return "NO_PARTITION";
            if (lower.Contains("timeout")) return "TIMEOUT";
            if (lower.Contains("write protect")) return "WRITE_PROTECT";

            return errorText.Length > 20 ? errorText.Substring(0, 20).ToUpperInvariant() : errorText.ToUpperInvariant();
        }
    }

    #endregion

    /// <summary>
    /// Firehose 协议客户端 - 完整版
    /// </summary>
    public class FirehoseClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string>? _log;
        private readonly Action<long, long>? _progress;
        private bool _disposed;
        private readonly StringBuilder _rxBuffer = new StringBuilder();

        // 配置
        private int _sectorSize = 4096;
        private int _maxPayloadSize = 4194304; // 4MB (优化: 从1MB提升)
#pragma warning disable CS0414 // 预留给未来使用
        private bool _flowControlEnabled = true;
#pragma warning restore CS0414
        private int _ackEveryNumPackets = 0;

        private const int MAX_ACK_RETRIES = 3;
        private const int ACK_TIMEOUT_MS = 5000;
        private const int WRITE_CHUNK_SIZE = 4194304;  // 4MB (优化: 从1MB提升)
        private const int MIN_PAYLOAD_SIZE = 64 * 1024;
        private const int MAX_PAYLOAD_SIZE = 16 * 1024 * 1024;
        
        // I/O 优化常量
        private const int FILE_BUFFER_SIZE = 256 * 1024;  // 256KB 文件缓冲区(优化: 从64KB提升)
        private const int OPTIMAL_PAYLOAD_REQUEST = 4 * 1024 * 1024;  // 请求4MB payload

        // 公开属性
        public string StorageType { get; private set; } = "ufs";
        public int SectorSize => _sectorSize;
        public int MaxPayloadSize => _maxPayloadSize;
        public List<string> SupportedFunctions { get; private set; } = new List<string>();

        // 芯片信息
        public string ChipSerial { get; set; } = "";
        public string ChipHwId { get; set; } = "";
        public string ChipPkHash { get; set; } = "";
        public uint SaharaVersion { get; set; } = 0;

        // OnePlus 认证参数
        public string OnePlusProgramToken { get; set; } = "";
        public string OnePlusProgramPk { get; set; } = "";
        public string OnePlusProjId { get; set; } = "";

        // 分区缓存
        private List<PartitionInfo>? _cachedPartitions = null;

        // 速度统计
        private Stopwatch? _transferStopwatch;
        private long _transferTotalBytes;

        public bool IsConnected => _port.IsOpen;

        #region 动态伪装策略 (VIP 模式)

        /// <summary>
        /// VIP 伪装策略
        /// </summary>
        public readonly struct VipSpoofStrategy
        {
            public string Filename { get; init; }
            public string Label { get; init; }
            public int Priority { get; init; }  // 优先级，数字越小优先级越高

            public VipSpoofStrategy(string filename, string label, int priority = 0)
            {
                Filename = filename;
                Label = label;
                Priority = priority;
            }

            public override string ToString() => $"{Label}/{Filename}";
        }

        /// <summary>
        /// 根据读取场景动态生成伪装策略列表
        /// 参考 edlclient 的自动伪装逻辑
        /// </summary>
        /// <param name="lun">LUN 编号</param>
        /// <param name="startSector">起始扇区</param>
        /// <param name="partitionName">分区名称 (可选)</param>
        /// <param name="isGptRead">是否为 GPT 读取</param>
        /// <returns>伪装策略列表 (按优先级排序)</returns>
        public static List<VipSpoofStrategy> GetDynamicSpoofStrategies(
            int lun, 
            long startSector, 
            string? partitionName = null,
            bool isGptRead = false)
        {
            var strategies = new List<VipSpoofStrategy>();

            // 1. GPT 区域特殊处理 (sector 0-33 通常是 GPT)
            if (isGptRead || startSector <= 33)
            {
                // 主 GPT 伪装 (最高优先级)
                strategies.Add(new VipSpoofStrategy($"gpt_main{lun}.bin", "PrimaryGPT", 0));
                strategies.Add(new VipSpoofStrategy($"gpt_backup{lun}.bin", "BackupGPT", 1));
            }

            // 2. 如果提供了分区名称，使用分区名伪装
            if (!string.IsNullOrEmpty(partitionName))
            {
                string safeName = SanitizePartitionName(partitionName);
                strategies.Add(new VipSpoofStrategy($"{safeName}.bin", safeName, 2));
                strategies.Add(new VipSpoofStrategy($"gpt_main0.bin", safeName, 3));
            }

            // 3. 通用 SSD 伪装 (中等优先级)
            strategies.Add(new VipSpoofStrategy("ssd", "ssd", 5));

            // 4. GPT 伪装作为回退
            strategies.Add(new VipSpoofStrategy("gpt_main0.bin", "gpt_main0.bin", 6));
            strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", "BackupGPT", 7));

            // 5. 缓冲区伪装 (最低优先级)
            strategies.Add(new VipSpoofStrategy("buffer.bin", "buffer", 8));

            // 6. 无伪装 (最后回退)
            strategies.Add(new VipSpoofStrategy("", "", 99));

            // 按优先级排序并去重
            return strategies
                .GroupBy(s => s.ToString())
                .Select(g => g.First())
                .OrderBy(s => s.Priority)
                .ToList();
        }

        /// <summary>
        /// 清理分区名称，使其适合用作文件名
        /// </summary>
        private static string SanitizePartitionName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "rawdata";
            
            // 移除非法字符
            var invalid = Path.GetInvalidFileNameChars();
            var safeName = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            
            // 转小写并截断
            safeName = safeName.ToLowerInvariant();
            if (safeName.Length > 32) safeName = safeName.Substring(0, 32);
            
            return string.IsNullOrEmpty(safeName) ? "rawdata" : safeName;
        }

        #endregion

        public FirehoseClient(SerialPortManager port, Action<string>? log = null, Action<long, long>? progress = null)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _log = log;
            _progress = progress;
        }

        #region 异步串口操作 (保持 UI 流畅)

        /// <summary>
        /// 异步写入数据 (在后台线程执行，不阻塞 UI)
        /// </summary>
        private Task WriteAsync(byte[] data, CancellationToken ct = default)
        {
            return Task.Run(() => _port.Write(data), ct);
        }

        /// <summary>
        /// 异步写入数据 (指定偏移和长度)
        /// </summary>
        private Task WriteAsync(byte[] data, int offset, int count, CancellationToken ct = default)
        {
            return Task.Run(() => _port.Write(data, offset, count), ct);
        }

        /// <summary>
        /// 异步写入 XML 命令
        /// </summary>
        private Task WriteXmlAsync(string xml, CancellationToken ct = default)
        {
            return WriteAsync(Encoding.UTF8.GetBytes(xml), ct);
        }

        /// <summary>
        /// 异步读取可用数据
        /// </summary>
        private Task<(byte[] data, int count)> ReadAvailableAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_port.BytesToRead <= 0)
                    return (Array.Empty<byte>(), 0);
                    
                var buffer = new byte[_port.BytesToRead];
                int read = _port.Read(buffer, 0, buffer.Length);
                return (buffer, read);
            }, ct);
        }

        /// <summary>
        /// 异步读取指定长度数据
        /// </summary>
        private Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
        {
            return Task.Run(() => _port.Read(buffer, offset, count), ct);
        }

        #endregion

        #region 基础配置

        /// <summary>
        /// 配置 Firehose (异步，保持 UI 流畅)
        /// </summary>
        public async Task<bool> ConfigureAsync(string storageType = "ufs", int? preferredPayloadSize = null, int ackEveryNumPackets = 0, CancellationToken ct = default)
        {
            StorageType = storageType.ToLower();
            _sectorSize = (StorageType == "emmc") ? 512 : 4096;
            _ackEveryNumPackets = ackEveryNumPackets;

            // 请求最大 payload 大小以提升传输速度
            int requestedPayload = preferredPayloadSize ?? OPTIMAL_PAYLOAD_REQUEST;

            string ackAttr = (ackEveryNumPackets > 0) ? $"AckRawDataEveryNumPackets=\"{ackEveryNumPackets}\" " : "";
            string xml = $"<?xml version=\"1.0\" ?><data><configure MemoryName=\"{storageType}\" Verbose=\"0\" AlwaysValidate=\"0\" MaxPayloadSizeToTargetInBytes=\"{requestedPayload}\" ZlpAwareHost=\"0\" SkipStorageInit=\"0\" CheckDevinfo=\"0\" EnableFlash=\"1\" {ackAttr}/></data>";

            _log?.Invoke("[Firehose] 配置设备...");
            PurgeBuffer();
            await WriteXmlAsync(xml, ct);

            for (int i = 0; i < 50; i++)
            {
                ct.ThrowIfCancellationRequested();
                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    string val = resp.Attribute("value")?.Value ?? "";
                    bool isAck = val.Equals("ACK", StringComparison.OrdinalIgnoreCase);

                    if (isAck || val.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                    {
                        string? ss = resp.Attribute("SectorSizeInBytes")?.Value;
                        if (int.TryParse(ss, out int size)) _sectorSize = size;

                        string? mp = resp.Attribute("MaxPayloadSizeToTargetInBytes")?.Value;
                        if (int.TryParse(mp, out int maxPayload) && maxPayload > 0)
                            _maxPayloadSize = Math.Max(MIN_PAYLOAD_SIZE, Math.Min(maxPayload, MAX_PAYLOAD_SIZE));

                        _log?.Invoke($"[Firehose] ✓ 配置成功 - SectorSize:{_sectorSize}, MaxPayload:{_maxPayloadSize / 1024}KB");
                        return true;
                    }
                }
                await Task.Delay(50, ct);
            }
            return false;
        }

        /// <summary>
        /// 设置存储扇区大小
        /// </summary>
        public void SetSectorSize(int size) => _sectorSize = size;

        /// <summary>
        /// 设置分包确认间隔
        /// </summary>
        public void SetAckInterval(int numPackets)
        {
            _ackEveryNumPackets = numPackets;
            _log?.Invoke($"[FlowControl] AckRawDataEveryNumPackets = {numPackets}");
        }

        #endregion

        #region VIP 认证

        /// <summary>
        /// VIP 认证 (OPPO/OnePlus/Realme)
        /// 完整 6 步流程 (参考 qdl-gpt 和 edl_vip_auth.py):
        /// 1. Digest → 2. TransferCfg → 3. Verify(EnableVip=1) → 4. Signature → 5. SHA256Init → 6. Configure
        /// </summary>
        public async Task<bool> PerformVipAuthAsync(string digestPath, string signaturePath, CancellationToken ct = default)
        {
            if (!File.Exists(digestPath) || !File.Exists(signaturePath))
            {
                _log?.Invoke("[VIP] ⚠ 缺少验证文件");
                return false;
            }

            _log?.Invoke("[VIP] 开始安全验证 (6步流程)...");
            
            try
            {
                // 清空缓冲区
                PurgeBuffer();

                // ========== Step 1: 发送 Digest (二进制数据) ==========
                var digestData = await File.ReadAllBytesAsync(digestPath, ct);
                _log?.Invoke($"[VIP] Step 1/6: 发送 Digest ({digestData.Length} 字节)...");
                await WriteAsync(digestData, ct);
                await Task.Delay(500, ct);
                string resp1 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                if (resp1.Contains("NAK") || resp1.Contains("ERROR"))
                {
                    _log?.Invoke("[VIP] ⚠ Digest 被拒绝，尝试继续...");
                }

                // ========== Step 2: 发送 TransferCfg (关键步骤！) ==========
                _log?.Invoke("[VIP] Step 2/6: 发送 TransferCfg...");
                string transferCfgXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><transfercfg reboot_type=\"off\" timeout_in_sec=\"90\" /></data>";
                await WriteXmlAsync(transferCfgXml, ct);
                await Task.Delay(300, ct);
                string resp2 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp2.Contains("NAK") || resp2.Contains("ERROR"))
                {
                    _log?.Invoke("[VIP] ⚠ TransferCfg 失败，尝试继续...");
                }

                // ========== Step 3: 发送 Verify (启用 VIP 模式) ==========
                _log?.Invoke("[VIP] Step 3/6: 发送 Verify (EnableVip=1)...");
                string verifyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><verify value=\"ping\" EnableVip=\"1\"/></data>";
                await WriteXmlAsync(verifyXml, ct);
                await Task.Delay(300, ct);
                string resp3 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp3.Contains("NAK") || resp3.Contains("ERROR"))
                {
                    _log?.Invoke("[VIP] ⚠ Verify 失败，尝试继续...");
                }

                // ========== Step 4: 发送 Signature (二进制数据) ==========
                var sigData = await File.ReadAllBytesAsync(signaturePath, ct);
                _log?.Invoke($"[VIP] Step 4/6: 发送 Signature ({sigData.Length} 字节)...");
                await WriteAsync(sigData, ct);
                await Task.Delay(500, ct);
                string resp4 = await ReadAndLogDeviceResponseAsync(ct, 3000);
                if (resp4.Contains("NAK") || resp4.Contains("ERROR"))
                {
                    _log?.Invoke("[VIP] ⚠ Signature 被拒绝");
                    // 签名失败是严重错误
                }

                // ========== Step 5: 发送 SHA256Init ==========
                _log?.Invoke("[VIP] Step 5/6: 发送 SHA256Init...");
                string sha256Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    "<data><sha256init Verbose=\"1\"/></data>";
                await WriteXmlAsync(sha256Xml, ct);
                await Task.Delay(300, ct);
                string resp5 = await ReadAndLogDeviceResponseAsync(ct, 2000);
                if (resp5.Contains("NAK") || resp5.Contains("ERROR"))
                {
                    _log?.Invoke("[VIP] ⚠ SHA256Init 失败，尝试继续...");
                }

                // Step 6: Configure 将在外部调用
                _log?.Invoke("[VIP] ✓ VIP 验证流程完成 (5/6 步)，等待 Configure...");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke("[VIP] 验证被取消");
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[VIP] 验证异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 读取并记录设备响应 (异步非阻塞，保持 UI 流畅)
        /// </summary>
        /// <returns>响应内容字符串</returns>
        private async Task<string> ReadAndLogDeviceResponseAsync(CancellationToken ct, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            var sb = new StringBuilder();
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                
                // 异步读取数据
                var (buffer, read) = await ReadAvailableAsync(ct);
                
                if (read > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    
                    // 检查是否收到完整响应
                    var content = sb.ToString();
                    
                    // 提取并显示设备日志
                    var logMatches = System.Text.RegularExpressions.Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                    foreach (System.Text.RegularExpressions.Match m in logMatches)
                    {
                        if (m.Groups.Count > 1)
                            _log?.Invoke($"[Device] {m.Groups[1].Value}");
                    }
                    
                    // 检查响应
                    if (content.Contains("<response") || content.Contains("</data>"))
                    {
                        // 检查是否成功
                        if (content.Contains("value=\"ACK\"") || content.Contains("verify passed"))
                        {
                            return content; // 成功
                        }
                        if (content.Contains("NAK") || content.Contains("ERROR"))
                        {
                            return content; // 失败但返回响应
                        }
                    }
                }
                
                await Task.Delay(50, ct);
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// VIP 认证 (同步版本)
        /// </summary>
        public bool PerformVipAuth(string digestPath, string signaturePath)
        {
            return PerformVipAuthAsync(digestPath, signaturePath).GetAwaiter().GetResult();
        }

        #endregion

        #region 签名认证

        /// <summary>
        /// Ping 设备 (保持连接)
        /// </summary>
        public void Ping()
        {
            try
            {
                string xml = "<?xml version=\"1.0\" ?><data><nop /></data>";
                _port.Write(Encoding.UTF8.GetBytes(xml));
            }
            catch { }
        }

        /// <summary>
        /// 发送签名数据 (小米认证)
        /// </summary>
        public bool SendSignature(byte[] signature)
        {
            try
            {
                string base64Sig = Convert.ToBase64String(signature);
                string xml = $"<?xml version=\"1.0\" ?><data><sig value=\"{base64Sig}\" /></data>";
                _port.Write(Encoding.UTF8.GetBytes(xml));

                Thread.Sleep(500);
                string response = ReadRawResponse(3000);

                // 检查是否真正验证成功
                // 成功标志: verify="0" 或 verify_res="0"
                // 失败标志: NAK, verify="1", 或包含 ERROR
                if (response.Contains("NAK") || response.Contains("ERROR") || 
                    response.Contains("verify=\"1\"") || response.Contains("verify_res=\"1\""))
                {
                    return false;
                }
                
                return response.Contains("verify=\"0\"") || response.Contains("verify_res=\"0\"") ||
                       (response.Contains("ACK") && !response.Contains("Only nop and sig"));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sig] 签名发送失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送原始 XML 命令 (异步)
        /// </summary>
        public async Task<string?> SendRawXmlAsync(string xml, CancellationToken ct = default)
        {
            try
            {
                PurgeBuffer();
                
                if (!xml.StartsWith("<?xml"))
                    xml = "<?xml version=\"1.0\" ?><data><" + xml + " /></data>";
                
                _port.Write(Encoding.UTF8.GetBytes(xml));
                await Task.Delay(500, ct);
                
                return ReadRawResponse(5000);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[XML] 命令发送失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 发送原始字节数据并获取响应 (用于小米认证等)
        /// 参考: edlclient firehose.py send_signed_digest
        /// </summary>
        public async Task<string?> SendRawBytesAndGetResponseAsync(byte[] data, CancellationToken ct = default)
        {
            try
            {
                PurgeBuffer();
                _port.Write(data);
                
                // 等待响应
                var sb = new StringBuilder();
                int timeout = 3000;
                int elapsed = 0;
                int interval = 50;
                
                while (elapsed < timeout)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    if (_port.BytesToRead > 0)
                    {
                        byte[] buffer = new byte[_port.BytesToRead];
                        int read = _port.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                            
                            // 检查是否收到完整响应
                            string response = sb.ToString();
                            if (response.Contains("<response") || response.Contains("authenticated"))
                            {
                                return response;
                            }
                        }
                    }
                    
                    await Task.Delay(interval, ct);
                    elapsed += interval;
                }
                
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[RawBytes] 发送失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 发送 XML 命令并提取属性值 (异步)
        /// </summary>
        public async Task<string?> SendXmlCommandWithAttributeResponseAsync(string xml, string attrName, int timeoutSeconds, CancellationToken ct = default)
        {
            try
            {
                PurgeBuffer();
                _port.Write(Encoding.UTF8.GetBytes(xml));
                
                await Task.Delay(300, ct);
                string response = ReadRawResponse(timeoutSeconds * 1000);
                
                if (string.IsNullOrEmpty(response))
                    return null;
                
                // 提取属性值
                int start = response.IndexOf(attrName + "=\"");
                if (start < 0) return null;
                
                start += attrName.Length + 2;
                int end = response.IndexOf("\"", start);
                if (end < 0) return null;
                
                return response.Substring(start, end - start);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 读取原始响应
        /// </summary>
        public string ReadRawResponse(int timeoutMs)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (_port.BytesToRead > 0)
                    {
                        byte[] buffer = new byte[_port.BytesToRead];
                        int read = _port.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            string data = Encoding.UTF8.GetString(buffer, 0, read);
                            sb.Append(data);
                            
                            // 检查是否接收完毕
                            string current = sb.ToString();
                            if (current.Contains("</data>") || current.Contains("</response>"))
                                break;
                        }
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }
                catch
                {
                    break;
                }
            }
            
            return sb.ToString();
        }

        #endregion

        #region 读取分区表

        /// <summary>
        /// 读取 GPT 分区表 (支持多 LUN)
        /// </summary>
        /// <param name="useVipMode">是否使用 VIP 伪装模式 (OPPO/Realme 需要，小米不需要)</param>
        public async Task<List<PartitionInfo>?> ReadGptPartitionsAsync(bool useVipMode = false, CancellationToken ct = default)
        {
            var partitions = new List<PartitionInfo>();

            for (int lun = 0; lun < 6; lun++)
            {
                byte[]? gptData = null;

                if (useVipMode)
                {
                    // VIP 设备读取策略 (OPPO/Realme 需要伪装)
                    var readStrategies = new (string label, string filename)[]
                    {
                        ("PrimaryGPT", $"gpt_main{lun}.bin"),
                        ("BackupGPT", $"gpt_backup{lun}.bin"),
                        ("ssd", "ssd"),
                        ("gpt", "ssd")
                    };

                    foreach (var (label, filename) in readStrategies)
                    {
                        try
                        {
                            gptData = await ReadGptPacketAsync(lun, 0, 34, label, filename, ct);
                            if (gptData != null && gptData.Length >= 512)
                            {
                                _log?.Invoke($"[GPT] LUN{lun} 使用伪装 {label} 成功");
                                break;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    // 普通读取模式 (小米/联想/标准设备)
                    try
                    {
                        // 每个 LUN 读取前清理缓冲区并短暂等待
                        PurgeBuffer();
                        if (lun > 0)
                        {
                            await Task.Delay(50, ct); // LUN 切换时等待设备准备
                        }
                        
                        _log?.Invoke($"[GPT] 读取 LUN{lun} (普通模式)...");
                        gptData = await ReadSectorsAsync(lun, 0, 34, ct);
                        if (gptData != null && gptData.Length >= 512)
                        {
                            _log?.Invoke($"[GPT] LUN{lun} 读取成功 ({gptData.Length} 字节)");
                        }
                        else
                        {
                            _log?.Invoke($"[GPT] LUN{lun} 无数据");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[GPT] LUN{lun} 读取异常: {ex.Message}");
                    }
                }

                if (gptData == null || gptData.Length < 512)
                    continue;

                var lunPartitions = ParseGptPartitions(gptData, lun);
                if (lunPartitions.Count > 0)
                {
                    partitions.AddRange(lunPartitions);
                    _log?.Invoke($"[Firehose] LUN {lun}: {lunPartitions.Count} 个分区");
                }
            }

            if (partitions.Count > 0)
            {
                _cachedPartitions = partitions;
                _log?.Invoke($"[Firehose] 共读取 {partitions.Count} 个分区");
            }

            return partitions.Count > 0 ? partitions : null;
        }

        /// <summary>
        /// 读取 GPT 数据包 (与 fh_loader 兼容，使用伪装)
        /// </summary>
        public async Task<byte[]?> ReadGptPacketAsync(int lun, long startSector, int numSectors, string label, string filename, CancellationToken ct = default)
        {
            double sizeKB = (numSectors * _sectorSize) / 1024.0;
            long startByte = startSector * _sectorSize;

            // 完整属性格式 (与 fh_loader --convertprogram2read 一致)
            string xml = $"<?xml version=\"1.0\" ?><data>\n" +
                         $"<read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                         $"file_sector_offset=\"0\" " +
                         $"filename=\"{filename}\" " +
                         $"label=\"{label}\" " +
                         $"num_partition_sectors=\"{numSectors}\" " +
                         $"partofsingleimage=\"true\" " +
                         $"physical_partition_number=\"{lun}\" " +
                         $"readbackverify=\"false\" " +
                         $"size_in_KB=\"{sizeKB:F1}\" " +
                         $"sparse=\"false\" " +
                         $"start_byte_hex=\"0x{startByte:X}\" " +
                         $"start_sector=\"{startSector}\" />\n" +
                         "</data>\n";

            _log?.Invoke($"[GPT] 读取 LUN{lun} (伪装: {label}/{filename})...");
            PurgeBuffer();
            await WriteXmlAsync(xml, ct);

            var buffer = new byte[numSectors * _sectorSize];
            if (await ReceiveDataAfterAckAsync(buffer, ct))
            {
                await WaitForAckAsync(ct); // 消耗最终 ACK
                _log?.Invoke($"[GPT] ✓ LUN{lun} 读取成功 ({buffer.Length} 字节)");
                return buffer;
            }

            _log?.Invoke($"[GPT] ✗ LUN{lun} 读取失败");
            return null;
        }

        /// <summary>
        /// 读取备份 GPT
        /// </summary>
        public async Task<byte[]?> ReadBackupGptAsync(int lun = 0, int numSectors = 6, CancellationToken ct = default)
        {
            PurgeBuffer();
            string start = $"NUM_DISK_SECTORS-{numSectors}.";

            string xml = $"<?xml version=\"1.0\" ?><data><read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" filename=\"ssd\" label=\"ssd\" num_partition_sectors=\"{numSectors}\" physical_partition_number=\"{lun}\" sparse=\"false\" start_sector=\"{start}\" /></data>";

            _log?.Invoke($"[Firehose] 读取 Backup GPT LUN{lun}...");
            _port.Write(Encoding.UTF8.GetBytes(xml));

            var buffer = new byte[numSectors * _sectorSize];
            if (await ReceiveDataAfterAckAsync(buffer, ct))
            {
                await WaitForAckAsync(ct); // 消耗最终 ACK
                return buffer;
            }
            return null;
        }

        /// <summary>
        /// 解析 GPT 分区
        /// </summary>
        public List<PartitionInfo> ParseGptPartitions(byte[] gptData, int lun)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                _log?.Invoke($"[GPT解析] LUN{lun}: 数据长度={gptData.Length}, SectorSize={_sectorSize}");
                
                // 搜索 GPT 签名 "EFI PART" (可能在多个位置)
                int headerOffset = -1;
                int[] searchOffsets = { _sectorSize, 512, 0, _sectorSize * 2 };

                foreach (int offset in searchOffsets)
                {
                    if (offset + 92 <= gptData.Length)
                    {
                        string sig = Encoding.ASCII.GetString(gptData, offset, 8);
                        if (sig == "EFI PART")
                        {
                            headerOffset = offset;
                            _log?.Invoke($"[GPT解析] ✓ 在偏移 {offset} (0x{offset:X}) 找到签名");
                            break;
                        }
                    }
                }

                if (headerOffset < 0)
                {
                    // 尝试暴力搜索
                    for (int i = 0; i <= gptData.Length - 8; i += 512)
                    {
                        string sig = Encoding.ASCII.GetString(gptData, i, 8);
                        if (sig == "EFI PART")
                        {
                            headerOffset = i;
                            _log?.Invoke($"[GPT解析] ✓ 暴力搜索在偏移 {i} 找到签名");
                            break;
                        }
                    }
                }

                if (headerOffset < 0)
                {
                    _log?.Invoke($"[GPT解析] ✗ 未找到 GPT 签名 (前16字节: {BitConverter.ToString(gptData, 0, Math.Min(16, gptData.Length))})");
                    return partitions;
                }

                int numPartitionEntries = BitConverter.ToInt32(gptData, headerOffset + 80);
                int partitionEntrySize = BitConverter.ToInt32(gptData, headerOffset + 84);
                long partitionEntriesLba = BitConverter.ToInt64(gptData, headerOffset + 72);

                _log?.Invoke($"[GPT解析] 条目数={numPartitionEntries}, 条目大小={partitionEntrySize}, 条目起始LBA={partitionEntriesLba}");

                // 计算分区条目偏移 (相对于 headerOffset)
                int entryOffset;
                if (partitionEntriesLba == 2)
                {
                    // 标准 GPT: 条目在 LBA 2
                    entryOffset = headerOffset + _sectorSize;
                }
                else
                {
                    // 使用 GPT 头中指定的 LBA
                    entryOffset = (int)(partitionEntriesLba * _sectorSize);
                }

                _log?.Invoke($"[GPT解析] 条目起始偏移={entryOffset}");

                if (entryOffset + 128 > gptData.Length)
                {
                    _log?.Invoke($"[GPT解析] ✗ 数据不足 (需要至少 {entryOffset + 128} 字节)");
                    return partitions;
                }

                for (int i = 0; i < numPartitionEntries && i < 128; i++)
                {
                    int offset = entryOffset + i * partitionEntrySize;
                    if (offset + 128 > gptData.Length)
                        break;

                    // 检查分区类型 GUID 是否为空
                    bool isEmpty = true;
                    for (int j = 0; j < 16; j++)
                    {
                        if (gptData[offset + j] != 0)
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                    if (isEmpty) continue;

                    // 分区名称 (UTF-16LE)
                    string name = Encoding.Unicode.GetString(gptData, offset + 56, 72).TrimEnd('\0');
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    long startLba = BitConverter.ToInt64(gptData, offset + 32);
                    long endLba = BitConverter.ToInt64(gptData, offset + 40);

                    partitions.Add(new PartitionInfo
                    {
                        Name = name,
                        Lun = lun,
                        StartSector = startLba,
                        NumSectors = endLba - startLba + 1,
                        SectorSize = _sectorSize
                    });
                }

                _log?.Invoke($"[GPT解析] LUN{lun} 解析完成: {partitions.Count} 个分区");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[GPT解析] 异常: {ex.Message}");
            }

            return partitions;
        }

        #endregion

        #region 读取分区

        /// <summary>
        /// 读取分区到文件
        /// </summary>
        public async Task<bool> ReadPartitionAsync(PartitionInfo partition, string savePath, CancellationToken ct = default)
        {
            _log?.Invoke($"[Firehose] 读取分区: {partition.Name}");

            var totalSectors = partition.NumSectors;
            var sectorsPerChunk = _maxPayloadSize / _sectorSize;
            var totalRead = 0L;

            StartTransferTimer(partition.Size);

            using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, FILE_BUFFER_SIZE);

            for (long sector = 0; sector < totalSectors; sector += sectorsPerChunk)
            {
                ct.ThrowIfCancellationRequested();

                var sectorsToRead = Math.Min(sectorsPerChunk, totalSectors - sector);
                var startSector = partition.StartSector + sector;

                var data = await ReadSectorsAsync(partition.Lun, startSector, (int)sectorsToRead, ct);
                if (data == null)
                {
                    _log?.Invoke($"[Firehose] ✗ 读取失败 @ sector {startSector}");
                    return false;
                }

                await fs.WriteAsync(data, ct);
                totalRead += data.Length;

                _progress?.Invoke(totalRead, partition.Size);
            }

            StopTransferTimer("读取", totalRead);
            _log?.Invoke($"[Firehose] ✓ 分区 {partition.Name} 读取完成: {totalRead:N0} 字节");
            return true;
        }

        /// <summary>
        /// 读取分区到文件 (参数版本，与策略兼容)
        /// </summary>
        public async Task<bool> ReadPartitionAsync(
            string savePath,
            string startSector,
            long numSectors,
            string lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? partitionName = null)
        {
            var name = partitionName ?? "rawdata";
            _log?.Invoke($"[Firehose] 读取: {name}");

            long totalBytes = numSectors * _sectorSize;
            int sectorsPerChunk = _maxPayloadSize / _sectorSize;
            long totalRead = 0L;
            long currentSector = long.Parse(startSector);
            long remaining = numSectors;

            using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, FILE_BUFFER_SIZE);

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int sectors = (int)Math.Min(sectorsPerChunk, remaining);
                var data = await ReadSectorsAsync(int.Parse(lun), currentSector, sectors, ct);

                if (data == null)
                {
                    _log?.Invoke($"[Firehose] ✗ 读取失败 @ sector {currentSector}");
                    return false;
                }

                await fs.WriteAsync(data, ct);
                totalRead += data.Length;
                currentSector += sectors;
                remaining -= sectors;

                progress?.Invoke(totalRead, totalBytes);
            }

            _log?.Invoke($"[Firehose] ✓ {name} 读取完成: {totalRead:N0} 字节");
            return true;
        }

        /// <summary>
        /// 读取扇区数据
        /// </summary>
        /// <param name="lun">LUN 编号</param>
        /// <param name="startSector">起始扇区</param>
        /// <param name="numSectors">扇区数量</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="useVipMode">是否使用 VIP 伪装模式 (仅 OPPO/Realme 需要)</param>
        /// <param name="partitionName">分区名称 (用于动态伪装)</param>
        public async Task<byte[]?> ReadSectorsAsync(
            int lun, 
            long startSector, 
            int numSectors, 
            CancellationToken ct, 
            bool useVipMode = false,
            string? partitionName = null)
        {
            if (useVipMode)
            {
                // 使用动态伪装策略
                bool isGptRead = startSector <= 33;
                var strategies = GetDynamicSpoofStrategies(lun, startSector, partitionName, isGptRead);

                foreach (var strategy in strategies)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        PurgeBuffer();

                        string xml;
                        double sizeKB = (numSectors * _sectorSize) / 1024.0;
                        
                        if (string.IsNullOrEmpty(strategy.Label))
                        {
                            // 无伪装读取
                            xml = $"<?xml version=\"1.0\" ?><data>\n" +
                                  $"<read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                                  $"num_partition_sectors=\"{numSectors}\" " +
                                  $"physical_partition_number=\"{lun}\" " +
                                  $"size_in_KB=\"{sizeKB:F1}\" " +
                                  $"start_sector=\"{startSector}\" />\n</data>\n";
                        }
                        else
                        {
                            // 动态伪装读取
                            xml = $"<?xml version=\"1.0\" ?><data>\n" +
                                  $"<read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                                  $"filename=\"{strategy.Filename}\" " +
                                  $"label=\"{strategy.Label}\" " +
                                  $"num_partition_sectors=\"{numSectors}\" " +
                                  $"physical_partition_number=\"{lun}\" " +
                                  $"size_in_KB=\"{sizeKB:F1}\" " +
                                  $"sparse=\"false\" " +
                                  $"start_sector=\"{startSector}\" />\n</data>\n";
                        }

                        _port.Write(Encoding.UTF8.GetBytes(xml));

                        int expectedSize = numSectors * _sectorSize;
                        var buffer = new byte[expectedSize];

                        if (await ReceiveDataAfterAckAsync(buffer, ct))
                        {
                            await WaitForAckAsync(ct);
                            // 成功，记录使用的策略
                            if (!string.IsNullOrEmpty(strategy.Label))
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIP] ✓ 伪装成功: {strategy}");
                            }
                            return buffer;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch 
                    { 
                        // 尝试下一个策略
                        System.Diagnostics.Debug.WriteLine($"[VIP] ✗ 策略失败: {strategy}");
                    }
                }

                return null;
            }
            else
            {
                // 普通读取模式 (小米/联想/标准设备)
                try
                {
                    PurgeBuffer();

                    double sizeKB = (numSectors * _sectorSize) / 1024.0;
                    
                    string xml = $"<?xml version=\"1.0\" ?><data>\n" +
                                 $"<read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                                 $"num_partition_sectors=\"{numSectors}\" " +
                                 $"physical_partition_number=\"{lun}\" " +
                                 $"size_in_KB=\"{sizeKB:F1}\" " +
                                 $"start_sector=\"{startSector}\" />\n" +
                                 $"</data>\n";

                    _port.Write(Encoding.UTF8.GetBytes(xml));
                    
                    int expectedSize = numSectors * _sectorSize;
                    var buffer = new byte[expectedSize];

                    if (await ReceiveDataAfterAckAsync(buffer, ct))
                    {
                        await WaitForAckAsync(ct);
                        return buffer;
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Read] 异常: {ex.Message}");
                }

                return null;
            }
        }

        #endregion

        #region 写入分区

        /// <summary>
        /// 写入分区数据 (支持 VIP 模式)
        /// </summary>
        public async Task<bool> WritePartitionAsync(PartitionInfo partition, string imagePath, bool useOppoMode = false, CancellationToken ct = default)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("镜像文件不存在", imagePath);

            var fileInfo = new FileInfo(imagePath);
            _log?.Invoke($"[Firehose] 写入分区: {partition.Name} ({fileInfo.Length:N0} 字节)");

            var totalBytes = fileInfo.Length;
            var sectorsPerChunk = _maxPayloadSize / _sectorSize;
            var bytesPerChunk = sectorsPerChunk * _sectorSize;
            var totalWritten = 0L;

            StartTransferTimer(totalBytes);

            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, FILE_BUFFER_SIZE);
            var buffer = new byte[bytesPerChunk];

            var totalSectors = (totalBytes + _sectorSize - 1) / _sectorSize;
            var currentSector = partition.StartSector;

            while (totalWritten < totalBytes)
            {
                ct.ThrowIfCancellationRequested();

                var bytesToRead = (int)Math.Min(bytesPerChunk, totalBytes - totalWritten);
                var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
                if (bytesRead == 0) break;

                // 补齐到扇区边界
                var paddedSize = ((bytesRead + _sectorSize - 1) / _sectorSize) * _sectorSize;
                if (paddedSize > bytesRead)
                    Array.Clear(buffer, bytesRead, paddedSize - bytesRead);

                var sectorsToWrite = paddedSize / _sectorSize;

                if (!await WriteSectorsAsync(partition.Lun, currentSector, buffer, paddedSize, partition.Name, useOppoMode, ct))
                {
                    _log?.Invoke($"[Firehose] ✗ 写入失败 @ sector {currentSector}");
                    return false;
                }

                totalWritten += bytesRead;
                currentSector += sectorsToWrite;

                _progress?.Invoke(totalWritten, totalBytes);
            }

            StopTransferTimer("写入", totalWritten);
            _log?.Invoke($"[Firehose] ✓ 分区 {partition.Name} 写入完成: {totalWritten:N0} 字节");
            return true;
        }

        /// <summary>
        /// 写入扇区数据 (支持 OPPO token)
        /// </summary>
        private async Task<bool> WriteSectorsAsync(int lun, long startSector, byte[] data, int length, string label, bool useOppoMode, CancellationToken ct)
        {
            int numSectors = length / _sectorSize;
            double sizeKB = length / 1024.0;
            long startByte = startSector * _sectorSize;

            // 使用 gpt_backup 伪装绕过 VIP 权限
            string fileName = $"gpt_backup{lun}.bin";
            string labelName = "BackupGPT";

            string xml;
            if (useOppoMode && !string.IsNullOrEmpty(OnePlusProgramToken))
            {
                xml = $"<?xml version=\"1.0\" ?><data>" +
                      $"<program SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                      $"filename=\"{fileName}\" label=\"{labelName}\" " +
                      $"num_partition_sectors=\"{numSectors}\" " +
                      $"physical_partition_number=\"{lun}\" " +
                      $"start_sector=\"{startSector}\" " +
                      $"token=\"{OnePlusProgramToken}\" pk=\"{OnePlusProgramPk}\" />" +
                      "</data>";
            }
            else
            {
                xml = $"<?xml version=\"1.0\" ?><data>" +
                      $"<program SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                      $"filename=\"{fileName}\" label=\"{labelName}\" " +
                      $"num_partition_sectors=\"{numSectors}\" " +
                      $"physical_partition_number=\"{lun}\" " +
                      $"start_sector=\"{startSector}\" />" +
                      "</data>";
            }

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            // 等待设备准备接收数据 (rawmode="true")
            if (!await WaitForRawDataModeAsync(ct))
            {
                _log?.Invoke("[Firehose] Program 命令未确认");
                return false;
            }

            // 发送数据
            _port.Write(data, 0, length);

            // 等待写入确认
            return await WaitForAckAsync(ct, 10);
        }

        #endregion

        #region 擦除分区

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(PartitionInfo partition, CancellationToken ct = default)
        {
            _log?.Invoke($"[Firehose] 擦除分区: {partition.Name}");

            var xml = $"<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" num_partition_sectors=\"{partition.NumSectors}\" physical_partition_number=\"{partition.Lun}\" start_sector=\"{partition.StartSector}\" /></data>";

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct))
            {
                _log?.Invoke($"[Firehose] ✓ 分区 {partition.Name} 擦除完成");
                return true;
            }

            _log?.Invoke($"[Firehose] ✗ 擦除失败");
            return false;
        }

        /// <summary>
        /// 擦除分区 (参数版本)
        /// </summary>
        public bool ErasePartition(string startSector, long numSectors, string lun)
        {
            var xml = $"<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" num_partition_sectors=\"{numSectors}\" physical_partition_number=\"{lun}\" start_sector=\"{startSector}\" /></data>";

            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return WaitForAckAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 从内存刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionFromMemoryAsync(
            byte[] data,
            string startSector,
            long maxSectors,
            string lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? partitionName = null)
        {
            if (data == null || data.Length == 0)
            {
                _log?.Invoke("[Flash] 数据为空");
                return false;
            }

            using var ms = new MemoryStream(data);
            return await FlashPartitionFromStreamAsync(ms, startSector, maxSectors, lun,
                progress, ct, partitionName);
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> ResetAsync(string mode = "reset", CancellationToken ct = default)
        {
            _log?.Invoke($"[Firehose] 重启设备 (模式: {mode})");

            var xml = $"<?xml version=\"1.0\" ?><data><power value=\"{mode}\" /></data>";
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 重启设备 (同步版本)
        /// </summary>
        public bool Reset(string mode = "reset")
        {
            return ResetAsync(mode, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 应用补丁 (rawprogram.xml 格式)
        /// </summary>
        public void ApplyPatch(string patchXml)
        {
            if (string.IsNullOrEmpty(patchXml)) return;

            try
            {
                _log?.Invoke("[Firehose] 应用补丁...");
                PurgeBuffer();
                _port.Write(Encoding.UTF8.GetBytes(patchXml));
                WaitForAckAsync(CancellationToken.None, 10).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Firehose] 应用补丁异常: {ex.Message}");
            }
        }

        /// <summary>
        /// Ping/NOP 测试连接
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" ?><data><nop /></data>"));
            return await WaitForAckAsync(ct, 3);
        }

        /// <summary>
        /// 设置启动 LUN
        /// </summary>
        public async Task<bool> SetBootLunAsync(int lun, CancellationToken ct = default)
        {
            _log?.Invoke($"[Firehose] 设置启动 LUN: {lun}");
            var xml = $"<?xml version=\"1.0\" ?><data><setbootablestoragedrive value=\"{lun}\" /></data>";
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 设置活动槽位 (A/B)
        /// </summary>
        public async Task<bool> SetActiveSlotAsync(int slot, CancellationToken ct = default)
        {
            _log?.Invoke($"[Firehose] 设置活动槽位: {(slot == 0 ? "A" : "B")}");
            var xml = $"<?xml version=\"1.0\" ?><data><setactiveslot slot=\"{slot}\" /></data>";
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 修复 GPT
        /// </summary>
        public async Task<bool> FixGptAsync(int lun = -1, bool growLastPartition = true, CancellationToken ct = default)
        {
            string lunValue = (lun == -1) ? "all" : lun.ToString();
            string growValue = growLastPartition ? "1" : "0";

            _log?.Invoke($"[Firehose] 修复 GPT (LUN={lunValue})...");
            var xml = $"<?xml version=\"1.0\" ?><data><fixgpt lun=\"{lunValue}\" grow_last_partition=\"{growValue}\" /></data>";
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            if (await WaitForAckAsync(ct, 10))
            {
                _log?.Invoke("[Firehose] ✓ GPT 修复成功");
                return true;
            }

            _log?.Invoke("[Firehose] ✗ GPT 修复失败");
            return false;
        }

        /// <summary>
        /// 获取存储信息
        /// </summary>
        public async Task<string> GetStorageInfoAsync(int lun = 0, CancellationToken ct = default)
        {
            var xml = $"<?xml version=\"1.0\" ?><data><getstorageinfo physical_partition_number=\"{lun}\"/></data>";
            PurgeBuffer();
            _port.Write(Encoding.UTF8.GetBytes(xml));

            var sb = new StringBuilder();
            for (int i = 0; i < 50; i++)
            {
                ct.ThrowIfCancellationRequested();
                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    string val = resp.Attribute("value")?.Value ?? "";
                    if (!string.IsNullOrEmpty(val))
                    {
                        foreach (var attr in resp.Attributes())
                        {
                            if (attr.Name != "value")
                                sb.AppendLine($"{attr.Name}: {attr.Value}");
                        }
                        return sb.ToString();
                    }
                }
                await Task.Delay(50, ct);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取存储信息 (同步版本)
        /// </summary>
        public string GetStorageInfo(int lun = 0)
        {
            return GetStorageInfoAsync(lun, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 分块读取分区 (支持伪装文件和标签)
        /// </summary>
        /// <param name="savePath">保存路径</param>
        /// <param name="startSector">起始扇区</param>
        /// <param name="numSectors">扇区数量</param>
        /// <param name="lun">LUN 编号</param>
        /// <param name="progress">进度回调</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="spoofLabel">伪装标签 (null = 自动选择)</param>
        /// <param name="spoofFilename">伪装文件名 (null = 自动选择)</param>
        /// <param name="append">是否追加写入</param>
        /// <param name="useAutoSpoof">是否启用自动动态伪装</param>
        /// <param name="partitionName">分区名称 (用于动态伪装)</param>
        public async Task<bool> ReadPartitionChunkedAsync(
            string savePath,
            string startSector,
            long numSectors,
            string lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? spoofLabel = null,
            string? spoofFilename = null,
            bool append = false,
            bool useAutoSpoof = false,
            string? partitionName = null)
        {
            long totalBytes = numSectors * _sectorSize;
            long bytesRead = 0;
            long currentSector = long.Parse(startSector);
            long remaining = numSectors;
            int lunNum = int.Parse(lun);

            int chunkSectors = MaxPayloadSize / _sectorSize;
            if (chunkSectors < 1) chunkSectors = 64;

            // 动态伪装策略: 如果启用自动伪装或未提供伪装参数
            string label;
            string filename;
            
            if (useAutoSpoof || (spoofLabel == null && spoofFilename == null))
            {
                // 自动选择最佳伪装策略
                var strategies = GetDynamicSpoofStrategies(lunNum, currentSector, partitionName ?? spoofLabel, currentSector <= 33);
                var bestStrategy = strategies.FirstOrDefault();
                label = bestStrategy.Label ?? "rawdata";
                filename = bestStrategy.Filename ?? "rawdata.bin";
                
                if (!string.IsNullOrEmpty(label))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIP] 自动伪装: {label}/{filename}");
                }
            }
            else
            {
                label = spoofLabel ?? "rawdata";
                filename = spoofFilename ?? "rawdata.bin";
            }

            try
            {
                using var fs = new FileStream(savePath, append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, FILE_BUFFER_SIZE, FileOptions.SequentialScan);

                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    int sectors = (int)Math.Min(chunkSectors, remaining);
                    double sizeKB = (sectors * _sectorSize) / 1024.0;

                    string xml = $"<?xml version=\"1.0\" ?><data>\n" +
                                 $"<read SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                                 $"filename=\"{filename}\" " +
                                 $"label=\"{label}\" " +
                                 $"num_partition_sectors=\"{sectors}\" " +
                                 $"physical_partition_number=\"{lun}\" " +
                                 $"size_in_KB=\"{sizeKB:F1}\" " +
                                 $"sparse=\"false\" " +
                                 $"start_sector=\"{currentSector}\" />\n</data>\n";

                    PurgeBuffer();
                    _port.Write(Encoding.UTF8.GetBytes(xml));

                    var buffer = new byte[sectors * _sectorSize];
                    
                    // 使用与 GPT 读取相同的方法：先消耗 ACK，再读取数据
                    if (!await ReceiveDataAfterAckAsync(buffer, ct))
                    {
                        _log?.Invoke($"[Read] 读取失败 @ sector {currentSector} (伪装: {label})");
                        return false;
                    }

                    // 等待最终 ACK
                    await WaitForAckAsync(ct);

                    await fs.WriteAsync(buffer, ct);

                    bytesRead += buffer.Length;
                    currentSector += sectors;
                    remaining -= sectors;

                    progress?.Invoke(bytesRead, totalBytes);
                }

                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Read] 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 智能分区读取 (自动瀑布式伪装策略)
        /// 自动尝试多种伪装策略直到成功
        /// </summary>
        public async Task<bool> ReadPartitionWithAutoSpoofAsync(
            string savePath,
            long startSector,
            long numSectors,
            int lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? partitionName = null)
        {
            // 获取动态伪装策略列表
            var strategies = GetDynamicSpoofStrategies(lun, startSector, partitionName, startSector <= 33);
            
            foreach (var strategy in strategies)
            {
                ct.ThrowIfCancellationRequested();
                
                try
                {
                    string label = string.IsNullOrEmpty(strategy.Label) ? "rawdata" : strategy.Label;
                    string filename = string.IsNullOrEmpty(strategy.Filename) ? "rawdata.bin" : strategy.Filename;
                    
                    _log?.Invoke($"[VIP] 尝试伪装策略: {label}/{filename}");
                    
                    bool success = await ReadPartitionChunkedAsync(
                        savePath,
                        startSector.ToString(),
                        numSectors,
                        lun.ToString(),
                        progress,
                        ct,
                        label,
                        filename,
                        append: false,
                        useAutoSpoof: false,  // 已手动指定策略
                        partitionName: partitionName);
                    
                    if (success)
                    {
                        _log?.Invoke($"[VIP] ✓ 伪装成功: {label}/{filename}");
                        return true;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIP] 策略 {strategy} 失败: {ex.Message}");
                }
                
                // 短暂延迟后尝试下一个策略
                await Task.Delay(100, ct);
            }
            
            _log?.Invoke("[VIP] ❌ 所有伪装策略均失败");
            return false;
        }

        /// <summary>
        /// 刷写分区 (支持伪装文件和标签)
        /// </summary>
        public async Task<bool> FlashPartitionAsync(
            string imagePath,
            string startSector,
            long maxSectors,
            string lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? partitionName = null,
            string? spoofFilename = null)
        {
            if (!File.Exists(imagePath))
            {
                _log?.Invoke($"[Flash] 文件不存在: {imagePath}");
                return false;
            }

            // 检测是否为 Sparse 格式，自动转换为透明流
            Stream stream;
            bool isSparse = Modules.Common.SparseStream.IsSparseFile(imagePath);
            
            if (isSparse)
            {
                _log?.Invoke($"[Flash] 检测到 Sparse 格式: {Path.GetFileName(imagePath)}");
                stream = Modules.Common.SparseStream.Open(imagePath, _log);
            }
            else
            {
                stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            }

            try
            {
                return await FlashPartitionFromStreamAsync(stream, startSector, maxSectors, lun,
                    progress, ct, partitionName, spoofFilename);
            }
            finally
            {
                stream.Dispose();
            }
        }

        /// <summary>
        /// 从流刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionFromStreamAsync(
            Stream stream,
            string startSector,
            long maxSectors,
            string lun,
            Action<long, long>? progress,
            CancellationToken ct,
            string? partitionName = null,
            string? spoofFilename = null)
        {
            long totalBytes = stream.Length;
            long totalSectors = (totalBytes + _sectorSize - 1) / _sectorSize;
            long currentSector = long.Parse(startSector);
            long bytesWritten = 0;

            int chunkSectors = MaxPayloadSize / _sectorSize;
            if (chunkSectors < 1) chunkSectors = 64;

            string label = partitionName ?? "rawdata";
            string filename = spoofFilename ?? $"{label}.bin";

            try
            {
                while (bytesWritten < totalBytes)
                {
                    ct.ThrowIfCancellationRequested();

                    int bytesToRead = (int)Math.Min(chunkSectors * _sectorSize, totalBytes - bytesWritten);
                    var buffer = new byte[bytesToRead];
                    int actualRead = await stream.ReadAsync(buffer, 0, bytesToRead, ct);

                    if (actualRead == 0) break;

                    // 对齐到扇区边界
                    if (actualRead % _sectorSize != 0)
                    {
                        int aligned = ((actualRead + _sectorSize - 1) / _sectorSize) * _sectorSize;
                        Array.Resize(ref buffer, aligned);
                    }

                    int sectors = buffer.Length / _sectorSize;
                    double sizeKB = buffer.Length / 1024.0;

                    string xml = $"<?xml version=\"1.0\" ?><data>\n" +
                                 $"<program SECTOR_SIZE_IN_BYTES=\"{_sectorSize}\" " +
                                 $"filename=\"{filename}\" " +
                                 $"label=\"{label}\" " +
                                 $"num_partition_sectors=\"{sectors}\" " +
                                 $"physical_partition_number=\"{lun}\" " +
                                 $"size_in_KB=\"{sizeKB:F1}\" " +
                                 $"sparse=\"false\" " +
                                 $"start_sector=\"{currentSector}\" />\n</data>\n";

                    PurgeBuffer();
                    _port.Write(Encoding.UTF8.GetBytes(xml));

                    // 等待设备准备接收
                    if (!await WaitForRawDataModeAsync(ct))
                    {
                        _log?.Invoke($"[Flash] 设备未就绪 @ sector {currentSector}");
                        return false;
                    }

                    // 发送数据
                    _port.Write(buffer, 0, buffer.Length);

                    // 等待 ACK
                    if (!await WaitForAckAsync(ct))
                    {
                        _log?.Invoke($"[Flash] ACK 失败 @ sector {currentSector}");
                        return false;
                    }

                    bytesWritten += actualRead;
                    currentSector += sectors;

                    progress?.Invoke(bytesWritten, totalBytes);
                }

                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Flash] 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 等待设备进入 Raw 数据模式 (用于写入前等待 ACK)
        /// </summary>
        private async Task<bool> WaitForRawDataModeAsync(CancellationToken ct, int timeoutMs = 5000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int originalTimeout = _port.ReadTimeout;
                    _port.ReadTimeout = timeoutMs;

                    try
                    {
                        var buffer = new byte[4096];
                        var sb = new System.Text.StringBuilder();

                        // 读取完整的 XML 响应，直到找到 </data> 或超时
                        while (true)
                        {
                            if (ct.IsCancellationRequested) return false;

                            int read = _port.Read(buffer, 0, buffer.Length);
                            if (read == 0) break;

                            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                            string response = sb.ToString();

                            // 检查是否收到 NAK
                            if (response.Contains("NAK"))
                            {
                                _log?.Invoke($"[Write] 设备拒绝: {response.Substring(0, Math.Min(response.Length, 100))}");
                                return false;
                            }

                            // 检查是否收到 rawmode="true" 表示设备准备好接收数据
                            if (response.Contains("rawmode=\"true\"") || response.Contains("rawmode='true'"))
                            {
                                // 确保完整消耗 ACK 响应
                                if (response.Contains("</data>"))
                                    return true;
                            }

                            // 普通 ACK 也可以
                            if (response.Contains("ACK") && response.Contains("</data>"))
                                return true;
                        }

                        return false;
                    }
                    finally
                    {
                        _port.ReadTimeout = originalTimeout;
                    }
                }
                catch (TimeoutException)
                {
                    _log?.Invoke("[Write] 等待设备就绪超时");
                    return false;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Write] 等待异常: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        #endregion

        #region 分区缓存

        public void SetPartitionCache(List<PartitionInfo> partitions) => _cachedPartitions = partitions;

        public PartitionInfo? FindPartition(string name)
        {
            return _cachedPartitions?.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region 通信方法

        private async Task<bool> SendXmlCommandAsync(string xml, bool ignoreResponse = false, CancellationToken ct = default)
        {
            await WriteXmlAsync(xml, ct);
            if (ignoreResponse)
            {
                await ProcessXmlResponseAsync(ct);
                return true;
            }
            return await WaitForAckAsync(ct);
        }

        /// <summary>
        /// 异步处理 XML 响应 (保持 UI 流畅)
        /// </summary>
        private async Task<XElement?> ProcessXmlResponseAsync(CancellationToken ct, int timeoutMs = 5000)
        {
            try
            {
                var sb = new StringBuilder();
                var startTime = DateTime.Now;

                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    ct.ThrowIfCancellationRequested();

                    // 异步读取数据
                    var (buffer, read) = await ReadAvailableAsync(ct);
                    
                    if (read > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

                        var content = sb.ToString();
                        
                        // 检查是否包含日志消息 (设备调试输出)
                        if (content.Contains("<log "))
                        {
                            // 提取并显示日志
                            var logMatches = System.Text.RegularExpressions.Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                            foreach (System.Text.RegularExpressions.Match m in logMatches)
                            {
                                if (m.Groups.Count > 1)
                                    _log?.Invoke($"[Device] {m.Groups[1].Value}");
                            }
                        }
                        
                        if (content.Contains("</data>") || content.Contains("<response"))
                        {
                            // 解析 response 节点
                            int start = content.IndexOf("<response");
                            if (start >= 0)
                            {
                                int end = content.IndexOf("/>", start);
                                if (end > start)
                                {
                                    var respXml = content.Substring(start, end - start + 2);
                                    return XElement.Parse(respXml);
                                }
                            }
                        }
                    }
                    await Task.Delay(10, ct);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Firehose] 响应解析异常: {ex.Message}");
            }
            return null;
        }

        private async Task<bool> WaitForAckAsync(CancellationToken ct, int maxRetries = 50)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                ct.ThrowIfCancellationRequested();

                var resp = await ProcessXmlResponseAsync(ct);
                if (resp != null)
                {
                    string val = resp.Attribute("value")?.Value ?? "";

                    if (val.Equals("ACK", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (val.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                    {
                        string errorDesc = resp.Attribute("error")?.Value ?? resp.ToString();
                        var (message, suggestion, isFatal, _) = FirehoseErrorHelper.ParseNakError(errorDesc);
                        _log?.Invoke($"[Firehose] ✗ NAK: {message}");
                        if (!string.IsNullOrEmpty(suggestion))
                            _log?.Invoke($"[Firehose] 💡 {suggestion}");
                        return false;
                    }
                }
            }

            _log?.Invoke("[Firehose] ⚠️ 等待 ACK 超时");
            return false;
        }

        /// <summary>
        /// 统一方法: 接收 read 命令的响应数据
        /// 参考 temp_source_full 的 ReceiveRawDataToMemoryAsync 实现
        /// 流程: 阻塞读取 → 搜索 rawmode="true" → 找到 </data> 后读取二进制数据
        /// </summary>
        private async Task<bool> ReceiveDataAfterAckAsync(byte[] buffer, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int totalBytes = buffer.Length;
                    int received = 0;
                    bool headerFound = false;
                    byte[] tempBuf = new byte[65536];
                    
                    // 设置合理的超时
                    int originalTimeout = _port.ReadTimeout;
                    _port.ReadTimeout = 5000; // 5秒超时
                    
                    try
                    {
                        while (received < totalBytes)
                        {
                            if (ct.IsCancellationRequested) return false;

                            // 未找到头时读小块，找到后读大块
                            int requestSize = headerFound ? Math.Min(tempBuf.Length, totalBytes - received) : 4096;
                            
                            int read = _port.Read(tempBuf, 0, requestSize);
                            if (read == 0)
                            {
                                _log?.Invoke("[Read] 超时，无数据");
                                return false;
                            }

                            int dataStartOffset = 0;

                            if (!headerFound)
                            {
                                string content = Encoding.UTF8.GetString(tempBuf, 0, read);
                                
                                // 搜索 rawmode="true" 标记 (设备准备发送数据的信号)
                                int ackIndex = content.IndexOf("rawmode=\"true\"", StringComparison.OrdinalIgnoreCase);
                                if (ackIndex == -1) 
                                    ackIndex = content.IndexOf("rawmode='true'", StringComparison.OrdinalIgnoreCase);

                                if (ackIndex >= 0)
                                {
                                    // 找到 rawmode，现在找 </data> 结束标记
                                    int xmlEndIndex = content.IndexOf("</data>", ackIndex);
                                    if (xmlEndIndex >= 0)
                                    {
                                        headerFound = true;
                                        dataStartOffset = xmlEndIndex + 7; // + len("</data>")
                                        
                                        // 跳过换行符
                                        while (dataStartOffset < read && (tempBuf[dataStartOffset] == '\n' || tempBuf[dataStartOffset] == '\r'))
                                            dataStartOffset++;
                                        
                                        // 如果本包全是 XML，没有数据，继续读
                                        if (dataStartOffset >= read) continue;
                                    }
                                }
                                else if (content.Contains("NAK"))
                                {
                                    _log?.Invoke($"[Read] 设备拒绝读取: {content.Substring(0, Math.Min(content.Length, 100))}");
                                    return false;
                                }
                                else
                                {
                                    // 既没找到 rawmode，也不是 NAK，可能是 Log 消息
                                    // 丢弃并继续读下一包
                                    if (content.Contains("<log "))
                                    {
                                        var logMatches = System.Text.RegularExpressions.Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                                        foreach (System.Text.RegularExpressions.Match m in logMatches)
                                        {
                                            if (m.Groups.Count > 1)
                                                _log?.Invoke($"[Device] {m.Groups[1].Value}");
                                        }
                                    }
                                    continue;
                                }
                            }

                            // 复制二进制数据
                            int dataLength = read - dataStartOffset;
                            if (dataLength > 0)
                            {
                                if (received + dataLength > buffer.Length)
                                    dataLength = buffer.Length - received;
                                Array.Copy(tempBuf, dataStartOffset, buffer, received, dataLength);
                                received += dataLength;
                            }
                        }
                        return true;
                    }
                    finally
                    {
                        _port.ReadTimeout = originalTimeout;
                    }
                }
                catch (TimeoutException)
                {
                    _log?.Invoke("[Read] 读取超时");
                    return false;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Read] 异常: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        private async Task<bool> ReceiveRawDataAsync(byte[] buffer, CancellationToken ct)
        {
            int totalBytes = buffer.Length;
            int received = 0;
            var startTime = DateTime.Now;
            
            // 优化: 根据数据量动态计算超时 (基础 10 秒 + 每 MB 增加 2 秒)
            int timeoutMs = Math.Max(10000, 5000 + (totalBytes / (512 * 1024)));
            
            // 连续空闲计数器
            int emptyReadCount = 0;
            const int maxEmptyReads = 100; // 100 * 1ms = 100ms 无数据视为完成或超时
            bool checkedForXml = false;

            while (received < totalBytes)
            {
                // 检查取消
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                // 检查超时
                if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                {
                    _log?.Invoke($"[Read] 超时 ({timeoutMs}ms)，已接收 {received}/{totalBytes}");
                    return received > 0 && received >= totalBytes * 0.9; // 90% 也算成功
                }

                try
                {
                    int available = _port.BytesToRead;
                    if (available > 0)
                    {
                        int toRead = Math.Min(available, totalBytes - received);
                        int read = _port.Read(buffer, received, toRead);
                        received += read;
                        emptyReadCount = 0; // 重置空闲计数
                        
                        // 检测是否收到的是 XML 响应而不是二进制数据
                        if (!checkedForXml && received >= 5)
                        {
                            checkedForXml = true;
                            // 检查开头是否是 '<?xml' 或 '<data' 或 '<response' 或 '<log'
                            if (buffer[0] == '<' && (buffer[1] == '?' || buffer[1] == 'd' || buffer[1] == 'r' || buffer[1] == 'l'))
                            {
                                // 收到的是 XML 响应，可能是 NAK 或 log
                                string xmlPeek = Encoding.UTF8.GetString(buffer, 0, Math.Min(received, 200));
                                
                                if (xmlPeek.Contains("NAK"))
                                {
                                    _log?.Invoke($"[Read] 设备拒绝读取 (NAK): {xmlPeek.Substring(0, Math.Min(xmlPeek.Length, 100))}");
                                    return false;
                                }
                                
                                // 如果是 log 消息，跳过并继续等待二进制数据
                                if (xmlPeek.Contains("<log "))
                                {
                                    _log?.Invoke($"[Read] 跳过设备日志，继续等待数据...");
                                    // 清除已读取的 XML 数据
                                    received = 0;
                                    Array.Clear(buffer, 0, buffer.Length);
                                    checkedForXml = false; // 重新检测
                                    continue;
                                }
                                
                                // 其他 XML 响应，可能是错误
                                _log?.Invoke($"[Read] 收到意外 XML: {xmlPeek.Substring(0, Math.Min(xmlPeek.Length, 80))}...");
                                return false;
                            }
                        }
                    }
                    else
                    {
                        emptyReadCount++;
                        if (emptyReadCount > maxEmptyReads && received > 0)
                        {
                            // 已有数据但连续无新数据，可能传输完成
                            break;
                        }
                        // 优化: 减少延迟，仅 1ms 让出 CPU
                        await Task.Delay(1, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Read] 读取异常: {ex.Message}");
                    await Task.Delay(10, ct);
                }
            }

            return received >= totalBytes;
        }

        private void PurgeBuffer()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _rxBuffer.Clear();
        }

        #endregion

        #region 速度统计

        private void StartTransferTimer(long totalBytes)
        {
            _transferStopwatch = Stopwatch.StartNew();
            _transferTotalBytes = totalBytes;
        }

        private void StopTransferTimer(string operationName, long bytesTransferred)
        {
            if (_transferStopwatch == null) return;

            _transferStopwatch.Stop();
            double seconds = _transferStopwatch.Elapsed.TotalSeconds;

            if (seconds > 0.1 && bytesTransferred > 0)
            {
                double mbps = (bytesTransferred / 1024.0 / 1024.0) / seconds;
                double mbTotal = bytesTransferred / 1024.0 / 1024.0;

                if (mbTotal >= 1)
                    _log?.Invoke($"[速度] {operationName}: {mbTotal:F1}MB 用时 {seconds:F1}s ({mbps:F2} MB/s)");
            }

            _transferStopwatch = null;
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
