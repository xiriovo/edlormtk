using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.DA;
using tools.Modules.MTK.Models;
using tools.Modules.MTK.Exploit;
using tools.Modules.MTK.Storage;
using tools.Modules.MTK.Resources;

namespace tools.Modules.MTK
{
    /// <summary>
    /// MTK 刷机服务状态
    /// </summary>
    public enum MtkState
    {
        Disconnected,
        WaitingForDevice,
        Handshaking,
        Connected,
        DisablingWatchdog,
        UploadingDA1,
        DA1Running,
        InitializingDram,
        UploadingDA2,
        DA2Running,
        Ready,
        Flashing,
        Reading,
        Erasing,
        Error
    }

    /// <summary>
    /// MTK 刷机主服务
    /// </summary>
    public class MtkService : IDisposable
    {
        private readonly PreloaderProtocol _preloader;
        private readonly XFlashProtocol _xflash;
        private readonly DAConfig _daConfig;
        private CancellationTokenSource? _cts;

        public event Action<string>? OnLog;
        public event Action<MtkState>? OnStateChanged;
        public event Action<long, long>? OnProgress;

        public MtkState State { get; private set; } = MtkState.Disconnected;
        public DeviceInfo? DeviceInfo => _preloader.DeviceInfo;
        public List<Protocol.MtkPartitionInfo> Partitions => _xflash.Partitions;
        public StorageInfo? StorageInfo => _xflash.StorageInfo;
        public ChipConfig? ChipConfig { get; private set; }
        public string DaVersion => _xflash.DaVersion;
        public DAMode CurrentDaMode { get; private set; } = DAMode.Legacy;

        public MtkService()
        {
            _preloader = new PreloaderProtocol();
            _xflash = new XFlashProtocol();
            _daConfig = new DAConfig();

            _preloader.OnLog += msg => OnLog?.Invoke(msg);
            _xflash.OnLog += msg => OnLog?.Invoke(msg);
            _xflash.OnProgress += (cur, total) => OnProgress?.Invoke(cur, total);
        }

        #region 初始化

        /// <summary>
        /// 加载 DA 文件
        /// </summary>
        public bool LoadDA(string daPath)
        {
            Log($"Loading DA file: {daPath}");
            bool result = _daConfig.LoadDAFile(daPath);
            if (result)
            {
                Log($"DA file loaded, supported chips: {string.Join(", ", _daConfig.GetSupportedChips())}");
            }
            return result;
        }

        /// <summary>
        /// 加载内置 DA 文件
        /// </summary>
        /// <param name="version">DA 版本 (5 或 6)</param>
        public bool LoadBuiltInDA(int version = 5)
        {
            Log($"Loading built-in DA V{version}...");
            var daData = PayloadManager.GetDA(version);
            if (daData == null)
            {
                Log($"Built-in DA V{version} not found");
                return false;
            }

            // 临时保存到文件并加载
            string tempPath = Path.Combine(Path.GetTempPath(), $"MTK_DA_V{version}.bin");
            File.WriteAllBytes(tempPath, daData);
            bool result = _daConfig.LoadDAFile(tempPath);
            if (result)
            {
                Log($"Built-in DA V{version} loaded successfully");
            }
            return result;
        }

        /// <summary>
        /// 获取芯片专用 Payload
        /// </summary>
        public byte[]? GetChipPayload(ushort hwCode)
        {
            return PayloadManager.GetChipPayload(hwCode);
        }

        /// <summary>
        /// 获取最佳 Payload (芯片专用或通用)
        /// </summary>
        public byte[]? GetBestPayload(ushort hwCode)
        {
            return PayloadManager.GetBestPayload(hwCode);
        }

        /// <summary>
        /// 加载 Preloader (用于提取 EMI)
        /// </summary>
        public bool LoadPreloader(string preloaderPath)
        {
            Log($"Loading preloader for EMI: {preloaderPath}");
            return _daConfig.ExtractEmi(preloaderPath);
        }

        #endregion

        #region 连接流程

        /// <summary>
        /// 完整连接流程
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
        {
            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                // ==================== 阶段1: Preloader 握手 ====================
                SetState(MtkState.Handshaking);
                Log("Connecting to device...");
                Log($"Port: {portName}");

                if (!await _preloader.ConnectAsync(portName, _cts.Token))
                {
                    Log("Handshake failed");
                    SetState(MtkState.Error);
                    return false;
                }

                SetState(MtkState.Connected);
                Log($"Connected! HW Code: 0x{DeviceInfo!.HwCode:X4}");

                // 获取芯片配置
                ChipConfig = ChipDatabase.GetConfig(DeviceInfo.HwCode) ?? ChipDatabase.GetDefaultConfig();
                Log($"Chip: {ChipConfig.Name} ({ChipConfig.Description})");
                Log($"DA Mode: {ChipConfig.DaMode}");
                CurrentDaMode = ChipConfig.DaMode;

                // 根据 Preloader 能力判断是否支持 XFlash
                if (DeviceInfo.SupportsXFlash)
                {
                    CurrentDaMode = DAMode.XFlash;
                    Log("Device supports XFlash mode");
                }

                // ==================== 阶段2: 禁用看门狗 ====================
                SetState(MtkState.DisablingWatchdog);
                Log($"Disabling watchdog at 0x{ChipConfig.Watchdog:X8}");
                _preloader.DisableWatchdog(ChipConfig.Watchdog);

                // ==================== 阶段3: 选择并加载 DA ====================
                if (!_daConfig.SelectDA(DeviceInfo.HwCode, DeviceInfo.HwVersion, DeviceInfo.SwVersion))
                {
                    Log($"No matching DA found for HW Code 0x{DeviceInfo.HwCode:X4}");
                    SetState(MtkState.Error);
                    return false;
                }

                var currentDA = _daConfig.CurrentDA!;
                Log($"Selected DA: HW=0x{currentDA.HwCode:X4}, Ver={currentDA.HwVersion}.{currentDA.SwVersion}");
                Log($"  DA1: Addr=0x{currentDA.DA1Region.StartAddress:X8}, Size=0x{currentDA.DA1Region.Length:X}");
                Log($"  DA2: Addr=0x{currentDA.DA2Region.StartAddress:X8}, Size=0x{currentDA.DA2Region.Length:X}");

                // ==================== 阶段4: 发送 DA1 ====================
                SetState(MtkState.UploadingDA1);
                Log("Uploading DA1 (Stage 1)...");

                var da1Region = currentDA.DA1Region;
                if (!_preloader.SendDA(
                    da1Region.StartAddress,
                    da1Region.Length,
                    da1Region.SignatureLength,
                    _daConfig.DA1Data!))
                {
                    Log("Failed to upload DA1");
                    SetState(MtkState.Error);
                    return false;
                }

                // ==================== 阶段5: 跳转到 DA1 ====================
                Log("Jumping to DA1...");
                if (!_preloader.JumpDA(da1Region.StartAddress))
                {
                    Log("Failed to jump to DA1");
                    SetState(MtkState.Error);
                    return false;
                }

                // ==================== 阶段6: DA1 同步 ====================
                SetState(MtkState.DA1Running);
                Log("Waiting for DA1 sync...");
                if (!_preloader.SyncDA1())
                {
                    Log("DA1 sync failed");
                    SetState(MtkState.Error);
                    return false;
                }
                Log("DA1 sync completed");

                // 获取串口并传递给 XFlash
                _xflash.AttachPort(_preloader.Port!);

                // ==================== 阶段7: 检查连接代理 ====================
                string agent = _xflash.GetConnectionAgent();
                Log($"Connection agent: {agent}");

                // ==================== 阶段8: 发送 EMI (如果从 BROM 启动) ====================
                if (agent == "brom")
                {
                    SetState(MtkState.InitializingDram);
                    
                    if (_daConfig.EmiData != null && _daConfig.EmiData.Length > 0)
                    {
                        Log($"Sending EMI configuration ({_daConfig.EmiData.Length} bytes)...");
                        if (!_xflash.SendEmi(_daConfig.EmiData))
                        {
                            Log("Failed to send EMI - DRAM may not initialize");
                            // 某些设备可能不需要 EMI，继续尝试
                        }
                        else
                        {
                            Log("EMI configuration sent, DRAM initialized");
                        }
                    }
                    else
                    {
                        Log("Warning: No EMI data available, DRAM may not initialize properly");
                        Log("Consider providing a preloader file to extract EMI configuration");
                    }
                }
                else if (agent == "preloader")
                {
                    Log("Connected via preloader, DRAM already initialized");
                }

                // ==================== 阶段9: 发送 DA2 ====================
                SetState(MtkState.UploadingDA2);
                Log("Uploading DA2 (Stage 2)...");

                var da2Region = currentDA.DA2Region;
                byte[] da2Data = _daConfig.DA2Data!;

                // 如果 DA2 需要补丁 (绕过安全检查)
                if (!DeviceInfo.TargetConfig.SbcEnabled)
                {
                    // 可以在这里应用补丁
                }

                if (!_xflash.BootTo(da2Region.StartAddress, da2Data))
                {
                    Log("Failed to boot DA2");
                    SetState(MtkState.Error);
                    return false;
                }

                // ==================== 阶段10: DA2 初始化 ====================
                SetState(MtkState.DA2Running);
                Log("DA2 running, initializing...");

                // 设置参数
                _xflash.SetResetKey(0x68);
                _xflash.SetChecksumLevel(0);

                // 检查 SLA 状态
                bool slaEnabled = _xflash.GetSlaStatus();
                if (slaEnabled)
                {
                    Log("DA SLA is enabled, authentication may be required");
                }

                // 初始化设备信息
                _xflash.Reinit(display: true);

                SetState(MtkState.Ready);
                Log("═══════════════════════════════════════════════════════════");
                Log($"Device Ready!");
                Log($"  Storage: {StorageInfo?.Type}");
                Log($"  Total Size: {StorageInfo?.TotalSize / 1024 / 1024 / 1024}GB");
                Log($"  DA Version: {DaVersion}");
                Log($"  Partitions: {Partitions.Count}");
                Log("═══════════════════════════════════════════════════════════");

                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect error: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                SetState(MtkState.Error);
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();

            try
            {
                if (State == MtkState.Ready)
                {
                    Log("Shutting down device...");
                    _xflash.Shutdown();
                }
            }
            catch { }

            _preloader.Disconnect();
            _xflash.Dispose();
            SetState(MtkState.Disconnected);
            Log("Disconnected");
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 查找分区
        /// </summary>
        public MtkPartitionInfo? FindPartition(string name)
        {
            return Partitions.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 读取分区
        /// </summary>
        public async Task<byte[]?> ReadPartitionAsync(string partitionName, CancellationToken ct = default)
        {
            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                Log($"Partition not found: {partitionName}");
                return null;
            }

            SetState(MtkState.Reading);
            Log($"Reading partition: {partitionName}");
            Log($"  Offset: 0x{partition.Offset:X}");
            Log($"  Size: 0x{partition.Size:X} ({partition.Size / 1024 / 1024}MB)");

            var data = await Task.Run(() =>
                _xflash.ReadFlash(partition.Offset, partition.Size, "user"), ct);

            SetState(MtkState.Ready);

            if (data != null)
            {
                Log($"Read completed: {data.Length} bytes");
            }
            else
            {
                Log("Read failed");
            }

            return data;
        }

        /// <summary>
        /// 读取分区到文件
        /// </summary>
        public async Task<bool> ReadPartitionToFileAsync(string partitionName, string filePath, CancellationToken ct = default)
        {
            var data = await ReadPartitionAsync(partitionName, ct);
            if (data == null)
                return false;

            await File.WriteAllBytesAsync(filePath, data, ct);
            Log($"Saved to: {filePath}");
            return true;
        }

        /// <summary>
        /// 读取原始地址
        /// </summary>
        public async Task<byte[]?> ReadFlashAsync(ulong address, ulong length, string partType = "user", CancellationToken ct = default)
        {
            SetState(MtkState.Reading);
            Log($"Reading flash at 0x{address:X}, length 0x{length:X}");

            var data = await Task.Run(() =>
                _xflash.ReadFlash(address, length, partType), ct);

            SetState(MtkState.Ready);
            return data;
        }

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, byte[] data, CancellationToken ct = default)
        {
            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                Log($"Partition not found: {partitionName}");
                return false;
            }

            if ((ulong)data.Length > partition.Size)
            {
                Log($"Data too large for partition: {data.Length} > {partition.Size}");
                return false;
            }

            SetState(MtkState.Flashing);
            Log($"Writing partition: {partitionName}");
            Log($"  Offset: 0x{partition.Offset:X}");
            Log($"  Data Size: 0x{data.Length:X} ({data.Length / 1024 / 1024}MB)");

            bool result = await Task.Run(() =>
                _xflash.WriteFlash(partition.Offset, data, "user"), ct);

            SetState(MtkState.Ready);

            if (result)
                Log($"Partition {partitionName} written successfully");
            else
                Log($"Failed to write partition {partitionName}");

            return result;
        }

        /// <summary>
        /// 从文件写入分区
        /// </summary>
        public async Task<bool> WritePartitionFromFileAsync(string partitionName, string filePath, CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
            {
                Log($"File not found: {filePath}");
                return false;
            }

            Log($"Loading file: {filePath}");
            byte[] data = await File.ReadAllBytesAsync(filePath, ct);
            Log($"File size: {data.Length} bytes");

            return await WritePartitionAsync(partitionName, data, ct);
        }

        /// <summary>
        /// 写入原始地址
        /// </summary>
        public async Task<bool> WriteFlashAsync(ulong address, byte[] data, string partType = "user", CancellationToken ct = default)
        {
            SetState(MtkState.Flashing);
            Log($"Writing flash at 0x{address:X}, length 0x{data.Length:X}");

            bool result = await Task.Run(() =>
                _xflash.WriteFlash(address, data, partType), ct);

            SetState(MtkState.Ready);
            return result;
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            var partition = FindPartition(partitionName);
            if (partition == null)
            {
                Log($"Partition not found: {partitionName}");
                return false;
            }

            SetState(MtkState.Erasing);
            Log($"Erasing partition: {partitionName}");
            Log($"  Offset: 0x{partition.Offset:X}");
            Log($"  Size: 0x{partition.Size:X}");

            bool result = await Task.Run(() =>
                _xflash.FormatFlash(partition.Offset, partition.Size, "user"), ct);

            SetState(MtkState.Ready);

            if (result)
                Log($"Partition {partitionName} erased successfully");
            else
                Log($"Failed to erase partition {partitionName}");

            return result;
        }

        /// <summary>
        /// 擦除原始地址
        /// </summary>
        public async Task<bool> EraseFlashAsync(ulong address, ulong length, string partType = "user", CancellationToken ct = default)
        {
            SetState(MtkState.Erasing);
            Log($"Erasing flash at 0x{address:X}, length 0x{length:X}");

            bool result = await Task.Run(() =>
                _xflash.FormatFlash(address, length, partType), ct);

            SetState(MtkState.Ready);
            return result;
        }

        #endregion

        #region 设备操作

        /// <summary>
        /// 重启设备
        /// </summary>
        public bool Reboot()
        {
            Log("Rebooting device...");
            return _xflash.Reboot();
        }

        /// <summary>
        /// 关机
        /// </summary>
        public bool Shutdown(ShutdownMode mode = ShutdownMode.Normal)
        {
            Log($"Shutting down device (mode: {mode})...");
            return _xflash.Shutdown(mode);
        }

        /// <summary>
        /// 进入 FastBoot 模式
        /// </summary>
        public bool RebootToFastboot()
        {
            Log("Rebooting to FastBoot...");
            return _xflash.Shutdown(ShutdownMode.FastBoot);
        }

        /// <summary>
        /// 获取数据包长度配置
        /// </summary>
        public PacketLength? GetPacketLength()
        {
            return _xflash.GetPacketLength();
        }

        #endregion

        #region 漏洞利用

        private ExploitManager? _exploitManager;

        /// <summary>
        /// 获取推荐的漏洞利用类型
        /// </summary>
        public string GetRecommendedExploit()
        {
            return ChipConfig?.GetRecommendedExploit() ?? "None";
        }

        /// <summary>
        /// 是否需要使用漏洞利用
        /// </summary>
        public bool NeedsExploit => DeviceInfo?.TargetConfig.SlaEnabled == true ||
                                    DeviceInfo?.TargetConfig.DaaEnabled == true;

        /// <summary>
        /// 初始化漏洞利用管理器
        /// </summary>
        private void InitExploitManager()
        {
            if (ChipConfig != null && DeviceInfo?.TargetConfig != null)
            {
                _exploitManager = new ExploitManager(_preloader, ChipConfig, DeviceInfo.TargetConfig);
                _exploitManager.OnLog += msg => OnLog?.Invoke(msg);
            }
        }

        /// <summary>
        /// 通过漏洞利用 Dump BROM
        /// </summary>
        public async Task<byte[]?> DumpBromAsync(string outputPath = "", CancellationToken ct = default)
        {
            if (_exploitManager == null)
            {
                InitExploitManager();
            }

            Log("Dumping BROM via exploit...");
            var brom = await _exploitManager!.DumpBromAsync();

            if (brom != null && !string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllBytesAsync(outputPath, brom, ct);
                Log($"BROM saved to {outputPath}");
            }

            return brom;
        }

        /// <summary>
        /// 通过漏洞利用 Dump Preloader
        /// </summary>
        public async Task<byte[]?> DumpPreloaderAsync(string outputPath = "", CancellationToken ct = default)
        {
            if (_exploitManager == null)
            {
                InitExploitManager();
            }

            Log("Dumping Preloader via exploit...");
            var preloader = await _exploitManager!.DumpPreloaderAsync();

            if (preloader != null && !string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllBytesAsync(outputPath, preloader, ct);
                Log($"Preloader saved to {outputPath}");
            }

            return preloader;
        }

        /// <summary>
        /// 禁用安全限制
        /// </summary>
        public bool DisableSecurity()
        {
            if (_exploitManager == null)
            {
                InitExploitManager();
            }

            Log("Disabling security via exploit...");
            return _exploitManager?.DisableSecurity() ?? false;
        }

        /// <summary>
        /// 运行自定义 Payload
        /// </summary>
        public async Task<bool> RunPayloadAsync(byte[] payload, uint? address = null, CancellationToken ct = default)
        {
            if (_exploitManager == null)
            {
                InitExploitManager();
            }

            Log("Running custom payload via exploit...");
            return await _exploitManager!.RunPayloadAsync(payload, address);
        }

        /// <summary>
        /// 崩溃设备进入 BROM
        /// </summary>
        public bool CrashToBrom()
        {
            if (_exploitManager == null)
            {
                InitExploitManager();
            }

            Log("Crashing device to BROM mode...");
            return _exploitManager?.CrashToBrom() ?? false;
        }

        #endregion

        #region 工具方法

        private void SetState(MtkState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[MTK] {message}");
            System.Diagnostics.Debug.WriteLine($"[MTK] {message}");
        }

        public void Dispose()
        {
            Disconnect();
            _exploitManager?.Dispose();
            _preloader.Dispose();
            _xflash.Dispose();
        }

        #endregion
    }
}
