using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace tools.Modules.MTK.DA
{
    /// <summary>
    /// DA Region 结构 (20 bytes)
    /// </summary>
    public class DARegion
    {
        public uint Buffer { get; set; }           // 文件内偏移
        public uint Length { get; set; }           // 数据长度
        public uint StartAddress { get; set; }     // 加载地址
        public uint StartOffset { get; set; }
        public uint SignatureLength { get; set; }  // 签名长度

        public static DARegion Parse(BinaryReader reader)
        {
            return new DARegion
            {
                Buffer = reader.ReadUInt32(),
                Length = reader.ReadUInt32(),
                StartAddress = reader.ReadUInt32(),
                StartOffset = reader.ReadUInt32(),
                SignatureLength = reader.ReadUInt32()
            };
        }

        public override string ToString()
        {
            return $"Addr=0x{StartAddress:X8}, Len=0x{Length:X}, SigLen=0x{SignatureLength:X}";
        }
    }

    /// <summary>
    /// DA 条目 - 对应一个芯片的 DA 配置
    /// </summary>
    public class DAEntry
    {
        public bool IsV6 { get; set; }
        public string LoaderPath { get; set; } = "";
        public ushort Magic { get; set; }
        public ushort HwCode { get; set; }
        public ushort HwSubCode { get; set; }
        public ushort HwVersion { get; set; }
        public ushort SwVersion { get; set; }
        public ushort PageSize { get; set; }
        public ushort EntryRegionIndex { get; set; }
        public ushort EntryRegionCount { get; set; }
        public List<DARegion> Regions { get; set; } = new();

        /// <summary>
        /// Region[1] = DA1 (Stage 1)
        /// </summary>
        public DARegion DA1Region => Regions.Count > 1 ? Regions[1] : null!;

        /// <summary>
        /// Region[2] = DA2 (Stage 2)
        /// </summary>
        public DARegion DA2Region => Regions.Count > 2 ? Regions[2] : null!;

        public static DAEntry Parse(BinaryReader reader, bool isOldLoader, bool isV6)
        {
            var entry = new DAEntry { IsV6 = isV6 };
            
            entry.Magic = reader.ReadUInt16();
            entry.HwCode = reader.ReadUInt16();
            entry.HwSubCode = reader.ReadUInt16();
            entry.HwVersion = reader.ReadUInt16();
            
            if (!isOldLoader)
            {
                entry.SwVersion = reader.ReadUInt16();
                reader.ReadUInt16(); // Reserved1
            }
            
            entry.PageSize = reader.ReadUInt16();
            reader.ReadUInt16(); // Reserved3
            entry.EntryRegionIndex = reader.ReadUInt16();
            entry.EntryRegionCount = reader.ReadUInt16();
            
            for (int i = 0; i < entry.EntryRegionCount; i++)
            {
                entry.Regions.Add(DARegion.Parse(reader));
            }
            
            return entry;
        }

        public override string ToString()
        {
            return $"HwCode=0x{HwCode:X4}, Ver={HwVersion}.{SwVersion}, Regions={Regions.Count}";
        }
    }

    /// <summary>
    /// DA 配置管理器 - 解析 MTK_AllInOne_DA.bin 文件
    /// </summary>
    public class DAConfig
    {
        private readonly Dictionary<ushort, List<DAEntry>> _daSetup = new();
        
        public DAEntry? CurrentDA { get; private set; }
        public byte[]? DA1Data { get; private set; }
        public byte[]? DA2Data { get; private set; }
        public byte[]? EmiData { get; private set; }
        
        /// <summary>
        /// DA 版本字符串
        /// </summary>
        public string? Version { get; private set; }
        
        /// <summary>
        /// 加载 DA 文件
        /// </summary>
        public bool LoadDAFile(string loaderPath)
        {
            if (!File.Exists(loaderPath))
                return false;

            try
            {
                using var fs = File.OpenRead(loaderPath);
                using var reader = new BinaryReader(fs);

                // 读取头部 (0x68 bytes)
                byte[] header = reader.ReadBytes(0x68);
                string headerStr = Encoding.ASCII.GetString(header);
                bool isV6 = headerStr.Contains("MTK_DA_v6");
                
                // 提取版本信息
                if (headerStr.Contains("MTK_DA_v"))
                {
                    int vIdx = headerStr.IndexOf("MTK_DA_v");
                    int endIdx = headerStr.IndexOf('\0', vIdx);
                    Version = endIdx > vIdx ? headerStr.Substring(vIdx, endIdx - vIdx) : headerStr.Substring(vIdx);
                }
                else
                {
                    Version = isV6 ? "V6" : "Legacy";
                }

                // DA 数量
                uint daCount = reader.ReadUInt32();

                // 检测是否是旧格式
                fs.Seek(0x6C + 0xD8, SeekOrigin.Begin);
                bool isOldLoader = reader.ReadUInt16() == 0xDADA;
                int entrySize = isOldLoader ? 0xD8 : 0xDC;

                // 解析每个 DA 条目
                for (int i = 0; i < daCount; i++)
                {
                    fs.Seek(0x6C + (i * entrySize), SeekOrigin.Begin);
                    var entry = DAEntry.Parse(reader, isOldLoader, isV6);
                    entry.LoaderPath = loaderPath;

                    if (entry.HwCode != 0)
                    {
                        if (!_daSetup.ContainsKey(entry.HwCode))
                            _daSetup[entry.HwCode] = new List<DAEntry>();
                        
                        _daSetup[entry.HwCode].Add(entry);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDAFile error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据硬件代码选择合适的 DA
        /// </summary>
        public bool SelectDA(ushort hwCode, ushort hwVersion, ushort swVersion)
        {
            if (!_daSetup.TryGetValue(hwCode, out var entries))
                return false;

            // 选择版本匹配的 DA
            foreach (var entry in entries)
            {
                if (entry.HwVersion <= hwVersion && entry.SwVersion <= swVersion)
                {
                    CurrentDA = entry;
                    return LoadDAData();
                }
            }

            // 如果没有完全匹配，选择第一个
            if (entries.Count > 0)
            {
                CurrentDA = entries[0];
                return LoadDAData();
            }

            return false;
        }

        /// <summary>
        /// 加载 DA1 和 DA2 数据
        /// </summary>
        private bool LoadDAData()
        {
            if (CurrentDA == null || string.IsNullOrEmpty(CurrentDA.LoaderPath))
                return false;

            try
            {
                using var fs = File.OpenRead(CurrentDA.LoaderPath);
                
                // 读取 DA1
                var da1Region = CurrentDA.DA1Region;
                if (da1Region != null)
                {
                    fs.Seek(da1Region.Buffer, SeekOrigin.Begin);
                    DA1Data = new byte[da1Region.Length];
                    fs.Read(DA1Data, 0, (int)da1Region.Length);
                }

                // 读取 DA2
                var da2Region = CurrentDA.DA2Region;
                if (da2Region != null)
                {
                    fs.Seek(da2Region.Buffer, SeekOrigin.Begin);
                    DA2Data = new byte[da2Region.Length];
                    fs.Read(DA2Data, 0, (int)da2Region.Length);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从 Preloader 提取 EMI 配置
        /// </summary>
        public bool ExtractEmi(string preloaderPath)
        {
            if (!File.Exists(preloaderPath))
                return false;

            try
            {
                byte[] data = File.ReadAllBytes(preloaderPath);
                
                // 查找 EMI 配置标记
                byte[] marker = Encoding.ASCII.GetBytes("MTK_BLOADER_INFO_v");
                int idx = FindPattern(data, marker);
                
                if (idx == -1)
                    return false;

                // 查找 MTK_BIN 标记
                byte[] binMarker = Encoding.ASCII.GetBytes("MTK_BIN");
                int binIdx = FindPattern(data, binMarker);
                
                if (binIdx != -1)
                {
                    EmiData = new byte[data.Length - binIdx - 0xC];
                    Array.Copy(data, binIdx + 0xC, EmiData, 0, EmiData.Length);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        /// <summary>
        /// 获取支持的芯片列表
        /// </summary>
        public IEnumerable<ushort> GetSupportedChips() => _daSetup.Keys;

        /// <summary>
        /// 验证 DA 数据完整性
        /// </summary>
        public bool ValidateDA()
        {
            if (CurrentDA == null)
            {
                System.Diagnostics.Debug.WriteLine("DA 验证失败: 未选择 DA");
                return false;
            }

            if (DA1Data == null || DA1Data.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("DA 验证失败: DA1 数据为空");
                return false;
            }

            if (DA2Data == null || DA2Data.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("DA 验证失败: DA2 数据为空");
                return false;
            }

            // 验证 DA1 大小是否与 Region 定义匹配
            if (CurrentDA.DA1Region != null && DA1Data.Length != CurrentDA.DA1Region.Length)
            {
                System.Diagnostics.Debug.WriteLine($"DA 验证警告: DA1 大小不匹配 (实际: {DA1Data.Length}, 预期: {CurrentDA.DA1Region.Length})");
            }

            // 验证 DA2 大小是否与 Region 定义匹配
            if (CurrentDA.DA2Region != null && DA2Data.Length != CurrentDA.DA2Region.Length)
            {
                System.Diagnostics.Debug.WriteLine($"DA 验证警告: DA2 大小不匹配 (实际: {DA2Data.Length}, 预期: {CurrentDA.DA2Region.Length})");
            }

            return true;
        }

        /// <summary>
        /// 获取 DA 验证状态信息
        /// </summary>
        public (bool isValid, string message) GetValidationStatus()
        {
            if (CurrentDA == null)
                return (false, "未选择 DA");

            if (DA1Data == null || DA1Data.Length == 0)
                return (false, "DA Stage 1 数据为空");

            if (DA2Data == null || DA2Data.Length == 0)
                return (false, "DA Stage 2 数据为空");

            // 检查 Region 配置
            if (CurrentDA.Regions.Count < 3)
                return (false, $"DA Region 配置不完整 (只有 {CurrentDA.Regions.Count} 个)");

            // 检查地址有效性
            if (CurrentDA.DA1Region.StartAddress == 0)
                return (false, "DA1 加载地址无效");

            if (CurrentDA.DA2Region.StartAddress == 0)
                return (false, "DA2 加载地址无效");

            return (true, $"DA 验证通过 - HW:0x{CurrentDA.HwCode:X4}, DA1:{DA1Data.Length}B, DA2:{DA2Data.Length}B");
        }

        /// <summary>
        /// 获取 DA Stage 1 数据
        /// </summary>
        public byte[]? GetStage1Data(ushort hwCode)
        {
            // 如果已加载，直接返回
            if (DA1Data != null && DA1Data.Length > 0)
                return DA1Data;

            // 尝试根据 hwCode 选择并加载
            if (hwCode > 0 && _daSetup.ContainsKey(hwCode))
            {
                SelectDA(hwCode, 0xFFFF, 0xFFFF);
                return DA1Data;
            }

            // 返回第一个可用的 DA1
            if (CurrentDA?.DA1Region != null && !string.IsNullOrEmpty(CurrentDA.LoaderPath))
            {
                LoadDAData();
                return DA1Data;
            }

            return null;
        }

        /// <summary>
        /// 获取 DA Stage 1 加载地址
        /// </summary>
        public uint GetStage1Address(ushort hwCode)
        {
            // 确保已加载
            if (CurrentDA == null && hwCode > 0)
            {
                SelectDA(hwCode, 0xFFFF, 0xFFFF);
            }

            return CurrentDA?.DA1Region?.StartAddress ?? 0x200000;
        }

        /// <summary>
        /// 获取 DA Stage 1 签名长度
        /// </summary>
        public uint GetStage1SignatureLength()
        {
            return CurrentDA?.DA1Region?.SignatureLength ?? 0;
        }

        /// <summary>
        /// 获取 DA Stage 2 数据
        /// </summary>
        public byte[]? GetStage2Data()
        {
            if (DA2Data != null && DA2Data.Length > 0)
                return DA2Data;

            if (CurrentDA?.DA2Region != null && !string.IsNullOrEmpty(CurrentDA.LoaderPath))
            {
                LoadDAData();
            }

            return DA2Data;
        }

        /// <summary>
        /// 获取 DA Stage 2 加载地址
        /// </summary>
        public uint GetStage2Address()
        {
            return CurrentDA?.DA2Region?.StartAddress ?? 0x40000000;
        }

        /// <summary>
        /// 获取 DA Stage 2 签名长度
        /// </summary>
        public uint GetStage2SignatureLength()
        {
            return CurrentDA?.DA2Region?.SignatureLength ?? 0;
        }
    }
}
