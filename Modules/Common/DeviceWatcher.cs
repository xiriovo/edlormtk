// ============================================================================
// MultiFlash TOOL - Device Watcher
// 设备监听器 | デバイスウォッチャー | 장치 감시자
// ============================================================================
// [EN] USB device detection and monitoring for Android flash operations
//      Supports Qualcomm EDL, MTK Preloader, Unisoc Download modes
// [中文] USB 设备检测和监控，用于 Android 刷机操作
//       支持高通 EDL、联发科 Preloader、展讯 Download 模式
// [日本語] Androidフラッシュ操作用のUSBデバイス検出と監視
//         Qualcomm EDL、MTK Preloader、Unisoc Downloadモードをサポート
// [한국어] 안드로이드 플래시 작업을 위한 USB 장치 감지 및 모니터링
//         퀄컴 EDL, MTK Preloader, Unisoc Download 모드 지원
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Common
{
    /// <summary>
    /// Device Info / 设备信息 / デバイス情報 / 장치 정보
    /// </summary>
    public class DeviceInfo
    {
        public string PortName { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Description { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string VID { get; set; } = "";
        public string PID { get; set; } = "";
        public DeviceType Type { get; set; } = DeviceType.Unknown;
        
        /// <summary>
        /// 端口是否可用 (可以打开)
        /// </summary>
        public bool IsPortAvailable { get; set; } = false;
        
        /// <summary>
        /// 端口状态信息
        /// </summary>
        public string PortStatus { get; set; } = "";
        
        /// <summary>
        /// 设备类型显示名
        /// </summary>
        public string TypeName => Type switch
        {
            DeviceType.Qualcomm9008 => "高通 9008",
            DeviceType.QualcommDiag => "高通 Diag",
            DeviceType.MTKPreloader => "MTK Preloader",
            DeviceType.MTKBrom => "MTK Brom",
            DeviceType.SpreadtrumDownload => "展讯下载",
            DeviceType.AdbDevice => "ADB",
            DeviceType.FastbootDevice => "Fastboot",
            _ => "未知"
        };
    }

    /// <summary>
    /// 设备类型
    /// </summary>
    public enum DeviceType
    {
        Unknown,
        Qualcomm9008,      // 高通 EDL 9008
        QualcommDiag,      // 高通 Diag
        MTKPreloader,      // MTK Preloader
        MTKBrom,           // MTK Brom
        SpreadtrumDownload, // 展讯下载模式
        AdbDevice,         // ADB 设备
        FastbootDevice     // Fastboot 设备
    }

    /// <summary>
    /// 设备监视器 - 监控设备插拔
    /// </summary>
    public class DeviceWatcher : IDisposable
    {
        private ManagementEventWatcher? _insertWatcher;
        private ManagementEventWatcher? _removeWatcher;
        private readonly object _lock = new();
        private bool _disposed;
        private Timer? _pollTimer;

        // 已知设备 VID/PID
        private static readonly Dictionary<(string VID, string PID), DeviceType> KnownDevices = new()
        {
            // 高通 9008 EDL 模式 (各种厂商)
            { ("05C6", "9008"), DeviceType.Qualcomm9008 },  // 高通标准
            { ("05C6", "9006"), DeviceType.Qualcomm9008 },  // 高通变体
            { ("05C6", "9007"), DeviceType.Qualcomm9008 },  // 高通变体
            { ("05C6", "9025"), DeviceType.Qualcomm9008 },  // 高通变体
            { ("05C6", "9091"), DeviceType.Qualcomm9008 },  // 高通变体
            { ("05C6", "900E"), DeviceType.QualcommDiag },  // Diag
            { ("2A70", "9008"), DeviceType.Qualcomm9008 },  // OnePlus/OPPO
            { ("2A70", "9011"), DeviceType.Qualcomm9008 },  // OnePlus/OPPO
            { ("22D9", "9008"), DeviceType.Qualcomm9008 },  // OPPO
            { ("1BBB", "9008"), DeviceType.Qualcomm9008 },  // T-Mobile
            { ("19D2", "9008"), DeviceType.Qualcomm9008 },  // ZTE
            { ("0FCE", "9008"), DeviceType.Qualcomm9008 },  // Sony
            { ("2717", "9008"), DeviceType.Qualcomm9008 },  // Xiaomi
            { ("2717", "9091"), DeviceType.Qualcomm9008 },  // Xiaomi
            { ("2717", "F003"), DeviceType.Qualcomm9008 },  // Xiaomi
            { ("18D1", "9008"), DeviceType.Qualcomm9008 },  // Google
            { ("04E8", "6800"), DeviceType.Qualcomm9008 },  // Samsung
            { ("04E8", "685D"), DeviceType.Qualcomm9008 },  // Samsung
            { ("2C7C", "9008"), DeviceType.Qualcomm9008 },  // Quectel
            { ("1EAC", "9008"), DeviceType.Qualcomm9008 },  // Vivo
            { ("17EF", "9008"), DeviceType.Qualcomm9008 },  // Lenovo
            { ("1E68", "9008"), DeviceType.Qualcomm9008 },  // Realme
            { ("1D4D", "9008"), DeviceType.Qualcomm9008 },  // Pegatron
            { ("2AB8", "9008"), DeviceType.Qualcomm9008 },  // Nothing
            
            // MTK
            { ("0E8D", "0003"), DeviceType.MTKPreloader },
            { ("0E8D", "2000"), DeviceType.MTKPreloader },
            { ("0E8D", "2001"), DeviceType.MTKPreloader },
            { ("0E8D", "2005"), DeviceType.MTKPreloader },
            { ("0E8D", "2006"), DeviceType.MTKPreloader },
            { ("0E8D", "0002"), DeviceType.MTKBrom },
            { ("0E8D", "20FF"), DeviceType.MTKBrom },
            
            // 展讯
            { ("1782", "4D00"), DeviceType.SpreadtrumDownload },
            { ("1782", "4D01"), DeviceType.SpreadtrumDownload },
            
            // Google ADB/Fastboot
            { ("18D1", "D00D"), DeviceType.FastbootDevice },
            { ("18D1", "4EE0"), DeviceType.AdbDevice },
            { ("18D1", "4EE7"), DeviceType.AdbDevice },
        };

        /// <summary>
        /// 设备插入事件
        /// </summary>
        public event EventHandler<DeviceInfo>? DeviceArrived;

        /// <summary>
        /// 设备移除事件
        /// </summary>
        public event EventHandler<DeviceInfo>? DeviceRemoved;

        /// <summary>
        /// 设备列表变化事件
        /// </summary>
        public event EventHandler<List<DeviceInfo>>? DevicesChanged;

        /// <summary>
        /// 开始监视设备
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                Stop();

                // 初始化端口列表
                _lastPorts = new HashSet<string>(SerialPort.GetPortNames());

                try
                {
                    // WMI 事件监视 - 设备插入
                    var insertQuery = new WqlEventQuery(
                        "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
                    _insertWatcher = new ManagementEventWatcher(insertQuery);
                    _insertWatcher.EventArrived += OnDeviceInserted;
                    _insertWatcher.Start();

                    // WMI 事件监视 - 设备移除
                    var removeQuery = new WqlEventQuery(
                        "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
                    _removeWatcher = new ManagementEventWatcher(removeQuery);
                    _removeWatcher.EventArrived += OnDeviceRemoved;
                    _removeWatcher.Start();
                }
                catch
                {
                    // WMI 不可用时，使用轮询模式
                    _pollTimer = new Timer(PollDevices, null, 0, 2000);
                }

                // 立即扫描已连接的设备
                Task.Run(() => ScanExistingDevices());
            }
        }

        /// <summary>
        /// 扫描已存在的设备
        /// </summary>
        private void ScanExistingDevices()
        {
            // 多次扫描确保检测可靠
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    var devices = GetAllDevices();
                    var validDevices = devices.Where(d => 
                        d.Type != DeviceType.Unknown || !string.IsNullOrEmpty(d.PortName)
                    ).ToList();
                    
                    foreach (var device in validDevices)
                    {
                        if (!string.IsNullOrEmpty(device.PortName))
                        {
                            // 测试端口可用性
                            TestDevicePort(device);
                            DeviceArrived?.Invoke(this, device);
                        }
                    }

                    if (validDevices.Count > 0)
                    {
                        _lastPorts = new HashSet<string>(validDevices.Select(d => d.PortName).Where(p => !string.IsNullOrEmpty(p))!);
                        DevicesChanged?.Invoke(this, validDevices);
                        break; // 扫描成功，退出
                    }
                    
                    // 未检测到设备，等待后重试
                    if (retry < 2)
                        Thread.Sleep(300);
                }
                catch { }
            }
        }

        /// <summary>
        /// 停止监视设备
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _insertWatcher?.Stop();
                _insertWatcher?.Dispose();
                _insertWatcher = null;

                _removeWatcher?.Stop();
                _removeWatcher?.Dispose();
                _removeWatcher = null;

                _pollTimer?.Dispose();
                _pollTimer = null;
            }
        }

        private HashSet<string> _lastPorts = new();

        private void PollDevices(object? state)
        {
            try
            {
                var currentPorts = new HashSet<string>(SerialPort.GetPortNames());
                
                // 检测新增
                foreach (var port in currentPorts.Except(_lastPorts))
                {
                    var device = GetDeviceInfo(port);
                    DeviceArrived?.Invoke(this, device);
                }

                // 检测移除
                foreach (var port in _lastPorts.Except(currentPorts))
                {
                    var device = new DeviceInfo { PortName = port };
                    DeviceRemoved?.Invoke(this, device);
                }

                if (!currentPorts.SetEquals(_lastPorts))
                {
                    _lastPorts = currentPorts;
                    DevicesChanged?.Invoke(this, GetAllDevices());
                }
            }
            catch { }
        }

        private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                var deviceId = instance["DeviceID"]?.ToString() ?? "";
                
                if (deviceId.Contains("COM") || deviceId.Contains("USB") || 
                    deviceId.Contains("9008") || deviceId.Contains("VID_"))
                {
                    // 设备插入后驱动加载需要时间，多次扫描确保检测到
                    Task.Run(async () =>
                    {
                        // 多次扫描，间隔递增
                        int[] delays = { 300, 500, 1000, 1500 };
                        
                        foreach (var delay in delays)
                        {
                            await Task.Delay(delay);
                            
                            try
                            {
                                var devices = GetAllDevices();
                                var ports = SerialPort.GetPortNames();
                                var newPorts = ports.Except(_lastPorts).ToList();
                                
                                if (newPorts.Count > 0 || devices.Any(d => d.Type != DeviceType.Unknown))
                                {
                                    foreach (var port in newPorts)
                                    {
                                        var device = GetDeviceInfo(port);
                                        TestDevicePort(device);
                                        DeviceArrived?.Invoke(this, device);
                                    }
                                    
                                    _lastPorts = new HashSet<string>(ports);
                                    DevicesChanged?.Invoke(this, devices);
                                    break; // 检测成功，退出循环
                                }
                            }
                            catch { }
                        }
                    });
                }
            }
            catch { }
        }

        private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var ports = SerialPort.GetPortNames();
                foreach (var port in _lastPorts.Except(ports))
                {
                    var device = new DeviceInfo { PortName = port };
                    DeviceRemoved?.Invoke(this, device);
                }
                _lastPorts = new HashSet<string>(ports);

                DevicesChanged?.Invoke(this, GetAllDevices());
            }
            catch { }
        }

        /// <summary>
        /// 获取所有设备
        /// </summary>
        public List<DeviceInfo> GetAllDevices()
        {
            var devices = new List<DeviceInfo>();
            var addedPorts = new HashSet<string>();

            // 多种查询方式确保检测可靠性
            var queries = new[]
            {
                // 1. 串口设备类 (最可靠)
                "SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4D36E978-E325-11CE-BFC1-08002BE10318}'",
                // 2. USB 串口设备
                "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%' AND Caption LIKE '%(COM%'",
                // 3. 9008 设备特征 (高通 EDL)
                "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_05C6&PID_9008%'",
                "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_2717%' AND DeviceID LIKE '%9008%'",
                // 4. 任何包含 COM 端口的设备
                "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"
            };

            foreach (var query in queries)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(query);
                    searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                    
                    foreach (var obj in searcher.Get())
                    {
                        var device = ParseDeviceObject(obj);
                        if (device != null && !string.IsNullOrEmpty(device.PortName))
                        {
                            if (addedPorts.Add(device.PortName))
                                devices.Add(device);
                        }
                    }
                }
                catch { }
            }

            // 最后检查系统串口列表中未识别的端口
            try
            {
                foreach (var portName in SerialPort.GetPortNames())
                {
                    if (!addedPorts.Contains(portName))
                    {
                        var device = GetDeviceInfoByPort(portName);
                        if (device != null)
                        {
                            addedPorts.Add(portName);
                            devices.Add(device);
                        }
                    }
                }
            }
            catch { }

            return devices;
        }

        /// <summary>
        /// 解析 WMI 设备对象
        /// </summary>
        private DeviceInfo? ParseDeviceObject(ManagementBaseObject obj)
        {
            try
            {
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var caption = obj["Caption"]?.ToString() ?? "";
                var name = obj["Name"]?.ToString() ?? "";
                var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                var service = obj["Service"]?.ToString() ?? "";
                var classGuid = obj["ClassGuid"]?.ToString() ?? "";
                var hardwareId = (obj["HardwareID"] as string[])?.FirstOrDefault() ?? "";

                // 提取 COM 端口
                string portName = ExtractPortName(caption) ?? ExtractPortName(name) ?? "";

                // 提取 VID/PID (从 DeviceID 或 HardwareID)
                string vid = "", pid = "";
                var idSource = !string.IsNullOrEmpty(hardwareId) ? hardwareId : deviceId;
                
                if (idSource.Contains("VID_"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(idSource, @"VID_([0-9A-Fa-f]{4})");
                    if (match.Success) vid = match.Groups[1].Value.ToUpper();
                }
                if (idSource.Contains("PID_"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(idSource, @"PID_([0-9A-Fa-f]{4})");
                    if (match.Success) pid = match.Groups[1].Value.ToUpper();
                }

                // 识别设备类型
                var type = IdentifyDeviceType(vid, pid, caption, name, manufacturer, service, deviceId);

                if (!string.IsNullOrEmpty(portName) || type != DeviceType.Unknown)
                {
                    return new DeviceInfo
                    {
                        PortName = portName,
                        DeviceId = deviceId,
                        Description = caption,
                        Manufacturer = manufacturer,
                        VID = vid,
                        PID = pid,
                        Type = type
                    };
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从字符串中提取COM端口名
        /// </summary>
        private string? ExtractPortName(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\(COM(\d+)\)");
            if (match.Success)
                return "COM" + match.Groups[1].Value;
            return null;
        }

        /// <summary>
        /// 通过端口名获取设备信息
        /// </summary>
        private DeviceInfo? GetDeviceInfoByPort(string portName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%({portName})%'");
                
                foreach (var obj in searcher.Get())
                {
                    var device = ParseDeviceObject(obj);
                    if (device != null)
                    {
                        device.PortName = portName;
                        return device;
                    }
                }
            }
            catch { }

            // 返回未知设备
            return new DeviceInfo { PortName = portName, Type = DeviceType.Unknown };
        }

        /// <summary>
        /// 综合识别设备类型
        /// </summary>
        private DeviceType IdentifyDeviceType(string vid, string pid, string caption, string name, 
            string manufacturer, string service, string deviceId)
        {
            // 1. VID/PID 精确匹配
            if (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(pid))
            {
                if (KnownDevices.TryGetValue((vid, pid), out var type))
                    return type;
            }

            // 2. 驱动服务名匹配
            var svc = service.ToUpperInvariant();
            if (svc.Contains("QCUSB") || svc.Contains("QDBUSB") || svc == "USBSER" && vid == "05C6")
                return DeviceType.Qualcomm9008;
            if (svc.Contains("MTKUSB") || svc.Contains("MTKCDC"))
                return DeviceType.MTKPreloader;

            // 3. 设备名称/描述匹配
            var text = $"{caption} {name} {manufacturer} {deviceId}".ToUpperInvariant();
            return IdentifyByDescription(text);
        }

        /// <summary>
        /// 根据设备描述识别类型
        /// </summary>
        private DeviceType IdentifyByDescription(string text)
        {
            // Qualcomm 9008 EDL
            if (text.Contains("QUALCOMM HS-USB QDLOADER 9008") ||
                text.Contains("QDLOADER 9008") ||
                text.Contains("QDL DEVICE") ||
                (text.Contains("9008") && text.Contains("QUALCOMM")) ||
                (text.Contains("EDL") && text.Contains("QUALCOMM")) ||
                text.Contains("QUALCOMM HS-USB DIAGNOSTICS 9008"))
            {
                return DeviceType.Qualcomm9008;
            }
            
            // Qualcomm Diag
            if ((text.Contains("QUALCOMM") && text.Contains("DIAG")) ||
                text.Contains("QUALCOMM HS-USB DIAGNOSTICS 900E") ||
                text.Contains("NMEA DEVICE"))
            {
                return DeviceType.QualcommDiag;
            }
            
            // MTK Preloader
            if (text.Contains("MEDIATEK PRELOADER") ||
                text.Contains("MTK PRELOADER") ||
                text.Contains("MEDIATEK USB PORT") ||
                text.Contains("MTK USB PORT") ||
                text.Contains("DA USB VCOM") ||
                (text.Contains("MEDIATEK") && text.Contains("USB VCOM")))
            {
                return DeviceType.MTKPreloader;
            }
            
            // MTK Brom
            if (text.Contains("MEDIATEK BROM") || 
                text.Contains("MTK BROM") ||
                (text.Contains("MEDIATEK") && text.Contains("BOOTROM")))
            {
                return DeviceType.MTKBrom;
            }

            // 展讯
            if (text.Contains("SPREADTRUM") || 
                text.Contains("UNISOC") ||
                text.Contains("SPRD"))
            {
                return DeviceType.SpreadtrumDownload;
            }
            
            // ADB
            if ((text.Contains("ANDROID") && text.Contains("ADB")) ||
                text.Contains("ADB INTERFACE"))
            {
                return DeviceType.AdbDevice;
            }
            
            // Fastboot
            if ((text.Contains("ANDROID") && text.Contains("FASTBOOT")) ||
                text.Contains("FASTBOOT INTERFACE"))
            {
                return DeviceType.FastbootDevice;
            }
            
            return DeviceType.Unknown;
        }

        /// <summary>
        /// 获取指定端口的设备信息
        /// </summary>
        public DeviceInfo GetDeviceInfo(string portName)
        {
            var devices = GetAllDevices();
            return devices.FirstOrDefault(d => d.PortName == portName) 
                ?? new DeviceInfo { PortName = portName };
        }

        /// <summary>
        /// 查找指定类型的设备
        /// </summary>
        public List<DeviceInfo> FindDevicesByType(DeviceType type)
        {
            return GetAllDevices().Where(d => d.Type == type).ToList();
        }

        /// <summary>
        /// 等待设备连接
        /// </summary>
        public async Task<DeviceInfo?> WaitForDeviceAsync(DeviceType type, int timeout = 30000, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            int scanCount = 0;
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                ct.ThrowIfCancellationRequested();
                scanCount++;

                // 每次都重新扫描全部设备
                var allDevices = GetAllDevices();
                var devices = allDevices.Where(d => d.Type == type).ToList();
                
                // 如果是 9008 设备，也检查 VID 匹配
                if (type == DeviceType.Qualcomm9008 && devices.Count == 0)
                {
                    devices = allDevices.Where(d => 
                        d.VID == "05C6" || d.VID == "2717" || d.VID == "2A70" ||
                        d.Description.Contains("9008", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
                
                if (devices.Count > 0)
                {
                    var device = devices[0];
                    TestDevicePort(device);
                    return device;
                }

                // 动态调整扫描间隔：开始时更频繁，后来减慢
                int delay = scanCount <= 5 ? 200 : (scanCount <= 10 ? 500 : 1000);
                await Task.Delay(delay, ct);
            }

            return null;
        }

        /// <summary>
        /// 测试端口是否可以打开
        /// </summary>
        public static bool TestPortAvailable(string portName, out string status)
        {
            status = "";
            if (string.IsNullOrEmpty(portName))
            {
                status = "端口名为空";
                return false;
            }

            SerialPort? port = null;
            try
            {
                port = new SerialPort(portName)
                {
                    BaudRate = 115200,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                
                port.Open();
                
                if (port.IsOpen)
                {
                    status = "✓ 端口可用";
                    return true;
                }
                else
                {
                    status = "端口未能打开";
                    return false;
                }
            }
            catch (UnauthorizedAccessException)
            {
                status = "⚠ 端口被占用";
                return false;
            }
            catch (System.IO.IOException ex)
            {
                status = $"IO错误: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                status = $"错误: {ex.Message}";
                return false;
            }
            finally
            {
                try
                {
                    if (port?.IsOpen == true)
                        port.Close();
                    port?.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// 测试设备端口并更新状态
        /// </summary>
        public static void TestDevicePort(DeviceInfo device)
        {
            if (device == null || string.IsNullOrEmpty(device.PortName)) return;
            
            device.IsPortAvailable = TestPortAvailable(device.PortName, out var status);
            device.PortStatus = status;
        }

        /// <summary>
        /// 获取所有设备并测试端口可用性
        /// </summary>
        public List<DeviceInfo> GetAllDevicesWithTest()
        {
            var devices = GetAllDevices();
            foreach (var device in devices)
            {
                TestDevicePort(device);
            }
            return devices;
        }

        /// <summary>
        /// 扫描并测试指定类型的设备
        /// </summary>
        public List<DeviceInfo> ScanAndTestDevices(DeviceType type)
        {
            var devices = FindDevicesByType(type);
            foreach (var device in devices)
            {
                TestDevicePort(device);
            }
            return devices;
        }

        /// <summary>
        /// 快速检测9008设备 (带重试)
        /// </summary>
        public DeviceInfo? FindAvailable9008Device(int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                var devices = FindDevicesByType(DeviceType.Qualcomm9008);
                
                // 如果没找到，尝试直接从串口列表中检测
                if (devices.Count == 0)
                {
                    devices = GetAllDevices().Where(d => 
                        d.Type == DeviceType.Qualcomm9008 ||
                        d.VID == "05C6" || d.VID == "2717" ||
                        d.Description.Contains("9008", StringComparison.OrdinalIgnoreCase) ||
                        d.Description.Contains("QDLOADER", StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
                
                foreach (var device in devices)
                {
                    TestDevicePort(device);
                    if (device.IsPortAvailable)
                        return device;
                }
                
                if (devices.Count > 0)
                    return devices[0]; // 返回第一个设备（即使端口被占用）
                
                // 等待后重试
                if (retry < maxRetries - 1)
                    Thread.Sleep(500);
            }
            
            return null;
        }

        /// <summary>
        /// 快速检测MTK设备
        /// </summary>
        public DeviceInfo? FindAvailableMtkDevice()
        {
            // 先找 Preloader
            var devices = FindDevicesByType(DeviceType.MTKPreloader);
            foreach (var device in devices)
            {
                TestDevicePort(device);
                if (device.IsPortAvailable)
                    return device;
            }
            
            // 再找 Brom
            devices = FindDevicesByType(DeviceType.MTKBrom);
            foreach (var device in devices)
            {
                TestDevicePort(device);
                if (device.IsPortAvailable)
                    return device;
            }
            
            return null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
