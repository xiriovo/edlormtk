using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// 设备详细信息
    /// </summary>
    public class DeviceDetailInfo
    {
        // 基本信息
        public string MarketName { get; set; } = "";
        public string MarketNameEn { get; set; } = "";
        public string Model { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Device { get; set; } = "";
        public string Manufacturer { get; set; } = "";

        // 系统信息
        public string AndroidVersion { get; set; } = "";
        public string SdkVersion { get; set; } = "";
        public string SecurityPatch { get; set; } = "";
        public string BuildId { get; set; } = "";
        public string DisplayId { get; set; } = "";
        public string Fingerprint { get; set; } = "";

        // OTA 信息
        public string OtaVersion { get; set; } = "";
        public string OtaVersionFull { get; set; } = "";

        // 平台信息
        public string Platform { get; set; } = "";
        public string Region { get; set; } = "";
        public string RegionMark { get; set; } = "";
        public string Project { get; set; } = "";
        public string NvId { get; set; } = "";
        public string Carrier { get; set; } = "";

        // 安全信息
        public string UnlockState { get; set; } = "";
        public string VerifiedBootState { get; set; } = "";
        public string IMEI { get; set; } = "";
        public string IMEI2 { get; set; } = "";

        // 编译信息
        public string BuildDate { get; set; } = "";
        public string BuildType { get; set; } = "";

        // 扩展信息 (Lenovo/Motorola 等)
        public string SerialNumber { get; set; } = "";
        public string SKU { get; set; } = "";
        public string ChipName { get; set; } = "";
        public string Series { get; set; } = "";

        /// <summary>
        /// 是否有有效数据 (任意关键信息都算有效)
        /// </summary>
        public bool HasData => !string.IsNullOrEmpty(MarketName) ||
                               !string.IsNullOrEmpty(Model) ||
                               !string.IsNullOrEmpty(AndroidVersion) ||
                               !string.IsNullOrEmpty(IMEI) ||
                               !string.IsNullOrEmpty(SerialNumber) ||
                               !string.IsNullOrEmpty(UnlockState);

        /// <summary>
        /// 转为字典格式 (用于UI显示)
        /// </summary>
        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(MarketName)) dict["Marketname"] = MarketName;
            if (!string.IsNullOrEmpty(Model)) dict["Model"] = Model;
            if (!string.IsNullOrEmpty(Brand)) dict["Brand"] = Brand;
            if (!string.IsNullOrEmpty(Device)) dict["Device"] = Device;
            if (!string.IsNullOrEmpty(Manufacturer)) dict["Manufacturer"] = Manufacturer;

            if (!string.IsNullOrEmpty(AndroidVersion))
            {
                var android = AndroidVersion;
                if (!string.IsNullOrEmpty(SdkVersion))
                    android += $" [SDK:{SdkVersion}]";
                dict["Android Version"] = android;
            }

            if (!string.IsNullOrEmpty(SecurityPatch)) dict["Security Patch Level"] = SecurityPatch;
            if (!string.IsNullOrEmpty(BuildId)) dict["BuildID"] = BuildId;
            if (!string.IsNullOrEmpty(DisplayId)) dict["DisplayID"] = DisplayId;
            if (!string.IsNullOrEmpty(OtaVersion)) dict["OTA Version"] = OtaVersion;
            if (!string.IsNullOrEmpty(OtaVersionFull)) dict["OTA Version Full"] = OtaVersionFull;
            if (!string.IsNullOrEmpty(Fingerprint)) dict["Fingerprint"] = Fingerprint;
            if (!string.IsNullOrEmpty(Platform)) dict["Platform"] = Platform;
            
            // 地区/运营商
            if (!string.IsNullOrEmpty(Region)) dict["Market-Region"] = Region;
            if (!string.IsNullOrEmpty(RegionMark)) dict["RegionMark"] = RegionMark;
            if (!string.IsNullOrEmpty(Carrier)) dict["Carrier"] = Carrier;
            
            // 安全状态
            if (!string.IsNullOrEmpty(UnlockState)) dict["Unlock State"] = UnlockState;
            if (!string.IsNullOrEmpty(VerifiedBootState)) dict["Verified Boot State"] = VerifiedBootState;
            
            // IMEI
            if (!string.IsNullOrEmpty(IMEI)) dict["IMEI"] = IMEI;
            if (!string.IsNullOrEmpty(IMEI2)) dict["IMEI2"] = IMEI2;
            
            if (!string.IsNullOrEmpty(BuildDate)) dict["BuiltDate"] = BuildDate;
            
            // 扩展信息
            if (!string.IsNullOrEmpty(SerialNumber)) dict["SerialNumber"] = SerialNumber;
            if (!string.IsNullOrEmpty(SKU)) dict["SKU"] = SKU;
            if (!string.IsNullOrEmpty(ChipName)) dict["ChipName"] = ChipName;
            if (!string.IsNullOrEmpty(Series)) dict["Series"] = Series;

            return dict;
        }

        /// <summary>
        /// 从字典填充
        /// </summary>
        public void FromDictionary(Dictionary<string, string> dict)
        {
            foreach (var (key, value) in dict)
            {
                SetProperty(key, value);
            }
        }

        public void SetProperty(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            switch (key)
            {
                case "MarketName":
                case "MarketName_CN":
                    if (string.IsNullOrEmpty(MarketName)) MarketName = value;
                    break;
                case "MarketNameEn":
                    if (string.IsNullOrEmpty(MarketNameEn)) MarketNameEn = value;
                    break;
                case "Model":
                    if (string.IsNullOrEmpty(Model)) Model = value;
                    break;
                case "Brand":
                    if (string.IsNullOrEmpty(Brand)) Brand = value;
                    break;
                case "Device":
                    if (string.IsNullOrEmpty(Device)) Device = value;
                    break;
                case "Manufacturer":
                    if (string.IsNullOrEmpty(Manufacturer)) Manufacturer = value;
                    break;
                case "ProductName":
                    if (string.IsNullOrEmpty(Model)) Model = value;
                    break;
                case "AndroidVersion":
                    if (string.IsNullOrEmpty(AndroidVersion)) AndroidVersion = value;
                    break;
                case "SdkVersion":
                    if (string.IsNullOrEmpty(SdkVersion)) SdkVersion = value;
                    break;
                case "SecurityPatch":
                    if (string.IsNullOrEmpty(SecurityPatch)) SecurityPatch = value;
                    break;
                case "BuildId":
                    if (string.IsNullOrEmpty(BuildId)) BuildId = value;
                    break;
                case "DisplayId":
                    if (string.IsNullOrEmpty(DisplayId)) DisplayId = value;
                    break;
                case "OtaVersion":
                    if (string.IsNullOrEmpty(OtaVersion)) OtaVersion = value;
                    break;
                case "OtaVersionFull":
                    if (string.IsNullOrEmpty(OtaVersionFull)) OtaVersionFull = value;
                    break;
                case "Fingerprint":
                    if (string.IsNullOrEmpty(Fingerprint)) Fingerprint = value;
                    break;
                case "Platform":
                    if (string.IsNullOrEmpty(Platform)) Platform = value;
                    break;
                case "Project":
                    if (string.IsNullOrEmpty(Project)) Project = value;
                    break;
                case "NvId":
                    if (string.IsNullOrEmpty(NvId)) NvId = value;
                    break;
                case "Region":
                    if (string.IsNullOrEmpty(Region)) Region = value;
                    break;
                case "RegionMark":
                    if (string.IsNullOrEmpty(RegionMark)) RegionMark = value;
                    break;
                case "Carrier":
                    if (string.IsNullOrEmpty(Carrier)) Carrier = value;
                    break;
                case "UnlockState":
                    if (string.IsNullOrEmpty(UnlockState)) UnlockState = value;
                    break;
                case "VerifiedBootState":
                    if (string.IsNullOrEmpty(VerifiedBootState)) VerifiedBootState = value;
                    break;
                case "AVBState":
                    if (string.IsNullOrEmpty(VerifiedBootState)) VerifiedBootState = value;
                    break;
                case "IMEI":
                    if (string.IsNullOrEmpty(IMEI)) IMEI = value;
                    break;
                case "IMEI2":
                    if (string.IsNullOrEmpty(IMEI2)) IMEI2 = value;
                    break;
                case "SerialNumber":
                    if (string.IsNullOrEmpty(SerialNumber)) SerialNumber = value;
                    break;
                case "SKU":
                    if (string.IsNullOrEmpty(SKU)) SKU = value;
                    break;
                case "ChipName":
                    if (string.IsNullOrEmpty(ChipName)) ChipName = value;
                    break;
                case "Hardware":
                    if (string.IsNullOrEmpty(ChipName)) ChipName = value;
                    break;
                case "Series":
                    if (string.IsNullOrEmpty(Series)) Series = value;
                    break;
                case "SocModel":
                    if (string.IsNullOrEmpty(ChipName)) ChipName = value;
                    break;
            }
        }
    }

    /// <summary>
    /// 设备信息读取器 - 从分区或固件包解析设备详细信息
    /// </summary>
    public class DeviceInfoReader
    {
        private readonly FirehoseClient? _firehose;
        private readonly List<PartitionInfo>? _partitions;
        private readonly Action<string>? _log;

        public DeviceInfoReader(FirehoseClient? firehose, List<PartitionInfo>? partitions, Action<string>? log = null)
        {
            _firehose = firehose;
            _partitions = partitions;
            _log = log;
        }

        /// <summary>
        /// 从设备分区读取设备信息 (完整读取策略)
        /// </summary>
        public async Task<DeviceDetailInfo?> ReadFromDeviceAsync(
            string? loaderPath = null, 
            string? chipPlatform = null,
            string? oemVendor = null,
            bool readFullInfo = true,
            CancellationToken ct = default)
        {
            // 使用高效版本
            return await ReadFromDeviceHighEfficiencyAsync(loaderPath, chipPlatform, oemVendor, readFullInfo, ct);
        }

        /// <summary>
        /// 高效读取设备信息 - 最小化读取次数，并行解析
        /// 
        /// 支持厂商: OPPO/Realme/OnePlus/Xiaomi/Vivo/Samsung
        /// 
        /// 优化策略:
        /// 1. 根据厂商选择最优读取策略
        /// 2. 批量并行读取关键分区
        /// 3. 智能跳过已获取信息的分区
        /// 4. 最小化读取量获取最大信息
        /// 
        /// 读取顺序 (按大小排序，最小优先):
        /// - devinfo: 4KB (1 扇区) - 解锁状态、AVB
        /// - super LP metadata: 8KB (2 扇区) - 获取子分区偏移
        /// - odm_a: 读取128MB - 设备详细信息 (小米/OPPO)
        /// - modemst1: 2MB - IMEI (如需要)
        /// </summary>
        public async Task<DeviceDetailInfo?> ReadFromDeviceHighEfficiencyAsync(
            string? loaderPath = null, 
            string? chipPlatform = null,
            string? oemVendor = null,
            bool readFullInfo = true,
            CancellationToken ct = default)
        {
            var info = new DeviceDetailInfo();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // ===== 阶段1: 无IO操作，直接推断 =====
                if (!string.IsNullOrEmpty(loaderPath))
                {
                    var loaderInfo = PartitionDeviceInfoParser.InferFromLoaderPath(loaderPath);
                    info.FromDictionary(loaderInfo);
                }

                if (!string.IsNullOrEmpty(chipPlatform))
                    info.Platform = chipPlatform;
                if (!string.IsNullOrEmpty(oemVendor) && string.IsNullOrEmpty(info.Manufacturer))
                    info.Manufacturer = oemVendor;

                if (_firehose == null || _partitions == null || _partitions.Count == 0)
                {
                    return info.HasData ? info : null;
                }

                // ===== 检测设备厂商类型 =====
                string vendor = DetectDeviceVendor(oemVendor, info.Manufacturer);
                bool isXiaomi = vendor.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase);
                bool isLenovo = vendor.Equals("Lenovo", StringComparison.OrdinalIgnoreCase) ||
                               vendor.Equals("Motorola", StringComparison.OrdinalIgnoreCase);
                
                _log?.Invoke($"[DevInfo] 🚀 高效读取模式 [{vendor}]...");

                // ===== 阶段2: 批量读取关键分区 =====
                var devinfoPart = _partitions.FirstOrDefault(p => p.Name.Equals("devinfo", StringComparison.OrdinalIgnoreCase));
                var superPart = _partitions.FirstOrDefault(p => p.Name.Equals("super", StringComparison.OrdinalIgnoreCase));
                var paramPart = _partitions.FirstOrDefault(p => p.Name.Equals("param", StringComparison.OrdinalIgnoreCase));
                var modemst1Part = _partitions.FirstOrDefault(p => p.Name.Equals("modemst1", StringComparison.OrdinalIgnoreCase));

                // 并行读取 devinfo + super LP metadata
                var readTasks = new List<Task<(string Name, byte[]? Data)>>();

                if (devinfoPart != null)
                {
                    // devinfo: 4KB 足够获取解锁状态
                    readTasks.Add(ReadPartitionAsync("devinfo", devinfoPart.Lun, devinfoPart.StartSector, 1, ct));
                }

                if (superPart != null && readFullInfo)
                {
                    // super LP metadata: 前 8KB
                    readTasks.Add(ReadPartitionAsync("super_lp", superPart.Lun, superPart.StartSector, 2, ct));
                }

                // 等待第一批读取完成
                var results = await Task.WhenAll(readTasks);
                
                // ===== 阶段2.1: 解析 devinfo =====
                var devinfoData = results.FirstOrDefault(r => r.Name == "devinfo").Data;
                if (devinfoData != null && devinfoData.Length > 0)
                {
                    // 根据厂商使用不同解析器
                    var parsed = isXiaomi 
                        ? PartitionDeviceInfoParser.ParseXiaomiDevInfo(devinfoData)
                        : PartitionDeviceInfoParser.ParseDevInfo(devinfoData);
                    info.FromDictionary(parsed);
                    _log?.Invoke($"[DevInfo] ✓ devinfo: {parsed.Count} 属性 (解锁:{info.UnlockState}, AVB:{info.VerifiedBootState})");
                }

                // ===== 阶段3: 解析 LP metadata 并读取子分区 =====
                LpMetadataParser.LpMetadata? lpMetadata = null;
                var superLpData = results.FirstOrDefault(r => r.Name == "super_lp").Data;
                bool hasSuperPartition = superPart != null;
                bool lpMetadataValid = false;
                
                if (superLpData != null && superLpData.Length >= 8192 && superPart != null)
                {
                    lpMetadata = LpMetadataParser.Parse(superLpData, superPart.StartSector);
                    if (lpMetadata != null && lpMetadata.IsValid)
                    {
                        lpMetadataValid = true;
                        _log?.Invoke($"[DevInfo] ✓ LP metadata: {lpMetadata.SubPartitionLocations.Count} 子分区");

                        // 根据厂商读取不同的子分区
                        if (isXiaomi)
                        {
                            await ReadXiaomiSuperSubPartitionsAsync(info, lpMetadata, superPart.Lun, ct);
                        }
                        else if (isLenovo)
                        {
                            await ReadLenovoSuperSubPartitionsAsync(info, lpMetadata, superPart.Lun, ct);
                        }
                        else
                        {
                            await ReadSuperSubPartitionsBatchAsync(info, lpMetadata, superPart.Lun, ct);
                        }
                    }
                }

                // ===== 阶段3.5: 如果没有 super 或 LP 无效，使用传统分区 =====
                if (!hasSuperPartition || !lpMetadataValid)
                {
                    await ReadLegacyPartitionsAsync(info, ct);
                }

                // ===== 阶段4: 读取 IMEI (从 modemst1) =====
                // 所有 Qualcomm 设备 (Xiaomi/OPPO/Realme/OnePlus/Vivo) 都使用 modemst 存储 IMEI
                if (readFullInfo && string.IsNullOrEmpty(info.IMEI) && modemst1Part != null)
                {
                    await ReadModemEfsForImeiAsync(info, modemst1Part, isXiaomi, ct);
                }

                // ===== 阶段5: 可选 - 读取 param (地区信息) =====
                if (string.IsNullOrEmpty(info.Region) && string.IsNullOrEmpty(info.Carrier) && paramPart != null)
                {
                    var paramData = await ReadPartitionAsync("param", paramPart.Lun, paramPart.StartSector, 64, ct);
                    if (paramData.Data != null)
                    {
                        var parsed = PartitionDeviceInfoParser.ParseParam(paramData.Data);
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ param: {parsed.Count} 属性");
                    }
                }

                // ===== 阶段6: Lenovo 专用分区 (proinfo, lenovocust) =====
                if (isLenovo && readFullInfo)
                {
                    await ReadLenovoSpecialPartitionsAsync(info, ct);
                }

                sw.Stop();
                _log?.Invoke($"[DevInfo] ✅ 完成，耗时 {sw.ElapsedMilliseconds}ms");

                return info.HasData ? info : null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] 读取异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检测设备厂商
        /// </summary>
        private string DetectDeviceVendor(string? oemVendor, string? manufacturer)
        {
            // 优先使用已知厂商
            if (!string.IsNullOrEmpty(oemVendor))
            {
                if (oemVendor.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase)) return "Xiaomi";
                if (oemVendor.Contains("OPPO", StringComparison.OrdinalIgnoreCase)) return "OPPO";
                if (oemVendor.Contains("Realme", StringComparison.OrdinalIgnoreCase)) return "Realme";
                if (oemVendor.Contains("OnePlus", StringComparison.OrdinalIgnoreCase)) return "OnePlus";
                if (oemVendor.Contains("Vivo", StringComparison.OrdinalIgnoreCase)) return "Vivo";
                if (oemVendor.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return "Lenovo";
                if (oemVendor.Contains("Motorola", StringComparison.OrdinalIgnoreCase)) return "Motorola";
            }
            
            if (!string.IsNullOrEmpty(manufacturer))
            {
                if (manufacturer.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase)) return "Xiaomi";
                if (manufacturer.Contains("OPPO", StringComparison.OrdinalIgnoreCase)) return "OPPO";
                if (manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return "Lenovo";
                if (manufacturer.Contains("Motorola", StringComparison.OrdinalIgnoreCase)) return "Motorola";
            }

            // 根据分区特征检测
            if (_partitions != null)
            {
                // Lenovo 特征分区
                if (_partitions.Any(p => p.Name.Equals("proinfo", StringComparison.OrdinalIgnoreCase) ||
                                         p.Name.Equals("lenovolock", StringComparison.OrdinalIgnoreCase) ||
                                         p.Name.Equals("lenovocust", StringComparison.OrdinalIgnoreCase) ||
                                         p.Name.Equals("lenovoraw", StringComparison.OrdinalIgnoreCase)))
                    return "Lenovo";

                // 小米特征分区
                if (_partitions.Any(p => p.Name.Equals("cust", StringComparison.OrdinalIgnoreCase) ||
                                         p.Name.Equals("exaid", StringComparison.OrdinalIgnoreCase)))
                    return "Xiaomi";

                // OPPO/Realme 特征分区
                if (_partitions.Any(p => p.Name.Equals("my_manifest", StringComparison.OrdinalIgnoreCase) ||
                                         p.Name.Equals("oplusreserve", StringComparison.OrdinalIgnoreCase)))
                    return "OPPO";
            }

            return oemVendor ?? "Unknown";
        }

        /// <summary>
        /// 读取小米 super 子分区 (odm_a, vendor_a)
        /// 小米设备信息主要在 odm_a 分区
        /// </summary>
        private async Task ReadXiaomiSuperSubPartitionsAsync(
            DeviceDetailInfo info,
            LpMetadataParser.LpMetadata lpMetadata,
            int superLun,
            CancellationToken ct)
        {
            // 小米优先级: odm_a (市场名称+型号+地区) > vendor_a (平台信息)
            var targetParts = new[]
            {
                ("odm_a", 128 * 1024 * 1024, true),     // 128MB，包含完整设备信息+地区
                ("vendor_a", 100 * 1024 * 1024, false), // 100MB，包含平台信息
            };

            foreach (var (name, maxSize, primary) in targetParts)
            {
                // 如果主要信息已获取，跳过次要分区
                if (!primary && !string.IsNullOrEmpty(info.MarketName) && !string.IsNullOrEmpty(info.Model))
                    continue;

                var subPart = LpMetadataParser.GetSubPartition(lpMetadata, name);
                if (subPart == null) continue;

                int readSize = (int)Math.Min(subPart.SizeInBytes, maxSize);
                int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                try
                {
                    _log?.Invoke($"[DevInfo] 读取 {name} ({readSize / 1024 / 1024}MB)...");
                    
                    var data = await _firehose.ReadSectorsAsync(
                        superLun, subPart.DeviceSector4096, numSectors, ct).ConfigureAwait(false);

                    if (data == null || data.Length == 0) continue;

                    // 使用小米专用解析器 - 在后台线程解析大数据
                    Dictionary<string, string> parsed;
                    if (data.Length > 10 * 1024 * 1024) // 大于10MB时使用后台线程
                    {
                        parsed = await Task.Run(() =>
                        {
                            if (name == "odm_a")
                                return PartitionDeviceInfoParser.ParseXiaomiOdmPartition(data);
                            else
                                return PartitionDeviceInfoParser.ParseXiaomiVendorPartition(data);
                        }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        if (name == "odm_a")
                            parsed = PartitionDeviceInfoParser.ParseXiaomiOdmPartition(data);
                        else
                            parsed = PartitionDeviceInfoParser.ParseXiaomiVendorPartition(data);
                    }

                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ {name}: {parsed.Count} 属性");
                        
                        // 如果从 odm_a 获取了关键信息，可以提前结束
                        if (primary && !string.IsNullOrEmpty(info.MarketName) && !string.IsNullOrEmpty(info.Model))
                        {
                            _log?.Invoke($"[DevInfo] 已获取关键信息，跳过后续分区");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] {name} 读取失败: {ex.Message}");
                }
            }

            // 如果还缺少地区信息，尝试从 cust 分区读取
            if (string.IsNullOrEmpty(info.Region))
            {
                await ReadXiaomiCustPartitionAsync(info, lpMetadata, superLun, ct);
            }
        }

        /// <summary>
        /// 读取小米 cust 分区 - 获取地区和运营商信息
        /// </summary>
        private async Task ReadXiaomiCustPartitionAsync(
            DeviceDetailInfo info,
            LpMetadataParser.LpMetadata lpMetadata,
            int superLun,
            CancellationToken ct)
        {
            // 尝试从 super 中读取 cust_a
            var custPart = LpMetadataParser.GetSubPartition(lpMetadata, "cust_a");
            
            // 如果 super 中没有，尝试独立的 cust 分区
            if (custPart == null)
            {
                var standaloneCust = _partitions?.FirstOrDefault(p => 
                    p.Name.Equals("cust", StringComparison.OrdinalIgnoreCase));
                
                if (standaloneCust != null)
                {
                    try
                    {
                        int readSize = Math.Min((int)standaloneCust.Size, 50 * 1024 * 1024);
                        int numSectors = readSize / _firehose!.SectorSize;

                        _log?.Invoke($"[DevInfo] 读取 cust ({readSize / 1024 / 1024}MB) 获取地区...");

                        var data = await _firehose.ReadSectorsAsync(
                            standaloneCust.Lun, standaloneCust.StartSector, numSectors, ct).ConfigureAwait(false);

                        if (data != null && data.Length > 0)
                        {
                            // 后台线程解析大数据
                            var parsed = await Task.Run(() => 
                                PartitionDeviceInfoParser.ParseXiaomiCustPartition(data), ct).ConfigureAwait(false);
                            if (parsed.Count > 0)
                            {
                                info.FromDictionary(parsed);
                                _log?.Invoke($"[DevInfo] ✓ cust: {parsed.Count} 属性");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[DevInfo] cust 读取失败: {ex.Message}");
                    }
                }
                return;
            }

            // 从 super 中读取 cust_a
            try
            {
                int readSize = (int)Math.Min(custPart.SizeInBytes, 50 * 1024 * 1024);
                int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                _log?.Invoke($"[DevInfo] 读取 cust_a ({readSize / 1024 / 1024}MB) 获取地区...");

                var data = await _firehose.ReadSectorsAsync(
                    superLun, custPart.DeviceSector4096, numSectors, ct).ConfigureAwait(false);

                if (data != null && data.Length > 0)
                {
                    // 后台线程解析大数据
                    var parsed = await Task.Run(() => 
                        PartitionDeviceInfoParser.ParseXiaomiCustPartition(data), ct).ConfigureAwait(false);
                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ cust_a: {parsed.Count} 属性");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] cust_a 读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取 Lenovo super 子分区 (odm_a, vendor_a)
        /// Lenovo/Motorola 设备信息主要在 odm_a 和 vendor_a 分区
        /// </summary>
        private async Task ReadLenovoSuperSubPartitionsAsync(
            DeviceDetailInfo info,
            LpMetadataParser.LpMetadata lpMetadata,
            int superLun,
            CancellationToken ct)
        {
            // Lenovo 优先级: odm_a (完整设备信息) > vendor_a (平台/OTA信息)
            var targetParts = new[]
            {
                ("odm_a", 100 * 1024 * 1024, true),    // 100MB，包含型号/市场名/OTA
                ("vendor_a", 80 * 1024 * 1024, false), // 80MB，包含平台/版本信息
            };

            foreach (var (name, maxSize, primary) in targetParts)
            {
                // 如果主要信息已获取，跳过次要分区
                if (!primary && !string.IsNullOrEmpty(info.Model) && !string.IsNullOrEmpty(info.OtaVersion))
                    continue;

                var subPart = LpMetadataParser.GetSubPartition(lpMetadata, name);
                if (subPart == null) continue;

                int readSize = (int)Math.Min(subPart.SizeInBytes, maxSize);
                int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                try
                {
                    _log?.Invoke($"[DevInfo] 读取 Lenovo {name} ({readSize / 1024 / 1024}MB)...");
                    
                    var data = await _firehose.ReadSectorsAsync(
                        superLun, subPart.DeviceSector4096, numSectors, ct).ConfigureAwait(false);

                    if (data == null || data.Length == 0) continue;

                    // 使用 Lenovo 专用解析器 - 在后台线程解析大数据
                    Dictionary<string, string> parsed;
                    if (data.Length > 10 * 1024 * 1024) // 大于10MB时使用后台线程
                    {
                        parsed = await Task.Run(() =>
                        {
                            if (name == "odm_a")
                                return PartitionDeviceInfoParser.ParseLenovoOdmPartition(data);
                            else
                                return PartitionDeviceInfoParser.ParseLenovoVendorPartition(data);
                        }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        if (name == "odm_a")
                            parsed = PartitionDeviceInfoParser.ParseLenovoOdmPartition(data);
                        else
                            parsed = PartitionDeviceInfoParser.ParseLenovoVendorPartition(data);
                    }

                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ {name}: {parsed.Count} 属性");
                        
                        // 如果从 odm_a 获取了关键信息，可以提前结束
                        if (primary && !string.IsNullOrEmpty(info.Model) && !string.IsNullOrEmpty(info.OtaVersion))
                        {
                            _log?.Invoke($"[DevInfo] 已获取 Lenovo 关键信息");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] {name} 读取失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 读取 Lenovo 专用分区 (proinfo, lenovocust)
        /// </summary>
        private async Task ReadLenovoSpecialPartitionsAsync(DeviceDetailInfo info, CancellationToken ct)
        {
            // 读取 proinfo (设备生产信息)
            var proinfoPart = _partitions?.FirstOrDefault(p => 
                p.Name.Equals("proinfo", StringComparison.OrdinalIgnoreCase));

            if (proinfoPart != null && (string.IsNullOrEmpty(info.Model) || string.IsNullOrEmpty(info.SerialNumber)))
            {
                try
                {
                    // proinfo 3MB，读取前 64KB
                    int readSize = Math.Min((int)proinfoPart.Size, 64 * 1024);
                    int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                    _log?.Invoke($"[DevInfo] 读取 proinfo ({readSize / 1024}KB)...");

                    var data = await _firehose.ReadSectorsAsync(
                        proinfoPart.Lun, proinfoPart.StartSector, numSectors, ct).ConfigureAwait(false);

                    if (data != null && data.Length > 0)
                    {
                        // proinfo 数据较小，不需要后台线程
                        var parsed = PartitionDeviceInfoParser.ParseLenovoProinfo(data);
                        if (parsed.Count > 0)
                        {
                            info.FromDictionary(parsed);
                            _log?.Invoke($"[DevInfo] ✓ proinfo: {parsed.Count} 属性");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] proinfo 读取失败: {ex.Message}");
                }
            }

            // 读取 lenovocust (地区/运营商定制)
            var lenovocustPart = _partitions?.FirstOrDefault(p => 
                p.Name.Equals("lenovocust", StringComparison.OrdinalIgnoreCase));

            if (lenovocustPart != null && (string.IsNullOrEmpty(info.Region) || string.IsNullOrEmpty(info.SKU)))
            {
                try
                {
                    // lenovocust 300MB，读取前 50MB
                    int readSize = Math.Min((int)lenovocustPart.Size, 50 * 1024 * 1024);
                    int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                    _log?.Invoke($"[DevInfo] 读取 lenovocust ({readSize / 1024 / 1024}MB)...");

                    var data = await _firehose.ReadSectorsAsync(
                        lenovocustPart.Lun, lenovocustPart.StartSector, numSectors, ct).ConfigureAwait(false);

                    if (data != null && data.Length > 0)
                    {
                        // 后台线程解析大数据
                        var parsed = await Task.Run(() => 
                            PartitionDeviceInfoParser.ParseLenovoCust(data), ct).ConfigureAwait(false);
                        if (parsed.Count > 0)
                        {
                            info.FromDictionary(parsed);
                            _log?.Invoke($"[DevInfo] ✓ lenovocust: {parsed.Count} 属性");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] lenovocust 读取失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 从 modemst1 读取 IMEI (通用 Qualcomm 平台)
        /// 适用于所有 Qualcomm 设备: Xiaomi/OPPO/Realme/OnePlus/Vivo
        /// </summary>
        private async Task ReadModemEfsForImeiAsync(
            DeviceDetailInfo info,
            PartitionInfo modemst1Part,
            bool isXiaomi,
            CancellationToken ct)
        {
            try
            {
                // 读取 modemst1 (通常 4MB，读取前 2MB 搜索 IMEI)
                int partSize = (int)Math.Min(modemst1Part.Size, 4 * 1024 * 1024);
                int readSize = Math.Min(partSize, 2 * 1024 * 1024);
                int numSectors = readSize / _firehose!.SectorSize;

                _log?.Invoke($"[DevInfo] 读取 modemst1 ({readSize / 1024}KB) 搜索 IMEI...");

                var data = await _firehose.ReadSectorsAsync(
                    modemst1Part.Lun, modemst1Part.StartSector, numSectors, ct).ConfigureAwait(false);

                if (data != null && data.Length > 0)
                {
                    // 使用通用 Modem EFS 解析器 (所有 Qualcomm 设备通用)
                    var parsed = await Task.Run(() => 
                        PartitionDeviceInfoParser.ParseModemEfs(data), ct).ConfigureAwait(false);
                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ modemst1: 找到 IMEI={info.IMEI}");
                    }
                    else
                    {
                        _log?.Invoke($"[DevInfo] modemst1: 未找到 IMEI，尝试 modemst2...");
                        // 尝试 modemst2
                        await ReadModemst2ForImeiAsync(info, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] modemst1 读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 modemst2 读取 IMEI (备份分区)
        /// </summary>
        private async Task ReadModemst2ForImeiAsync(DeviceDetailInfo info, CancellationToken ct)
        {
            var modemst2Part = _partitions?.FirstOrDefault(p => 
                p.Name.Equals("modemst2", StringComparison.OrdinalIgnoreCase));
            
            if (modemst2Part == null) return;

            try
            {
                int readSize = Math.Min((int)modemst2Part.Size, 2 * 1024 * 1024);
                int numSectors = readSize / _firehose!.SectorSize;

                var data = await _firehose.ReadSectorsAsync(
                    modemst2Part.Lun, modemst2Part.StartSector, numSectors, ct).ConfigureAwait(false);

                if (data != null && data.Length > 0)
                {
                    var parsed = await Task.Run(() => 
                        PartitionDeviceInfoParser.ParseModemEfs(data), ct).ConfigureAwait(false);
                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ modemst2: 找到 IMEI={info.IMEI}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] modemst2 读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量读取 super 子分区 (OPPO/Realme/OnePlus)
        /// 按大小排序，优先读取小分区
        /// </summary>
        private async Task ReadSuperSubPartitionsBatchAsync(
            DeviceDetailInfo info, 
            LpMetadataParser.LpMetadata lpMetadata, 
            int superLun,
            CancellationToken ct)
        {
            // OPLUS 优先级: my_manifest (小) > my_region > odm_a
            var targetParts = new[]
            {
                ("my_manifest_a", 600 * 1024),    // ~600KB，包含市场名称
                ("my_region_a", 3 * 1024 * 1024), // ~3MB，包含地区
                ("odm_a", 10 * 1024 * 1024),      // 10MB头部，搜索 build.prop
            };

            foreach (var (name, maxSize) in targetParts)
            {
                // 检查是否已有足够信息，跳过不必要的读取
                if (name == "my_manifest_a" && !string.IsNullOrEmpty(info.MarketName))
                    continue;
                if (name == "my_region_a" && !string.IsNullOrEmpty(info.RegionMark))
                    continue;
                if (name == "odm_a" && !string.IsNullOrEmpty(info.AndroidVersion))
                    continue;

                var subPart = LpMetadataParser.GetSubPartition(lpMetadata, name);
                if (subPart == null) continue;

                int readSize = (int)Math.Min(subPart.SizeInBytes, maxSize);
                int numSectors = (readSize + _firehose!.SectorSize - 1) / _firehose.SectorSize;

                try
                {
                    var data = await _firehose.ReadSectorsAsync(
                        superLun, subPart.DeviceSector4096, numSectors, ct).ConfigureAwait(false);

                    if (data == null || data.Length == 0) continue;

                    // 解析 (OPPO 分区较小，不需要后台线程)
                    Dictionary<string, string> parsed;
                    if (name == "my_manifest_a")
                        parsed = PartitionDeviceInfoParser.ParseMyManifest(data);
                    else if (name == "my_region_a")
                        parsed = PartitionDeviceInfoParser.ParseMyRegion(data);
                    else
                        parsed = PartitionDeviceInfoParser.ParseOdmHeader(data);

                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ {name}: {parsed.Count} 属性");
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] {name} 读取失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 读取传统分区 (无 super 分区的旧机型)
        /// Android 9 及更早版本使用传统分区方案
        /// </summary>
        private async Task ReadLegacyPartitionsAsync(DeviceDetailInfo info, CancellationToken ct)
        {
            _log?.Invoke("[DevInfo] 传统分区模式 (无 super)...");

            var legacyTargets = new[]
            {
                ("oem", 512 * 1024, "oem"),
                ("odm", 1024 * 1024, "odm"),
                ("vendor", 1024 * 1024, "vendor"),
                ("system", 512 * 1024, "system"),
                ("oppo_product", 512 * 1024, "oplus"),
                ("cust", 10 * 1024 * 1024, "cust"),  // 小米 cust 分区
                ("reserve", 256 * 1024, "reserve"),
            };

            foreach (var (partName, maxSize, parseType) in legacyTargets)
            {
                if (!string.IsNullOrEmpty(info.AndroidVersion) && !string.IsNullOrEmpty(info.MarketName))
                    break;

                var partition = _partitions!.FirstOrDefault(p =>
                    p.Name.Equals(partName, StringComparison.OrdinalIgnoreCase));

                if (partition == null) continue;

                try
                {
                    int numSectors = Math.Min((int)(partition.Size / _firehose!.SectorSize), maxSize / _firehose.SectorSize);
                    if (numSectors <= 0) numSectors = 128;

                    _log?.Invoke($"[DevInfo] 读取 {partName} ({numSectors * 4}KB)...");

                    var data = await _firehose.ReadSectorsAsync(
                        partition.Lun, partition.StartSector, numSectors, ct).ConfigureAwait(false);

                    if (data == null || data.Length == 0) continue;

                    var parsed = PartitionDeviceInfoParser.Parse(data, data.Length);

                    if (parsed.Count > 0)
                    {
                        info.FromDictionary(parsed);
                        _log?.Invoke($"[DevInfo] ✓ {partName}: {parsed.Count} 属性");
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DevInfo] {partName} 读取失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 读取分区数据 (异步)
        /// </summary>
        private async Task<(string Name, byte[]? Data)> ReadPartitionAsync(
            string name, int lun, long startSector, int numSectors, CancellationToken ct)
        {
            try
            {
                var data = await _firehose!.ReadSectorsAsync(lun, startSector, numSectors, ct).ConfigureAwait(false);
                return (name, data);
            }
            catch
            {
                return (name, null);
            }
        }

        /// <summary>
        /// 快速读取 - 仅读取最小分区获取基本信息
        /// 适用于只需要 Model/Manufacturer/Platform 的场景
        /// 总读取量: ~4KB (devinfo)
        /// </summary>
        public async Task<DeviceDetailInfo?> ReadQuickAsync(
            string? loaderPath = null, 
            string? chipPlatform = null,
            string? oemVendor = null,
            CancellationToken ct = default)
        {
            return await ReadFromDeviceHighEfficiencyAsync(loaderPath, chipPlatform, oemVendor, false, ct);
        }

        /// <summary>
        /// 从固件包读取设备信息
        /// </summary>
        public DeviceDetailInfo? ReadFromFirmware(string firmwarePath)
        {
            if (!Directory.Exists(firmwarePath))
            {
                _log?.Invoke($"[DevInfo] 固件路径不存在: {firmwarePath}");
                return null;
            }

            var info = new DeviceDetailInfo();

            try
            {
                // 1. 读取 version_info.txt (JSON格式) - 最优先
                var versionInfoPath = Path.Combine(firmwarePath, "version_info.txt");
                if (File.Exists(versionInfoPath))
                {
                    _log?.Invoke("[DevInfo] 解析 version_info.txt...");
                    ParseVersionInfoFile(versionInfoPath, info);
                }

                // 2. 读取 build.prop
                var buildPropPath = Path.Combine(firmwarePath, "build.prop");
                if (File.Exists(buildPropPath))
                {
                    _log?.Invoke("[DevInfo] 解析 build.prop...");
                    ParseBuildPropFile(buildPropPath, info);
                }

                // 3. 从IMAGES目录读取分区镜像
                var imagesDir = Path.Combine(firmwarePath, "IMAGES");
                if (Directory.Exists(imagesDir))
                {
                    // 读取 my_manifest 分区 (小，包含市场名称)
                    foreach (var manifestFile in Directory.GetFiles(imagesDir, "my_manifest*.img"))
                    {
                        _log?.Invoke($"[DevInfo] 解析 {Path.GetFileName(manifestFile)}...");
                        ParsePartitionImage(manifestFile, info);
                        if (!string.IsNullOrEmpty(info.MarketName)) break;
                    }

                    // 从 static_nvbk 文件名获取项目ID
                    foreach (var nvbkFile in Directory.GetFiles(imagesDir, "static_nvbk*.bin"))
                    {
                        var match = Regex.Match(Path.GetFileName(nvbkFile), @"\.(\d+)\.");
                        if (match.Success && string.IsNullOrEmpty(info.Project))
                        {
                            info.Project = match.Groups[1].Value;
                        }
                    }
                }

                return info.HasData ? info : null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] 固件解析异常: {ex.Message}");
                return null;
            }
        }

        #region 文件解析

        private void ParseVersionInfoFile(string filePath, DeviceDetailInfo info)
        {
            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);

                // 尝试解析为数组
                Dictionary<string, object>? obj = null;

                if (json.TrimStart().StartsWith("["))
                {
                    var array = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                    if (array != null && array.Count > 0)
                    {
                        obj = array[0];
                    }
                }
                else
                {
                    obj = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }

                if (obj == null) return;

                // 字段映射
                var mapping = new Dictionary<string, string>
                {
                    ["market_name"] = "MarketName",
                    ["product_name"] = "Model",
                    ["product_model"] = "Model",
                    ["platform"] = "Platform",
                    ["nv_id"] = "NvId",
                    ["project"] = "Project",
                    ["version_name"] = "DisplayId",
                    ["compile_time"] = "BuildDate",
                };

                foreach (var (jsonKey, propName) in mapping)
                {
                    if (obj.TryGetValue(jsonKey, out var value) && value != null)
                    {
                        var strValue = value.ToString();
                        if (!string.IsNullOrEmpty(strValue))
                        {
                            info.SetProperty(propName, strValue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] version_info.txt 解析失败: {ex.Message}");
            }
        }

        private void ParseBuildPropFile(string filePath, DeviceDetailInfo info)
        {
            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);

                // build.prop 关键属性映射
                var mapping = new Dictionary<string, string>
                {
                    ["ro.vendor.oplus.market.name"] = "MarketName",
                    ["ro.vendor.oplus.market.enname"] = "MarketNameEn",
                    ["ro.product.model"] = "Model",
                    ["ro.product.brand"] = "Brand",
                    ["ro.product.device"] = "Device",
                    ["ro.product.name"] = "Model",
                    ["ro.product.manufacturer"] = "Manufacturer",
                    ["ro.build.version.release"] = "AndroidVersion",
                    ["ro.build.version.sdk"] = "SdkVersion",
                    ["ro.build.version.security_patch"] = "SecurityPatch",
                    ["ro.build.id"] = "BuildId",
                    ["ro.build.display.id"] = "DisplayId",
                    ["ro.build.display.full_id"] = "OtaVersionFull",
                    ["ro.build.version.ota"] = "OtaVersion",
                    ["ro.build.fingerprint"] = "Fingerprint",
                    ["ro.system.build.date"] = "BuildDate",
                    ["ro.build.type"] = "BuildType",
                };

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;

                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();

                    if (mapping.TryGetValue(key, out var propName))
                    {
                        info.SetProperty(propName, value);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] build.prop 解析失败: {ex.Message}");
            }
        }

        private void ParsePartitionImage(string filePath, DeviceDetailInfo info)
        {
            try
            {
                var data = File.ReadAllBytes(filePath);
                var parsed = PartitionDeviceInfoParser.Parse(data);
                info.FromDictionary(parsed);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DevInfo] 分区镜像解析失败: {ex.Message}");
            }
        }

        #endregion

        #region 静态工具方法

        /// <summary>
        /// 从固件目录快速读取设备信息
        /// </summary>
        public static DeviceDetailInfo? QuickReadFromFirmware(string firmwarePath)
        {
            var reader = new DeviceInfoReader(null, null);
            return reader.ReadFromFirmware(firmwarePath);
        }

        /// <summary>
        /// 从原始分区数据解析设备信息
        /// </summary>
        public static DeviceDetailInfo ParseFromRawData(byte[] data)
        {
            var info = new DeviceDetailInfo();
            var parsed = PartitionDeviceInfoParser.Parse(data);
            info.FromDictionary(parsed);
            return info;
        }

        #endregion
    }
}
