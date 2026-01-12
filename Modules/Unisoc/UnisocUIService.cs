// ============================================================================
// MultiFlash TOOL - Unisoc UI Service
// 展讯 UI 服务 | Unisoc UIサービス | Unisoc UI 서비스
// ============================================================================
// [EN] UI service layer connecting WPF interface with SPRD protocol
//      Handles device detection, PAC firmware, partition operations
// [中文] UI 服务层，连接 WPF 界面与 SPRD 协议
//       处理设备检测、PAC 固件、分区操作
// [日本語] WPFインターフェースとSPRDプロトコルを接続するUIサービスレイヤー
//         デバイス検出、PACファームウェア、パーティション操作を処理
// [한국어] WPF 인터페이스와 SPRD 프로토콜을 연결하는 UI 서비스 레이어
//         장치 감지, PAC 펌웨어, 파티션 작업 처리
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
using tools.Modules.Unisoc.Models;
using tools.Modules.Unisoc.Protocol;
using tools.Modules.Unisoc.Firmware;
using tools.Modules.Unisoc.Diag;
using tools.Modules.Unisoc.Exploit;
using tools.Modules.Common;
using tools.Utils;

namespace tools.Modules.Unisoc
{
    /// <summary>
    /// Unisoc UI Service / 展讯 UI 服务 / Unisoc UIサービス / Unisoc UI 서비스
    /// [EN] Connects UI with underlying SPRD protocol
    /// [中文] 连接 UI 与底层 SPRD 协议
    /// </summary>
    public class UnisocUIService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, string> _log;
        private readonly Action<double, string> _updateProgress;
        private readonly Action<string> _updateStatus;
        private readonly Action<UnisocDeviceInfo> _updateDeviceInfo;

        // 日志格式化器
        private readonly LogFormatter _fmt;

        // 核心组件
        private SprdProtocol? _sprdProtocol;
        private DiagProtocol? _diagProtocol;
        private PacExtractor? _pacExtractor;

        // 设备监听
        private DeviceWatcher? _deviceWatcher;
        private CancellationTokenSource? _cts;

        // 状态
        public bool IsConnected => _sprdProtocol?.IsConnected == true || _diagProtocol?.IsConnected == true;
        public bool IsOperating { get; private set; }
        public string? CurrentPort { get; private set; }
        public UnisocDeviceInfo? CurrentDevice { get; private set; }

        // FDL 信息
        public string? Fdl1Path { get; set; }
        public string? Fdl2Path { get; set; }
        public string Fdl1Address { get; set; } = "0x5000";
        public string Fdl2Address { get; set; } = "0x9efffe00";
        public bool UseExploit { get; set; }

        // PAC 固件
        public PacExtractor? CurrentPac => _pacExtractor;
        public string? PacFilePath { get; private set; }

        // 分区列表
        public ObservableCollection<UnisocPartitionInfo> Partitions { get; } = new();

        // 事件
        public event Action<string>? DeviceArrived;
        public event Action? DeviceRemoved;
        public event Action<List<UnisocPartitionInfo>>? PartitionsLoaded;
        public event Action<long, long>? TransferProgress;

        /// <summary>
        /// 构造函数
        /// </summary>
        public UnisocUIService(
            Dispatcher dispatcher,
            Action<string, string> log,
            Action<double, string> updateProgress,
            Action<string> updateStatus,
            Action<UnisocDeviceInfo> updateDeviceInfo)
        {
            _dispatcher = dispatcher;
            _log = log;
            _updateProgress = updateProgress;
            _updateStatus = updateStatus;
            _updateDeviceInfo = updateDeviceInfo;

            _fmt = new LogFormatter(log);

            // 初始化组件
            _sprdProtocol = new SprdProtocol();
            _diagProtocol = new DiagProtocol();
            _pacExtractor = new PacExtractor();

            // 订阅日志
            _sprdProtocol.OnLog += msg => _log($"[SPRD] {msg}", "#FF6B35");
            _sprdProtocol.OnProgress += (cur, total) =>
            {
                double percent = total > 0 ? (double)cur / total * 100 : 0;
                _updateProgress(percent, $"传输中 {cur}/{total}");
                TransferProgress?.Invoke(cur, total);
            };

            _diagProtocol.OnLog += msg => _log($"[Diag] {msg}", "#9B59B6");

            _pacExtractor.OnLog += msg => _log($"[PAC] {msg}", "#3498DB");
            _pacExtractor.OnProgress += (cur, total) =>
            {
                double percent = total > 0 ? (double)cur / total * 100 : 0;
                _updateProgress(percent, $"解析中 {cur}/{total}");
            };
        }

        #region 设备监听

        /// <summary>
        /// 开始监听设备
        /// </summary>
        public void StartDeviceWatch()
        {
            if (_deviceWatcher != null) return;

            _fmt.Section("Unisoc 设备监听");
            _log("[Unisoc] 设备监听已启动", "#10B981");

            _deviceWatcher = new DeviceWatcher();
            _deviceWatcher.DeviceArrived += OnDeviceArrival;
            _deviceWatcher.DeviceRemoved += OnDeviceRemoval;
            _deviceWatcher.Start();
        }

        /// <summary>
        /// 停止监听设备
        /// </summary>
        public void StopDeviceWatch()
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.DeviceArrived -= OnDeviceArrival;
                _deviceWatcher.DeviceRemoved -= OnDeviceRemoval;
                _deviceWatcher.Stop();
                _deviceWatcher.Dispose();
                _deviceWatcher = null;
            }
        }

        private void OnDeviceArrival(object? sender, DeviceInfo device)
        {
            // 检测 Unisoc 设备
            // VID: 1782 (Spreadtrum) 或 SpreadtrumDownload 类型
            if (device.Type == DeviceType.SpreadtrumDownload ||
                device.VID == "1782" || 
                device.Description?.Contains("SPRD", StringComparison.OrdinalIgnoreCase) == true ||
                device.Description?.Contains("Spreadtrum", StringComparison.OrdinalIgnoreCase) == true)
            {
                CurrentPort = device.PortName;
                _dispatcher.Invoke(() =>
                {
                    DeviceArrived?.Invoke(device.PortName);
                    _log($"[Unisoc] 检测到设备: {device.PortName}", "#10B981");
                });
            }
        }

        private void OnDeviceRemoval(object? sender, DeviceInfo device)
        {
            if (device.PortName == CurrentPort)
            {
                CurrentPort = null;
                Disconnect();
                _dispatcher.Invoke(() =>
                {
                    DeviceRemoved?.Invoke();
                    _log("[Unisoc] 设备已断开", "#EF4444");
                });
            }
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接设备 (下载模式)
        /// </summary>
        public async Task<bool> ConnectDownloadModeAsync(string port)
        {
            if (IsOperating) return false;

            _fmt.Section("连接 Unisoc 下载模式");
            _updateStatus("连接中...");

            try
            {
                if (!_sprdProtocol!.Open(port))
                {
                    _fmt.Error("端口打开失败");
                    return false;
                }

                CurrentPort = port;

                // 握手
                if (!await _sprdProtocol.HandshakeAsync())
                {
                    _fmt.Warning("握手失败，尝试发送 FDL...");
                }

                // 更新设备信息
                CurrentDevice = new UnisocDeviceInfo
                {
                    Port = port,
                    Mode = "Download",
                    Fdl1Address = Fdl1Address,
                    Fdl2Address = Fdl2Address,
                    SupportsExploit = RsaExploit.IsExploitSupported(Fdl1Address),
                    ExploitAddress = RsaExploit.GetExploitAddress(Fdl1Address) ?? ""
                };

                _updateDeviceInfo(CurrentDevice);
                _fmt.Status("下载模式连接", true);

                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 连接诊断模式
        /// </summary>
        public async Task<bool> ConnectDiagModeAsync(string port)
        {
            if (IsOperating) return false;

            _fmt.Section("连接 Unisoc 诊断模式");
            _updateStatus("连接中...");

            try
            {
                if (!_diagProtocol!.Open(port))
                {
                    _fmt.Error("端口打开失败");
                    return false;
                }

                CurrentPort = port;

                // 进入诊断模式
                await _diagProtocol.EnterDiagModeAsync();

                // 获取版本信息
                var version = await _diagProtocol.GetSoftwareVersionAsync();

                CurrentDevice = new UnisocDeviceInfo
                {
                    Port = port,
                    Mode = "Diag",
                    BootVersion = version ?? "Unknown"
                };

                _updateDeviceInfo(CurrentDevice);
                _fmt.Status("诊断模式连接", true);

                return true;
            }
            catch (Exception ex)
            {
                _fmt.Error($"连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _sprdProtocol?.Close();
            _diagProtocol?.Close();
            CurrentPort = null;
            CurrentDevice = null;
        }

        #endregion

        #region FDL 操作

        /// <summary>
        /// 发送 FDL1
        /// </summary>
        public async Task<bool> SendFdl1Async()
        {
            if (!IsConnected || string.IsNullOrEmpty(Fdl1Path))
            {
                _fmt.Error("未连接或未设置 FDL1 路径");
                return false;
            }

            if (!File.Exists(Fdl1Path))
            {
                _fmt.Error($"FDL1 文件不存在: {Fdl1Path}");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("发送 FDL1");
                _updateStatus("发送 FDL1...");

                var fdl1Data = await File.ReadAllBytesAsync(Fdl1Path, _cts.Token);
                _log($"[Unisoc] FDL1 大小: {fdl1Data.Length} 字节", "#3498DB");

                var result = await _sprdProtocol!.SendFdlAsync(fdl1Data, Fdl1Address, _cts.Token);

                if (result && CurrentDevice != null)
                {
                    CurrentDevice.FdlLoaded = true;
                    _updateDeviceInfo(CurrentDevice);
                }

                _fmt.Status("FDL1 发送", result);
                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"FDL1 发送失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 发送 FDL2
        /// </summary>
        public async Task<bool> SendFdl2Async()
        {
            if (!IsConnected || string.IsNullOrEmpty(Fdl2Path))
            {
                _fmt.Error("未连接或未设置 FDL2 路径");
                return false;
            }

            if (!File.Exists(Fdl2Path))
            {
                _fmt.Error($"FDL2 文件不存在: {Fdl2Path}");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("发送 FDL2");
                _updateStatus("发送 FDL2...");

                var fdl2Data = await File.ReadAllBytesAsync(Fdl2Path, _cts.Token);
                _log($"[Unisoc] FDL2 大小: {fdl2Data.Length} 字节", "#3498DB");

                var result = await _sprdProtocol!.SendFdlAsync(fdl2Data, Fdl2Address, _cts.Token);
                _fmt.Status("FDL2 发送", result);
                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"FDL2 发送失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region PAC 固件操作

        /// <summary>
        /// 加载 PAC 固件
        /// </summary>
        public async Task<bool> LoadPacFirmwareAsync(string pacPath)
        {
            if (IsOperating) return false;

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.Section("加载 PAC 固件");
                _updateStatus("解析 PAC...");

                PacFilePath = pacPath;

                var result = await _pacExtractor!.ParseAsync(pacPath, _cts.Token);

                if (result)
                {
                    // 更新分区列表
                    Partitions.Clear();
                    foreach (var part in _pacExtractor.Partitions)
                    {
                        if (part.DataSize > 0 && !string.IsNullOrEmpty(part.FileName))
                        {
                            // 跳过 XML/INI 等配置文件
                            if (part.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                part.FileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                                continue;

                            Partitions.Add(new UnisocPartitionInfo
                            {
                                FileId = part.PartitionName,
                                Name = part.PartitionName.ToLower(),
                                Size = part.DataSize,
                                FilePath = part.FileName,
                                IsSelected = !part.PartitionName.Contains("nv", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }

                    // 提取 FDL 信息
                    var (fdl1, fdl2) = _pacExtractor.GetFdlInfo();
                    if (fdl1 != null)
                    {
                        _log($"[PAC] FDL1: {fdl1.FileName} (地址: 0x{fdl1.Address:X})", "#3498DB");
                        Fdl1Address = $"0x{fdl1.Address:X}";
                    }
                    if (fdl2 != null)
                    {
                        _log($"[PAC] FDL2: {fdl2.FileName}", "#3498DB");
                    }

                    PartitionsLoaded?.Invoke(Partitions.ToList());
                    _fmt.Status("PAC 加载", true);
                }

                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"PAC 加载失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 提取 PAC 固件
        /// </summary>
        public async Task<bool> ExtractPacFirmwareAsync(string outputDir)
        {
            if (_pacExtractor == null || _pacExtractor.Partitions.Count == 0)
            {
                _fmt.Error("请先加载 PAC 固件");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("提取 PAC 固件");
                _updateStatus("提取中...");

                var result = await _pacExtractor.ExtractAsync(outputDir, null, _cts.Token);
                _fmt.Status("PAC 提取", result);
                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"PAC 提取失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 刷写分区
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partitionName, string filePath)
        {
            if (!IsConnected)
            {
                _fmt.Error("设备未连接");
                return false;
            }

            if (!File.Exists(filePath))
            {
                _fmt.Error($"文件不存在: {filePath}");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection($"刷写分区: {partitionName}");
                _updateStatus($"刷写 {partitionName}...");

                var data = await File.ReadAllBytesAsync(filePath, _cts.Token);
                var result = await _sprdProtocol!.WritePartitionAsync(partitionName, data, _cts.Token);

                _fmt.Status($"刷写 {partitionName}", result);
                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"刷写失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 备份分区
        /// </summary>
        public async Task<bool> BackupPartitionAsync(string partitionName, long size, string outputPath)
        {
            if (!IsConnected)
            {
                _fmt.Error("设备未连接");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection($"备份分区: {partitionName}");
                _updateStatus($"备份 {partitionName}...");

                var data = await _sprdProtocol!.ReadPartitionAsync(partitionName, 0, size, _cts.Token);

                if (data != null)
                {
                    await File.WriteAllBytesAsync(outputPath, data, _cts.Token);
                    _fmt.Status($"备份 {partitionName}", true);
                    return true;
                }

                _fmt.Status($"备份 {partitionName}", false);
                return false;
            }
            catch (Exception ex)
            {
                _fmt.Error($"备份失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName)
        {
            if (!IsConnected)
            {
                _fmt.Error("设备未连接");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection($"擦除分区: {partitionName}");
                _updateStatus($"擦除 {partitionName}...");

                var result = await _sprdProtocol!.ErasePartitionAsync(partitionName, _cts.Token);
                _fmt.Status($"擦除 {partitionName}", result);
                return result;
            }
            catch (Exception ex)
            {
                _fmt.Error($"擦除失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region Diag 操作

        /// <summary>
        /// 读取 IMEI
        /// </summary>
        public async Task<(string? Imei1, string? Imei2)> ReadImeiAsync()
        {
            if (_diagProtocol == null || !_diagProtocol.IsConnected)
            {
                _fmt.Error("诊断通道未连接");
                return (null, null);
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("读取 IMEI");

                var imei1 = await _diagProtocol.ReadImeiAsync(1, _cts.Token);
                var imei2 = await _diagProtocol.ReadImeiAsync(2, _cts.Token);

                return (imei1, imei2);
            }
            catch (Exception ex)
            {
                _fmt.Error($"读取 IMEI 失败: {ex.Message}");
                return (null, null);
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 写入 IMEI
        /// </summary>
        public async Task<bool> WriteImeiAsync(string imei, int slot = 1)
        {
            if (_diagProtocol == null || !_diagProtocol.IsConnected)
            {
                _fmt.Error("诊断通道未连接");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection($"写入 IMEI{slot}");
                return await _diagProtocol.WriteImeiAsync(imei, slot, _cts.Token);
            }
            catch (Exception ex)
            {
                _fmt.Error($"写入 IMEI 失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        /// <summary>
        /// 恢复出厂设置
        /// </summary>
        public async Task<bool> FactoryResetAsync()
        {
            if (_diagProtocol == null || !_diagProtocol.IsConnected)
            {
                _fmt.Error("诊断通道未连接");
                return false;
            }

            IsOperating = true;
            _cts = new CancellationTokenSource();

            try
            {
                _fmt.SubSection("恢复出厂设置");
                return await _diagProtocol.FactoryResetAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _fmt.Error($"恢复出厂设置失败: {ex.Message}");
                return false;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootDeviceAsync()
        {
            if (_sprdProtocol?.IsConnected == true)
            {
                return await _sprdProtocol.ResetDeviceAsync();
            }
            else if (_diagProtocol?.IsConnected == true)
            {
                return await _diagProtocol.RebootAsync();
            }

            _fmt.Error("设备未连接");
            return false;
        }

        /// <summary>
        /// 关闭设备
        /// </summary>
        public async Task<bool> PowerOffDeviceAsync()
        {
            if (_sprdProtocol?.IsConnected == true)
            {
                return await _sprdProtocol.PowerOffAsync();
            }
            else if (_diagProtocol?.IsConnected == true)
            {
                return await _diagProtocol.PowerOffAsync();
            }

            _fmt.Error("设备未连接");
            return false;
        }

        /// <summary>
        /// 取消当前操作
        /// </summary>
        public void CancelOperation()
        {
            _cts?.Cancel();
        }

        #endregion

        public void Dispose()
        {
            StopDeviceWatch();
            Disconnect();
            _sprdProtocol?.Dispose();
            _diagProtocol?.Dispose();
            _pacExtractor?.Dispose();
        }
    }
}
