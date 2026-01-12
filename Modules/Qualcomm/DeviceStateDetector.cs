using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm
{
    /// <summary>
    /// 设备协议状态
    /// </summary>
    public enum DeviceProtocolState
    {
        /// <summary>未知状态</summary>
        Unknown = 0,
        
        /// <summary>端口已打开，等待检测</summary>
        PortOpened = 1,
        
        /// <summary>Sahara 模式 - 等待 Loader</summary>
        SaharaWaitingLoader = 2,
        
        /// <summary>Sahara 模式 - Loader 传输中</summary>
        SaharaTransferring = 3,
        
        /// <summary>Sahara 模式 - Loader 传输完成</summary>
        SaharaComplete = 4,
        
        /// <summary>Firehose 模式 - 未配置</summary>
        FirehoseNotConfigured = 5,
        
        /// <summary>Firehose 模式 - 配置成功</summary>
        FirehoseConfigured = 6,
        
        /// <summary>Firehose 模式 - 配置失败</summary>
        FirehoseConfigureFailed = 7,
        
        /// <summary>Firehose 模式 - 已认证</summary>
        FirehoseAuthenticated = 8,
        
        /// <summary>设备无响应</summary>
        NoResponse = 9,
        
        /// <summary>端口被占用或错误</summary>
        PortError = 10
    }

    /// <summary>
    /// 设备状态检测结果
    /// </summary>
    public class DeviceStateInfo
    {
        /// <summary>协议状态</summary>
        public DeviceProtocolState State { get; set; } = DeviceProtocolState.Unknown;
        
        /// <summary>状态描述</summary>
        public string Description { get; set; } = "";
        
        /// <summary>Sahara 版本 (如果处于 Sahara 模式)</summary>
        public uint SaharaVersion { get; set; }
        
        /// <summary>Sahara 设备模式</summary>
        public uint SaharaMode { get; set; }
        
        /// <summary>是否支持 64 位传输</summary>
        public bool Supports64Bit { get; set; }
        
        /// <summary>Firehose 存储类型 (ufs/emmc)</summary>
        public string StorageType { get; set; } = "";
        
        /// <summary>Firehose MaxPayloadSize</summary>
        public int MaxPayloadSize { get; set; }
        
        /// <summary>建议操作</summary>
        public string SuggestedAction { get; set; } = "";
        
        /// <summary>是否可以继续操作</summary>
        public bool CanProceed { get; set; }
        
        /// <summary>检测时间</summary>
        public DateTime DetectedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 设备状态检测器
    /// 自动识别设备当前处于 Sahara 还是 Firehose 模式
    /// </summary>
    public class DeviceStateDetector
    {
        private readonly SerialPortManager _port;
        private readonly Action<string>? _log;
        
        // Sahara Hello 包特征
        private static readonly byte[] SAHARA_HELLO_SIGNATURE = { 0x01, 0x00, 0x00, 0x00 }; // Command = 0x01 (Hello)
        
        // Firehose XML 响应特征
        private static readonly byte[] FIREHOSE_XML_START = Encoding.UTF8.GetBytes("<?xml");
        private static readonly byte[] FIREHOSE_RESPONSE_START = Encoding.UTF8.GetBytes("<response");
        private static readonly byte[] FIREHOSE_DATA_START = Encoding.UTF8.GetBytes("<data>");
        private static readonly byte[] FIREHOSE_LOG_START = Encoding.UTF8.GetBytes("<log");

        public DeviceStateDetector(SerialPortManager port, Action<string>? log = null)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _log = log;
        }

        /// <summary>
        /// 检测设备当前状态 (非破坏性检测)
        /// </summary>
        public async Task<DeviceStateInfo> DetectStateAsync(CancellationToken ct = default)
        {
            var result = new DeviceStateInfo();
            
            try
            {
                if (!_port.IsOpen)
                {
                    result.State = DeviceProtocolState.PortError;
                    result.Description = "端口未打开";
                    result.SuggestedAction = "请先打开端口";
                    return result;
                }

                result.State = DeviceProtocolState.PortOpened;
                _log?.Invoke("[状态检测] 开始检测设备协议状态...");

                // 步骤 1: 检查缓冲区是否有数据 (可能是 Sahara Hello)
                int bytesAvailable = _port.BytesToRead;
                
                if (bytesAvailable >= 8)
                {
                    _log?.Invoke($"[状态检测] 缓冲区有 {bytesAvailable} 字节数据，尝试解析...");
                    
                    // 读取数据
                    byte[] buffer = new byte[Math.Min(bytesAvailable, 512)];
                    int read = _port.Read(buffer, 0, buffer.Length);
                    
                    if (read >= 8)
                    {
                        var detected = AnalyzeBuffer(buffer, read, result);
                        if (detected) return result;
                    }
                }

                // 步骤 2: 发送探测包
                _log?.Invoke("[状态检测] 发送探测包...");
                
                // 2.1 尝试 Firehose NOP 命令
                if (await TryFirehoseNopAsync(result, ct))
                {
                    return result;
                }
                
                // 2.2 尝试触发 Sahara Hello (发送空数据或等待)
                if (await TrySaharaHelloAsync(result, ct))
                {
                    return result;
                }

                // 步骤 3: 无响应
                result.State = DeviceProtocolState.NoResponse;
                result.Description = "设备无响应";
                result.SuggestedAction = "请检查设备是否正确连接，或尝试重新进入 EDL 模式";
                result.CanProceed = false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[状态检测] 异常: {ex.Message}");
                result.State = DeviceProtocolState.PortError;
                result.Description = $"检测失败: {ex.Message}";
                result.SuggestedAction = "请重新连接设备";
                result.CanProceed = false;
            }
            
            return result;
        }

        /// <summary>
        /// 分析缓冲区数据
        /// </summary>
        private bool AnalyzeBuffer(byte[] buffer, int length, DeviceStateInfo result)
        {
            // 检查是否是 Sahara Hello 包
            if (length >= 48 && IsSaharaHello(buffer))
            {
                ParseSaharaHello(buffer, result);
                return true;
            }
            
            // 检查是否是 Firehose XML 响应
            if (IsFirehoseResponse(buffer, length))
            {
                ParseFirehoseResponse(buffer, length, result);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 检查是否是 Sahara Hello 包
        /// </summary>
        private bool IsSaharaHello(byte[] buffer)
        {
            if (buffer.Length < 8) return false;
            
            // Sahara Hello 包: Command(4) + Length(4) + Version(4) + ...
            uint command = BitConverter.ToUInt32(buffer, 0);
            uint length = BitConverter.ToUInt32(buffer, 4);
            
            // Hello 命令 = 0x01, 长度 = 48
            return command == 0x01 && length == 48;
        }

        /// <summary>
        /// 解析 Sahara Hello 包
        /// </summary>
        private void ParseSaharaHello(byte[] buffer, DeviceStateInfo result)
        {
            result.State = DeviceProtocolState.SaharaWaitingLoader;
            
            if (buffer.Length >= 48)
            {
                result.SaharaVersion = BitConverter.ToUInt32(buffer, 8);
                uint versionSupported = BitConverter.ToUInt32(buffer, 12);
                uint maxCmdLen = BitConverter.ToUInt32(buffer, 16);
                result.SaharaMode = BitConverter.ToUInt32(buffer, 20);
                
                result.Supports64Bit = result.SaharaVersion >= 2 && versionSupported >= 2;
                
                string modeStr = result.SaharaMode switch
                {
                    0 => "等待镜像传输",
                    1 => "镜像传输完成",
                    2 => "内存调试",
                    3 => "命令模式",
                    _ => $"未知({result.SaharaMode})"
                };
                
                result.Description = $"Sahara 模式 (版本 {result.SaharaVersion}, {modeStr})";
                result.SuggestedAction = "可以上传 Loader";
                result.CanProceed = true;
                
                _log?.Invoke($"[状态检测] ✓ 检测到 Sahara Hello:");
                _log?.Invoke($"  - 版本: {result.SaharaVersion}");
                _log?.Invoke($"  - 模式: {modeStr}");
                _log?.Invoke($"  - 64位支持: {(result.Supports64Bit ? "是" : "否")}");
            }
        }

        /// <summary>
        /// 检查是否是 Firehose XML 响应
        /// </summary>
        private bool IsFirehoseResponse(byte[] buffer, int length)
        {
            if (length < 5) return false;
            
            // 查找 XML 特征
            string text = Encoding.UTF8.GetString(buffer, 0, Math.Min(length, 200));
            
            return text.Contains("<?xml") || 
                   text.Contains("<response") || 
                   text.Contains("<data>") ||
                   text.Contains("<log ");
        }

        /// <summary>
        /// 解析 Firehose 响应
        /// </summary>
        private void ParseFirehoseResponse(byte[] buffer, int length, DeviceStateInfo result)
        {
            string response = Encoding.UTF8.GetString(buffer, 0, length);
            
            // 检查是否配置成功
            if (response.Contains("value=\"ACK\"") || response.Contains("value=\"NAK\""))
            {
                bool isAck = response.Contains("value=\"ACK\"");
                
                if (isAck)
                {
                    result.State = DeviceProtocolState.FirehoseConfigured;
                    result.Description = "Firehose 已配置";
                    result.SuggestedAction = "可以执行读写操作";
                    result.CanProceed = true;
                }
                else
                {
                    result.State = DeviceProtocolState.FirehoseConfigureFailed;
                    result.Description = "Firehose 配置失败";
                    result.SuggestedAction = "尝试重新配置或重启设备";
                    result.CanProceed = false;
                }
            }
            else if (response.Contains("<log "))
            {
                // 收到日志消息，说明 Firehose 在运行
                result.State = DeviceProtocolState.FirehoseNotConfigured;
                result.Description = "Firehose 运行中 (未配置)";
                result.SuggestedAction = "发送 Configure 命令";
                result.CanProceed = true;
            }
            else
            {
                result.State = DeviceProtocolState.FirehoseNotConfigured;
                result.Description = "Firehose 模式 (状态未知)";
                result.SuggestedAction = "尝试发送 NOP 命令确认状态";
                result.CanProceed = true;
            }
            
            // 尝试提取存储类型
            var memMatch = System.Text.RegularExpressions.Regex.Match(response, @"MemoryName=""(\w+)""");
            if (memMatch.Success)
            {
                result.StorageType = memMatch.Groups[1].Value.ToLower();
            }
            
            // 尝试提取 MaxPayloadSize
            var payloadMatch = System.Text.RegularExpressions.Regex.Match(response, @"MaxPayloadSizeToTargetInBytes=""(\d+)""");
            if (payloadMatch.Success)
            {
                result.MaxPayloadSize = int.Parse(payloadMatch.Groups[1].Value);
            }
            
            _log?.Invoke($"[状态检测] ✓ 检测到 Firehose 响应:");
            _log?.Invoke($"  - 状态: {result.Description}");
            if (!string.IsNullOrEmpty(result.StorageType))
                _log?.Invoke($"  - 存储: {result.StorageType.ToUpper()}");
        }

        /// <summary>
        /// 尝试 Firehose NOP 探测
        /// </summary>
        private async Task<bool> TryFirehoseNopAsync(DeviceStateInfo result, CancellationToken ct)
        {
            try
            {
                // 发送 NOP 命令
                string nop = "<?xml version=\"1.0\" ?><data><nop /></data>";
                byte[] nopBytes = Encoding.UTF8.GetBytes(nop);
                
                _port.Write(nopBytes, 0, nopBytes.Length);
                
                // 等待响应
                await Task.Delay(500, ct);
                
                int available = _port.BytesToRead;
                if (available > 0)
                {
                    byte[] response = new byte[Math.Min(available, 4096)];
                    int read = _port.Read(response, 0, response.Length);
                    
                    if (read > 0)
                    {
                        string text = Encoding.UTF8.GetString(response, 0, read);
                        
                        // 检查是否是 Firehose 响应
                        if (text.Contains("<response") || text.Contains("<log"))
                        {
                            ParseFirehoseResponse(response, read, result);
                            return true;
                        }
                        
                        // 检查是否收到 Sahara 包 (设备可能重启了)
                        if (IsSaharaHello(response))
                        {
                            ParseSaharaHello(response, result);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[状态检测] NOP 探测异常: {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// 尝试等待 Sahara Hello
        /// </summary>
        private async Task<bool> TrySaharaHelloAsync(DeviceStateInfo result, CancellationToken ct)
        {
            try
            {
                _log?.Invoke("[状态检测] 等待 Sahara Hello...");
                
                // 等待设备发送 Hello (最多 3 秒)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000 && !ct.IsCancellationRequested)
                {
                    int available = _port.BytesToRead;
                    if (available >= 48)
                    {
                        byte[] buffer = new byte[available];
                        int read = _port.Read(buffer, 0, buffer.Length);
                        
                        if (read >= 48 && IsSaharaHello(buffer))
                        {
                            ParseSaharaHello(buffer, result);
                            return true;
                        }
                    }
                    
                    await Task.Delay(100, ct);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[状态检测] Sahara 检测异常: {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// 快速检测 (不发送任何命令，仅检查缓冲区)
        /// </summary>
        public DeviceStateInfo QuickDetect()
        {
            var result = new DeviceStateInfo();
            
            try
            {
                if (!_port.IsOpen)
                {
                    result.State = DeviceProtocolState.PortError;
                    result.Description = "端口未打开";
                    return result;
                }

                int available = _port.BytesToRead;
                
                if (available == 0)
                {
                    result.State = DeviceProtocolState.PortOpened;
                    result.Description = "端口已打开，无数据";
                    result.SuggestedAction = "等待设备响应或发送探测包";
                    return result;
                }
                
                if (available >= 48)
                {
                    // 可能是 Sahara Hello
                    result.State = DeviceProtocolState.SaharaWaitingLoader;
                    result.Description = $"检测到 {available} 字节数据 (可能是 Sahara Hello)";
                    result.SuggestedAction = "读取并解析数据";
                    result.CanProceed = true;
                }
                else if (available > 0)
                {
                    result.State = DeviceProtocolState.FirehoseNotConfigured;
                    result.Description = $"检测到 {available} 字节数据 (可能是 Firehose 日志)";
                    result.SuggestedAction = "读取并解析数据";
                    result.CanProceed = true;
                }
            }
            catch (Exception ex)
            {
                result.State = DeviceProtocolState.PortError;
                result.Description = ex.Message;
            }
            
            return result;
        }

        /// <summary>
        /// 获取状态显示文本
        /// </summary>
        public static string GetStateDisplayText(DeviceProtocolState state)
        {
            return state switch
            {
                DeviceProtocolState.Unknown => "❓ 未知",
                DeviceProtocolState.PortOpened => "🔌 端口已打开",
                DeviceProtocolState.SaharaWaitingLoader => "📤 Sahara - 等待 Loader",
                DeviceProtocolState.SaharaTransferring => "⏳ Sahara - 传输中",
                DeviceProtocolState.SaharaComplete => "✅ Sahara - 传输完成",
                DeviceProtocolState.FirehoseNotConfigured => "🔧 Firehose - 未配置",
                DeviceProtocolState.FirehoseConfigured => "✅ Firehose - 已配置",
                DeviceProtocolState.FirehoseConfigureFailed => "❌ Firehose - 配置失败",
                DeviceProtocolState.FirehoseAuthenticated => "🔐 Firehose - 已认证",
                DeviceProtocolState.NoResponse => "⚠️ 无响应",
                DeviceProtocolState.PortError => "❌ 端口错误",
                _ => state.ToString()
            };
        }

        /// <summary>
        /// 获取状态颜色 (用于 UI)
        /// </summary>
        public static string GetStateColor(DeviceProtocolState state)
        {
            return state switch
            {
                DeviceProtocolState.SaharaWaitingLoader => "#FFA500",      // 橙色
                DeviceProtocolState.SaharaTransferring => "#1E90FF",       // 蓝色
                DeviceProtocolState.SaharaComplete => "#32CD32",           // 绿色
                DeviceProtocolState.FirehoseNotConfigured => "#FFD700",    // 金色
                DeviceProtocolState.FirehoseConfigured => "#32CD32",       // 绿色
                DeviceProtocolState.FirehoseConfigureFailed => "#FF4500",  // 红色
                DeviceProtocolState.FirehoseAuthenticated => "#00CED1",    // 青色
                DeviceProtocolState.NoResponse => "#FF6347",               // 番茄红
                DeviceProtocolState.PortError => "#DC143C",                // 深红
                _ => "#808080"                                             // 灰色
            };
        }
    }
}
