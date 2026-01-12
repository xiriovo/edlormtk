// ============================================================================
// MultiFlash TOOL - ADB/Fastboot UI Service
// ADB/Fastboot UI 服务 | ADB/Fastboot UIサービス | ADB/Fastboot UI 서비스
// ============================================================================
// [EN] UI service layer for ADB and Fastboot operations
//      Supports device detection, shell commands, partition flash
// [中文] ADB 和 Fastboot 操作的 UI 服务层
//       支持设备检测、Shell 命令、分区刷写
// [日本語] ADBおよびFastboot操作用UIサービスレイヤー
//         デバイス検出、シェルコマンド、パーティションフラッシュをサポート
// [한국어] ADB 및 Fastboot 작업을 위한 UI 서비스 레이어
//         장치 감지, 셸 명령, 파티션 플래시 지원
// [Español] Capa de servicio UI para operaciones ADB y Fastboot
//           Soporta detección de dispositivos, comandos shell, flash de particiones
// [Русский] Уровень сервиса UI для операций ADB и Fastboot
//           Поддержка обнаружения устройств, shell-команд, прошивки разделов
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using tools.Modules.Common;

namespace tools.Modules.AdbFastboot
{
    /// <summary>
    /// ADB/Fastboot Partition Info
    /// ADB/Fastboot 分区信息 | パーティション情報 | 파티션 정보
    /// </summary>
    public class AdbFastbootPartitionInfo
    {
        public string Name { get; set; } = "";
        public string Size { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Type { get; set; } = "";
        public bool IsSelected { get; set; }
        public string FilePath { get; set; } = "";
        public bool HasFile => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    }

    /// <summary>
    /// ADB/Fastboot UI 服务
    /// </summary>
    public class AdbFastbootUIService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, string> _log;
        private readonly Action<double, string> _updateProgress;
        private readonly Action<string> _updateStatus;

        // 协议
        private AdbProtocol? _adb;
        private FastbootProtocol? _fastboot;
        private DeviceWatcher? _deviceWatcher;

        // 状态
        public bool IsConnected { get; private set; }
        public bool IsAdb { get; private set; }
        public bool IsFastboot { get; private set; }
        public bool IsOperating { get; private set; }

        // 设备信息
        public string DeviceSerial { get; private set; } = "";
        public string DeviceModel { get; private set; } = "";
        public string DeviceProduct { get; private set; } = "";
        public string BootloaderStatus { get; private set; } = "";
        public string CurrentPort { get; private set; } = "";

        // 分区
        public ObservableCollection<AdbFastbootPartitionInfo> Partitions { get; } = new();

        // 取消令牌
        private CancellationTokenSource? _cts;

        // 事件
        public event Action<string>? DeviceArrived;
        public event Action? DeviceRemoved;
#pragma warning disable CS0067 // 事件预留给未来使用
        public event Action<TimeSpan, double, long>? TransferStatsUpdated;
#pragma warning restore CS0067

        public AdbFastbootUIService(
            Dispatcher dispatcher,
            Action<string, string> log,
            Action<double, string> updateProgress,
            Action<string> updateStatus)
        {
            _dispatcher = dispatcher;
            _log = log;
            _updateProgress = updateProgress;
            _updateStatus = updateStatus;

            _adb = new AdbProtocol();
            _fastboot = new FastbootProtocol();

            _adb.OnLog += msg => _log(msg, "#10B981");
            _adb.OnProgress += (cur, total) => ReportProgress(cur, total);

            _fastboot.OnLog += msg => _log(msg, "#F59E0B");
            _fastboot.OnProgress += (cur, total) => ReportProgress(cur, total);
        }

        #region 设备监听

        /// <summary>
        /// 启动设备监听
        /// </summary>
        public void StartDeviceWatcher()
        {
            _deviceWatcher = new DeviceWatcher();
            _deviceWatcher.DeviceArrived += OnDeviceArrived;
            _deviceWatcher.DeviceRemoved += OnDeviceRemoved;
            _deviceWatcher.Start();
            _log("[ADB/Fastboot] 设备监听已启动", "#888888");
        }

        /// <summary>
        /// 停止设备监听
        /// </summary>
        public void StopDeviceWatcher()
        {
            _deviceWatcher?.Stop();
            _deviceWatcher = null;
        }

        private void OnDeviceArrived(object? sender, DeviceInfo device)
        {
            // 检查是否是 ADB 或 Fastboot 设备
            if (device.Type == DeviceType.AdbDevice)
            {
                _log($"[ADB] 检测到 ADB 设备: {device.PortName}", "#10B981");
                CurrentPort = device.PortName;
                _dispatcher.Invoke(() => DeviceArrived?.Invoke(device.PortName));
            }
            else if (device.Type == DeviceType.FastbootDevice)
            {
                _log($"[Fastboot] 检测到 Fastboot 设备: {device.PortName}", "#F59E0B");
                CurrentPort = device.PortName;
                _dispatcher.Invoke(() => DeviceArrived?.Invoke(device.PortName));
            }
        }

        private void OnDeviceRemoved(object? sender, DeviceInfo device)
        {
            if (device.PortName == CurrentPort)
            {
                _log($"[ADB/Fastboot] 设备已移除: {device.PortName}", "#EF4444");
                Disconnect();
                _dispatcher.Invoke(() => DeviceRemoved?.Invoke());
            }
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接 ADB 设备
        /// </summary>
        public async Task<bool> ConnectAdbAsync()
        {
            if (IsConnected || _adb == null) return false;

            _cts = new CancellationTokenSource();
            _updateStatus("正在连接 ADB...");

            try
            {
                if (await _adb.ConnectUsbAsync(ct: _cts.Token))
                {
                    IsConnected = true;
                    IsAdb = true;
                    IsFastboot = false;

                    DeviceSerial = _adb.DeviceInfo?.SerialNumber ?? "";
                    DeviceModel = _adb.DeviceInfo?.Model ?? "";
                    DeviceProduct = _adb.DeviceInfo?.Product ?? "";

                    _updateStatus("ADB 已连接");
                    _log($"[ADB] 已连接: {DeviceModel} ({DeviceSerial})", "#10B981");
                    return true;
                }

                _updateStatus("ADB 连接失败");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[ADB] 连接错误: {ex.Message}", "#EF4444");
                _updateStatus("连接错误");
                return false;
            }
        }

        /// <summary>
        /// 连接 Fastboot 设备
        /// </summary>
        public bool ConnectFastboot()
        {
            if (IsConnected || _fastboot == null) return false;

            _cts = new CancellationTokenSource();
            _updateStatus("正在连接 Fastboot...");

            try
            {
                if (_fastboot.Connect())
                {
                    IsConnected = true;
                    IsAdb = false;
                    IsFastboot = true;

                    DeviceSerial = _fastboot.DeviceInfo?.SerialNumber ?? "";
                    DeviceModel = _fastboot.DeviceInfo?.Product ?? "";
                    BootloaderStatus = _fastboot.DeviceInfo?.Unlocked ?? "";

                    _updateStatus("Fastboot 已连接");
                    _log($"[Fastboot] 已连接: {DeviceModel} ({DeviceSerial})", "#F59E0B");

                    // 获取分区列表
                    LoadFastbootPartitions();
                    return true;
                }

                _updateStatus("Fastboot 连接失败");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[Fastboot] 连接错误: {ex.Message}", "#EF4444");
                _updateStatus("连接错误");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _adb?.Disconnect();
            _fastboot?.Disconnect();

            IsConnected = false;
            IsAdb = false;
            IsFastboot = false;
            DeviceSerial = "";
            DeviceModel = "";
            DeviceProduct = "";
            BootloaderStatus = "";
            CurrentPort = "";

            _dispatcher.Invoke(() => Partitions.Clear());
            _updateStatus("已断开连接");
        }

        #endregion

        #region ADB 操作

        /// <summary>
        /// 执行 Shell 命令
        /// </summary>
        public async Task<string> ShellAsync(string command)
        {
            if (!IsAdb || _adb == null) return "";

            _log($"[Shell] > {command}", "#888888");
            var result = await _adb.ShellAsync(command, _cts?.Token ?? CancellationToken.None);
            _log($"[Shell] {result.TrimEnd()}", "#888888");
            return result;
        }

        /// <summary>
        /// 推送文件
        /// </summary>
        public async Task<bool> PushFileAsync(string localPath, string remotePath)
        {
            if (!IsAdb || _adb == null || !File.Exists(localPath)) return false;

            IsOperating = true;
            _updateProgress(0, $"推送: {Path.GetFileName(localPath)}");

            try
            {
                return await _adb.PushAsync(localPath, remotePath, _cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                IsOperating = false;
                _updateProgress(100, "完成");
            }
        }

        /// <summary>
        /// 拉取文件
        /// </summary>
        public async Task<bool> PullFileAsync(string remotePath, string localPath)
        {
            if (!IsAdb || _adb == null) return false;

            IsOperating = true;
            _updateProgress(0, $"拉取: {Path.GetFileName(remotePath)}");

            try
            {
                return await _adb.PullAsync(remotePath, localPath, _cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                IsOperating = false;
                _updateProgress(100, "完成");
            }
        }

        /// <summary>
        /// 安装 APK
        /// </summary>
        public async Task<bool> InstallApkAsync(string apkPath)
        {
            if (!IsAdb || _adb == null || !File.Exists(apkPath)) return false;

            _log($"[ADB] 安装 APK: {Path.GetFileName(apkPath)}", "#3B82F6");

            // 推送 APK 到临时目录
            string remotePath = $"/data/local/tmp/{Path.GetFileName(apkPath)}";
            if (!await PushFileAsync(apkPath, remotePath))
            {
                _log("[ADB] APK 推送失败", "#EF4444");
                return false;
            }

            // 使用 pm install 安装
            var result = await ShellAsync($"pm install -r \"{remotePath}\"");

            // 删除临时文件
            await ShellAsync($"rm \"{remotePath}\"");

            if (result.Contains("Success"))
            {
                _log("[ADB] APK 安装成功", "#10B981");
                return true;
            }

            _log($"[ADB] APK 安装失败: {result}", "#EF4444");
            return false;
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(string target = "")
        {
            if (!IsAdb || _adb == null) return false;

            _log($"[ADB] 重启设备{(string.IsNullOrEmpty(target) ? "" : $" 到 {target}")}", "#3B82F6");
            return await _adb.RebootAsync(target, _cts?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 重启到 Fastboot
        /// </summary>
        public async Task<bool> RebootToFastbootAsync()
        {
            if (!IsAdb || _adb == null) return false;

            _log("[ADB] 重启到 Fastboot...", "#F59E0B");
            return await _adb.RebootBootloaderAsync(_cts?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 重启到 Recovery
        /// </summary>
        public async Task<bool> RebootToRecoveryAsync()
        {
            if (!IsAdb || _adb == null) return false;

            _log("[ADB] 重启到 Recovery...", "#3B82F6");
            return await _adb.RebootRecoveryAsync(_cts?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 重启到 EDL
        /// </summary>
        public async Task<bool> RebootToEdlAsync()
        {
            if (!IsAdb || _adb == null) return false;

            _log("[ADB] 重启到 EDL...", "#EF4444");
            return await _adb.RebootEdlAsync(_cts?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 获取设备属性
        /// </summary>
        public async Task<Dictionary<string, string>> GetPropertiesAsync()
        {
            var props = new Dictionary<string, string>();
            if (!IsAdb || _adb == null) return props;

            var result = await ShellAsync("getprop");
            foreach (var line in result.Split('\n'))
            {
                if (line.StartsWith("[") && line.Contains("]: ["))
                {
                    int keyEnd = line.IndexOf(']');
                    int valueStart = line.IndexOf('[', keyEnd);
                    if (keyEnd > 1 && valueStart > 0)
                    {
                        string key = line.Substring(1, keyEnd - 1);
                        string value = line.Substring(valueStart + 1).TrimEnd(']', '\r', '\n');
                        props[key] = value;
                    }
                }
            }
            return props;
        }

        #endregion

        #region Fastboot 操作

        /// <summary>
        /// 加载 Fastboot 分区列表
        /// </summary>
        private void LoadFastbootPartitions()
        {
            if (_fastboot == null) return;

            var partitionNames = _fastboot.GetPartitions();
            var allVars = _fastboot.DeviceInfo?.Variables ?? new Dictionary<string, string>();

            _dispatcher.Invoke(() =>
            {
                Partitions.Clear();
                foreach (var name in partitionNames)
                {
                    string sizeKey = $"partition-size:{name}";
                    string typeKey = $"partition-type:{name}";

                    allVars.TryGetValue(sizeKey, out string? sizeStr);
                    allVars.TryGetValue(typeKey, out string? typeStr);

                    long sizeBytes = 0;
                    if (!string.IsNullOrEmpty(sizeStr) && sizeStr.StartsWith("0x"))
                    {
                        long.TryParse(sizeStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out sizeBytes);
                    }

                    Partitions.Add(new AdbFastbootPartitionInfo
                    {
                        Name = name,
                        Size = FormatSize(sizeBytes),
                        SizeBytes = sizeBytes,
                        Type = typeStr ?? ""
                    });
                }
            });
        }

        /// <summary>
        /// 刷入分区
        /// </summary>
        public async Task<bool> FlashPartitionAsync(string partition, string imagePath)
        {
            if (!IsFastboot || _fastboot == null) return false;

            IsOperating = true;
            _updateStatus($"正在刷入 {partition}...");
            _updateProgress(0, $"刷入: {partition}");

            try
            {
                return await _fastboot.FlashAsync(partition, imagePath, _cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                IsOperating = false;
                _updateProgress(100, "完成");
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public bool ErasePartition(string partition)
        {
            if (!IsFastboot || _fastboot == null) return false;

            _updateStatus($"正在擦除 {partition}...");
            return _fastboot.Erase(partition);
        }

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public bool UnlockBootloader()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.OemUnlock();
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public bool LockBootloader()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.OemLock();
        }

        /// <summary>
        /// 重启到系统
        /// </summary>
        public bool RebootToSystem()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.Reboot();
        }

        /// <summary>
        /// 重启到 Fastboot
        /// </summary>
        public bool RebootToBootloader()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.RebootBootloader();
        }

        /// <summary>
        /// 重启到 Recovery (Fastboot)
        /// </summary>
        public bool FastbootRebootRecovery()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.RebootRecovery();
        }

        /// <summary>
        /// 重启到 EDL (Fastboot)
        /// </summary>
        public bool FastbootRebootEdl()
        {
            if (!IsFastboot || _fastboot == null) return false;
            return _fastboot.RebootEdl();
        }

        /// <summary>
        /// 获取 Fastboot 变量
        /// </summary>
        public string? GetFastbootVar(string name)
        {
            if (!IsFastboot || _fastboot == null) return null;
            return _fastboot.GetVar(name);
        }

        /// <summary>
        /// 发送 OEM 命令
        /// </summary>
        public FastbootResponse? SendOemCommand(string command)
        {
            if (!IsFastboot || _fastboot == null) return null;
            return _fastboot.OemCommand(command);
        }

        /// <summary>
        /// 刷入固件 (多个分区)
        /// </summary>
        public async Task<bool> FlashFirmwareAsync(Dictionary<string, string> partitionImages)
        {
            if (!IsFastboot || _fastboot == null) return false;

            IsOperating = true;
            int total = partitionImages.Count;
            int current = 0;
            int success = 0;

            try
            {
                foreach (var kv in partitionImages)
                {
                    current++;
                    _updateProgress((double)current / total * 100, $"刷入 {kv.Key} ({current}/{total})");

                    if (await FlashPartitionAsync(kv.Key, kv.Value))
                    {
                        success++;
                    }
                    else
                    {
                        _log($"[Fastboot] 分区 {kv.Key} 刷入失败", "#EF4444");
                    }
                }

                _log($"[Fastboot] 刷入完成: {success}/{total} 成功", success == total ? "#10B981" : "#D97706");
                return success == total;
            }
            finally
            {
                IsOperating = false;
            }
        }

        #endregion

        #region 辅助方法

        private void ReportProgress(long current, long total)
        {
            if (total > 0)
            {
                double percent = (double)current / total * 100;
                _updateProgress(percent, $"{percent:F0}%");
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        public void Dispose()
        {
            StopDeviceWatcher();
            Disconnect();
            _adb?.Dispose();
            _fastboot?.Dispose();
        }

        #endregion

        #region 测试功能

        private void Log(string level, string message)
        {
            string color = level switch
            {
                "ERROR" => "#EF4444",  // Red
                "WARN" => "#F59E0B",   // Amber
                "INFO" => "#10B981",   // Green
                _ => "#888888"         // Gray
            };
            _log($"[{level}] {message}", color);
        }

        /// <summary>
        /// 测试 ADB 连接 (通过 ADB Server)
        /// </summary>
        public async Task<bool> TestAdbConnectionAsync()
        {
            Log("INFO", "开始测试 ADB 连接...");

            try
            {
                // 1. 检查 ADB Server
                Log("INFO", "检查 ADB Server...");
                var devices = await AdbProtocol.GetDevicesAsync();

                if (devices.Count == 0)
                {
                    Log("WARN", "未检测到设备，请确保:");
                    Log("WARN", "  1. ADB Server 已运行 (adb start-server)");
                    Log("WARN", "  2. 设备已连接并授权 USB 调试");
                    _updateStatus("未检测到设备");
                    return false;
                }

                Log("INFO", $"检测到 {devices.Count} 个设备:");
                foreach (var (serial, state) in devices)
                {
                    Log("INFO", $"  {serial} - {state}");
                }

                // 2. 连接第一个设备
                var (firstSerial, firstState) = devices[0];
                if (firstState != "device")
                {
                    Log("WARN", $"设备状态异常: {firstState}");
                    return false;
                }

                Log("INFO", $"连接设备: {firstSerial}");
                _adb = new AdbProtocol();
                _adb.OnLog += msg => Log("ADB", msg);

                bool connected = await _adb.ConnectViaServerAsync(firstSerial);
                if (!connected)
                {
                    Log("ERROR", "连接失败");
                    return false;
                }

                Log("INFO", "连接成功!");
                IsConnected = true;
                IsAdb = true;
                DeviceSerial = firstSerial;

                // 3. 获取设备信息
                Log("INFO", "获取设备信息...");
                DeviceModel = await _adb.GetModelAsync();
                DeviceProduct = await _adb.GetPropAsync("ro.product.brand");
                string android = await _adb.GetAndroidVersionAsync();

                Log("INFO", $"  型号: {DeviceModel}");
                Log("INFO", $"  品牌: {DeviceProduct}");
                Log("INFO", $"  Android: {android}");

                _updateStatus($"已连接: {DeviceModel}");

                // 4. 测试 Shell 命令
                Log("INFO", "测试 Shell 命令...");
                string result = await _adb.ShellAsync("echo 'ADB Test OK'");
                Log("INFO", $"  响应: {result.Trim()}");

                Log("INFO", "ADB 测试完成 ✓");
                return true;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"测试失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 测试 Fastboot 连接
        /// </summary>
        public async Task<bool> TestFastbootConnectionAsync()
        {
            Log("INFO", "开始测试 Fastboot 连接...");

            try
            {
                _fastboot = new FastbootProtocol();
                _fastboot.OnLog += msg => Log("Fastboot", msg);

                Log("INFO", "搜索 Fastboot 设备...");
                bool connected = _fastboot.Connect();

                if (!connected)
                {
                    Log("WARN", "未检测到 Fastboot 设备");
                    Log("WARN", "请将设备重启到 Fastboot 模式:");
                    Log("WARN", "  - adb reboot bootloader");
                    Log("WARN", "  - 或按住电源+音量下");
                    _updateStatus("未检测到 Fastboot 设备");
                    return false;
                }

                Log("INFO", "Fastboot 连接成功!");
                IsConnected = true;
                IsFastboot = true;

                // 获取设备信息
                Log("INFO", "获取设备信息...");
                _fastboot.RefreshDeviceInfo();

                if (_fastboot.DeviceInfo != null)
                {
                    DeviceProduct = _fastboot.DeviceInfo.Product;
                    DeviceSerial = _fastboot.DeviceInfo.SerialNumber;
                    BootloaderStatus = _fastboot.DeviceInfo.Unlocked == "yes" ? "已解锁" : "已锁定";

                    Log("INFO", $"  产品: {DeviceProduct}");
                    Log("INFO", $"  序列号: {DeviceSerial}");
                    Log("INFO", $"  Bootloader: {BootloaderStatus}");
                    Log("INFO", $"  Fastbootd: {_fastboot.DeviceInfo.IsFastbootd}");
                    Log("INFO", $"  当前槽位: {_fastboot.DeviceInfo.CurrentSlot}");

                    // 分区信息
                    var partitions = _fastboot.GetPartitionDetails();
                    Log("INFO", $"  分区数量: {partitions.Count}");

                    _updateStatus($"Fastboot: {DeviceProduct}");
                }

                Log("INFO", "Fastboot 测试完成 ✓");
                return true;
            }
            catch (Exception ex)
            {
                Log("ERROR", $"测试失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 执行 Shell 命令测试
        /// </summary>
        public async Task<string> ExecuteShellTestAsync(string command)
        {
            if (_adb == null || !IsAdb)
            {
                Log("WARN", "ADB 未连接");
                return "";
            }

            Log("INFO", $"执行: {command}");
            string result = await _adb.ShellAsync(command);
            Log("INFO", $"结果:\n{result}");
            return result;
        }

        #endregion
    }
}
