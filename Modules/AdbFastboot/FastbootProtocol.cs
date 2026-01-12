// ============================================================================
// MultiFlash TOOL - Fastboot Protocol Implementation
// Fastboot 协议实现 | Fastbootプロトコル | Fastboot 프로토콜
// ============================================================================
// [EN] Fastboot protocol for Android bootloader operations
//      Supports flash, erase, reboot, getvar, oem commands
// [中文] Android Bootloader 操作的 Fastboot 协议
//       支持刷写、擦除、重启、getvar、OEM 命令
// [日本語] Android Bootloader操作用のFastbootプロトコル
//         フラッシュ、消去、再起動、getvar、OEMコマンドをサポート
// [한국어] 안드로이드 부트로더 작업을 위한 Fastboot 프로토콜
//         플래시, 삭제, 재부팅, getvar, OEM 명령 지원
// [Español] Protocolo Fastboot para operaciones de bootloader Android
//           Soporta flash, borrado, reinicio, getvar, comandos OEM
// [Русский] Протокол Fastboot для операций с загрузчиком Android
//           Поддержка прошивки, очистки, перезагрузки, getvar, OEM-команд
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace tools.Modules.AdbFastboot
{
    /// <summary>
    /// Fastboot Response Type / Fastboot 响应类型
    /// Fastboot応答タイプ / Fastboot 응답 유형
    /// </summary>
    public enum FastbootResponseType
    {
        OKAY,   // Success / 成功 / 成功 / 성공
        FAIL,   // Failed / 失败 / 失敗 / 실패
        INFO,   // Info message / 信息 / 情報 / 정보
        DATA,   // 数据传输请求
        Unknown
    }

    /// <summary>
    /// Fastboot 响应
    /// </summary>
    public class FastbootResponse
    {
        public FastbootResponseType Type { get; set; }
        public string Message { get; set; } = "";
        public uint DataSize { get; set; }
        public byte[]? RawData { get; set; }

        public bool IsSuccess => Type == FastbootResponseType.OKAY;
        public bool IsFail => Type == FastbootResponseType.FAIL;

        public static FastbootResponse Parse(byte[] data)
        {
            if (data == null || data.Length < 4)
                return new FastbootResponse { Type = FastbootResponseType.Unknown };

            string header = Encoding.ASCII.GetString(data, 0, 4);
            string message = data.Length > 4 ? Encoding.ASCII.GetString(data, 4, data.Length - 4).TrimEnd('\0') : "";

            var response = new FastbootResponse { Message = message, RawData = data };

            switch (header)
            {
                case "OKAY":
                    response.Type = FastbootResponseType.OKAY;
                    break;
                case "FAIL":
                    response.Type = FastbootResponseType.FAIL;
                    break;
                case "INFO":
                    response.Type = FastbootResponseType.INFO;
                    break;
                case "DATA":
                    response.Type = FastbootResponseType.DATA;
                    // DATA 响应包含 8 字符的十六进制大小
                    if (message.Length >= 8 && uint.TryParse(message.Substring(0, 8), 
                        System.Globalization.NumberStyles.HexNumber, null, out uint size))
                    {
                        response.DataSize = size;
                    }
                    break;
                default:
                    response.Type = FastbootResponseType.Unknown;
                    break;
            }

            return response;
        }
    }

    /// <summary>
    /// Fastboot 设备信息
    /// </summary>
    public class FastbootDeviceInfo
    {
        public string Product { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Secure { get; set; } = "";
        public string Unlocked { get; set; } = "";
        public string CurrentSlot { get; set; } = "";
        public string MaxDownloadSize { get; set; } = "";
        public string PartitionType { get; set; } = "";
        public string Version { get; set; } = "";
        public string VersionBaseband { get; set; } = "";
        public string VersionBootloader { get; set; } = "";
        public Dictionary<string, string> Variables { get; } = new();

        // Fastbootd 增强信息
        public bool IsFastbootd { get; set; } = false;         // 是否在用户空间 fastbootd
        public bool IsSeamlessUpdate { get; set; } = false;    // 是否支持 A/B 分区
        public string SnapshotUpdateStatus { get; set; } = ""; // VAB 状态: none/snapshotted/merging
        public bool HasCowPartitions { get; set; } = false;    // 是否有 COW 分区

        // 分区信息
        public Dictionary<string, long> PartitionSizes { get; } = new();
        public Dictionary<string, bool> PartitionIsLogical { get; } = new();
    }

    /// <summary>
    /// Fastboot 分区信息
    /// </summary>
    public class FastbootPartitionInfo
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public bool IsLogical { get; set; }
        public string SizeFormatted => FormatSize(Size);

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
    }

    /// <summary>
    /// Android Fastboot 协议实现
    /// 
    /// 协议概述：
    /// - 主机驱动的同步 USB 通信
    /// - 使用两个 bulk 端点 (IN/OUT)
    /// - 命令格式: 纯 ASCII 字符串
    /// - 响应格式: OKAY/FAIL/INFO/DATA + 消息
    /// </summary>
    public class FastbootProtocol : IDisposable
    {
        // USB 常量
        private const int FASTBOOT_VID = 0x18D1;  // Google VID
        private const int FASTBOOT_PID = 0x4EE0;  // Fastboot PID
        private const int USB_TIMEOUT = 5000;
        private const int MAX_PACKET_SIZE = 512;
        private const int MAX_DOWNLOAD_SIZE = 256 * 1024 * 1024; // 256MB 默认

        // USB 设备
        private IUsbDevice? _device;
        private UsbEndpointReader? _reader;
        private UsbEndpointWriter? _writer;

        // 状态
        public bool IsConnected => _device != null;
        public FastbootDeviceInfo? DeviceInfo { get; private set; }

        // 事件
        public event Action<string>? OnLog;
        public event Action<long, long>? OnProgress;

        // 已知的 Fastboot VID/PID 列表
        private static readonly (int Vid, int Pid, string Name)[] KnownDevices = new[]
        {
            (0x18D1, 0x4EE0, "Google Fastboot"),
            (0x18D1, 0xD00D, "Google Fastboot (Alt)"),
            (0x22B8, 0x2281, "Motorola Fastboot"),
            (0x0BB4, 0x0C01, "HTC Fastboot"),
            (0x0BB4, 0x0FFF, "HTC Fastboot (Alt)"),
            (0x04E8, 0x6601, "Samsung Fastboot"),
            (0x2717, 0xFF40, "Xiaomi Fastboot"),
            (0x2717, 0xFF48, "Xiaomi Fastboot (Alt)"),
            (0x22D9, 0x2764, "OPPO/OnePlus Fastboot"),
            (0x22D9, 0x2765, "OPPO/OnePlus Fastboot (Alt)"),
            (0x1949, 0x0C21, "ASUS Fastboot"),
            (0x12D1, 0x1057, "Huawei Fastboot"),
            (0x2A70, 0x9011, "OnePlus Fastboot"),
            (0x2A70, 0x9039, "OnePlus Fastboot (Alt)"),
            (0x05C6, 0x9006, "Qualcomm Fastboot"),
            (0x05C6, 0x9008, "Qualcomm EDL (not Fastboot)"),
            (0x2B4C, 0x1001, "Realme Fastboot"),
            (0x0E8D, 0x201C, "MediaTek Fastboot"),
        };

        #region 连接管理

        /// <summary>
        /// 连接 Fastboot 设备
        /// </summary>
        public bool Connect(int vid = 0, int pid = 0)
        {
            try
            {
                Log("正在搜索 Fastboot 设备...");

                // 如果指定了 VID/PID，直接尝试连接
                if (vid > 0 && pid > 0)
                {
                    return ConnectToDevice(vid, pid);
                }

                // 否则尝试已知设备列表
                foreach (var (knownVid, knownPid, name) in KnownDevices)
                {
                    if (ConnectToDevice(knownVid, knownPid))
                    {
                        Log($"已连接: {name}");
                        return true;
                    }
                }

                Log("未找到 Fastboot 设备");
                return false;
            }
            catch (Exception ex)
            {
                Log($"连接失败: {ex.Message}");
                return false;
            }
        }

        private bool ConnectToDevice(int vid, int pid)
        {
            try
            {
                var finder = new UsbDeviceFinder(vid, pid);
                _device = UsbDevice.OpenUsbDevice(finder) as IUsbDevice;

                if (_device == null)
                    return false;

                // 配置设备
                _device.SetConfiguration(1);
                _device.ClaimInterface(0);

                // 查找端点
                var config = _device.Configs[0];
                var iface = config.InterfaceInfoList[0];

                foreach (var ep in iface.EndpointInfoList)
                {
                    if ((ep.Descriptor.EndpointID & 0x80) != 0)
                    {
                        // IN 端点
                        _reader = _device.OpenEndpointReader((ReadEndpointID)ep.Descriptor.EndpointID);
                    }
                    else
                    {
                        // OUT 端点
                        _writer = _device.OpenEndpointWriter((WriteEndpointID)ep.Descriptor.EndpointID);
                    }
                }

                if (_reader == null || _writer == null)
                {
                    Disconnect();
                    return false;
                }

                Log($"已连接设备: VID=0x{vid:X4}, PID=0x{pid:X4}");

                // 获取设备信息
                RefreshDeviceInfo();

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _reader?.Dispose();
                _writer?.Dispose();

                if (_device != null)
                {
                    _device.ReleaseInterface(0);
                    _device.Close();
                    _device = null;
                }

                DeviceInfo = null;
                Log("已断开连接");
            }
            catch (Exception ex)
            {
                Log($"断开连接错误: {ex.Message}");
            }
        }

        #endregion

        #region 基础命令

        /// <summary>
        /// 发送命令
        /// </summary>
        public FastbootResponse? SendCommand(string command, int timeout = USB_TIMEOUT)
        {
            if (!IsConnected || _writer == null || _reader == null)
                return null;

            try
            {
                // 发送命令
                byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
                int transferred;
                var writeResult = _writer.Write(cmdBytes, timeout, out transferred);

                if (writeResult != ErrorCode.None)
                {
                    Log($"发送命令失败: {writeResult}");
                    return null;
                }

                Log($">> {command}");

                // 读取响应 (可能有多个 INFO 消息)
                FastbootResponse? finalResponse = null;
                var buffer = new byte[MAX_PACKET_SIZE];

                while (true)
                {
                    var readResult = _reader.Read(buffer, timeout, out transferred);
                    if (readResult != ErrorCode.None || transferred == 0)
                    {
                        break;
                    }

                    var responseData = new byte[transferred];
                    Array.Copy(buffer, responseData, transferred);
                    var response = FastbootResponse.Parse(responseData);

                    Log($"<< {response.Type}: {response.Message}");

                    if (response.Type == FastbootResponseType.INFO)
                    {
                        // INFO 是中间消息，继续读取
                        continue;
                    }

                    finalResponse = response;
                    break;
                }

                return finalResponse;
            }
            catch (Exception ex)
            {
                Log($"命令执行错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取变量
        /// </summary>
        public string? GetVar(string name)
        {
            var response = SendCommand($"getvar:{name}");
            return response?.IsSuccess == true ? response.Message : null;
        }

        /// <summary>
        /// 获取所有变量
        /// </summary>
        public Dictionary<string, string> GetAllVars()
        {
            var vars = new Dictionary<string, string>();

            if (!IsConnected || _writer == null || _reader == null)
                return vars;

            try
            {
                byte[] cmdBytes = Encoding.ASCII.GetBytes("getvar:all");
                int transferred;
                _writer.Write(cmdBytes, USB_TIMEOUT, out transferred);

                var buffer = new byte[MAX_PACKET_SIZE];

                while (true)
                {
                    var readResult = _reader.Read(buffer, USB_TIMEOUT, out transferred);
                    if (readResult != ErrorCode.None || transferred == 0)
                        break;

                    var responseData = new byte[transferred];
                    Array.Copy(buffer, responseData, transferred);
                    var response = FastbootResponse.Parse(responseData);

                    if (response.Type == FastbootResponseType.INFO)
                    {
                        // 解析 "name: value" 格式
                        int colonIdx = response.Message.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = response.Message.Substring(0, colonIdx).Trim();
                            string value = response.Message.Substring(colonIdx + 1).Trim();
                            vars[key] = value;
                        }
                    }
                    else if (response.Type == FastbootResponseType.OKAY)
                    {
                        break;
                    }
                    else if (response.Type == FastbootResponseType.FAIL)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"获取变量失败: {ex.Message}");
            }

            return vars;
        }

        /// <summary>
        /// 刷新设备信息
        /// </summary>
        public void RefreshDeviceInfo()
        {
            DeviceInfo = new FastbootDeviceInfo
            {
                Product = GetVar("product") ?? "",
                SerialNumber = GetVar("serialno") ?? "",
                Secure = GetVar("secure") ?? "",
                Unlocked = GetVar("unlocked") ?? "",
                CurrentSlot = GetVar("current-slot") ?? "",
                MaxDownloadSize = GetVar("max-download-size") ?? "",
                PartitionType = GetVar("partition-type:boot") ?? "",
                Version = GetVar("version") ?? "",
                VersionBaseband = GetVar("version-baseband") ?? "",
                VersionBootloader = GetVar("version-bootloader") ?? ""
            };

            // Fastbootd 检测
            string? isUserspace = GetVar("is-userspace");
            DeviceInfo.IsFastbootd = isUserspace == "yes";

            // A/B 分区检测
            DeviceInfo.IsSeamlessUpdate = !string.IsNullOrEmpty(DeviceInfo.CurrentSlot);

            // VAB 状态检测
            DeviceInfo.SnapshotUpdateStatus = GetVar("snapshot-update-status") ?? "";

            // 获取所有变量
            var allVars = GetAllVars();
            foreach (var kv in allVars)
            {
                DeviceInfo.Variables[kv.Key] = kv.Value;

                // 解析分区大小
                if (kv.Key.StartsWith("partition-size:"))
                {
                    string partName = kv.Key.Substring("partition-size:".Length);
                    string sizeStr = kv.Value.Replace("0x", "");
                    if (long.TryParse(sizeStr, System.Globalization.NumberStyles.HexNumber, null, out long size))
                    {
                        DeviceInfo.PartitionSizes[partName] = size;
                    }

                    // 检测 COW 分区
                    if (partName.EndsWith("-cow") || partName.EndsWith("_cow"))
                    {
                        DeviceInfo.HasCowPartitions = true;
                    }
                }

                // 解析逻辑分区标志
                if (kv.Key.StartsWith("is-logical:"))
                {
                    string partName = kv.Key.Substring("is-logical:".Length);
                    DeviceInfo.PartitionIsLogical[partName] = kv.Value == "yes";
                }
            }
        }

        /// <summary>
        /// 获取详细分区列表
        /// </summary>
        public List<FastbootPartitionInfo> GetPartitionDetails()
        {
            var partitions = new List<FastbootPartitionInfo>();

            if (DeviceInfo == null)
                RefreshDeviceInfo();

            if (DeviceInfo == null)
                return partitions;

            foreach (var kv in DeviceInfo.PartitionSizes)
            {
                DeviceInfo.PartitionIsLogical.TryGetValue(kv.Key, out bool isLogical);
                partitions.Add(new FastbootPartitionInfo
                {
                    Name = kv.Key,
                    Size = kv.Value,
                    IsLogical = isLogical
                });
            }

            return partitions.OrderBy(p => p.Name).ToList();
        }

        #endregion

        #region 分区操作

        /// <summary>
        /// 刷入分区
        /// </summary>
        public async Task<bool> FlashAsync(string partition, string imagePath, CancellationToken ct = default)
        {
            if (!File.Exists(imagePath))
            {
                Log($"文件不存在: {imagePath}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(imagePath);
                long fileSize = fileInfo.Length;
                Log($"准备刷入: {partition}, 文件: {Path.GetFileName(imagePath)}, 大小: {fileSize / (1024.0 * 1024):F2} MB");

                // 1. 下载数据到设备 RAM
                if (!await DownloadAsync(imagePath, ct))
                {
                    Log("数据下载失败");
                    return false;
                }

                // 2. 刷入分区
                Log($"正在刷入分区: {partition}");
                var response = SendCommand($"flash:{partition}", 120000); // 2分钟超时

                if (response?.IsSuccess != true)
                {
                    Log($"刷入失败: {response?.Message ?? "未知错误"}");
                    return false;
                }

                Log($"分区 {partition} 刷入成功");
                return true;
            }
            catch (Exception ex)
            {
                Log($"刷入错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 下载数据到设备 RAM
        /// </summary>
        public async Task<bool> DownloadAsync(string filePath, CancellationToken ct = default)
        {
            if (!IsConnected || _writer == null || _reader == null)
                return false;

            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                // 发送下载命令
                var response = SendCommand($"download:{fileSize:x8}");
                if (response?.Type != FastbootResponseType.DATA)
                {
                    Log($"设备拒绝下载: {response?.Message ?? "未知错误"}");
                    return false;
                }

                // 传输数据
                Log($"正在传输数据: {fileSize / (1024.0 * 1024):F2} MB");
                long totalSent = 0;

                using (var fs = File.OpenRead(filePath))
                {
                    var buffer = new byte[MAX_PACKET_SIZE];
                    int bytesRead;

                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        int transferred;
                        var writeResult = _writer.Write(buffer, 0, bytesRead, USB_TIMEOUT, out transferred);

                        if (writeResult != ErrorCode.None)
                        {
                            Log($"传输错误: {writeResult}");
                            return false;
                        }

                        totalSent += transferred;
                        OnProgress?.Invoke(totalSent, fileSize);
                    }
                }

                // 等待 OKAY 响应
                var finalBuffer = new byte[MAX_PACKET_SIZE];
                int finalTransferred;
                _reader.Read(finalBuffer, USB_TIMEOUT, out finalTransferred);

                var finalResponse = FastbootResponse.Parse(finalBuffer.AsSpan(0, finalTransferred).ToArray());
                if (finalResponse.Type != FastbootResponseType.OKAY)
                {
                    Log($"传输失败: {finalResponse.Message}");
                    return false;
                }

                Log("数据传输完成");
                return true;
            }
            catch (OperationCanceledException)
            {
                Log("传输已取消");
                return false;
            }
            catch (Exception ex)
            {
                Log($"下载错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public bool Erase(string partition)
        {
            Log($"正在擦除分区: {partition}");
            var response = SendCommand($"erase:{partition}", 60000);

            if (response?.IsSuccess == true)
            {
                Log($"分区 {partition} 擦除成功");
                return true;
            }

            Log($"擦除失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 获取分区列表
        /// </summary>
        public List<string> GetPartitions()
        {
            var partitions = new List<string>();
            var allVars = GetAllVars();

            foreach (var kv in allVars)
            {
                // 查找 partition-size:xxx 格式的变量
                if (kv.Key.StartsWith("partition-size:"))
                {
                    string partName = kv.Key.Substring("partition-size:".Length);
                    partitions.Add(partName);
                }
            }

            return partitions;
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启到系统
        /// </summary>
        public bool Reboot()
        {
            Log("正在重启到系统...");
            var response = SendCommand("reboot");
            Disconnect();
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 重启到 Bootloader
        /// </summary>
        public bool RebootBootloader()
        {
            Log("正在重启到 Bootloader...");
            var response = SendCommand("reboot-bootloader");
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 重启到 Recovery
        /// </summary>
        public bool RebootRecovery()
        {
            Log("正在重启到 Recovery...");
            var response = SendCommand("reboot-recovery");
            Disconnect();
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 重启到 Fastbootd (用户空间 Fastboot)
        /// </summary>
        public bool RebootFastboot()
        {
            Log("正在重启到 Fastbootd...");
            var response = SendCommand("reboot-fastboot");
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 重启到 EDL 模式
        /// </summary>
        public bool RebootEdl()
        {
            Log("正在重启到 EDL 模式...");
            var response = SendCommand("reboot-edl");
            Disconnect();
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 继续启动
        /// </summary>
        public bool Continue()
        {
            Log("继续启动...");
            var response = SendCommand("continue");
            Disconnect();
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 关机
        /// </summary>
        public bool Poweroff()
        {
            Log("正在关机...");
            var response = SendCommand("poweroff");
            Disconnect();
            return response?.IsSuccess == true;
        }

        #endregion

        #region Bootloader 解锁

        /// <summary>
        /// 获取解锁数据 (用于 OEM 解锁)
        /// </summary>
        public byte[]? GetUnlockData()
        {
            var response = SendCommand("get_unlock_data");
            return response?.RawData;
        }

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public bool OemUnlock()
        {
            Log("正在解锁 Bootloader...");
            var response = SendCommand("oem unlock", 30000);

            if (response?.IsSuccess == true)
            {
                Log("Bootloader 解锁成功");
                return true;
            }

            Log($"解锁失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 使用解锁码解锁 Bootloader
        /// </summary>
        public bool OemUnlockWithCode(string unlockCode)
        {
            Log("正在使用解锁码解锁 Bootloader...");
            var response = SendCommand($"oem unlock {unlockCode}", 30000);

            if (response?.IsSuccess == true)
            {
                Log("Bootloader 解锁成功");
                return true;
            }

            Log($"解锁失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public bool OemLock()
        {
            Log("正在锁定 Bootloader...");
            var response = SendCommand("oem lock", 30000);

            if (response?.IsSuccess == true)
            {
                Log("Bootloader 锁定成功");
                return true;
            }

            Log($"锁定失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 解锁 Critical 分区
        /// </summary>
        public bool FlashingUnlockCritical()
        {
            Log("正在解锁 Critical 分区...");
            var response = SendCommand("flashing unlock_critical", 30000);
            return response?.IsSuccess == true;
        }

        /// <summary>
        /// 锁定 Critical 分区
        /// </summary>
        public bool FlashingLockCritical()
        {
            Log("正在锁定 Critical 分区...");
            var response = SendCommand("flashing lock_critical", 30000);
            return response?.IsSuccess == true;
        }

        #endregion

        #region OEM 特定命令

        /// <summary>
        /// 发送 OEM 命令
        /// </summary>
        public FastbootResponse? OemCommand(string command)
        {
            return SendCommand($"oem {command}");
        }

        /// <summary>
        /// 获取设备信息 (OEM)
        /// </summary>
        public string? OemDeviceInfo()
        {
            var response = OemCommand("device-info");
            return response?.Message;
        }

        #endregion

        #region Fastbootd 逻辑分区管理 (仅用户空间)

        /// <summary>
        /// 检查是否在 Fastbootd 模式
        /// </summary>
        public bool IsFastbootd => DeviceInfo?.IsFastbootd ?? false;

        /// <summary>
        /// 创建逻辑分区 (仅 fastbootd)
        /// </summary>
        public bool CreateLogicalPartition(string name, long size)
        {
            if (!IsFastbootd)
            {
                Log("创建逻辑分区需要 fastbootd 模式");
                return false;
            }

            Log($"正在创建逻辑分区: {name}, 大小: {size}");
            var response = SendCommand($"create-logical-partition:{name}:{size}");

            if (response?.IsSuccess == true)
            {
                Log($"逻辑分区 {name} 创建成功");
                RefreshDeviceInfo();
                return true;
            }

            Log($"创建失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 删除逻辑分区 (仅 fastbootd)
        /// </summary>
        public bool DeleteLogicalPartition(string name)
        {
            if (!IsFastbootd)
            {
                Log("删除逻辑分区需要 fastbootd 模式");
                return false;
            }

            // 检查是否为逻辑分区
            if (DeviceInfo?.PartitionIsLogical.TryGetValue(name, out bool isLogical) == true && !isLogical)
            {
                Log($"{name} 不是逻辑分区，无法删除");
                return false;
            }

            Log($"正在删除逻辑分区: {name}");
            var response = SendCommand($"delete-logical-partition:{name}");

            if (response?.IsSuccess == true)
            {
                Log($"逻辑分区 {name} 删除成功");
                RefreshDeviceInfo();
                return true;
            }

            Log($"删除失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 调整逻辑分区大小 (仅 fastbootd)
        /// </summary>
        public bool ResizeLogicalPartition(string name, long newSize)
        {
            if (!IsFastbootd)
            {
                Log("调整逻辑分区需要 fastbootd 模式");
                return false;
            }

            Log($"正在调整逻辑分区: {name}, 新大小: {newSize}");
            var response = SendCommand($"resize-logical-partition:{name}:{newSize}");

            if (response?.IsSuccess == true)
            {
                Log($"逻辑分区 {name} 调整成功");
                RefreshDeviceInfo();
                return true;
            }

            Log($"调整失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        #endregion

        #region A/B 分区管理

        /// <summary>
        /// 是否支持 A/B 分区
        /// </summary>
        public bool IsSeamlessUpdate => DeviceInfo?.IsSeamlessUpdate ?? false;

        /// <summary>
        /// 当前活动槽位
        /// </summary>
        public string CurrentSlot => DeviceInfo?.CurrentSlot ?? "";

        /// <summary>
        /// 切换活动槽位
        /// </summary>
        public bool SetActiveSlot(string slot)
        {
            if (!IsSeamlessUpdate)
            {
                Log("设备不支持 A/B 分区");
                return false;
            }

            if (slot != "a" && slot != "b")
            {
                Log("槽位必须是 'a' 或 'b'");
                return false;
            }

            Log($"正在切换活动槽位到: {slot}");
            var response = SendCommand($"set_active:{slot}");

            if (response?.IsSuccess == true)
            {
                Log($"活动槽位已切换到 {slot}");
                RefreshDeviceInfo();
                return true;
            }

            Log($"切换失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 切换到另一个槽位
        /// </summary>
        public bool SwitchSlot()
        {
            string newSlot = CurrentSlot == "a" ? "b" : "a";
            return SetActiveSlot(newSlot);
        }

        #endregion

        #region Virtual A/B (VAB) 管理

        /// <summary>
        /// VAB 更新状态
        /// </summary>
        public string SnapshotUpdateStatus => DeviceInfo?.SnapshotUpdateStatus ?? "";

        /// <summary>
        /// 是否有待处理的 VAB 更新
        /// </summary>
        public bool HasPendingUpdate => !string.IsNullOrEmpty(SnapshotUpdateStatus) && SnapshotUpdateStatus != "none";

        /// <summary>
        /// 是否有 COW 分区
        /// </summary>
        public bool HasCowPartitions => DeviceInfo?.HasCowPartitions ?? false;

        /// <summary>
        /// 取消 VAB 更新
        /// </summary>
        public bool CancelSnapshotUpdate()
        {
            if (!HasPendingUpdate)
            {
                Log("没有待处理的 VAB 更新");
                return true;
            }

            Log("正在取消 VAB 更新...");
            var response = SendCommand("snapshot-update:cancel");

            if (response?.IsSuccess == true)
            {
                Log("VAB 更新已取消");
                RefreshDeviceInfo();
                return true;
            }

            Log($"取消失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 合并 VAB 更新
        /// </summary>
        public bool MergeSnapshotUpdate()
        {
            Log("正在合并 VAB 更新...");
            var response = SendCommand("snapshot-update:merge");

            if (response?.IsSuccess == true)
            {
                Log("VAB 更新合并完成");
                RefreshDeviceInfo();
                return true;
            }

            Log($"合并失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        #endregion

        #region 特殊分区刷入

        /// <summary>
        /// 刷入 vbmeta 分区 (支持禁用验证)
        /// </summary>
        public async Task<bool> FlashVbmetaAsync(string imagePath, bool disableVerity = false, bool disableVerification = false, CancellationToken ct = default)
        {
            if (!File.Exists(imagePath))
            {
                Log($"文件不存在: {imagePath}");
                return false;
            }

            string partition = "vbmeta";
            if (IsSeamlessUpdate && !string.IsNullOrEmpty(CurrentSlot))
            {
                partition = $"vbmeta_{CurrentSlot}";
            }

            // 下载数据
            if (!await DownloadAsync(imagePath, ct))
            {
                Log("数据下载失败");
                return false;
            }

            // 构建命令
            string cmd = $"flash:{partition}";
            if (disableVerity)
                cmd += " --disable-verity";
            if (disableVerification)
                cmd += " --disable-verification";

            Log($"正在刷入 {partition}，禁用验证: {disableVerity}, 禁用校验: {disableVerification}");
            var response = SendCommand(cmd, 60000);

            if (response?.IsSuccess == true)
            {
                Log($"{partition} 刷入成功");
                return true;
            }

            Log($"刷入失败: {response?.Message ?? "未知错误"}");
            return false;
        }

        /// <summary>
        /// 刷入带槽位后缀的分区
        /// </summary>
        public async Task<bool> FlashWithSlotAsync(string partition, string imagePath, CancellationToken ct = default)
        {
            // 自动添加槽位后缀
            string targetPartition = partition;
            if (IsSeamlessUpdate && !string.IsNullOrEmpty(CurrentSlot) && !partition.EndsWith($"_{CurrentSlot}"))
            {
                targetPartition = $"{partition}_{CurrentSlot}";
            }

            return await FlashAsync(targetPartition, imagePath, ct);
        }

        #endregion

        #region 辅助方法

        private void Log(string message)
        {
            OnLog?.Invoke($"[Fastboot] {message}");
        }

        public void Dispose()
        {
            Disconnect();
        }

        #endregion
    }
}
