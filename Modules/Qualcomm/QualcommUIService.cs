// ============================================================================
// MultiFlash TOOL - Qualcomm UI Service
// 高通 UI 服务 | Qualcomm UIサービス | 퀄컴 UI 서비스
// ============================================================================
// [EN] UI service layer for Qualcomm EDL flash operations
//      Connects WPF interface with Sahara/Firehose protocols
// [中文] 高通 EDL 刷机操作的 UI 服务层
//       连接 WPF 界面与 Sahara/Firehose 协议
// [日本語] Qualcomm EDLフラッシュ操作用UIサービスレイヤー
//         WPFインターフェースとSahara/Firehoseプロトコルを接続
// [한국어] 퀄컴 EDL 플래시 작업을 위한 UI 서비스 레이어
//         WPF 인터페이스와 Sahara/Firehose 프로토콜 연결
// [Español] Capa de servicio UI para operaciones de flash Qualcomm EDL
//           Conecta la interfaz WPF con protocolos Sahara/Firehose
// [Русский] Уровень сервиса UI для операций прошивки Qualcomm EDL
//           Соединяет интерфейс WPF с протоколами Sahara/Firehose
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using tools.Modules.Common;
using tools.Modules.Qualcomm.Authentication;
using tools.Modules.Qualcomm.Services;
using tools.Modules.Qualcomm.Strategies;
using tools.Modules.Qualcomm.SuperDef;
using tools.Utils;

namespace tools.Modules.Qualcomm
{
    /// <summary>
    /// Authentication Type Enumeration
    /// 认证类型枚举 | 認証タイプ列挙 | 인증 유형 열거
    /// </summary>
    public enum AuthType
    {
        Standard,   // 标准 (无验证)
        Vip,        // Oppo/Realme VIP
        Xiaomi,     // 小米 EDL 签名认证
        OnePlus,    // OnePlus 签名认证
        Nothing     // Nothing Phone 认证
    }

    /// <summary>
    /// 高通 UI 服务 - 连接 UI 与底层服务
    /// </summary>
    public class QualcommUIService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, string> _log;
        private readonly Action<double, string> _updateProgress;
        private readonly Action<string> _updateStatus;
        private readonly Action<QcDeviceInfo> _updateDeviceInfo;
        
        // 日志格式化器
        private readonly LogFormatter _fmt;

        // 核心组件
        private SerialPortManager? _portManager;
        private SaharaClient? _sahara;
        private FirehoseClient? _firehose;
        private FlashTaskExecutor? _executor;
        private IDeviceStrategy _currentStrategy;

        // 设备监听
        private DeviceWatcher? _deviceWatcher;

        // 取消令牌
        private CancellationTokenSource? _cts;

        // 状态
        public bool IsConnected => _firehose != null && _portManager?.IsOpen == true;
        public bool IsOperating { get; private set; }
        public string? CurrentPort { get; private set; }
        public AuthType CurrentAuthType { get; set; } = AuthType.Standard;

        // 分区列表
        public ObservableCollection<PartitionInfo> Partitions { get; } = new();

        // 设备信息
        public QcDeviceInfo? CurrentDevice { get; private set; }

        // 事件
        public event Action<string>? DeviceArrived;
        public event Action? DeviceRemoved;
        public event Action<List<PartitionInfo>>? PartitionsLoaded;
        public event Action<long, long>? TransferProgress;  // (current, total) 字节传输进度

        public QualcommUIService(
            Dispatcher dispatcher,
            Action<string, string> log,
            Action<double, string> updateProgress,
            Action<string> updateStatus,
            Action<QcDeviceInfo> updateDeviceInfo)
        {
            _dispatcher = dispatcher;
            _log = log;
            _updateProgress = updateProgress;
            _updateStatus = updateStatus;
            _updateDeviceInfo = updateDeviceInfo;
            _currentStrategy = new StandardDeviceStrategy();
            
            // 初始化日志格式化器
            _fmt = new LogFormatter(log);
        }

        /// <summary>
        /// 启动设备监听
        /// </summary>
        public void StartDeviceWatcher()
        {
            // 输出启动横幅
            _fmt.Header("Qualcomm EDL Protocol", DateTime.Now.ToString("yyyy.MM.dd"), "tools");
            
            _deviceWatcher = new DeviceWatcher();
            _deviceWatcher.DeviceArrived += OnDeviceArrived;
            _deviceWatcher.DeviceRemoved += OnDeviceRemoved;
            _deviceWatcher.Start();
            _fmt.Success("设备监听已启动");
        }

        private void OnDeviceArrived(object? sender, DeviceInfo device)
        {
            if (device.Type != DeviceType.Qualcomm9008) return;
            
            _dispatcher.Invoke(() =>
            {
                CurrentPort = device.PortName;
                DeviceArrived?.Invoke(device.PortName);
                
                // 显示设备信息和端口状态
                var status = device.IsPortAvailable ? "✓ 可用" : device.PortStatus;
                _fmt.Status($"检测到 9008 设备: {device.PortName} ({status})", device.IsPortAvailable);
                
                if (!string.IsNullOrEmpty(device.VID) && !string.IsNullOrEmpty(device.PID))
                {
                    _log($"   └─ VID:{device.VID} PID:{device.PID}", LogColors.Debug);
                }
            });
        }

        private void OnDeviceRemoved(object? sender, DeviceInfo device)
        {
            _dispatcher.Invoke(() =>
            {
                if (CurrentPort == device.PortName)
                {
                    Disconnect();
                    DeviceRemoved?.Invoke();
                    _fmt.Error($"设备已断开: {device.PortName}");
                }
            });
        }

        /// <summary>
        /// 获取策略对象
        /// </summary>
        private IDeviceStrategy GetDeviceStrategy(AuthType type)
        {
            return type switch
            {
                AuthType.Vip => new OppoVipDeviceStrategy(),
                AuthType.Xiaomi => new XiaomiDeviceStrategy(),
                AuthType.OnePlus => new OnePlusDeviceStrategy(),
                AuthType.Nothing => new NothingDeviceStrategy(),
                _ => new StandardDeviceStrategy()
            };
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, string loaderPath, string storageType = "ufs", 
            string? digestPath = null, string? signaturePath = null)
        {
            if (IsOperating) return false;
            if (string.IsNullOrEmpty(portName))
            {
                _fmt.Error("端口名称为空");
                return false;
            }
            if (string.IsNullOrEmpty(loaderPath))
            {
                _fmt.Error("Loader 路径为空");
                return false;
            }
            
            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("连接设备");
                _updateStatus("正在连接...");
                _updateProgress(10, "打开串口...");

                // 1. 打开串口 (⚠️ 不清空缓冲区，保留设备发送的 Hello 包)
                _portManager = new SerialPortManager();
                bool portOpened = false;
                try
                {
                    _fmt.BeginOperation($"打开端口 {portName}");
                    portOpened = await _portManager.OpenAsync(portName, 3, discardBuffer: false, _cts.Token);
                }
                catch (Exception ex)
                {
                    _fmt.Error($"打开端口异常: {ex.Message}");
                    return false;
                }
                
                if (!portOpened)
                {
                    _fmt.Status($"打开端口 {portName}", false);
                    return false;
                }

                CurrentPort = portName;
                _fmt.Status($"打开端口 {portName}", true);

                // ⚠️ 参考串口监控数据：官方工具先发送 Reset (0x13) 触发 Hello
                int initialBytes = _portManager.BytesToRead;
                _fmt.Debug($"端口打开后缓冲区: {initialBytes} 字节");
                
                // 如果缓冲区为空，发送 ResetStateMachine (0x13) 触发设备发送 Hello
                if (initialBytes == 0)
                {
                    _fmt.BeginOperation("发送 ResetStateMachine (0x13) 触发 Hello");
                    
                    // 发送 Reset 命令: [CmdId:4][Length:4] = [0x13][0x08]
                    byte[] resetCmd = new byte[8];
                    BitConverter.GetBytes((uint)0x13).CopyTo(resetCmd, 0);  // ResetStateMachine = 0x13
                    BitConverter.GetBytes((uint)8).CopyTo(resetCmd, 4);     // Length = 8
                    _portManager.Write(resetCmd, 0, 8);
                    
                    // 等待设备响应 (参考串口数据: 1ms 内就收到了)
                    await Task.Delay(100, _cts.Token);
                    
                    int afterResetBytes = _portManager.BytesToRead;
                    _fmt.Debug($"Reset 后缓冲区: {afterResetBytes} 字节");
                }
                else
                {
                    _fmt.Debug($"缓冲区已有数据: {initialBytes} 字节");
                }
                
                // 检查是否有 Hello 数据
                int availableBytes = _portManager.BytesToRead;
                if (availableBytes >= 8)
                {
                    _fmt.Debug("尝试读取 Hello 数据...");
                    try
                    {
                        byte[] initialData = new byte[Math.Min(availableBytes, 256)];
                        int read = _portManager.Read(initialData, 0, initialData.Length);
                        if (read >= 8)
                        {
                            uint cmdId = BitConverter.ToUInt32(initialData, 0);
                            uint pktLen = BitConverter.ToUInt32(initialData, 4);
                            _fmt.Debug($"读取到 {read} 字节: Cmd=0x{cmdId:X2}, Len={pktLen}");
                            
                            // 检查是否是 Sahara Hello (0x01)
                            if (cmdId == 0x01 && pktLen == 0x30)
                            {
                                _fmt.Status("收到 Sahara Hello 包", true);
                                _pendingHelloData = new byte[read];
                                Array.Copy(initialData, 0, _pendingHelloData, 0, read);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _fmt.Warning($"读取异常: {ex.Message}");
                    }
                }

                // 2. 检测设备当前模式
                _updateProgress(15, "检测设备状态...");
                string deviceMode = await DetectDeviceModeAsync();
                
                if (deviceMode == "firehose")
                {
                    _fmt.Status("设备已在 Firehose 模式，跳过 Sahara 握手", true);
                }
                else if (deviceMode == "sahara")
                {
                    // 3. Sahara 握手 + 上传 Loader
                    _fmt.SubSection("Sahara 协议");
                    _updateProgress(20, "Sahara 握手...");
                    _sahara = new SaharaClient(_portManager, s => _log(s, LogColors.Sahara));
                    
                    // 传递已读取的 Hello 数据 (如果有)
                    if (_pendingHelloData != null)
                    {
                        _sahara.SetPendingHelloData(_pendingHelloData);
                        _pendingHelloData = null;
                    }

                    if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    {
                        _updateProgress(40, "上传 Loader...");
                        _fmt.BeginOperation($"上传 Loader: {Path.GetFileName(loaderPath)}");

                        try
                        {
                            if (!await _sahara.HandshakeAndUploadAsync(loaderPath, _cts.Token))
                            {
                                _fmt.Status("Sahara 握手", false);
                                _fmt.Info("请检查: 1.设备是否真正进入9008模式 2.驱动是否正确 3.USB线是否支持数据");
                                return false;
                            }
                            _fmt.Status("Sahara 握手", true);
                        }
                        catch (TimeoutException tex)
                        {
                            _fmt.Error($"Sahara 通信超时: {tex.Message}");
                            _fmt.Info("设备可能未响应，请重新插拔设备");
                            return false;
                        }
                        catch (IOException iex)
                        {
                            _fmt.Error($"串口IO错误: {iex.Message}");
                            _fmt.Info("端口可能被占用或设备已断开");
                            return false;
                        }
                        catch (Exception ex)
                        {
                            _fmt.Error($"Sahara 握手异常: {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        _fmt.Error("请先选择 Loader 文件");
                        return false;
                    }
                }
                else // deviceMode == "error"
                {
                    _fmt.Error("设备检测失败，请重新插拔设备");
                    return false;
                }

                // 4. 等待 Firehose 启动
                _fmt.SubSection("Firehose 协议");
                _updateProgress(60, "等待 Firehose...");
                _firehose = new FirehoseClient(_portManager, s => _log(s, LogColors.Firehose));

                // 传递 Sahara 读取的芯片信息 (如果有)
                if (_sahara != null)
                {
                    _firehose.ChipSerial = _sahara.ChipSerial;
                    _firehose.ChipHwId = _sahara.ChipHwId;
                    _firehose.ChipPkHash = _sahara.ChipPkHash;
                    
                    // 输出芯片信息
                    _fmt.SubSection("设备信息");
                    _log($" • Chip Serial       : {_sahara.ChipSerial}", LogColors.Value);
                    _log($" • Chip HW ID        : {_sahara.ChipHwId}", LogColors.Value);
                    if (!string.IsNullOrEmpty(_sahara.ChipPkHash))
                        _log($" • OEM PK Hash       : {_sahara.ChipPkHash.Substring(0, Math.Min(32, _sahara.ChipPkHash.Length))}...", LogColors.Value);
                }

                // ⚠️ 关键：VIP 验证必须在 Configure 之前执行！
                // 参考官方工具流程：Sahara → Digest → Verify → Signature → SHA256Init → Configure
                _currentStrategy = GetDeviceStrategy(CurrentAuthType);
                
                // 5. 如果是 VIP 模式，先执行 VIP 验证
                if (CurrentAuthType == AuthType.Vip && 
                    !string.IsNullOrEmpty(digestPath) && !string.IsNullOrEmpty(signaturePath))
                {
                    _fmt.SubSection("VIP 认证");
                    _updateProgress(65, "VIP 验证中...");
                    _log($" • Digest            : {Path.GetFileName(digestPath)}", LogColors.Value);
                    _log($" • Signature         : {Path.GetFileName(signaturePath)}", LogColors.Value);
                    
                    // 等待 Firehose 就绪
                    await Task.Delay(500, _cts.Token);
                    
                    // 执行 VIP 验证 (在 Configure 之前)
                    _fmt.BeginOperation("执行 VIP 验证");
                    bool vipResult = await _firehose.PerformVipAuthAsync(digestPath, signaturePath, _cts.Token);
                    _fmt.Status("VIP 验证", vipResult);
                    if (!vipResult)
                    {
                        _fmt.Warning("VIP 验证可能失败，继续尝试配置...");
                    }
                }

                // 5.5. 小米认证必须在 Configure 之前执行
                if (CurrentAuthType == AuthType.Xiaomi)
                {
                    _updateProgress(70, "小米认证中...");
                    _fmt.BeginOperation($"执行 {_currentStrategy.Name} 认证");
                    
                    var xiaomiAuthResult = await _currentStrategy.AuthenticateAsync(
                        _firehose, loaderPath,
                        s => _log(s, LogColors.Debug),
                        null, digestPath, signaturePath, _cts.Token);

                    _fmt.Status($"{_currentStrategy.Name} 认证", xiaomiAuthResult);
                    
                    if (!xiaomiAuthResult)
                    {
                        _fmt.Warning("小米认证失败，设备可能需要官方授权签名");
                    }
                }

                // 6. 配置存储类型 (带重试机制)
                _fmt.BeginOperation($"配置存储类型 {storageType.ToUpper()}");
                _updateProgress(75, $"配置 {storageType.ToUpper()}...");
                bool configured = false;
                int[] delays = { 300, 500, 1000, 1500, 2000 }; // 渐进延时

                for (int i = 0; i < delays.Length && !configured; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    
                    // 等待
                    await Task.Delay(delays[i], _cts.Token);
                    _updateProgress(75 + i * 3, $"尝试配置 ({i + 1}/{delays.Length})...");
                    
                    // 尝试配置
                    try
                    {
                        configured = await _firehose.ConfigureAsync(storageType, null, 0, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _fmt.Warning($"配置异常: {ex.Message}");
                    }
                    
                    if (!configured && i < delays.Length - 1)
                    {
                        _fmt.Debug($"配置超时，增加等待时间重试 ({i + 1}/{delays.Length})...");
                    }
                }

                // 尝试切换存储类型
                if (!configured)
                {
                    string altType = storageType == "ufs" ? "emmc" : "ufs";
                    _fmt.Warning($"{storageType} 配置失败，尝试 {altType}...");
                    
                    _updateProgress(90, $"配置 {altType.ToUpper()}...");
                    try
                    {
                        configured = await _firehose.ConfigureAsync(altType, null, 0, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _fmt.Warning($"{altType} 配置异常: {ex.Message}");
                    }
                }

                if (!configured)
                {
                    _fmt.Status("配置存储类型", false);
                    _fmt.Info("请检查 Loader 是否与设备匹配");
                    return false;
                }
                
                _fmt.Status($"配置存储类型 {storageType.ToUpper()}", true);
                _log($" • Sector Size       : {_firehose.SectorSize}", LogColors.Value);
                _log($" • Max Payload       : {_firehose.MaxPayloadSize / 1024} KB", LogColors.Value);

                // 7. 执行其他认证 (非 VIP/非小米模式 - VIP 和小米已在 Configure 前执行)
                if (CurrentAuthType != AuthType.Vip && CurrentAuthType != AuthType.Xiaomi)
                {
                    _updateProgress(95, "认证中...");
                    _fmt.BeginOperation($"执行 {_currentStrategy.Name} 认证");
                    var authResult = await _currentStrategy.AuthenticateAsync(
                        _firehose, loaderPath,
                        s => _log(s, LogColors.Debug),
                        null, digestPath, signaturePath, _cts.Token);

                    _fmt.Status($"{_currentStrategy.Name} 认证", authResult);
                }

                // 8. 创建执行器
                _executor = new FlashTaskExecutor(_firehose, _currentStrategy,
                    s => _log(s, LogColors.Debug), _firehose.SectorSize);

                // 更新设备信息
                UpdateDeviceInfo();

                _updateProgress(100, "已连接");
                _updateStatus($"已连接 ({storageType.ToUpper()})");
                _fmt.Separator('═', 50);
                _fmt.Success($"设备连接成功 (策略: {_currentStrategy.Name})");

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("连接操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"连接失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 检测设备当前模式
        /// 
        /// ⚠️ 关键发现 (分析串口监控数据)：
        /// 官方工具在打开端口后发送 ResetStateMachine (0x13) 触发设备发送 Hello！
        /// 设备在收到 0x13 后 1ms 内就回复 Hello (0x01)
        /// </summary>
        private async Task<string> DetectDeviceModeAsync()
        {
            if (_portManager == null || !_portManager.IsOpen)
                return "error";

            try
            {
                // 如果已经有预读的 Hello 数据，直接返回
                if (_pendingHelloData != null && _pendingHelloData.Length >= 8)
                {
                    _fmt.Status("使用预读取的 Hello 数据", true);
                    return "sahara";
                }
                
                _fmt.BeginOperation("检测设备模式");
                
                // ⚠️ 参考串口监控数据：先检查缓冲区
                int bytesAvailable = _portManager.BytesToRead;
                _fmt.Debug($"缓冲区: {bytesAvailable} 字节");
                
                // 如果没有数据，发送 Reset 触发 Hello
                if (bytesAvailable == 0)
                {
                    _fmt.Info("发送 ResetStateMachine (0x13)...");
                    
                    byte[] resetCmd = new byte[8];
                    BitConverter.GetBytes((uint)0x13).CopyTo(resetCmd, 0);
                    BitConverter.GetBytes((uint)8).CopyTo(resetCmd, 4);
                    _portManager.Write(resetCmd, 0, 8);
                    
                    // 等待响应
                    await Task.Delay(200, _cts?.Token ?? CancellationToken.None);
                }
                
                // 尝试读取 Hello
                int maxWaitTime = 5000;
                int totalWaitTime = 0;
                int checkInterval = 100;
                
                while (totalWaitTime < maxWaitTime)
                {
                    bytesAvailable = _portManager.BytesToRead;
                    
                    if (bytesAvailable >= 8)
                    {
                        var data = await _portManager.TryReadAnyAsync(256, 500, _cts?.Token ?? CancellationToken.None);
                        
                        if (data != null && data.Length >= 8)
                        {
                            uint cmdId = BitConverter.ToUInt32(data, 0);
                            uint pktLen = BitConverter.ToUInt32(data, 4);
                            
                            _fmt.Debug($"收到数据: Cmd=0x{cmdId:X2}, Len={pktLen}");
                            
                            // 检查是否是 Sahara Hello 包 (0x01)
                            if (cmdId == 0x01)
                            {
                                _fmt.Status("收到 Sahara Hello 包", true);
                                _pendingHelloData = data;
                                return "sahara";
                            }
                            
                            // 检查是否是 Firehose XML 响应
                            try
                            {
                                string str = System.Text.Encoding.UTF8.GetString(data);
                                if (str.Contains("<?xml") || str.Contains("<response") || str.Contains("<log"))
                                {
                                    _fmt.Status("设备已在 Firehose 模式", true);
                                    return "firehose";
                                }
                            }
                            catch { }
                            
                            // 其他数据，交给 Sahara 处理
                            _pendingHelloData = data;
                            return "sahara";
                        }
                    }
                    
                    await Task.Delay(checkInterval, _cts?.Token ?? CancellationToken.None);
                    totalWaitTime += checkInterval;
                    
                    if (totalWaitTime % 1000 == 0)
                    {
                        _fmt.Debug($"等待响应... ({totalWaitTime / 1000}s)");
                    }
                }
                
                // 超时，尝试 Sahara 握手
                _fmt.Warning("未收到响应，尝试 Sahara 握手...");
                return "sahara";
            }
            catch (Exception ex)
            {
                _fmt.Warning($"检测异常: {ex.Message}");
                return "error";
            }
        }
        
        // 缓存的 Hello 数据 (如果在检测阶段已读取)
        private byte[]? _pendingHelloData = null;

        /// <summary>
        /// 更新设备信息到 UI
        /// </summary>
        private void UpdateDeviceInfo()
        {
            if (_firehose == null) return;

            CurrentDevice = new QcDeviceInfo
            {
                Port = CurrentPort ?? "---",
                Serial = _firehose.ChipSerial,
                HwId = _firehose.ChipHwId,
                Vendor = QualcommDatabase.GetVendorByPkHash(_firehose.ChipPkHash),
                ChipName = !string.IsNullOrEmpty(_firehose.ChipHwId)
                    ? QualcommDatabase.GetChipName(Convert.ToUInt32(_firehose.ChipHwId.Substring(0, Math.Min(8, _firehose.ChipHwId.Length)), 16))
                    : "Unknown"
            };

            _dispatcher.Invoke(() => _updateDeviceInfo(CurrentDevice));
        }

        /// <summary>
        /// 读取 GPT 分区表
        /// </summary>
        public async Task<bool> ReadGptAsync()
        {
            if (!IsConnected || IsOperating)
            {
                _log("[错误] 设备未连接或正在操作中", "#EF4444");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("读取分区表");
                _updateStatus("读取分区表...");
                _updateProgress(10, "读取 GPT...");
                _fmt.BeginOperation("读取 GPT 分区表");

                var partitions = await _currentStrategy.ReadGptAsync(_firehose!, _cts.Token,
                    s => _log(s, LogColors.Debug));

                _dispatcher.Invoke(() =>
                {
                    Partitions.Clear();
                    foreach (var p in partitions)
                        Partitions.Add(p);

                    PartitionsLoaded?.Invoke(partitions);
                });

                _updateProgress(100, "完成");
                _updateStatus($"已读取 {partitions.Count} 个分区");
                _fmt.Status($"读取分区表 ({partitions.Count} 个分区)", true);

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("GPT 读取已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"GPT 读取失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 读取设备详细信息 (型号/OTA版本/IMEI/解锁状态等)
        /// 自动检测厂商并使用对应的解析策略
        /// </summary>
        /// <param name="readFullInfo">是否读取完整信息 (包含IMEI等，较慢)</param>
        /// <returns>设备详细信息，失败返回 null</returns>
        public async Task<DeviceDetailInfo?> ReadDeviceInfoAsync(bool readFullInfo = true)
        {
            if (!IsConnected || IsOperating)
            {
                _log("[错误] 设备未连接或正在操作中", "#EF4444");
                return null;
            }

            if (Partitions.Count == 0)
            {
                _log("[错误] 请先读取 GPT 分区表", "#EF4444");
                return null;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("读取设备信息");
                _updateStatus("读取设备信息...");
                _updateProgress(10, "分析分区表...");

                // 检测厂商
                string detectedVendor = DetectVendorFromPartitions();
                _log($"[DevInfo] 检测到厂商: {detectedVendor}", "#3B82F6");

                _updateProgress(20, $"读取 {detectedVendor} 设备信息...");

                // 创建 DeviceInfoReader
                var reader = new DeviceInfoReader(
                    _firehose,
                    Partitions.ToList(),
                    s => _log(s, "#6B7280")
                );

                // 读取设备信息
                var info = await reader.ReadFromDeviceAsync(
                    loaderPath: null,
                    chipPlatform: CurrentDevice?.ChipName,
                    oemVendor: detectedVendor,
                    readFullInfo: readFullInfo,
                    ct: _cts.Token
                );

                if (info != null && info.HasData)
                {
                    _updateProgress(100, "完成");
                    _fmt.Status("读取设备信息", true);
                    
                    // 更新 CurrentDevice 信息，供 UI 显示
                    if (CurrentDevice != null)
                    {
                        CurrentDevice.Model = info.Model ?? "";
                        CurrentDevice.Manufacturer = info.Manufacturer ?? info.Brand ?? "";
                        CurrentDevice.OtaVersion = info.OtaVersion ?? "";
                        
                        // 触发 UI 更新
                        _updateDeviceInfo(CurrentDevice);
                    }
                    
                    // 输出关键信息
                    _fmt.SubSection("设备详情");
                    if (!string.IsNullOrEmpty(info.Model))
                        _log($" • 型号          : {info.Model}", "#10B981");
                    if (!string.IsNullOrEmpty(info.MarketName))
                        _log($" • 市场名        : {info.MarketName}", "#10B981");
                    if (!string.IsNullOrEmpty(info.Brand))
                        _log($" • 品牌          : {info.Brand}", "#10B981");
                    if (!string.IsNullOrEmpty(info.OtaVersion))
                        _log($" • OTA 版本      : {info.OtaVersion}", "#10B981");
                    if (!string.IsNullOrEmpty(info.AndroidVersion))
                        _log($" • Android       : {info.AndroidVersion}", "#10B981");
                    if (!string.IsNullOrEmpty(info.UnlockState))
                        _log($" • 解锁状态      : {info.UnlockState}", info.UnlockState.Contains("Unlock") ? "#10B981" : "#EF4444");
                    if (!string.IsNullOrEmpty(info.IMEI))
                        _log($" • IMEI          : {info.IMEI}", "#10B981");
                    if (!string.IsNullOrEmpty(info.Region))
                        _log($" • 地区          : {info.Region}", "#10B981");

                    return info;
                }
                else
                {
                    _fmt.Warning("未能读取到有效的设备信息");
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("设备信息读取已取消");
                return null;
            }
            catch (Exception ex)
            {
                _fmt.Error($"设备信息读取失败: {ex.Message}");
                return null;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 根据分区表检测设备厂商
        /// </summary>
        private string DetectVendorFromPartitions()
        {
            if (Partitions.Count == 0) return "Unknown";

            // Lenovo/Motorola 特征分区
            if (Partitions.Any(p => p.Name.Equals("proinfo", StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals("lenovolock", StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals("lenovocust", StringComparison.OrdinalIgnoreCase)))
                return "Lenovo";

            // Xiaomi 特征分区
            if (Partitions.Any(p => p.Name.Equals("cust", StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals("exaid", StringComparison.OrdinalIgnoreCase)))
                return "Xiaomi";

            // OPPO/Realme/OnePlus 特征分区
            if (Partitions.Any(p => p.Name.Equals("my_manifest", StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals("oplusreserve", StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals("my_region", StringComparison.OrdinalIgnoreCase)))
                return "OPPO";

            // Vivo 特征分区
            if (Partitions.Any(p => p.Name.Equals("vivo", StringComparison.OrdinalIgnoreCase)))
                return "Vivo";

            // Samsung 特征分区
            if (Partitions.Any(p => p.Name.Equals("param", StringComparison.OrdinalIgnoreCase) &&
                                   Partitions.Any(q => q.Name.Equals("efs", StringComparison.OrdinalIgnoreCase))))
                return "Samsung";

            return "Unknown";
        }

        /// <summary>
        /// 备份选中分区
        /// </summary>
        public async Task<bool> BackupPartitionsAsync(List<PartitionInfo> partitions, string outputDir)
        {
            if (!IsConnected || IsOperating || _executor == null)
            {
                _fmt.Error("设备未连接或正在操作中");
                return false;
            }

            if (partitions.Count == 0)
            {
                _fmt.Error("请先选择要备份的分区");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("备份分区");
                _updateStatus("备份中...");
                _fmt.BeginOperation($"备份 {partitions.Count} 个分区到 {outputDir}");

                // 转换为 FlashPartitionInfo
                var tasks = partitions.Select(p => new FlashPartitionInfo(
                    p.Lun.ToString(),
                    p.Name,
                    p.StartSector.ToString(),
                    p.NumSectors,
                    $"{p.Name}.bin"
                )).ToList();

                _executor.ProgressChanged += OnProgressChanged;
                _executor.StatusChanged += OnStatusChanged;

                await _executor.ExecuteReadTasksAsync(tasks, outputDir, _cts.Token);

                _updateProgress(100, "备份完成");
                _fmt.Status("分区备份", true);

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("备份已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"备份失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (_executor != null)
                {
                    _executor.ProgressChanged -= OnProgressChanged;
                    _executor.StatusChanged -= OnStatusChanged;
                }
                IsOperating = false;
            }
        }

        /// <summary>
        /// 擦除选中分区
        /// </summary>
        public async Task<bool> ErasePartitionsAsync(List<PartitionInfo> partitions, bool protectLun5)
        {
            if (!IsConnected || IsOperating || _executor == null)
            {
                _fmt.Error("设备未连接或正在操作中");
                return false;
            }

            if (partitions.Count == 0)
            {
                _fmt.Error("请先选择要擦除的分区");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("擦除分区");
                _updateStatus("擦除中...");
                _fmt.Warning($"⚠️ 开始擦除 {partitions.Count} 个分区");

                var tasks = partitions.Select(p => new FlashPartitionInfo(
                    p.Lun.ToString(),
                    p.Name,
                    p.StartSector.ToString(),
                    p.NumSectors
                )).ToList();

                _executor.ProgressChanged += OnProgressChanged;
                _executor.StatusChanged += OnStatusChanged;

                await _executor.ExecuteEraseTasksAsync(tasks, protectLun5, _cts.Token);

                _updateProgress(100, "擦除完成");
                _fmt.Status("分区擦除", true);

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("擦除已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"擦除失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (_executor != null)
                {
                    _executor.ProgressChanged -= OnProgressChanged;
                    _executor.StatusChanged -= OnStatusChanged;
                }
                IsOperating = false;
            }
        }

        /// <summary>
        /// 刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionsAsync(List<FlashPartitionInfo> tasks, bool protectLun5, List<string>? patchFiles = null)
        {
            if (!IsConnected || IsOperating || _executor == null)
            {
                _fmt.Error("设备未连接或正在操作中");
                return false;
            }

            if (tasks.Count == 0)
            {
                _fmt.Error("没有要刷写的分区");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("刷写分区");
                _updateStatus("刷写中...");
                _fmt.BeginOperation($"刷写 {tasks.Count} 个分区");

                _executor.ProgressChanged += OnProgressChanged;
                _executor.StatusChanged += OnStatusChanged;

                await _executor.ExecuteFlashTasksAsync(tasks, protectLun5, patchFiles, _cts.Token);

                _updateProgress(100, "刷写完成");
                _fmt.Separator('═', 50);
                _fmt.Success("分区刷写完成");

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("刷写已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"刷写失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (_executor != null)
                {
                    _executor.ProgressChanged -= OnProgressChanged;
                    _executor.StatusChanged -= OnStatusChanged;
                }
                IsOperating = false;
            }
        }

        /// <summary>
        /// Super 直刷 (传统模式)
        /// </summary>
        public async Task<bool> FlashSuperDirectAsync(string jsonPath, string imageDir, bool protectLun5)
        {
            if (!IsConnected || IsOperating || _executor == null)
            {
                _fmt.Error("设备未连接或正在操作中");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("Super 直刷");
                _updateStatus("Super 直刷...");
                _fmt.BeginOperation("Super 分区直刷");

                _executor.ProgressChanged += OnProgressChanged;
                _executor.StatusChanged += OnStatusChanged;

                await _executor.FlashSuperNoMergeAsync(jsonPath, imageDir, protectLun5, _cts.Token);

                _updateProgress(100, "Super 刷写完成");
                _fmt.Separator('═', 50);
                _fmt.Success("Super 分区直刷完成");

                return true;
            }
            catch (OperationCanceledException)
            {
                _fmt.Warning("Super 刷写已取消");
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"Super 刷写失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (_executor != null)
                {
                    _executor.ProgressChanged -= OnProgressChanged;
                    _executor.StatusChanged -= OnStatusChanged;
                }
                IsOperating = false;
            }
        }

        #region Super Meta 模式刷写 (OPLUS/Realme)

        private SuperFlasher? _superFlasher;

        /// <summary>
        /// 检查固件是否支持 Super Meta 模式
        /// </summary>
        public bool IsSuperMetaSupported(string firmwareDir, out string? nvId)
        {
            nvId = null;
            if (_firehose == null) return false;

            _superFlasher ??= new SuperFlasher(_firehose,
                s => _log(s, "#8B5CF6"),
                OnSuperFlashProgress);

            return _superFlasher.IsSuperMetaSupported(firmwareDir, out nvId);
        }

        /// <summary>
        /// 获取 Super 分区信息摘要
        /// </summary>
        public string? GetSuperMetaSummary(string firmwareDir, string? nvId = null)
        {
            if (_firehose == null) return null;

            _superFlasher ??= new SuperFlasher(_firehose,
                s => _log(s, "#8B5CF6"),
                OnSuperFlashProgress);

            return _superFlasher.GetSuperSummary(firmwareDir, nvId);
        }

        /// <summary>
        /// Super Meta 模式刷写 (OPLUS/Realme 方式)
        /// </summary>
        /// <param name="firmwareDir">固件目录 (包含 META/super_def.json)</param>
        /// <param name="nvId">NV ID (如 10010111)</param>
        /// <param name="flashSlotB">是否同时刷写 B 槽位</param>
        public async Task<bool> FlashSuperMetaAsync(string firmwareDir, string? nvId = null, bool flashSlotB = false)
        {
            if (!IsConnected || IsOperating || _firehose == null)
            {
                _log("[错误] 设备未连接或正在操作中", "#EF4444");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _updateStatus("Super Meta 模式刷写...");
                _log("[Super Meta] 🚀 启动 Super Meta 模式刷写...", "#8B5CF6");

                // 检测是否支持
                if (!IsSuperMetaSupported(firmwareDir, out var detectedNvId))
                {
                    _log("[Super Meta] ❌ 固件不支持 Super Meta 模式", "#EF4444");
                    return false;
                }

                nvId ??= detectedNvId;
                _log($"[Super Meta] 📦 NV ID: {nvId}", "#8B5CF6");

                // 显示摘要
                var summary = GetSuperMetaSummary(firmwareDir, nvId);
                if (!string.IsNullOrEmpty(summary))
                {
                    _log($"[Super Meta] 📋 {summary}", "#8B5CF6");
                }

                // 执行刷写
                _superFlasher ??= new SuperFlasher(_firehose,
                    s => _log(s, "#8B5CF6"),
                    OnSuperFlashProgress);

                var result = await _superFlasher.FlashSuperAsync(firmwareDir, nvId, flashSlotB, _cts.Token);

                if (result.Success)
                {
                    _updateProgress(100, "Super Meta 刷写完成");
                    _log($"[Super Meta] ✅ 刷写完成: {result.FlashedPartitions}/{result.TotalPartitions} 个分区", "#059669");
                    return true;
                }
                else
                {
                    _log($"[Super Meta] ⚠️ 部分失败: {result.FailedPartitions} 个分区刷写失败", "#D97706");
                    if (result.FailedPartitionNames.Count > 0)
                    {
                        _log($"[Super Meta] 失败分区: {string.Join(", ", result.FailedPartitionNames)}", "#EF4444");
                    }
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _log("[取消] Super Meta 刷写已取消", "#D97706");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[错误] Super Meta 刷写失败: {ex.Message}", "#EF4444");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// Super 刷写进度回调
        /// </summary>
        private void OnSuperFlashProgress(SuperFlashProgress progress)
        {
            _dispatcher.Invoke(() =>
            {
                double overallPercent = progress.TotalBytes > 0
                    ? (double)progress.CurrentBytes / progress.TotalBytes * 100
                    : progress.OverallProgress;

                _updateProgress(overallPercent,
                    $"[{progress.CurrentIndex}/{progress.TotalCount}] {progress.CurrentPartition}");
            });
        }

        #endregion

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(string mode)
        {
            if (!IsConnected || _firehose == null)
            {
                _log("[错误] 设备未连接", "#EF4444");
                return false;
            }

            try
            {
                _log($"[重启] 重启到 {mode}...", "#3B82F6");
                var result = await _firehose.ResetAsync(mode);

                if (result)
                    _log($"[重启] ✅ 已发送重启命令 ({mode})", "#059669");
                else
                    _log($"[重启] ⚠️ 重启命令可能未成功", "#D97706");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[错误] 重启失败: {ex.Message}", "#EF4444");
                return false;
            }
        }

        /// <summary>
        /// 停止当前操作
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _log("[停止] 正在停止当前操作...", "#EF4444");
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _firehose = null;
            _sahara = null;
            _executor = null;

            _portManager?.Close();
            _portManager?.Dispose();
            _portManager = null;

            CurrentPort = null;
            CurrentDevice = null;

            _dispatcher.Invoke(() => Partitions.Clear());
            _updateStatus("未连接");
        }

        private void OnProgressChanged(long current, long total)
        {
            if (total > 0)
            {
                double percent = (double)current / total * 100;
                _dispatcher.Invoke(() => 
                {
                    _updateProgress(percent, $"{percent:F0}%");
                    // 触发字节传输更新事件
                    TransferProgress?.Invoke(current, total);
                });
            }
        }

        private void OnStatusChanged(string status)
        {
            _dispatcher.Invoke(() => _updateStatus(status));
        }

        public void Dispose()
        {
            _deviceWatcher?.Stop();
            Disconnect();
        }
    }

    /// <summary>
    /// 高通设备信息
    /// </summary>
    public class QcDeviceInfo
    {
        public string Port { get; set; } = "---";
        public string Serial { get; set; } = "---";
        public string HwId { get; set; } = "---";
        public string Vendor { get; set; } = "---";         // PK Hash 推断的厂商
        public string Manufacturer { get; set; } = "";      // 读取到的厂商
        public string ChipName { get; set; } = "---";       // 芯片型号 (如 SM8650)
        public string Model { get; set; } = "";             // 设备型号 (如 TB321FU)
        public string OtaVersion { get; set; } = "";        // OTA 版本
    }
}
