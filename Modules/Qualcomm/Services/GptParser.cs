// ============================================================================
// MultiFlash TOOL - GPT Partition Table Parser
// GPT 分区表解析器 | GPTパーティションテーブルパーサー | GPT 파티션 테이블 파서
// ============================================================================
// [EN] GUID Partition Table (GPT) parser for Qualcomm devices
//      Compatible with edl-master/gpt.py format
// [中文] 高通设备 GUID 分区表 (GPT) 解析器
//       兼容 edl-master/gpt.py 格式
// [日本語] Qualcommデバイス用GUIDパーティションテーブル（GPT）パーサー
//         edl-master/gpt.py形式と互換
// [한국어] 퀄컴 장치용 GUID 파티션 테이블(GPT) 파서
//         edl-master/gpt.py 형식과 호환
// [Español] Analizador de tabla de particiones GUID (GPT) para dispositivos Qualcomm
//           Compatible con el formato edl-master/gpt.py
// [Русский] Парсер таблицы разделов GUID (GPT) для устройств Qualcomm
//           Совместим с форматом edl-master/gpt.py
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// GPT Partition Table Parser (edl-master compatible)
    /// GPT 分区表解析器 | GPTパーサー | GPT 파서
    /// </summary>
    public static class GptParser
    {
        private const string GPT_SIGNATURE = "EFI PART";
        private const int GPT_HEADER_SIZE = 0x5C; // 92 bytes

        /// <summary>
        /// 解析 GPT 数据 (edl-master 兼容)
        /// </summary>
        public static List<PartitionInfo> ParseGptBytes(byte[] gptData, int lun, int sectorSize = 4096)
        {
            var partitions = new List<PartitionInfo>();

            if (gptData == null || gptData.Length < sectorSize * 2)
            {
                System.Diagnostics.Debug.WriteLine($"[GPT] 数据太短: {gptData?.Length ?? 0} < {sectorSize * 2}");
                return partitions;
            }

            try
            {
                // 尝试不同扇区大小 (参考 edl-master)
                foreach (int trySize in new[] { sectorSize, 512, 4096 })
                {
                    if (gptData.Length < trySize * 2 + GPT_HEADER_SIZE)
                        continue;

                    // GPT 头始终在 sectorSize 偏移 (LBA 1)
                    int headerOffset = trySize;

                    // 检查签名
                    string sig = Encoding.ASCII.GetString(gptData, headerOffset, 8);
                    if (sig != GPT_SIGNATURE)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GPT] 扇区大小 {trySize}: 签名不匹配 '{sig}'");
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"[GPT] ✓ 找到签名，扇区大小={trySize}");

                    // 解析 GPT 头
                    uint revision = BitConverter.ToUInt32(gptData, headerOffset + 8);
                    if (revision != 0x10000)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GPT] 未知版本: 0x{revision:X}");
                    }

                    // 从 GPT 头读取分区条目信息
                    long partEntryStartLba = (long)BitConverter.ToUInt64(gptData, headerOffset + 72);
                    int numPartEntries = BitConverter.ToInt32(gptData, headerOffset + 80);
                    int partEntrySize = BitConverter.ToInt32(gptData, headerOffset + 84);

                    System.Diagnostics.Debug.WriteLine($"[GPT] partEntryStartLba={partEntryStartLba}, numPartEntries={numPartEntries}, entrySize={partEntrySize}");

                    // 分区条目起始位置
                    // 默认: 2 * sectorSize (MBR + 头), 或使用 GPT 头指定的位置
                    int entryOffset;
                    if (partEntryStartLba > 0 && partEntryStartLba < 100)
                    {
                        entryOffset = (int)(partEntryStartLba * trySize);
                    }
                    else
                    {
                        entryOffset = 2 * trySize;  // 默认 LBA 2
                    }

                    System.Diagnostics.Debug.WriteLine($"[GPT] 分区条目起始偏移: {entryOffset} (0x{entryOffset:X})");

                    if (partEntrySize < 128)
                        partEntrySize = 128;

                    // 解析每个分区条目
                    for (int i = 0; i < numPartEntries && i < 128; i++)
                    {
                        int offset = entryOffset + (i * partEntrySize);
                        if (offset + partEntrySize > gptData.Length)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GPT] 条目 {i} 超出数据范围");
                            break;
                        }

                        // 检查 unique GUID 是否为零 (参考 edl-master)
                        bool isEmpty = true;
                        for (int j = 16; j < 32; j++)  // unique GUID at offset 16-32
                        {
                            if (gptData[offset + j] != 0)
                            {
                                isEmpty = false;
                                break;
                            }
                        }

                        if (isEmpty) break;  // edl-master 遇到空条目就停止

                        // 检查分区类型是否为 EFI_UNUSED
                        uint typeFirst = BitConverter.ToUInt32(gptData, offset);
                        if (typeFirst == 0)
                            continue;

                        // 提取分区名称 (UTF-16LE)
                        string name = ReadUtf16Name(gptData, offset + 56, 72);
                        if (string.IsNullOrEmpty(name))
                            continue;

                        ulong firstLba = BitConverter.ToUInt64(gptData, offset + 32);
                        ulong lastLba = BitConverter.ToUInt64(gptData, offset + 40);

                        var partition = new PartitionInfo
                        {
                            Lun = lun,
                            SectorSize = trySize,
                            TypeGuid = FormatGuid(gptData, offset),
                            UniqueGuid = FormatGuid(gptData, offset + 16),
                            StartSector = (long)firstLba,
                            NumSectors = (long)(lastLba - firstLba + 1),
                            Attributes = BitConverter.ToUInt64(gptData, offset + 48),
                            Name = name
                        };

                        partitions.Add(partition);
                        System.Diagnostics.Debug.WriteLine($"[GPT] 分区 {i}: {name}, LBA {firstLba}-{lastLba}");
                    }

                    if (partitions.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GPT] ✓ 解析成功: {partitions.Count} 个分区");
                        return partitions;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GPT] 解析异常: {ex.Message}");
            }

            return partitions;
        }

        /// <summary>
        /// 格式化 GUID
        /// </summary>
        private static string FormatGuid(byte[] data, int offset)
        {
            if (offset + 16 > data.Length) return "";

            // GUID 格式: DWORD-WORD-WORD-WORD-6BYTES
            uint d1 = BitConverter.ToUInt32(data, offset);
            ushort d2 = BitConverter.ToUInt16(data, offset + 4);
            ushort d3 = BitConverter.ToUInt16(data, offset + 6);
            ushort d4 = (ushort)((data[offset + 8] << 8) | data[offset + 9]);
            string d5 = BitConverter.ToString(data, offset + 10, 6).Replace("-", "").ToLowerInvariant();

            return $"{d1:x8}-{d2:x4}-{d3:x4}-{d4:x4}-{d5}";
        }

        /// <summary>
        /// 读取 UTF-16 分区名称 (参考 edl-master)
        /// </summary>
        private static string ReadUtf16Name(byte[] data, int offset, int maxLength)
        {
            if (offset + maxLength > data.Length)
                maxLength = data.Length - offset;

            // 先找到双零字节结束位置
            int endPos = maxLength;
            for (int i = 0; i < maxLength - 1; i += 2)
            {
                if (data[offset + i] == 0 && data[offset + i + 1] == 0)
                {
                    endPos = i;
                    break;
                }
            }

            if (endPos == 0)
                return "";

            try
            {
                return Encoding.Unicode.GetString(data, offset, endPos);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 直接解析，带日志输出
        /// </summary>
        public static List<PartitionInfo> ParseWithLog(byte[] gptData, int lun, int sectorSize, Action<string>? log)
        {
            log?.Invoke($"[GPT] 解析 LUN{lun}: 数据长度={gptData?.Length ?? 0}, 扇区={sectorSize}");

            if (gptData == null || gptData.Length < 512)
            {
                log?.Invoke("[GPT] ✗ 数据太短");
                return new List<PartitionInfo>();
            }

            // 显示前 64 字节 (调试)
            log?.Invoke($"[GPT] 数据头: {BitConverter.ToString(gptData, 0, Math.Min(64, gptData.Length))}");

            var result = ParseGptBytes(gptData, lun, sectorSize);
            log?.Invoke($"[GPT] 解析结果: {result.Count} 个分区");

            return result;
        }
    }
}
