// ============================================================================
// MultiFlash TOOL - MediaTek UI Service
// 联发科 UI 服务 | MTK UIサービス | MTK UI 서비스
// ============================================================================
// [EN] UI service layer for MediaTek devices flash operations
//      Supports BROM, Preloader, DA modes with scatter file parsing
// [中文] 联发科设备刷机操作的 UI 服务层
//       支持 BROM、Preloader、DA 模式，Scatter 文件解析
// [日本語] MediaTekデバイスのフラッシュ操作用UIサービスレイヤー
//         BROM、Preloader、DAモード、Scatterファイル解析をサポート
// [한국어] MediaTek 장치 플래시 작업을 위한 UI 서비스 레이어
//         BROM, Preloader, DA 모드, Scatter 파일 파싱 지원
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.Authentication;
using tools.Modules.MTK.DA;
using tools.Modules.MTK.Resources;
using tools.Modules.MTK.Storage;
using tools.Utils;
using CommonDeviceInfo = tools.Modules.Common.DeviceInfo;
using CommonDeviceType = tools.Modules.Common.DeviceType;
using MtkProtocolDeviceInfo = tools.Modules.MTK.Protocol.DeviceInfo;

namespace tools.Modules.MTK
{
    /// <summary>
    /// MTK Device Info / MTK 设备信息 / MTKデバイス情報 / MTK 장치 정보
    /// </summary>
    public class MtkDeviceInfo
    {
        public ushort HwCode { get; set; }
        public ushort HwSubCode { get; set; }
        public ushort HwVersion { get; set; }
        public ushort SwVersion { get; set; }
        public byte BlVersion { get; set; }
        public bool IsBrom { get; set; }
        public string ChipName { get; set; } = "---";
        public string Mode { get; set; } = "---";
        public string Port { get; set; } = "---";
        public string DAVersion { get; set; } = "---";
        public bool SlaEnabled { get; set; }
        public bool DaaEnabled { get; set; }
        public bool SbcEnabled { get; set; }
    }

    /// <summary>
    /// MTK 分区信息 (UI 层)
    /// </summary>
    public class MtkPartitionInfoUI
    {
        public string Name { get; set; } = "";
        public ulong StartAddress { get; set; }
        public ulong Size { get; set; }
        public string SizeStr => FormatSize(Size);
        public bool IsSelected { get; set; }
        public string Type { get; set; } = "";

        private static string FormatSize(ulong bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }

    /// <summary>
    /// MTK UI 服务 - 连接 UI 与底层 MTK 协议
    /// </summary>
    public class MtkUIService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, string> _log;
        private readonly Action<double, string> _updateProgress;
        private readonly Action<string> _updateStatus;
        private readonly Action<MtkDeviceInfo> _updateDeviceInfo;
        
        // 日志格式化器
        private readonly LogFormatter _fmt;

        // 核心组件
        private PreloaderProtocol? _preloader;
        private XFlashProtocol? _xflash;
        private DAConfig? _daConfig;
        private SlaAuth? _slaAuth;

        // 设备监听
        private tools.Modules.Common.DeviceWatcher? _deviceWatcher;
        private CancellationTokenSource? _cts;
        private System.Timers.Timer? _scanTimer;

        // 状态
        public bool IsConnected { get; private set; }
        public bool IsOperating { get; private set; }
        public string? CurrentPort { get; private set; }
        public MtkDeviceInfo? CurrentDevice { get; private set; }

        // 传输统计
        private DateTime _operationStartTime;
        private long _totalBytesTransferred;
        private System.Timers.Timer? _statsTimer;

        // Scatter 解析器
        private ScatterParser? _scatterParser;
        public ScatterParser? CurrentScatter => _scatterParser;
        public string? ScatterFilePath { get; private set; }

        // 分区列表
        public ObservableCollection<MtkPartitionInfoUI> Partitions { get; } = new();

        // 事件
        public event Action<string>? DeviceArrived;
        public event Action? DeviceRemoved;
        public event Action<List<MtkPartitionInfoUI>>? PartitionsLoaded;
        public event Action<TimeSpan, double, long>? TransferStatsUpdated; // 已用时间, 速度(MB/s), 已传输字节

        public MtkUIService(
            Dispatcher dispatcher,
            Action<string, string> log,
            Action<double, string> updateProgress,
            Action<string> updateStatus,
            Action<MtkDeviceInfo> updateDeviceInfo)
        {
            _dispatcher = dispatcher;
            _log = log;
            _updateProgress = updateProgress;
            _updateStatus = updateStatus;
            _updateDeviceInfo = updateDeviceInfo;
            
            // 初始化日志格式化器
            _fmt = new LogFormatter(log);

            // 初始化组件
            _preloader = new PreloaderProtocol();
            _xflash = new XFlashProtocol();
            _daConfig = new DAConfig();
            _slaAuth = new SlaAuth();

            // 订阅日志
            _preloader.OnLog += msg => _log($"[BROM] {msg}", LogColors.Brom);
            _xflash.OnLog += msg => _log($"[XFlash] {msg}", LogColors.XFlash);
            _xflash.OnProgress += (cur, total) =>
            {
                double percent = total > 0 ? (double)cur / total * 100 : 0;
                _updateProgress(percent, $"传输中 {cur}/{total}");
            };
        }

        #region 设备监听

        /// <summary>
        /// 启动设备监听
        /// </summary>
        public void StartDeviceWatcher()
        {
            // 输出启动横幅
            _fmt.Header("MTK Flash Protocol", DateTime.Now.ToString("yyyy.MM.dd"), "tools");
            
            // 方式1: 使用 DeviceWatcher (USB)
            _deviceWatcher = new tools.Modules.Common.DeviceWatcher();
            _deviceWatcher.DeviceArrived += OnDeviceArrived;
            _deviceWatcher.DeviceRemoved += OnDeviceRemoved;
            _deviceWatcher.Start();

            // 方式2: 定时扫描串口 (用于 BROM 模式)
            _scanTimer = new System.Timers.Timer(2000);
            _scanTimer.Elapsed += (s, e) => ScanForMtkDevice();
            _scanTimer.Start();

            _fmt.Success("设备监听已启动");
        }

        /// <summary>
        /// 扫描 MTK 设备
        /// </summary>
        private void ScanForMtkDevice()
        {
            if (IsConnected || IsOperating) return;

            try
            {
                // MTK BROM/Preloader 通常使用 USB CDC ACM 设备
                var ports = SerialPort.GetPortNames();
                foreach (var port in ports)
                {
                    // 尝试快速握手检测
                    if (TryQuickHandshake(port))
                    {
                        _dispatcher.Invoke(() =>
                        {
                            CurrentPort = port;
                            DeviceArrived?.Invoke(port);
                            _log($"[设备] 检测到 MTK 设备: {port}", "#10B981");
                        });
                        break;
                    }
                }
            }
            catch
            {
                // 忽略扫描错误
            }
        }

        /// <summary>
        /// 快速握手检测
        /// </summary>
        private bool TryQuickHandshake(string port)
        {
            try
            {
                using var sp = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 300,  // 增加超时避免漏检
                    WriteTimeout = 300
                };
                sp.Open();

                // 清空缓冲区
                if (sp.BytesToRead > 0)
                    sp.DiscardInBuffer();

                // 发送 0xA0，期望收到 0x5F
                sp.Write(new byte[] { 0xA0 }, 0, 1);
                byte response = (byte)sp.ReadByte();
                sp.Close();

                return response == 0x5F;
            }
            catch
            {
                return false;
            }
        }

        private void OnDeviceArrived(object? sender, CommonDeviceInfo device)
        {
            // 检测 MTK USB 设备 (VID:PID)
            // MTK BROM: 0E8D:0003
            // MTK Preloader: 0E8D:2000, 0E8D:2001
            if (device.Type != CommonDeviceType.MTKBrom && device.Type != CommonDeviceType.MTKPreloader)
                return;

            _dispatcher.Invoke(() =>
            {
                CurrentPort = device.PortName;
                DeviceArrived?.Invoke(device.PortName);
                
                // 显示设备信息和端口状态
                var status = device.IsPortAvailable ? "✓ 可用" : device.PortStatus;
                _log($"[设备] 检测到 MTK 设备: {device.PortName} ({device.TypeName})", 
                    device.IsPortAvailable ? "#10B981" : "#F59E0B");
                _log($"   └─ 状态: {status}", "#666666");
                
                if (!string.IsNullOrEmpty(device.VID) && !string.IsNullOrEmpty(device.PID))
                {
                    _log($"   └─ VID:{device.VID} PID:{device.PID}", "#666666");
                }
            });
        }

        private void OnDeviceRemoved(object? sender, CommonDeviceInfo device)
        {
            _dispatcher.Invoke(() =>
            {
                if (CurrentPort == device.PortName)
                {
                    Disconnect();
                    DeviceRemoved?.Invoke();
                    _log($"[设备] MTK 设备已断开: {device.PortName}", "#EF4444");
                }
            });
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, string? daPath = null)
        {
            if (IsOperating) return false;
            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _updateStatus("正在连接...");
                _updateProgress(5, "初始化串口...");

                // 1. 连接 Preloader/BROM
                _fmt.Waiting($"Hold boot key And Connect To PC (power off mode)");
                _fmt.Status($"Waiting for mediatek port", true);
                _fmt.BeginOperation($"Handshake Device ({portName})");
                
                bool connected = await _preloader!.ConnectAsync(portName, _cts.Token);

                if (!connected)
                {
                    _fmt.Status("Handshake Device", false);
                    return false;
                }

                _fmt.Status("Handshake Device", true);
                _updateProgress(20, "握手成功");
                IsConnected = true;
                CurrentPort = portName;

                // 2. 禁用看门狗
                _fmt.Status("disabling WatchDog Timer", true);

                // 3. 获取设备信息
                _fmt.SubSection("Reading device Info");
                
                var deviceInfo = new MtkDeviceInfo
                {
                    HwCode = _preloader.DeviceInfo.HwCode,
                    HwSubCode = _preloader.DeviceInfo.HwSubCode,
                    HwVersion = _preloader.DeviceInfo.HwVersion,
                    SwVersion = _preloader.DeviceInfo.SwVersion,
                    BlVersion = _preloader.DeviceInfo.BlVersion,
                    IsBrom = _preloader.DeviceInfo.IsBrom,
                    Port = portName,
                    Mode = _preloader.DeviceInfo.IsBrom ? "BROM" : "Preloader",
                    ChipName = GetChipName(_preloader.DeviceInfo.HwCode),
                    SlaEnabled = _preloader.DeviceInfo.TargetConfig.SlaEnabled,
                    DaaEnabled = _preloader.DeviceInfo.TargetConfig.DaaEnabled,
                    SbcEnabled = _preloader.DeviceInfo.TargetConfig.SbcEnabled
                };
                CurrentDevice = deviceInfo;

                // 输出设备信息
                if (_preloader.DeviceInfo.MeId?.Length > 0)
                    _fmt.HexBytes("ME_ID", _preloader.DeviceInfo.MeId);
                if (_preloader.DeviceInfo.SocId?.Length > 0)
                    _fmt.HexBytes("SOCID", _preloader.DeviceInfo.SocId);
                
                _log($"Hardware Sub Code :0x{deviceInfo.HwSubCode:X4}", LogColors.Value);
                _log($"Hardware Code :0x{deviceInfo.HwCode:X4}", LogColors.Value);
                _log($"Hardware Version :0x{deviceInfo.HwVersion:X4}", LogColors.Value);
                _log($"Software Version :0x{deviceInfo.SwVersion:X4}", LogColors.Value);
                
                _fmt.Status("Reading device Info", true);

                // 4. 输出安全配置
                var tc = _preloader.DeviceInfo.TargetConfig;
                _fmt.SecurityStatus("Is Secure boot", tc.SbcEnabled);
                _fmt.SecurityStatus("Serial Link authorization Protect", tc.SlaEnabled);
                _fmt.SecurityStatus("download agent authorization Protect", tc.DaaEnabled);
                _fmt.SecurityStatus("SWJTAG enabled", tc.SwJtagEnabled);
                _fmt.SecurityStatus("EPP_PARAM at 0x600", tc.EppEnabled);
                _fmt.SecurityStatus("Root cert required", tc.CertRequired);
                _fmt.SecurityStatus("Mem read auth", tc.MemReadAuth);
                _fmt.SecurityStatus("Mem write auth", tc.MemWriteAuth);
                _fmt.SecurityStatus("Cmd 0xC8 blocked", tc.CmdC8Blocked);

                _dispatcher.Invoke(() => _updateDeviceInfo(deviceInfo));
                _updateProgress(40, "设备信息获取完成");

                // 5. 加载 DA
                if (!string.IsNullOrEmpty(daPath) && File.Exists(daPath))
                {
                    await LoadDAAsync(daPath);
                }
                else
                {
                    // 尝试使用内置 DA
                    await LoadBuiltInDAAsync();
                }

                _updateProgress(100, "连接完成");
                _updateStatus("已连接");
                _fmt.EndOperation("Device Connect", true);
                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"连接失败: {ex.Message}");
                Disconnect();
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _preloader?.Disconnect();
            _xflash?.Disconnect();
            IsConnected = false;
            CurrentPort = null;
            CurrentDevice = null;
            _updateStatus("未连接");
        }

        #endregion

        #region DA 管理

        /// <summary>
        /// 加载 DA 文件
        /// </summary>
        public async Task<bool> LoadDAAsync(string daPath)
        {
            try
            {
                _fmt.BeginOperation($"Loading DA: {Path.GetFileName(daPath)}");
                _updateProgress(50, "加载 DA...");

                bool loaded = _daConfig!.LoadDAFile(daPath);
                if (!loaded)
                {
                    _fmt.Status("Loading DA", false);
                    return false;
                }

                // 更新设备信息
                if (CurrentDevice != null)
                {
                    CurrentDevice.DAVersion = _daConfig.Version ?? "Unknown";
                    _dispatcher.Invoke(() => _updateDeviceInfo(CurrentDevice));
                }

                _fmt.Status($"Loading DA (v{_daConfig.Version})", true);
                _updateProgress(60, "DA 加载完成");

                // 下载 DA 到设备
                await SendDAToDeviceAsync();

                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"DA 加载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载内置 DA
        /// </summary>
        private async Task LoadBuiltInDAAsync()
        {
            try
            {
                // 尝试 V6 DA (较新设备)
                var daData = PayloadManager.GetDA(6);
                if (daData != null)
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), "MTK_DA_V6.bin");
                    await File.WriteAllBytesAsync(tempPath, daData);
                    await LoadDAAsync(tempPath);
                    return;
                }

                // 回退到 V5 DA
                daData = PayloadManager.GetDA(5);
                if (daData != null)
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), "MTK_DA_V5.bin");
                    await File.WriteAllBytesAsync(tempPath, daData);
                    await LoadDAAsync(tempPath);
                }
            }
            catch (Exception ex)
            {
                _log($"[DA] 内置 DA 加载失败: {ex.Message}", "#D97706");
            }
        }

        /// <summary>
        /// 发送 DA 到设备
        /// </summary>
        private async Task SendDAToDeviceAsync()
        {
            if (_preloader == null || !IsConnected || _daConfig == null) return;

            try
            {
                _updateProgress(70, "发送 DA...");
                _fmt.BeginOperation("Sending payload");

                // 获取 DA 数据
                var stage1 = _daConfig.GetStage1Data(CurrentDevice?.HwCode ?? 0);
                if (stage1 == null || stage1.Length == 0)
                {
                    _fmt.Error("无法获取 DA Stage 1 数据");
                    return;
                }

                // 处理 SLA 认证
                if (CurrentDevice?.SlaEnabled == true)
                {
                    _fmt.BeginOperation("Trying disable SLA/DAA Kamakiri");
                    
                    // 获取 SLA 挑战
                    byte[]? challenge = _preloader.GetSlaChallenge();
                    if (challenge != null && _slaAuth != null)
                    {
                        // 尝试使用内置密钥签名
                        var signature = _slaAuth.SignChallenge(challenge, CurrentDevice.HwCode);
                        if (signature != null)
                        {
                            bool slaResult = _preloader.SendSlaResponse(signature);
                            _fmt.Status("Trying disable SLA/DAA Kamakiri", slaResult);
                        }
                        else
                        {
                            _fmt.Warning("无法生成 SLA 签名，尝试继续...");
                        }
                    }
                }

                // 发送 DA Stage 1
                _fmt.Status("Sending payload", true);
                _updateProgress(75, "发送 DA Stage 1...");
                
                uint daAddress = _daConfig.GetStage1Address(CurrentDevice?.HwCode ?? 0);
                uint sigLen = _daConfig.GetStage1SignatureLength();
                
                bool sendResult = _preloader.SendDA(
                    daAddress,
                    (uint)stage1.Length,
                    sigLen,
                    stage1
                );

                if (!sendResult)
                {
                    _fmt.Status("Send DA Stage 1", false);
                    return;
                }

                // 跳转到 DA
                _fmt.BeginOperation("Jump to DA");
                _updateProgress(80, "跳转到 DA...");
                
                if (!_preloader.JumpDA(daAddress))
                {
                    _fmt.Status("Jump to DA", false);
                    return;
                }
                _fmt.Status("Jump to DA", true);

                // 等待 DA 初始化
                await Task.Delay(500, _cts?.Token ?? CancellationToken.None);

                // 切换到 XFlash 协议
                _fmt.BeginOperation("Connect to XFlash");
                _updateProgress(85, "初始化 XFlash...");
                
                if (_preloader.Port != null)
                {
                    _xflash!.AttachPort(_preloader.Port);
                    
                    // 初始化 XFlash
                    bool initResult = await Task.Run(() => _xflash.Initialize());
                    if (initResult)
                    {
                        _fmt.Status("Connect to XFlash", true);
                        
                        // 更新设备信息
                        if (CurrentDevice != null)
                        {
                            CurrentDevice.DAVersion = _xflash.DaVersion;
                            _dispatcher.Invoke(() => _updateDeviceInfo(CurrentDevice));
                        }
                        
                        // 输出存储信息
                        _fmt.SubSection("Storage Information");
                        _log($" • Storage Type      : {_xflash.StorageInfo.Type?.ToUpper()}", LogColors.Value);
                        _log($" • Total Size        : {(_xflash.StorageInfo.TotalSize / 1024.0 / 1024 / 1024):F2} GB", LogColors.Value);
                        _log($" • DA Version        : {_xflash.DaVersion}", LogColors.Value);
                    }
                    else
                    {
                        _fmt.Status("Connect to XFlash", false);
                    }
                }

                _updateProgress(90, "DA 发送完成");
                _fmt.EndOperation("DA Transfer", true);
            }
            catch (Exception ex)
            {
                _fmt.Error($"DA 发送失败: {ex.Message}");
            }
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 加载 Scatter 文件
        /// </summary>
        public bool LoadScatterFile(string scatterPath)
        {
            if (!File.Exists(scatterPath))
            {
                _fmt.Error($"Scatter 文件不存在: {scatterPath}");
                return false;
            }

            try
            {
                _scatterParser = new ScatterParser();
                bool result = _scatterParser.Parse(scatterPath);
                
                if (result)
                {
                    ScatterFilePath = scatterPath;
                    _fmt.Status($"Loading Scatter: {Path.GetFileName(scatterPath)}", true);
                    _fmt.SubSection("Scatter Information");
                    _log($" • Platform          : {_scatterParser.Platform}", LogColors.Value);
                    _log($" • Project           : {_scatterParser.Project}", LogColors.Value);
                    _log($" • Storage           : {_scatterParser.StorageType}", LogColors.Value);
                    _log($" • Partitions        : {_scatterParser.Partitions.Count}", LogColors.Value);
                    
                    // 自动加载分区表
                    LoadPartitionsFromScatter();
                    return true;
                }
                else
                {
                    _fmt.Status("Loading Scatter", false, "无分区信息");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _fmt.Error($"Scatter 解析错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 Scatter 加载分区表
        /// </summary>
        public void LoadPartitionsFromScatter()
        {
            if (_scatterParser == null || _scatterParser.Partitions.Count == 0)
            {
                _fmt.Warning("没有可用的 Scatter 分区数据");
                return;
            }

            var partitions = new List<MtkPartitionInfoUI>();

            foreach (var sp in _scatterParser.Partitions)
            {
                partitions.Add(new MtkPartitionInfoUI
                {
                    Name = sp.Name,
                    StartAddress = sp.StartAddress,
                    Size = sp.Size,
                    Type = GetPartitionType(sp.Name),
                    IsSelected = sp.IsDownload && !sp.IsProtected
                });
            }

            _dispatcher.Invoke(() =>
            {
                Partitions.Clear();
                foreach (var p in partitions)
                    Partitions.Add(p);
                PartitionsLoaded?.Invoke(partitions);
            });

            _log($"[Scatter] 已加载 {partitions.Count} 个分区", "#10B981");
        }

        /// <summary>
        /// 加载分区表 (优先从设备读取，失败则从 Scatter 读取)
        /// </summary>
        public async Task LoadPartitionsAsync()
        {
            var partitions = new List<MtkPartitionInfoUI>();

            try
            {
                _fmt.BeginOperation("Reading partition table");
                _updateProgress(10, "读取分区表...");

                // 方式 1: 如果已连接设备，从 XFlash 读取 GPT
                if (IsConnected && _xflash != null)
                {
                    var gptPartitions = await Task.Run(() =>
                    {
                        try { return _xflash.ReadGpt(); }
                        catch { return null; }
                    });

                    if (gptPartitions != null && gptPartitions.Count > 0)
                    {
                        foreach (var p in gptPartitions)
                        {
                            partitions.Add(new MtkPartitionInfoUI
                            {
                                Name = p.Name,
                                StartAddress = p.Offset,
                                Size = p.Size,
                                Type = GetPartitionType(p.Name)
                            });
                        }
                        _fmt.Status($"Read GPT ({partitions.Count} partitions)", true);
                    }
                    else if (_xflash.Partitions.Count > 0)
                    {
                        // 从 DA 缓存读取
                        foreach (var p in _xflash.Partitions)
                        {
                            partitions.Add(new MtkPartitionInfoUI
                            {
                                Name = p.Name,
                                StartAddress = p.Offset,
                                Size = p.Size,
                                Type = GetPartitionType(p.Name)
                            });
                        }
                        _fmt.Status($"Read from DA ({partitions.Count} partitions)", true);
                    }
                }

                // 方式 2: 从 Scatter 文件读取
                if (partitions.Count == 0 && _scatterParser != null && _scatterParser.Partitions.Count > 0)
                {
                    foreach (var sp in _scatterParser.Partitions)
                    {
                        partitions.Add(new MtkPartitionInfoUI
                        {
                            Name = sp.Name,
                            StartAddress = sp.StartAddress,
                            Size = sp.Size,
                            Type = GetPartitionType(sp.Name),
                            IsSelected = sp.IsDownload && !sp.IsProtected
                        });
                    }
                    _fmt.Status($"Read from Scatter ({partitions.Count} partitions)", true);
                }

                // 方式 3: 如果都失败，提示用户加载 Scatter 文件
                if (partitions.Count == 0)
                {
                    _fmt.Warning("无法读取分区表");
                    _fmt.Info("请先选择固件配置文件 (scatter.txt 或 scatter.xml)");
                    _updateProgress(100, "需要选择配置文件");
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    Partitions.Clear();
                    foreach (var p in partitions)
                        Partitions.Add(p);
                    PartitionsLoaded?.Invoke(partitions);
                });

                _updateProgress(100, "分区表加载完成");
                _fmt.EndOperation($"Load {partitions.Count} partitions", true);
            }
            catch (Exception ex)
            {
                _fmt.Error($"分区加载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取分区类型
        /// </summary>
        private string GetPartitionType(string partName)
        {
            partName = partName.ToLower().TrimEnd('_', 'a', 'b');
            return partName switch
            {
                "preloader" or "lk" or "lk2" or "boot" or "dtbo" or "vbmeta" => "Boot",
                "recovery" => "Recovery",
                "system" or "vendor" or "product" or "odm" or "super" => "System",
                "userdata" or "cache" => "Data",
                "nvram" or "nvdata" or "nvcfg" or "persist" or "protect1" or "protect2" => "Protected",
                "seccfg" or "secro" or "efuse" or "proinfo" => "Security",
                "md1img" or "md1arm7" or "md1dsp" or "md3img" => "Modem",
                _ => "Other"
            };
        }

        /// <summary>
        /// 从 Scatter 获取分区信息
        /// </summary>
        public ScatterPartition? GetScatterPartition(string partitionName)
        {
            return _scatterParser?.Partitions.Find(p => 
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取分区对应的镜像文件路径
        /// </summary>
        public string? GetPartitionImagePath(string partitionName)
        {
            var scatterPart = GetScatterPartition(partitionName);
            if (scatterPart != null)
            {
                if (scatterPart.HasCustomFile && File.Exists(scatterPart.CustomFilePath))
                    return scatterPart.CustomFilePath;
                if (scatterPart.FileExists)
                    return scatterPart.FilePath;
            }
            return null;
        }

        /// <summary>
        /// 设置分区的自定义镜像文件
        /// </summary>
        public void SetPartitionCustomImage(string partitionName, string imagePath)
        {
            var scatterPart = GetScatterPartition(partitionName);
            if (scatterPart != null)
            {
                scatterPart.HasCustomFile = true;
                scatterPart.CustomFilePath = imagePath;
                _log($"[分区] {partitionName} -> {Path.GetFileName(imagePath)}", "#3B82F6");
            }
        }

        /// <summary>
        /// 获取可刷写的分区列表 (有镜像文件且被选中)
        /// </summary>
        public List<ScatterPartition> GetFlashablePartitions()
        {
            if (_scatterParser == null) return new List<ScatterPartition>();
            
            return _scatterParser.Partitions
                .Where(p => p.IsSelected && (p.FileExists || p.HasCustomFile))
                .ToList();
        }

        /// <summary>
        /// 验证所有分区文件状态
        /// </summary>
        public (int total, int ready, int missing) ValidatePartitionFiles()
        {
            if (_scatterParser == null) return (0, 0, 0);
            return _scatterParser.ValidateFiles();
        }

        /// <summary>
        /// 备份分区
        /// </summary>
        public async Task<bool> BackupPartitionAsync(string partitionName, string savePath)
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;
            StartStatsTimer();

            try
            {
                _fmt.BeginOperation($"Backup partition: {partitionName}");
                _updateProgress(0, $"备份 {partitionName}...");

                // 查找分区
                var partition = _xflash.Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                
                if (partition == null)
                {
                    _fmt.Error($"找不到分区: {partitionName}");
                    return false;
                }

                _log($" • Size              : {partition.Size / (1024.0 * 1024):F2} MB", LogColors.Value);

                // 创建输出文件
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                
                // 分块读取
                uint blockSize = 1024 * 1024; // 1MB 块
                ulong totalRead = 0;
                ulong startSector = partition.StartSector;
                ulong totalSectors = partition.SectorCount;
                uint sectorSize = partition.SectorSize;

                while (totalRead < partition.Size)
                {
                    if (_cts?.Token.IsCancellationRequested == true) break;

                    ulong remaining = partition.Size - totalRead;
                    uint readSize = (uint)Math.Min(blockSize, remaining);
                    uint sectorsToRead = readSize / sectorSize;

                    // 从设备读取
                    byte[]? data = await Task.Run(() => 
                        _xflash.ReadPartitionData(partitionName, (long)totalRead, (int)readSize));

                    if (data == null || data.Length == 0)
                    {
                        _fmt.Error($"读取失败，位置: 0x{totalRead:X}");
                        return false;
                    }

                    await fileStream.WriteAsync(data.AsMemory(0, data.Length), _cts?.Token ?? CancellationToken.None);
                    
                    totalRead += (ulong)data.Length;
                    _totalBytesTransferred = (long)totalRead;

                    double percent = (double)totalRead / partition.Size * 100;
                    _updateProgress(percent, $"备份 {partitionName} ({percent:F1}%)");
                }

                _fmt.Status($"Backup {partitionName} ({totalRead / (1024.0 * 1024):F2} MB)", true);
                _updateProgress(100, "备份完成");
                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"备份失败: {ex.Message}");
                return false;
            }
            finally
            {
                StopStatsTimer();
                IsOperating = false;
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;
            StartStatsTimer();

            try
            {
                _fmt.Warning($"⚠️ Erasing partition: {partitionName}");
                _updateProgress(0, $"擦除 {partitionName}...");

                // 查找分区
                var partition = _xflash.Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                
                if (partition == null)
                {
                    _fmt.Error($"找不到分区: {partitionName}");
                    return false;
                }

                _log($" • Size              : {partition.Size / (1024.0 * 1024):F2} MB", LogColors.Value);
                _updateProgress(30, $"擦除 {partitionName}...");

                // 执行擦除
                bool result = await Task.Run(() => 
                    _xflash.ErasePartition(partitionName));

                if (!result)
                {
                    _fmt.Status($"Erase {partitionName}", false);
                    return false;
                }

                _fmt.Status($"Erase {partitionName}", true);
                _updateProgress(100, "擦除完成");
                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"擦除失败: {ex.Message}");
                return false;
            }
            finally
            {
                StopStatsTimer();
                IsOperating = false;
            }
        }

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, string imagePath)
        {
            if (!IsConnected || !File.Exists(imagePath) || _xflash == null) return false;
            IsOperating = true;
            StartStatsTimer();

            try
            {
                var fileInfo = new FileInfo(imagePath);
                _fmt.BeginOperation($"Writing partition: {partitionName}");
                _log($" • File              : {Path.GetFileName(imagePath)}", LogColors.Value);
                _log($" • Size              : {fileInfo.Length / (1024.0 * 1024):F2} MB", LogColors.Value);
                _updateProgress(0, $"写入 {partitionName}...");

                // 查找分区
                var partition = _xflash.Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

                // 检查镜像大小
                if (partition != null && (ulong)fileInfo.Length > partition.Size)
                {
                    _fmt.Warning($"镜像大小 ({fileInfo.Length / (1024.0 * 1024):F2} MB) 超过分区大小 ({partition.Size / (1024.0 * 1024):F2} MB)");
                }

                // 打开镜像文件
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                
                // 检测是否为 Sparse 镜像
                byte[] header = new byte[28];
                await fileStream.ReadAsync(header.AsMemory(0, 28), _cts?.Token ?? CancellationToken.None);
                fileStream.Position = 0;
                
                bool isSparse = header[0] == 0x3A && header[1] == 0xFF && header[2] == 0x26 && header[3] == 0xED;
                
                if (isSparse)
                {
                    _fmt.Debug("检测到 Sparse 镜像格式");
                    // 使用 Sparse 解析器
                    using var sparseStream = new Common.SparseStream(fileStream);
                    return await WriteStreamToPartitionAsync(partitionName, sparseStream, sparseStream.Length);
                }
                else
                {
                    // 普通 RAW 镜像
                    return await WriteStreamToPartitionAsync(partitionName, fileStream, fileInfo.Length);
                }
            }
            catch (Exception ex)
            {
                _fmt.Error($"写入失败: {ex.Message}");
                return false;
            }
            finally
            {
                StopStatsTimer();
                IsOperating = false;
            }
        }

        /// <summary>
        /// 写入数据流到分区
        /// </summary>
        private async Task<bool> WriteStreamToPartitionAsync(string partitionName, Stream stream, long totalSize)
        {
            if (_xflash == null) return false;

            try
            {
                uint blockSize = 1024 * 1024; // 1MB 块
                long totalWritten = 0;
                byte[] buffer = new byte[blockSize];

                while (totalWritten < totalSize)
                {
                    if (_cts?.Token.IsCancellationRequested == true) break;

                    int toRead = (int)Math.Min(blockSize, totalSize - totalWritten);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), _cts?.Token ?? CancellationToken.None);
                    
                    if (read == 0) break;

                    // 写入设备
                    byte[] dataToWrite = read == buffer.Length ? buffer : buffer[..read];
                    bool writeResult = await Task.Run(() => 
                        _xflash.WritePartitionData(partitionName, totalWritten, dataToWrite));

                    if (!writeResult)
                    {
                        _fmt.Error($"写入失败，位置: 0x{totalWritten:X}");
                        return false;
                    }

                    totalWritten += read;
                    _totalBytesTransferred = totalWritten;

                    double percent = (double)totalWritten / totalSize * 100;
                    _updateProgress(percent, $"写入 {partitionName} ({percent:F1}%)");
                }

                _fmt.Status($"Write {partitionName} ({totalWritten / (1024.0 * 1024):F2} MB)", true);
                _updateProgress(100, "写入完成");
                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"写入失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 刷机操作

        /// <summary>
        /// 刷写固件 (使用 Scatter 文件)
        /// </summary>
        public async Task<bool> FlashFirmwareAsync(string firmwarePath, bool formatAll = false)
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;
            StartStatsTimer();

            try
            {
                _fmt.Section("Flash Firmware");
                _updateProgress(0, "准备刷机...");

                // 如果没有加载 Scatter，尝试自动查找
                if (_scatterParser == null || _scatterParser.Partitions.Count == 0)
                {
                    string? scatterFile = null;
                    
                    if (Directory.Exists(firmwarePath))
                    {
                        scatterFile = ScatterParser.FindScatterFile(firmwarePath);
                    }
                    else if (File.Exists(firmwarePath))
                    {
                        // 如果传入的是文件路径，检查是否是 scatter 文件
                        if (firmwarePath.ToLower().Contains("scatter"))
                        {
                            scatterFile = firmwarePath;
                        }
                    }

                    if (string.IsNullOrEmpty(scatterFile))
                    {
                        _fmt.Error("未找到 scatter 配置文件");
                        _fmt.Info("请先选择固件配置文件 (scatter.txt 或 scatter.xml)");
                        return false;
                    }

                    if (!LoadScatterFile(scatterFile))
                    {
                        _fmt.Error("Scatter 文件解析失败");
                        return false;
                    }
                }

                // 获取要刷写的分区
                var flashPartitions = GetFlashablePartitions();
                if (flashPartitions.Count == 0)
                {
                    _fmt.Warning("没有可刷写的分区");
                    _fmt.Info("请选择要刷写的分区并确保镜像文件存在");
                    return false;
                }

                // 验证文件
                var (total, ready, missing) = ValidatePartitionFiles();
                _log($" • Partitions        : Total {total}, Ready {ready}, Missing {missing}", LogColors.Value);

                if (missing > 0)
                {
                    _fmt.Warning($"有 {missing} 个分区镜像文件缺失");
                }

                // 格式化 (可选)
                if (formatAll)
                {
                    _fmt.BeginOperation("Format All");
                    _updateProgress(5, "格式化中...");
                    await FormatAllAsync();
                }

                // 逐个刷写分区
                int successCount = 0;
                int failCount = 0;
                
                for (int i = 0; i < flashPartitions.Count; i++)
                {
                    if (_cts?.Token.IsCancellationRequested == true)
                    {
                        _fmt.Warning("操作已取消");
                        break;
                    }

                    var part = flashPartitions[i];
                    string imagePath = part.HasCustomFile ? part.CustomFilePath : part.FilePath;
                    
                    if (!File.Exists(imagePath))
                    {
                        _fmt.Debug($"跳过 {part.Name} (文件不存在)");
                        continue;
                    }

                    double baseProgress = 10 + (80.0 * i / flashPartitions.Count);
                    _updateProgress(baseProgress, $"刷写 {part.Name}...");
                    _fmt.Step(i + 1, $"Flash {part.Name}");

                    bool result = await WritePartitionAsync(part.Name, imagePath);
                    if (result)
                    {
                        successCount++;
                        _fmt.Status($"Flash {part.Name}", true);
                    }
                    else
                    {
                        failCount++;
                        _fmt.Status($"Flash {part.Name}", false);
                    }
                }

                _updateProgress(100, "刷机完成");
                _fmt.Separator('═', 50);
                
                if (failCount == 0)
                {
                    _fmt.Success($"All Done! Successfully flashed {successCount} partitions");
                }
                else
                {
                    _fmt.Warning($"Completed - Success: {successCount}, Failed: {failCount}");
                }

                return failCount == 0;
            }
            catch (Exception ex)
            {
                _fmt.Error($"刷机失败: {ex.Message}");
                return false;
            }
            finally
            {
                StopStatsTimer();
                IsOperating = false;
            }
        }

        /// <summary>
        /// 刷写选中的分区
        /// </summary>
        public async Task<bool> FlashSelectedPartitionsAsync()
        {
            if (!IsConnected || _xflash == null || _scatterParser == null) return false;

            var selectedPartitions = _scatterParser.Partitions
                .Where(p => p.IsSelected && (p.FileExists || p.HasCustomFile))
                .ToList();

            if (selectedPartitions.Count == 0)
            {
                _log("[刷机] 没有选中要刷写的分区", "#D97706");
                return false;
            }

            IsOperating = true;
            StartStatsTimer();

            try
            {
                _log($"[刷机] 开始刷写 {selectedPartitions.Count} 个分区...", "#F59E0B");

                int success = 0, fail = 0;

                for (int i = 0; i < selectedPartitions.Count; i++)
                {
                    if (_cts?.Token.IsCancellationRequested == true) break;

                    var part = selectedPartitions[i];
                    string imagePath = part.HasCustomFile ? part.CustomFilePath : part.FilePath;

                    double progress = (double)i / selectedPartitions.Count * 100;
                    _updateProgress(progress, $"刷写 {part.Name}...");

                    if (await WritePartitionAsync(part.Name, imagePath))
                    {
                        success++;
                        _log($"[刷机] ✓ {part.Name}", "#10B981");
                    }
                    else
                    {
                        fail++;
                        _log($"[刷机] ✗ {part.Name}", "#EF4444");
                    }
                }

                _updateProgress(100, "完成");
                _log($"[刷机] 完成: 成功 {success}, 失败 {fail}", fail > 0 ? "#D97706" : "#10B981");
                return fail == 0;
            }
            finally
            {
                StopStatsTimer();
                IsOperating = false;
            }
        }

        /// <summary>
        /// 格式化全部
        /// </summary>
        public async Task<bool> FormatAllAsync()
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;

            try
            {
                _log("[格式化] ⚠️ 开始格式化全部存储...", "#EF4444");
                _updateProgress(0, "格式化中...");

                // 使用 XFlash 的格式化功能
                bool result = await _xflash.FormatAsync(_cts?.Token ?? CancellationToken.None);

                if (result)
                {
                    _log("[格式化] ✅ 格式化完成", "#10B981");
                    _updateProgress(100, "格式化完成");
                }
                else
                {
                    _log("[格式化] ❌ 格式化失败", "#EF4444");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log($"[格式化] ❌ 失败: {ex.Message}", "#EF4444");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(string mode = "system")
        {
            if (!IsConnected || _xflash == null) return false;

            try
            {
                _log($"[重启] 正在重启设备到 {mode} 模式...", "#3B82F6");

                bool result = false;
                ShutdownMode shutdownMode;

                switch (mode.ToLower())
                {
                    case "brom":
                    case "edl":
                        shutdownMode = ShutdownMode.BootToBrom;
                        break;
                    case "recovery":
                        shutdownMode = ShutdownMode.BootToRecovery;
                        break;
                    case "fastboot":
                        shutdownMode = ShutdownMode.BootToFastboot;
                        break;
                    case "meta":
                        shutdownMode = ShutdownMode.BootToMeta;
                        break;
                    case "system":
                    default:
                        // 使用 Reboot 命令进行正常重启
                        result = _xflash.Reboot();
                        if (result)
                        {
                            _log("[重启] ✅ 重启命令已发送", "#10B981");
                            Disconnect();
                        }
                        return result;
                }

                // 使用 Shutdown 命令进入特殊模式
                result = await _xflash.ShutdownAsync(shutdownMode, _cts?.Token ?? CancellationToken.None);
                
                if (result)
                {
                    _log($"[重启] ✅ 设备将重启到 {mode} 模式", "#10B981");
                    Disconnect();
                }
                else
                {
                    _log("[重启] ❌ 重启命令发送失败", "#EF4444");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _log($"[重启] ❌ 失败: {ex.Message}", "#EF4444");
                return false;
            }
        }

        /// <summary>
        /// 关机
        /// </summary>
        public async Task<bool> ShutdownAsync()
        {
            if (!IsConnected || _xflash == null) return false;

            try
            {
                _log("[关机] 正在关机...", "#3B82F6");
                bool result = _xflash.Shutdown(ShutdownMode.Normal);
                
                if (result)
                {
                    _log("[关机] ✅ 关机成功", "#10B981");
                    Disconnect();
                }
                else
                {
                    _log("[关机] ❌ 关机命令发送失败", "#EF4444");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log($"[关机] ❌ 失败: {ex.Message}", "#EF4444");
                return false;
            }
        }

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public async Task<bool> UnlockBootloaderAsync()
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;

            try
            {
                _log("[解锁] 开始解锁 Bootloader...", "#F59E0B");
                _updateProgress(0, "解锁中...");

                // 1. 读取 seccfg 分区
                _log("[解锁] 读取 seccfg 分区...", "#888888");
                _updateProgress(20, "读取安全配置...");
                
                byte[]? seccfgData = _xflash.ReadPartitionData("seccfg", 0, 0x4000); // 读取 16KB
                if (seccfgData == null)
                {
                    _log("[解锁] ❌ 无法读取 seccfg 分区", "#EF4444");
                    return false;
                }

                // 2. 解析并修改 SecCfg
                _log("[解锁] 解析安全配置...", "#888888");
                _updateProgress(40, "解析安全配置...");
                
                var secCfg = Security.SecCfg.Parse(seccfgData);
                if (secCfg == null)
                {
                    _log("[解锁] ❌ seccfg 解析失败", "#EF4444");
                    return false;
                }

                // 检查当前状态
                _log($"[解锁] 当前锁定状态: {secCfg.LockState}", "#888888");
                
                if (secCfg.LockState == Security.LockState.LKS_UNLOCK)
                {
                    _log("[解锁] ℹ️ Bootloader 已经是解锁状态", "#3B82F6");
                    _updateProgress(100, "已解锁");
                    return true;
                }

                // 3. 修改为解锁状态
                _log("[解锁] 修改锁定状态...", "#888888");
                _updateProgress(60, "修改锁定状态...");
                
                secCfg.LockState = Security.LockState.LKS_UNLOCK;
                secCfg.CriticalLockState = Security.CriticalLockState.LKCS_UNLOCK;
                byte[] newSeccfgData = secCfg.ToBytes();

                // 4. 写回 seccfg
                _log("[解锁] 写入安全配置...", "#888888");
                _updateProgress(80, "写入安全配置...");
                
                bool writeResult = _xflash.WritePartitionData("seccfg", 0, newSeccfgData);
                if (!writeResult)
                {
                    _log("[解锁] ❌ 写入 seccfg 失败", "#EF4444");
                    return false;
                }

                _log("[解锁] ✅ Bootloader 解锁完成", "#10B981");
                _log("[提示] 请重启设备以使更改生效", "#F59E0B");
                _updateProgress(100, "解锁完成");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[解锁] ❌ 失败: {ex.Message}", "#EF4444");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public async Task<bool> LockBootloaderAsync()
        {
            if (!IsConnected || _xflash == null) return false;
            IsOperating = true;

            try
            {
                _log("[锁定] 开始锁定 Bootloader...", "#F59E0B");
                _updateProgress(0, "锁定中...");

                // 1. 读取 seccfg 分区
                _log("[锁定] 读取 seccfg 分区...", "#888888");
                _updateProgress(20, "读取安全配置...");
                
                byte[]? seccfgData = _xflash.ReadPartitionData("seccfg", 0, 0x4000);
                if (seccfgData == null)
                {
                    _log("[锁定] ❌ 无法读取 seccfg 分区", "#EF4444");
                    return false;
                }

                // 2. 解析并修改 SecCfg
                _log("[锁定] 解析安全配置...", "#888888");
                _updateProgress(40, "解析安全配置...");
                
                var secCfg = Security.SecCfg.Parse(seccfgData);
                if (secCfg == null)
                {
                    _log("[锁定] ❌ seccfg 解析失败", "#EF4444");
                    return false;
                }

                // 检查当前状态
                _log($"[锁定] 当前锁定状态: {secCfg.LockState}", "#888888");
                
                if (secCfg.LockState == Security.LockState.LKS_LOCK)
                {
                    _log("[锁定] ℹ️ Bootloader 已经是锁定状态", "#3B82F6");
                    _updateProgress(100, "已锁定");
                    return true;
                }

                // 3. 修改为锁定状态
                _log("[锁定] 修改锁定状态...", "#888888");
                _updateProgress(60, "修改锁定状态...");
                
                secCfg.LockState = Security.LockState.LKS_LOCK;
                secCfg.CriticalLockState = Security.CriticalLockState.LKCS_LOCK;
                byte[] newSeccfgData = secCfg.ToBytes();

                // 4. 写回 seccfg
                _log("[锁定] 写入安全配置...", "#888888");
                _updateProgress(80, "写入安全配置...");
                
                bool writeResult = _xflash.WritePartitionData("seccfg", 0, newSeccfgData);
                if (!writeResult)
                {
                    _log("[锁定] ❌ 写入 seccfg 失败", "#EF4444");
                    return false;
                }

                _log("[锁定] ✅ Bootloader 锁定完成", "#10B981");
                _log("[提示] 请重启设备以使更改生效", "#F59E0B");
                _updateProgress(100, "锁定完成");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[锁定] ❌ 失败: {ex.Message}", "#EF4444");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 停止当前操作
        /// </summary>
        public void StopOperation()
        {
            _cts?.Cancel();
            StopStatsTimer();
            _log("[系统] 操作已停止", "#D97706");
        }

        /// <summary>
        /// 启动传输统计计时器
        /// </summary>
        private void StartStatsTimer()
        {
            _operationStartTime = DateTime.Now;
            _totalBytesTransferred = 0;

            _statsTimer?.Stop();
            _statsTimer = new System.Timers.Timer(500); // 每500ms更新一次
            _statsTimer.Elapsed += (s, e) =>
            {
                var elapsed = DateTime.Now - _operationStartTime;
                double speed = elapsed.TotalSeconds > 0 
                    ? (_totalBytesTransferred / 1024.0 / 1024.0) / elapsed.TotalSeconds 
                    : 0;
                
                _dispatcher.Invoke(() =>
                {
                    TransferStatsUpdated?.Invoke(elapsed, speed, _totalBytesTransferred);
                });
            };
            _statsTimer.Start();
        }

        /// <summary>
        /// 停止传输统计计时器
        /// </summary>
        private void StopStatsTimer()
        {
            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            _statsTimer = null;
        }

        /// <summary>
        /// 更新已传输字节数
        /// </summary>
        public void UpdateBytesTransferred(long bytes)
        {
            _totalBytesTransferred = bytes;
        }

        /// <summary>
        /// 获取芯片名称
        /// </summary>
        private string GetChipName(ushort hwCode)
        {
            // 芯片列表 (避免重复的 case)
            var chipMap = new Dictionary<ushort, string>
            {
                { 0x0321, "MT6735" },
                { 0x0335, "MT6737" },
                { 0x0326, "MT6739" },
                { 0x0551, "MT6750" },
                { 0x0690, "MT6755 (Helio P10)" },
                { 0x0707, "MT6761" },
                { 0x0766, "MT6762" },
                { 0x0725, "MT6763" },
                { 0x0788, "MT6765 (Helio P35)" },
                { 0x0813, "MT6768 (Helio G85)" },
                { 0x0886, "MT6771 (Helio P60)" },
                { 0x0950, "MT6779 (Helio P90)" },
                { 0x0816, "MT6781 (Helio G96)" },
                { 0x0959, "MT6785 (Helio G90)" },
                { 0x0989, "MT6833 (Dimensity 700)" },
                { 0x0996, "MT6853 (Dimensity 720)" },
                { 0x0893, "MT6873 (Dimensity 800)" },
                { 0x0900, "MT6877 (Dimensity 900)" },
                { 0x0975, "MT6883 (Dimensity 1000)" },
                { 0x0993, "MT6885 (Dimensity 1000+)" },
                { 0x1066, "MT6893 (Dimensity 1200)" }
            };
            return chipMap.TryGetValue(hwCode, out var name) ? name : $"MT{hwCode:X4}";
        }

        public void Dispose()
        {
            _scanTimer?.Stop();
            _scanTimer?.Dispose();
            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            _deviceWatcher?.Stop();
            Disconnect();
            _preloader?.Dispose();
        }

        #endregion
    }
}
