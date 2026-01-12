using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// 从分区原始数据解析设备信息
    /// 支持 sparse 格式镜像和原始数据
    /// 
    /// 最优读取策略 (基于固件分析):
    /// - devinfo: 4KB (LUN4) - 最小最快
    /// - param: 1MB (LUN5) - 备选
    /// - 注意: odm/vendor/my_manifest 在 super 动态分区内，无法直接读取
    /// </summary>
    public static class PartitionDeviceInfoParser
    {
        private const uint SPARSE_MAGIC = 0xED26FF3A;
        private const ushort CHUNK_TYPE_RAW = 0xCAC1;
        private const ushort CHUNK_TYPE_FILL = 0xCAC2;
        private const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;

        /// <summary>
        /// 搜索模式 (属性名, 字节模式)
        /// </summary>
        private static readonly (string Name, byte[] Pattern)[] SearchPatterns = new[]
        {
            ("MarketName", Encoding.ASCII.GetBytes("ro.vendor.oplus.market.name=")),
            ("MarketNameEn", Encoding.ASCII.GetBytes("ro.vendor.oplus.market.enname=")),
            ("Model", Encoding.ASCII.GetBytes("ro.product.model=")),
            ("Brand", Encoding.ASCII.GetBytes("ro.product.brand=")),
            ("Device", Encoding.ASCII.GetBytes("ro.product.device=")),
            ("ProductName", Encoding.ASCII.GetBytes("ro.product.name=")),
            ("Manufacturer", Encoding.ASCII.GetBytes("ro.product.manufacturer=")),
            ("AndroidVersion", Encoding.ASCII.GetBytes("ro.build.version.release=")),
            ("SdkVersion", Encoding.ASCII.GetBytes("ro.build.version.sdk=")),
            ("SecurityPatch", Encoding.ASCII.GetBytes("ro.build.version.security_patch=")),
            ("DisplayId", Encoding.ASCII.GetBytes("ro.build.display.id=")),
            ("OtaVersionFull", Encoding.ASCII.GetBytes("ro.build.display.full_id=")),
            ("OtaVersion", Encoding.ASCII.GetBytes("ro.build.version.ota=")),
            ("Fingerprint", Encoding.ASCII.GetBytes("ro.build.fingerprint=")),
            ("BuildId", Encoding.ASCII.GetBytes("ro.build.id=")),
        };

        // 中文市场名称前缀 (UTF-8 "真我" = E7 9C 9F E6 88 91)
        private static readonly byte[] ChineseMarketPrefix_ZhenWo = new byte[] { 0xE7, 0x9C, 0x9F, 0xE6, 0x88, 0x91 };
        // UTF-8 "一加" = E4 B8 80 E5 8A A0
        private static readonly byte[] ChineseMarketPrefix_YiJia = new byte[] { 0xE4, 0xB8, 0x80, 0xE5, 0x8A, 0xA0 };

        /// <summary>
        /// 从分区原始数据解析设备信息
        /// </summary>
        /// <param name="rawData">原始分区数据（可能是sparse格式）</param>
        /// <param name="maxUnspareSize">sparse解压最大大小</param>
        /// <returns>设备信息字典</returns>
        public static Dictionary<string, string> Parse(byte[] rawData, int maxUnspareSize = 10 * 1024 * 1024)
        {
            var result = new Dictionary<string, string>();

            // 如果是sparse格式，先解压
            byte[] data = IsSparse(rawData) ? Unsparse(rawData, maxUnspareSize) : rawData;

            // 搜索属性
            foreach (var (name, pattern) in SearchPatterns)
            {
                int offset = FindPattern(data, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(data, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            // 搜索中文市场名称 - 真我
            int cnOffset = FindPattern(data, ChineseMarketPrefix_ZhenWo);
            if (cnOffset >= 0)
            {
                string value = ExtractUtf8Value(data, cnOffset, 50);
                if (!string.IsNullOrEmpty(value))
                {
                    result["MarketName_CN"] = value;
                    if (!result.ContainsKey("MarketName"))
                    {
                        result["MarketName"] = value;
                    }
                }
            }

            // 搜索中文市场名称 - 一加
            cnOffset = FindPattern(data, ChineseMarketPrefix_YiJia);
            if (cnOffset >= 0 && !result.ContainsKey("MarketName_CN"))
            {
                string value = ExtractUtf8Value(data, cnOffset, 50);
                if (!string.IsNullOrEmpty(value))
                {
                    result["MarketName_CN"] = value;
                    if (!result.ContainsKey("MarketName"))
                    {
                        result["MarketName"] = value;
                    }
                }
            }

            // 搜索型号模式 (RMX/CPH/PKR)
            if (!result.ContainsKey("Model"))
            {
                var modelPatterns = new[]
                {
                    Encoding.ASCII.GetBytes("RMX"),
                    Encoding.ASCII.GetBytes("CPH"),
                    Encoding.ASCII.GetBytes("PKR"),
                    Encoding.ASCII.GetBytes("PHB"),
                };

                foreach (var pattern in modelPatterns)
                {
                    int offset = FindPattern(data, pattern);
                    if (offset >= 0)
                    {
                        string model = ExtractModelString(data, offset);
                        if (!string.IsNullOrEmpty(model) && model.Length >= 6)
                        {
                            result["Model"] = model;
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 检查是否是sparse格式
        /// </summary>
        public static bool IsSparse(byte[] data)
        {
            if (data == null || data.Length < 28) return false;
            uint magic = BitConverter.ToUInt32(data, 0);
            return magic == SPARSE_MAGIC;
        }

        /// <summary>
        /// 解压sparse镜像
        /// </summary>
        public static byte[] Unsparse(byte[] sparseData, int maxSize = 100 * 1024 * 1024)
        {
            if (!IsSparse(sparseData)) return sparseData;

            ushort fileHdrSz = BitConverter.ToUInt16(sparseData, 8);
            ushort chunkHdrSz = BitConverter.ToUInt16(sparseData, 10);
            uint blkSz = BitConverter.ToUInt32(sparseData, 12);
            uint totalChunks = BitConverter.ToUInt32(sparseData, 20);

            var output = new List<byte>();
            int pos = fileHdrSz;

            for (uint i = 0; i < totalChunks && output.Count < maxSize; i++)
            {
                if (pos + chunkHdrSz > sparseData.Length) break;

                ushort chunkType = BitConverter.ToUInt16(sparseData, pos);
                uint chunkBlocks = BitConverter.ToUInt32(sparseData, pos + 4);
                uint totalSz = BitConverter.ToUInt32(sparseData, pos + 8);

                int dataStart = pos + chunkHdrSz;
                int dataSz = (int)(totalSz - chunkHdrSz);

                if (chunkType == CHUNK_TYPE_RAW)
                {
                    int copyLen = Math.Min(dataSz, maxSize - output.Count);
                    for (int j = 0; j < copyLen; j++)
                    {
                        if (dataStart + j < sparseData.Length)
                            output.Add(sparseData[dataStart + j]);
                    }
                }
                else if (chunkType == CHUNK_TYPE_FILL)
                {
                    if (dataStart + 4 <= sparseData.Length)
                    {
                        byte[] fill = new byte[4];
                        Array.Copy(sparseData, dataStart, fill, 0, 4);
                        int fillCount = (int)(chunkBlocks * blkSz);
                        for (int j = 0; j < fillCount && output.Count < maxSize; j++)
                        {
                            output.Add(fill[j % 4]);
                        }
                    }
                }
                else if (chunkType == CHUNK_TYPE_DONT_CARE)
                {
                    int skipCount = (int)Math.Min(chunkBlocks * blkSz, maxSize - output.Count);
                    for (int j = 0; j < skipCount; j++)
                    {
                        output.Add(0);
                    }
                }

                pos = dataStart + dataSz;
            }

            return output.ToArray();
        }

        /// <summary>
        /// 在数据中查找模式
        /// </summary>
        public static int FindPattern(byte[] data, byte[] pattern)
        {
            if (data == null || pattern == null || data.Length < pattern.Length)
                return -1;

            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        /// <summary>
        /// 提取ASCII值（到换行或null）
        /// </summary>
        private static string ExtractValue(byte[] data, int start)
        {
            var sb = new StringBuilder();
            for (int i = start; i < data.Length && i < start + 200; i++)
            {
                byte b = data[i];
                if (b == 0x0A || b == 0x0D || b == 0x00 || b == 0x7C) // LF, CR, NULL, |
                    break;
                if (b >= 0x20 && b < 0x7F)
                    sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 提取UTF-8字符串
        /// </summary>
        private static string ExtractUtf8Value(byte[] data, int start, int maxChars)
        {
            int end = start;
            int charCount = 0;

            while (end < data.Length && charCount < maxChars)
            {
                byte b = data[end];
                if (b == 0x00 || b == 0x0A || b == 0x0D || b == 0x7C)
                    break;

                // UTF-8 multi-byte sequence
                if ((b & 0x80) == 0)
                {
                    end++; charCount++;
                }
                else if ((b & 0xE0) == 0xC0 && end + 1 < data.Length)
                {
                    end += 2; charCount++;
                }
                else if ((b & 0xF0) == 0xE0 && end + 2 < data.Length)
                {
                    end += 3; charCount++;
                }
                else if ((b & 0xF8) == 0xF0 && end + 3 < data.Length)
                {
                    end += 4; charCount++;
                }
                else
                {
                    end++;
                }
            }

            try
            {
                return Encoding.UTF8.GetString(data, start, end - start).Trim();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 提取型号字符串 (RMX5010, CPH2451, PKR110等)
        /// </summary>
        private static string ExtractModelString(byte[] data, int start)
        {
            var sb = new StringBuilder();
            for (int i = start; i < data.Length && i < start + 20; i++)
            {
                byte b = data[i];
                if (char.IsLetterOrDigit((char)b))
                    sb.Append((char)b);
                else
                    break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取需要读取的分区列表 (按优先级，最小最快)
        /// 注意: odm/vendor/my_manifest 等都在 super 动态分区内，无法直接读取
        /// </summary>
        public static string[] GetTargetPartitions()
        {
            return new[]
            {
                "devinfo",          // 最小! 只有 4KB (LUN4)
                "param",            // 1MB (LUN5)
                "oplusreserve1",    // 8MB (LUN5)
            };
        }

        /// <summary>
        /// 获取分区读取建议 (最小化读取)
        /// </summary>
        public static (string Partition, long StartSector, int NumSectors, int Lun) GetReadSuggestion(string partitionName, int sectorSize = 4096)
        {
            // 基于固件分析的建议 - 最小化读取
            return partitionName.ToLower() switch
            {
                "devinfo" => ("devinfo", 0, 1, 4),              // 只需 1 扇区 = 4KB (LUN4)
                "param" => ("param", 0, 64, 5),                 // 读取 256KB (LUN5)
                "oplusreserve1" => ("oplusreserve1", 0, 16, 5), // 读取 64KB (LUN5)
                _ => (partitionName, 0, 16, 0)                  // 默认 64KB
            };
        }

        /// <summary>
        /// 从 Loader 文件名推断设备信息
        /// 例如: OPPO_SM8750_New_Chimera_V1.0.05.melf -> Platform: SM8750, Vendor: OPPO
        /// </summary>
        public static Dictionary<string, string> InferFromLoaderPath(string loaderPath)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(loaderPath)) return result;

            var fileName = Path.GetFileName(loaderPath);

            // 提取平台 (SM8750, SM8650, MT6xxx 等)
            var platformMatch = System.Text.RegularExpressions.Regex.Match(
                fileName, @"(SM\d{4}|MT\d{4}|SDM\d{3})", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (platformMatch.Success)
            {
                result["Platform"] = platformMatch.Groups[1].Value.ToUpper();
            }

            // 提取厂商
            var vendors = new[] { "OPPO", "Realme", "OnePlus", "Vivo", "Xiaomi", "Samsung" };
            foreach (var vendor in vendors)
            {
                if (fileName.Contains(vendor, StringComparison.OrdinalIgnoreCase))
                {
                    result["Manufacturer"] = vendor;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 解析 devinfo 分区 (4KB)
        /// OPPO/Realme/OnePlus 设备的 devinfo 结构
        /// </summary>
        public static Dictionary<string, string> ParseDevInfo(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 64) return result;

            // OPPO devinfo 结构分析:
            // 不同厂商的 devinfo 结构不同，这里尝试通用解析

            // 检查 Magic (常见: "DEVU", "DEVI", "OPPO", 0x4F505055)
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            
            // OPLUS devinfo 可能包含:
            // - Unlock state (解锁状态)
            // - Tamper state (篡改状态)
            // - Yellow state (黄色状态)
            // - Orange state (橙色状态)

            // 搜索解锁状态关键字
            int unlockOffset = FindPattern(data, Encoding.ASCII.GetBytes("unlocked"));
            if (unlockOffset >= 0)
            {
                result["UnlockState"] = "Unlocked";
            }
            else
            {
                unlockOffset = FindPattern(data, Encoding.ASCII.GetBytes("locked"));
                if (unlockOffset >= 0)
                {
                    result["UnlockState"] = "Locked";
                }
            }

            // 搜索 AVB 状态
            int avbOffset = FindPattern(data, Encoding.ASCII.GetBytes("avb"));
            if (avbOffset >= 0)
            {
                // 检查后面的值
                string avbValue = ExtractValue(data, avbOffset);
                if (!string.IsNullOrEmpty(avbValue))
                {
                    result["AVBState"] = avbValue;
                }
            }

            // OPLUS 特定: 检查是否有 "orange" 或 "yellow" 状态
            if (FindPattern(data, Encoding.ASCII.GetBytes("orange")) >= 0)
            {
                result["VerifiedBootState"] = "Orange";
            }
            else if (FindPattern(data, Encoding.ASCII.GetBytes("yellow")) >= 0)
            {
                result["VerifiedBootState"] = "Yellow";
            }
            else if (FindPattern(data, Encoding.ASCII.GetBytes("green")) >= 0)
            {
                result["VerifiedBootState"] = "Green";
            }

            // 尝试搜索 IMEI (通常是 15 位数字)
            for (int i = 0; i < data.Length - 15; i++)
            {
                if (IsImeiCandidate(data, i))
                {
                    string imei = Encoding.ASCII.GetString(data, i, 15);
                    // IMEI 通常以 86 开头 (中国)，35 (TAC) 等
                    if (imei.StartsWith("86") || imei.StartsWith("35") || imei.StartsWith("01"))
                    {
                        if (!result.ContainsKey("IMEI"))
                            result["IMEI"] = imei;
                        else if (!result.ContainsKey("IMEI2"))
                            result["IMEI2"] = imei;
                    }
                }
            }

            // 搜索通用属性
            var parsed = Parse(data, data.Length);
            foreach (var kvp in parsed)
            {
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// 解析 param 分区
        /// </summary>
        public static Dictionary<string, string> ParseParam(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 64) return result;

            // param 分区可能包含运营商和地区配置
            var patterns = new[]
            {
                (Encoding.ASCII.GetBytes("carrier="), "Carrier"),
                (Encoding.ASCII.GetBytes("region="), "Region"),
                (Encoding.ASCII.GetBytes("country="), "Country"),
                (Encoding.ASCII.GetBytes("operator="), "Operator"),
                (Encoding.ASCII.GetBytes("nv_id="), "NvId"),
                (Encoding.ASCII.GetBytes("sales_code="), "SalesCode"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(data, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(data, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value))
                    {
                        result[name] = value;
                    }
                }
            }

            // 通用属性搜索
            var parsed = Parse(data, data.Length);
            foreach (var kvp in parsed)
            {
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// 检查是否是有效的 IMEI 候选
        /// </summary>
        private static bool IsImeiCandidate(byte[] data, int offset)
        {
            for (int i = 0; i < 15; i++)
            {
                byte b = data[offset + i];
                if (b < '0' || b > '9')
                    return false;
            }
            // 检查前后是否是非数字 (避免误匹配)
            if (offset > 0 && data[offset - 1] >= '0' && data[offset - 1] <= '9')
                return false;
            if (offset + 15 < data.Length && data[offset + 15] >= '0' && data[offset + 15] <= '9')
                return false;
            return true;
        }

        /// <summary>
        /// 解析 my_manifest 分区 (约 532KB)
        /// 包含市场名称
        /// </summary>
        public static Dictionary<string, string> ParseMyManifest(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 64) return result;

            // my_manifest 是 sparse 格式，需要先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 2 * 1024 * 1024) : data;

            // 搜索市场名称
            // UTF-8 "真我" = E7 9C 9F E6 88 91
            int offset = FindPattern(unsparsed, ChineseMarketPrefix_ZhenWo);
            if (offset >= 0)
            {
                string value = ExtractUtf8Value(unsparsed, offset, 50);
                // 去掉末尾的 | 符号
                if (value.Contains('|'))
                    value = value.Split('|')[0];
                if (!string.IsNullOrEmpty(value))
                {
                    result["MarketName_CN"] = value;
                    result["MarketName"] = value;
                }
            }

            // 搜索 "一加"
            offset = FindPattern(unsparsed, ChineseMarketPrefix_YiJia);
            if (offset >= 0 && !result.ContainsKey("MarketName_CN"))
            {
                string value = ExtractUtf8Value(unsparsed, offset, 50);
                if (value.Contains('|'))
                    value = value.Split('|')[0];
                if (!string.IsNullOrEmpty(value))
                {
                    result["MarketName_CN"] = value;
                    result["MarketName"] = value;
                }
            }

            // 搜索英文市场名称 (realme GT 7 Pro 等)
            var enPatterns = new[]
            {
                Encoding.ASCII.GetBytes("realme GT"),
                Encoding.ASCII.GetBytes("OnePlus "),
                Encoding.ASCII.GetBytes("OPPO "),
            };

            foreach (var pattern in enPatterns)
            {
                offset = FindPattern(unsparsed, pattern);
                if (offset >= 0 && !result.ContainsKey("MarketNameEn"))
                {
                    string value = ExtractValue(unsparsed, offset);
                    if (!string.IsNullOrEmpty(value) && value.Length > 5)
                    {
                        result["MarketNameEn"] = value;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析 my_region 分区 (约 2MB)
        /// 包含地区和运营商信息
        /// </summary>
        public static Dictionary<string, string> ParseMyRegion(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 64) return result;

            // my_region 是 sparse 格式
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 3 * 1024 * 1024) : data;

            // 搜索地区相关属性
            var patterns = new[]
            {
                (Encoding.ASCII.GetBytes("ro.oplus.regionmark="), "RegionMark"),
                (Encoding.ASCII.GetBytes("ro.vendor.oplus.regionmark="), "RegionMark"),
                (Encoding.ASCII.GetBytes("ro.product.locale.region="), "LocaleRegion"),
                (Encoding.ASCII.GetBytes("ro.oplus.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.build.oplus.operator="), "Operator"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析 odm 分区头部 (搜索 build.prop)
        /// </summary>
        public static Dictionary<string, string> ParseOdmHeader(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // odm 是 sparse 格式，但非常大 (2.5GB)
            // 只解压前 10MB 搜索 build.prop 内容
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 10 * 1024 * 1024) : data;

            // 使用通用解析搜索所有属性
            return Parse(unsparsed, unsparsed.Length);
        }

        #region 小米设备专用解析

        /// <summary>
        /// 解析小米 devinfo 分区 - 获取解锁状态、AVB状态、防回滚版本
        /// 小米 devinfo 分区通常位于 LUN0, sector 12288
        /// 只需读取 4KB (1 扇区) 即可获取关键信息
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiDevInfo(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 64) return result;

            // 1. 检查解锁状态
            // 方法1: 搜索字符串
            if (FindPattern(data, Encoding.ASCII.GetBytes("unlocked")) >= 0)
            {
                result["UnlockState"] = "Unlocked";
            }
            else if (FindPattern(data, Encoding.ASCII.GetBytes("locked")) >= 0)
            {
                result["UnlockState"] = "Locked";
            }
            else if (data.Length >= 0x14)
            {
                // 方法2: 检查偏移 0x10 的标志位
                uint flag = BitConverter.ToUInt32(data, 0x10);
                if (flag == 1)
                    result["UnlockState"] = "Unlocked";
                else if (flag == 0)
                    result["UnlockState"] = "Locked";
            }

            // 2. 检查 AVB/Verified Boot 状态
            var avbPatterns = new[]
            {
                (Encoding.ASCII.GetBytes("orange"), "Orange"),
                (Encoding.ASCII.GetBytes("green"), "Green"),
                (Encoding.ASCII.GetBytes("yellow"), "Yellow"),
                (Encoding.ASCII.GetBytes("red"), "Red"),
            };

            foreach (var (pattern, state) in avbPatterns)
            {
                if (FindPatternIgnoreCase(data, pattern) >= 0)
                {
                    result["VerifiedBootState"] = state;
                    break;
                }
            }

            // 3. 搜索防回滚版本 (anti=X 或 anti:X)
            var antiMatch = System.Text.RegularExpressions.Regex.Match(
                Encoding.ASCII.GetString(data), @"anti[=:](\d+)");
            if (antiMatch.Success)
            {
                result["AntiVersion"] = antiMatch.Groups[1].Value;
            }

            // 4. 搜索 tamper 标志
            int tamperIdx = FindPatternIgnoreCase(data, Encoding.ASCII.GetBytes("tamper"));
            if (tamperIdx >= 0 && tamperIdx + 8 < data.Length)
            {
                result["TamperFlag"] = data[tamperIdx + 7] != 0 ? "Set" : "Clear";
            }

            // 5. 通用属性搜索
            var parsed = Parse(data, data.Length);
            foreach (var kvp in parsed)
            {
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// 解析 modemst 分区 - 获取 IMEI (通用 Qualcomm 平台)
        /// 适用于: Xiaomi/OPPO/Realme/OnePlus/Vivo 等所有 Qualcomm 设备
        /// IMEI 存储格式: NV Item 550 (08 XA YZ YZ YZ YZ YZ YZ YZ)
        /// </summary>
        public static Dictionary<string, string> ParseModemEfs(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 256) return result;

            var foundImeis = new List<string>();

            // 方法1: 搜索 NV Item 550 格式的 IMEI (最准确)
            // 格式: 08 XA YZ YZ YZ YZ YZ YZ YZ (9字节)
            // X = 第一位数字, A = 0xA (填充), 后续为 BCD 编码
            // 例如 IMEI "867584030123456" -> 08 8A 67 58 40 30 12 34 56
            for (int i = 0; i < data.Length - 9; i++)
            {
                if (data[i] == 0x08)
                {
                    string nvImei = TryDecodeNvImei(data, i);
                    if (!string.IsNullOrEmpty(nvImei) && !foundImeis.Contains(nvImei))
                    {
                        foundImeis.Add(nvImei);
                        if (foundImeis.Count >= 2) break;
                    }
                }
            }

            // 方法2: 搜索明文 IMEI (作为补充或后备)
            if (foundImeis.Count < 2)
            {
                // 搜索所有 15 位数字序列，以常见前缀开头
                // 常见前缀: 86(中国), 35(国际), 01, 99, 49(德国), 45(丹麦)
                var imeiRegex = new System.Text.RegularExpressions.Regex(@"(?<!\d)((?:86|35|01|99|49|45|44|46|91)\d{13})(?!\d)");
                string asciiData = Encoding.ASCII.GetString(data.Select(b => (b >= 0x20 && b < 0x7F) ? b : (byte)'.').ToArray());
                var matches = imeiRegex.Matches(asciiData);
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    string imei = match.Groups[1].Value;
                    if (ValidateImeiLuhn(imei) && !foundImeis.Contains(imei))
                    {
                        foundImeis.Add(imei);
                        if (foundImeis.Count >= 2) break;
                    }
                }
            }

            // 填充结果
            if (foundImeis.Count > 0)
            {
                result["IMEI"] = foundImeis[0];
            }
            if (foundImeis.Count > 1)
            {
                result["IMEI2"] = foundImeis[1];
            }

            return result;
        }
        
        /// <summary>
        /// 解码 NV Item 550 格式的 IMEI
        /// 格式: 08 XA YZ YZ YZ YZ YZ YZ YZ (9字节)
        /// </summary>
        private static string TryDecodeNvImei(byte[] data, int offset)
        {
            if (offset + 9 > data.Length) return "";
            if (data[offset] != 0x08) return "";
            
            // 第2字节: 高4位是第1位数字，低4位必须是 0xA
            byte b1 = data[offset + 1];
            if ((b1 & 0x0F) != 0x0A) return "";
            
            int firstDigit = (b1 >> 4) & 0x0F;
            if (firstDigit > 9) return "";
            
            var sb = new StringBuilder();
            sb.Append(firstDigit);
            
            // 后续 7 字节，每字节包含 2 位数字
            for (int j = 2; j < 9; j++)
            {
                byte b = data[offset + j];
                int high = (b >> 4) & 0x0F;
                int low = b & 0x0F;
                
                if (high > 9 || low > 9) return "";
                
                sb.Append(high);
                sb.Append(low);
            }
            
            string imei = sb.ToString();
            if (imei.Length == 15 && ValidateImeiLuhn(imei))
            {
                return imei;
            }
            
            return "";
        }

        /// <summary>
        /// 解析小米 modemst 分区 - 别名方法保持兼容
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiModemEfs(byte[] data) => ParseModemEfs(data);

        /// <summary>
        /// 解析 OPPO/Realme modemst 分区 - 别名方法
        /// </summary>
        public static Dictionary<string, string> ParseOplusModemEfs(byte[] data) => ParseModemEfs(data);

        /// <summary>
        /// 使用 Luhn 算法验证 IMEI
        /// </summary>
        private static bool ValidateImeiLuhn(string imei)
        {
            if (string.IsNullOrEmpty(imei) || imei.Length != 15) return false;
            
            foreach (char c in imei)
            {
                if (!char.IsDigit(c)) return false;
            }

            int total = 0;
            for (int i = 0; i < 14; i++)
            {
                int d = imei[i] - '0';
                if (i % 2 == 1)
                {
                    d *= 2;
                    if (d > 9) d -= 9;
                }
                total += d;
            }

            int check = (10 - (total % 10)) % 10;
            return check == (imei[14] - '0');
        }

        /// <summary>
        /// 忽略大小写搜索模式
        /// </summary>
        private static int FindPatternIgnoreCase(byte[] data, byte[] pattern)
        {
            if (data == null || pattern == null || data.Length < pattern.Length)
                return -1;

            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    byte d = data[i + j];
                    byte p = pattern[j];
                    // 转小写比较
                    if (d >= 'A' && d <= 'Z') d = (byte)(d + 32);
                    if (p >= 'A' && p <= 'Z') p = (byte)(p + 32);
                    
                    if (d != p)
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        /// <summary>
        /// 解析小米固件 misc.txt 文件
        /// 小米固件包中的 misc.txt 包含:
        /// - device=xxx (设备代号)
        /// - build_number=xxx (MIUI版本)
        /// - userdata_version=xxx (编译日期)
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiMiscTxt(string content)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(content)) return result;

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;

                string key = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();

                switch (key.ToLower())
                {
                    case "device":
                        result["Device"] = value;
                        break;
                    case "build_number":
                        result["OtaVersion"] = value;
                        // 解析 MIUI 版本 (V14.0.6.0.TGACNXM -> MIUI 14)
                        if (value.StartsWith("V") && value.Length > 2)
                        {
                            var majorMatch = System.Text.RegularExpressions.Regex.Match(value, @"^V(\d+)");
                            if (majorMatch.Success)
                            {
                                result["DisplayId"] = $"MIUI {majorMatch.Groups[1].Value}";
                            }
                        }
                        break;
                    case "userdata_version":
                        // 格式: 20230904.0000.00
                        if (value.Length >= 8)
                        {
                            result["BuildDate"] = value.Substring(0, 8);
                        }
                        break;
                    case "custom_bulid_number":
                    case "custom_build_number":
                        result["Project"] = value;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// 从小米固件目录名解析版本信息
        /// 格式: thyme_images_V14.0.6.0.TGACNXM_20230904.0000.00_13.0_cn_c97bf99cf1
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiFirmwareDirName(string dirName)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(dirName)) return result;

            // 设备代号 (第一个 _images 之前)
            var deviceMatch = System.Text.RegularExpressions.Regex.Match(dirName, @"^([a-z]+)_images", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (deviceMatch.Success)
            {
                result["Device"] = deviceMatch.Groups[1].Value.ToLower();
            }

            // MIUI 版本 (V开头的版本号)
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dirName, @"(V\d+\.\d+\.\d+\.\d+\.[A-Z]+)");
            if (versionMatch.Success)
            {
                result["OtaVersion"] = versionMatch.Groups[1].Value;
                // 提取 MIUI 主版本号
                var majorMatch = System.Text.RegularExpressions.Regex.Match(versionMatch.Groups[1].Value, @"^V(\d+)");
                if (majorMatch.Success)
                {
                    result["DisplayId"] = $"MIUI {majorMatch.Groups[1].Value}";
                }
            }

            // Android 版本
            var androidMatch = System.Text.RegularExpressions.Regex.Match(dirName, @"_(\d+\.\d+)_");
            if (androidMatch.Success)
            {
                result["AndroidVersion"] = androidMatch.Groups[1].Value;
            }

            // 地区
            var regionMatch = System.Text.RegularExpressions.Regex.Match(dirName, @"_\d+\.\d+_([a-z]+)_", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (regionMatch.Success)
            {
                string region = regionMatch.Groups[1].Value.ToUpper();
                result["Region"] = region;
                // 地区映射
                result["RegionMark"] = region switch
                {
                    "CN" => "China",
                    "IN" => "India",
                    "RU" => "Russia",
                    "EU" => "Europe",
                    "TW" => "Taiwan",
                    "JP" => "Japan",
                    "KR" => "Korea",
                    "ID" => "Indonesia",
                    "TR" => "Turkey",
                    "GLOBAL" => "Global",
                    _ => region
                };
            }

            // 编译日期
            var dateMatch = System.Text.RegularExpressions.Regex.Match(dirName, @"(\d{8})\.\d+\.\d+");
            if (dateMatch.Success)
            {
                result["BuildDate"] = dateMatch.Groups[1].Value;
            }

            // 品牌和厂商
            result["Brand"] = "Xiaomi";
            result["Manufacturer"] = "Xiaomi";

            return result;
        }

        /// <summary>
        /// 解析小米 super.img 中 odm_a 分区的属性
        /// odm_a 分区包含详细的设备信息:
        /// - ro.product.odm.model
        /// - ro.product.odm.marketname
        /// - ro.product.odm.brand
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiOdmPartition(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 128 * 1024 * 1024) : data;

            // 小米 ODM 特有属性模式
            var xiaomiPatterns = new (byte[] Pattern, string Name)[]
            {
                (Encoding.ASCII.GetBytes("ro.product.odm.marketname="), "MarketName"),
                (Encoding.ASCII.GetBytes("ro.product.odm.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.odm.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.odm.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.odm.manufacturer="), "Manufacturer"),
                (Encoding.ASCII.GetBytes("ro.product.odm.name="), "ProductName"),
                (Encoding.ASCII.GetBytes("ro.product.odm.cert="), "Cert"),
                (Encoding.ASCII.GetBytes("ro.odm.build.version.incremental="), "OtaVersion"),
                (Encoding.ASCII.GetBytes("ro.odm.build.version.release="), "AndroidVersion"),
                (Encoding.ASCII.GetBytes("ro.odm.build.version.sdk="), "SdkVersion"),
                (Encoding.ASCII.GetBytes("ro.odm.build.id="), "BuildId"),
                (Encoding.ASCII.GetBytes("ro.odm.build.fingerprint="), "Fingerprint"),
                (Encoding.ASCII.GetBytes("ro.odm.build.date="), "BuildDate"),
                // 小米地区/运营商相关
                (Encoding.ASCII.GetBytes("ro.miui.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.miui.country_code="), "CountryCode"),
                (Encoding.ASCII.GetBytes("ro.carrier.name="), "Carrier"),
                (Encoding.ASCII.GetBytes("ro.miui.cust_variant="), "CustVariant"),
                (Encoding.ASCII.GetBytes("ro.rom.zone="), "RomZone"),
                (Encoding.ASCII.GetBytes("ro.miui.ui.version.name="), "MIUIVersion"),
                (Encoding.ASCII.GetBytes("ro.miui.ui.version.code="), "MIUIVersionCode"),
            };

            foreach (var (pattern, name) in xiaomiPatterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            // 通用属性搜索
            var parsed = Parse(unsparsed, unsparsed.Length);
            foreach (var kvp in parsed)
            {
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// 解析小米 super.img 中 vendor_a 分区的属性
        /// vendor_a 分区包含硬件和平台信息:
        /// - ro.board.platform
        /// - ro.product.vendor.marketname
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiVendorPartition(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 100 * 1024 * 1024) : data;

            // 小米 Vendor 特有属性模式
            var vendorPatterns = new (byte[] Pattern, string Name)[]
            {
                (Encoding.ASCII.GetBytes("ro.board.platform="), "Platform"),
                (Encoding.ASCII.GetBytes("ro.product.board="), "Board"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.marketname="), "MarketName"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.manufacturer="), "Manufacturer"),
                (Encoding.ASCII.GetBytes("ro.product.first_api_level="), "FirstApiLevel"),
                (Encoding.ASCII.GetBytes("ro.product.mod_device="), "ModDevice"),
                (Encoding.ASCII.GetBytes("ro.vendor.build.fingerprint="), "VendorFingerprint"),
                (Encoding.ASCII.GetBytes("ro.hardware.chipname="), "ChipName"),
                // 小米地区/运营商相关
                (Encoding.ASCII.GetBytes("ro.miui.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.miui.country_code="), "CountryCode"),
                (Encoding.ASCII.GetBytes("ro.carrier.name="), "Carrier"),
                (Encoding.ASCII.GetBytes("ro.telephony.default_network="), "DefaultNetwork"),
                (Encoding.ASCII.GetBytes("persist.sys.timezone="), "Timezone"),
            };

            foreach (var (pattern, name) in vendorPatterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析 Lenovo proinfo 分区 - 获取设备生产信息
        /// proinfo 分区包含:
        /// - 设备型号
        /// - 生产信息
        /// - 解锁状态备份
        /// </summary>
        public static Dictionary<string, string> ParseLenovoProinfo(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 256) return result;

            // Lenovo proinfo 结构分析
            // 通常是简单的文本格式或二进制结构

            // 搜索型号信息 (TB开头)
            var modelMatch = System.Text.RegularExpressions.Regex.Match(
                Encoding.ASCII.GetString(data),
                @"(TB\d+[A-Z]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (modelMatch.Success)
            {
                result["Model"] = modelMatch.Groups[1].Value;
            }

            // 搜索序列号模式
            var snMatch = System.Text.RegularExpressions.Regex.Match(
                Encoding.ASCII.GetString(data),
                @"serialno=([A-Z0-9]{10,20})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (snMatch.Success)
            {
                result["SerialNumber"] = snMatch.Groups[1].Value;
            }

            // 搜索属性
            var patterns = new (byte[] Pattern, string Name)[]
            {
                (Encoding.ASCII.GetBytes("ro.product.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.manufacturer="), "Manufacturer"),
                (Encoding.ASCII.GetBytes("ro.serialno="), "SerialNumber"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(data, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(data, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            // 设置厂商
            if (!result.ContainsKey("Brand"))
                result["Brand"] = "Lenovo";
            if (!result.ContainsKey("Manufacturer"))
                result["Manufacturer"] = "Lenovo";

            return result;
        }

        /// <summary>
        /// 解析 Lenovo lenovocust 分区 - 获取地区和定制信息
        /// </summary>
        public static Dictionary<string, string> ParseLenovoCust(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 100 * 1024 * 1024) : data;

            // Lenovo 定制分区属性
            var patterns = new (byte[] Pattern, string Name)[]
            {
                // 地区相关
                (Encoding.ASCII.GetBytes("ro.lenovo.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.lenovo.country="), "Country"),
                (Encoding.ASCII.GetBytes("ro.lenovo.sku="), "SKU"),
                (Encoding.ASCII.GetBytes("ro.lenovo.series="), "Series"),
                (Encoding.ASCII.GetBytes("persist.sys.timezone="), "Timezone"),
                (Encoding.ASCII.GetBytes("persist.sys.language="), "Language"),
                (Encoding.ASCII.GetBytes("persist.sys.country="), "Country"),
                // 设备信息
                (Encoding.ASCII.GetBytes("ro.product.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.name="), "ProductName"),
                (Encoding.ASCII.GetBytes("ro.build.display.id="), "DisplayId"),
                (Encoding.ASCII.GetBytes("ro.build.version.release="), "AndroidVersion"),
                (Encoding.ASCII.GetBytes("ro.build.version.incremental="), "OtaVersion"),
                (Encoding.ASCII.GetBytes("ro.build.fingerprint="), "Fingerprint"),
                // 运营商
                (Encoding.ASCII.GetBytes("ro.carrier="), "Carrier"),
                (Encoding.ASCII.GetBytes("ro.com.google.clientidbase="), "ClientIdBase"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析 Lenovo odm 分区 - 获取设备型号和 OTA 版本
        /// odm_a 分区包含:
        /// - 设备型号 (TB321FU)
        /// - 市场名称
        /// - OTA 版本
        /// - 安卓版本
        /// </summary>
        public static Dictionary<string, string> ParseLenovoOdmPartition(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 100 * 1024 * 1024) : data;

            // Lenovo ODM 分区属性
            var patterns = new (byte[] Pattern, string Name)[]
            {
                // 设备型号
                (Encoding.ASCII.GetBytes("ro.product.odm.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.odm.marketname="), "MarketName"),
                (Encoding.ASCII.GetBytes("ro.product.odm.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.odm.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.odm.manufacturer="), "Manufacturer"),
                (Encoding.ASCII.GetBytes("ro.product.odm.name="), "ProductName"),
                // OTA/版本信息
                (Encoding.ASCII.GetBytes("ro.odm.build.version.incremental="), "OtaVersion"),
                (Encoding.ASCII.GetBytes("ro.odm.build.version.release="), "AndroidVersion"),
                (Encoding.ASCII.GetBytes("ro.odm.build.fingerprint="), "Fingerprint"),
                (Encoding.ASCII.GetBytes("ro.odm.build.display.id="), "DisplayId"),
                (Encoding.ASCII.GetBytes("ro.odm.build.version.security_patch="), "SecurityPatch"),
                // 通用属性
                (Encoding.ASCII.GetBytes("ro.product.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.build.version.incremental="), "OtaVersion"),
                (Encoding.ASCII.GetBytes("ro.build.version.release="), "AndroidVersion"),
                (Encoding.ASCII.GetBytes("ro.build.display.id="), "DisplayId"),
                (Encoding.ASCII.GetBytes("ro.build.fingerprint="), "Fingerprint"),
                // 平台
                (Encoding.ASCII.GetBytes("ro.board.platform="), "Platform"),
                (Encoding.ASCII.GetBytes("ro.hardware.chipname="), "ChipName"),
                // Lenovo 特有
                (Encoding.ASCII.GetBytes("ro.lenovo.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.lenovo.series="), "Series"),
                (Encoding.ASCII.GetBytes("ro.lenovo.sku="), "SKU"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            // 设置默认厂商
            if (!result.ContainsKey("Brand"))
                result["Brand"] = "Lenovo";
            if (!result.ContainsKey("Manufacturer"))
                result["Manufacturer"] = "Lenovo";

            return result;
        }

        /// <summary>
        /// 解析 Lenovo vendor 分区 - 获取平台和版本信息
        /// vendor_a 分区包含:
        /// - 平台信息 (SM8650)
        /// - 版本信息
        /// - 指纹
        /// </summary>
        public static Dictionary<string, string> ParseLenovoVendorPartition(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 80 * 1024 * 1024) : data;

            // Lenovo Vendor 分区属性
            var patterns = new (byte[] Pattern, string Name)[]
            {
                // Vendor 设备信息
                (Encoding.ASCII.GetBytes("ro.product.vendor.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.device="), "Device"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.manufacturer="), "Manufacturer"),
                (Encoding.ASCII.GetBytes("ro.product.vendor.name="), "ProductName"),
                // 版本信息
                (Encoding.ASCII.GetBytes("ro.vendor.build.version.incremental="), "OtaVersion"),
                (Encoding.ASCII.GetBytes("ro.vendor.build.version.release="), "AndroidVersion"),
                (Encoding.ASCII.GetBytes("ro.vendor.build.fingerprint="), "Fingerprint"),
                (Encoding.ASCII.GetBytes("ro.vendor.build.version.security_patch="), "SecurityPatch"),
                (Encoding.ASCII.GetBytes("ro.vendor.build.display.id="), "DisplayId"),
                // 平台
                (Encoding.ASCII.GetBytes("ro.board.platform="), "Platform"),
                (Encoding.ASCII.GetBytes("ro.hardware.chipname="), "ChipName"),
                (Encoding.ASCII.GetBytes("ro.hardware="), "Hardware"),
                (Encoding.ASCII.GetBytes("ro.soc.model="), "SocModel"),
                (Encoding.ASCII.GetBytes("ro.soc.manufacturer="), "SocManufacturer"),
                // API 级别
                (Encoding.ASCII.GetBytes("ro.product.first_api_level="), "FirstApiLevel"),
                (Encoding.ASCII.GetBytes("ro.board.api_level="), "BoardApiLevel"),
                // 时区/地区
                (Encoding.ASCII.GetBytes("persist.sys.timezone="), "Timezone"),
            };

            foreach (var (pattern, name) in patterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析小米 cust 分区 - 获取地区和运营商信息
        /// cust 分区包含:
        /// - 地区配置
        /// - 运营商定制
        /// - 语言设置
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiCustPartition(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 1024) return result;

            // 如果是 sparse 格式，先解压
            byte[] unsparsed = IsSparse(data) ? Unsparse(data, 50 * 1024 * 1024) : data;

            // 小米 cust 分区地区/运营商相关属性
            var custPatterns = new (byte[] Pattern, string Name)[]
            {
                // 地区相关
                (Encoding.ASCII.GetBytes("ro.miui.region="), "Region"),
                (Encoding.ASCII.GetBytes("ro.miui.country_code="), "CountryCode"),
                (Encoding.ASCII.GetBytes("ro.product.locale="), "Locale"),
                (Encoding.ASCII.GetBytes("ro.product.locale.region="), "LocaleRegion"),
                (Encoding.ASCII.GetBytes("persist.sys.country="), "Country"),
                (Encoding.ASCII.GetBytes("persist.sys.language="), "Language"),
                (Encoding.ASCII.GetBytes("persist.sys.timezone="), "Timezone"),
                // 运营商相关
                (Encoding.ASCII.GetBytes("ro.carrier.name="), "Carrier"),
                (Encoding.ASCII.GetBytes("ro.miui.cust_variant="), "CustVariant"),
                (Encoding.ASCII.GetBytes("ro.telephony.default_network="), "DefaultNetwork"),
                (Encoding.ASCII.GetBytes("gsm.sim.operator.alpha="), "SimOperator"),
                (Encoding.ASCII.GetBytes("gsm.operator.alpha="), "Operator"),
                // MIUI 版本相关
                (Encoding.ASCII.GetBytes("ro.miui.ui.version.name="), "MIUIVersion"),
                (Encoding.ASCII.GetBytes("ro.miui.ui.version.code="), "MIUIVersionCode"),
                (Encoding.ASCII.GetBytes("ro.rom.zone="), "RomZone"),
                // 设备信息
                (Encoding.ASCII.GetBytes("ro.product.model="), "Model"),
                (Encoding.ASCII.GetBytes("ro.product.brand="), "Brand"),
                (Encoding.ASCII.GetBytes("ro.product.device="), "Device"),
            };

            foreach (var (pattern, name) in custPatterns)
            {
                int offset = FindPattern(unsparsed, pattern);
                if (offset >= 0)
                {
                    string value = ExtractValue(unsparsed, offset + pattern.Length);
                    if (!string.IsNullOrEmpty(value) && !result.ContainsKey(name))
                    {
                        result[name] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解析小米 vbmeta.img - 获取 AVB 元数据
        /// </summary>
        public static Dictionary<string, string> ParseXiaomiVbmeta(byte[] data)
        {
            var result = new Dictionary<string, string>();
            if (data == null || data.Length < 128) return result;

            // VBMeta header magic: "AVB0"
            if (data[0] != 'A' || data[1] != 'V' || data[2] != 'B' || data[3] != '0')
                return result;

            // AVB version (big-endian)
            uint avbMajor = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
            uint avbMinor = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]);
            result["AVBVersion"] = $"{avbMajor}.{avbMinor}";

            // Algorithm type at offset 28 (big-endian)
            if (data.Length >= 32)
            {
                uint algoType = (uint)((data[28] << 24) | (data[29] << 16) | (data[30] << 8) | data[31]);
                result["AVBAlgorithm"] = algoType switch
                {
                    0 => "NONE",
                    1 => "SHA256_RSA2048",
                    2 => "SHA256_RSA4096",
                    3 => "SHA256_RSA8192",
                    4 => "SHA512_RSA2048",
                    5 => "SHA512_RSA4096",
                    6 => "SHA512_RSA8192",
                    _ => $"UNKNOWN({algoType})"
                };
            }

            // Flags at offset 120 (big-endian)
            if (data.Length >= 124)
            {
                uint flags = (uint)((data[120] << 24) | (data[121] << 16) | (data[122] << 8) | data[123]);
                
                // Bit 0: Verification disabled
                if ((flags & 0x01) != 0)
                    result["AVBVerification"] = "Disabled";
                else
                    result["AVBVerification"] = "Enabled";

                // Bit 1: Hashtree disabled
                if ((flags & 0x02) != 0)
                    result["AVBHashtree"] = "Disabled";
                else
                    result["AVBHashtree"] = "Enabled";
            }

            return result;
        }

        #endregion
    }
}
