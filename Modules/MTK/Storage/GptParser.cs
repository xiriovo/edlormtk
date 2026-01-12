using System;
using System.Collections.Generic;
using System.Text;
using tools.Modules.MTK.Models;

namespace tools.Modules.MTK.Storage
{
    /// <summary>
    /// GPT (GUID Partition Table) 解析器
    /// </summary>
    public class GptParser
    {
        // GPT 签名
        private const ulong GPT_SIGNATURE = 0x5452415020494645; // "EFI PART"

        // GPT 头部大小
        private const int GPT_HEADER_SIZE = 92;

        // 分区条目大小
        private const int GPT_ENTRY_SIZE = 128;

        /// <summary>
        /// 解析 GPT 分区表
        /// </summary>
        public static List<MtkPartition> Parse(byte[] gptData, uint sectorSize = 512)
        {
            var partitions = new List<MtkPartition>();

            if (gptData == null || gptData.Length < sectorSize * 2)
            {
                return partitions;
            }

            // GPT 头在 LBA 1 (跳过保护性 MBR)
            int headerOffset = (int)sectorSize;

            // 验证签名
            ulong signature = BitConverter.ToUInt64(gptData, headerOffset);
            if (signature != GPT_SIGNATURE)
            {
                // 尝试从头部开始
                headerOffset = 0;
                signature = BitConverter.ToUInt64(gptData, headerOffset);
                if (signature != GPT_SIGNATURE)
                {
                    return partitions;
                }
            }

            // 解析 GPT 头
            uint revision = BitConverter.ToUInt32(gptData, headerOffset + 8);
            uint headerSize = BitConverter.ToUInt32(gptData, headerOffset + 12);
            uint headerCrc32 = BitConverter.ToUInt32(gptData, headerOffset + 16);
            ulong currentLba = BitConverter.ToUInt64(gptData, headerOffset + 24);
            ulong backupLba = BitConverter.ToUInt64(gptData, headerOffset + 32);
            ulong firstUsableLba = BitConverter.ToUInt64(gptData, headerOffset + 40);
            ulong lastUsableLba = BitConverter.ToUInt64(gptData, headerOffset + 48);

            // Disk GUID (16 bytes)
            byte[] diskGuid = new byte[16];
            Array.Copy(gptData, headerOffset + 56, diskGuid, 0, 16);

            ulong partitionEntryLba = BitConverter.ToUInt64(gptData, headerOffset + 72);
            uint numPartitionEntries = BitConverter.ToUInt32(gptData, headerOffset + 80);
            uint partitionEntrySize = BitConverter.ToUInt32(gptData, headerOffset + 84);
            uint partitionArrayCrc32 = BitConverter.ToUInt32(gptData, headerOffset + 88);

            // 计算分区条目起始偏移
            int entryOffset;
            if (partitionEntryLba == 2)
            {
                // 标准位置: LBA 2
                entryOffset = (int)(sectorSize * 2);
            }
            else if (headerOffset == 0)
            {
                // 如果头从 0 开始，条目紧随其后
                entryOffset = GPT_HEADER_SIZE;
            }
            else
            {
                entryOffset = (int)(partitionEntryLba * sectorSize);
            }

            // 解析每个分区条目
            for (int i = 0; i < numPartitionEntries; i++)
            {
                int offset = entryOffset + (int)(i * partitionEntrySize);

                if (offset + partitionEntrySize > gptData.Length)
                    break;

                // 解析分区条目
                var partition = ParseEntry(gptData, offset, sectorSize);

                // 跳过空分区
                if (partition == null || string.IsNullOrEmpty(partition.Name))
                    continue;

                partitions.Add(partition);
            }

            return partitions;
        }

        /// <summary>
        /// 解析单个分区条目
        /// </summary>
        private static MtkPartition? ParseEntry(byte[] data, int offset, uint sectorSize)
        {
            // Type GUID (16 bytes)
            byte[] typeGuidBytes = new byte[16];
            Array.Copy(data, offset, typeGuidBytes, 0, 16);
            Guid typeGuid = new Guid(typeGuidBytes);

            // 空分区检查
            if (typeGuid == Guid.Empty)
                return null;

            // Unique GUID (16 bytes)
            byte[] uniqueGuidBytes = new byte[16];
            Array.Copy(data, offset + 16, uniqueGuidBytes, 0, 16);
            Guid uniqueGuid = new Guid(uniqueGuidBytes);

            // Starting LBA (8 bytes)
            ulong startLba = BitConverter.ToUInt64(data, offset + 32);

            // Ending LBA (8 bytes)
            ulong endLba = BitConverter.ToUInt64(data, offset + 40);

            // Attributes (8 bytes)
            ulong attributes = BitConverter.ToUInt64(data, offset + 48);

            // Partition Name (72 bytes, UTF-16LE)
            string name = Encoding.Unicode.GetString(data, offset + 56, 72).TrimEnd('\0');

            return new MtkPartition
            {
                Name = name,
                StartSector = startLba,
                SectorCount = endLba - startLba + 1,
                SectorSize = sectorSize,
                TypeGuid = typeGuid,
                UniqueGuid = uniqueGuid,
                Attributes = attributes
            };
        }

        /// <summary>
        /// 解析原始分区表数据 (PMT 格式)
        /// </summary>
        public static List<MtkPartition> ParsePmt(byte[] pmtData, uint sectorSize = 512)
        {
            var partitions = new List<MtkPartition>();

            if (pmtData == null || pmtData.Length < 16)
                return partitions;

            // PMT 签名检查
            string sig = Encoding.ASCII.GetString(pmtData, 0, 4);
            if (sig != "PT" && sig != " PT ")
            {
                // 尝试 GPT 解析
                return Parse(pmtData, sectorSize);
            }

            // PMT 分区条目 (每个 64-128 字节)
            int entrySize = 64;
            int offset = 4;

            // 检查版本
            if (pmtData.Length > 8)
            {
                uint version = BitConverter.ToUInt32(pmtData, 4);
                if (version >= 0x100)
                {
                    entrySize = 128;
                    offset = 8;
                }
            }

            while (offset + entrySize <= pmtData.Length)
            {
                // 分区名 (32 bytes)
                string name = Encoding.ASCII.GetString(pmtData, offset, 32).TrimEnd('\0');
                if (string.IsNullOrEmpty(name))
                    break;

                // 起始地址 (8 bytes)
                ulong startAddr = BitConverter.ToUInt64(pmtData, offset + 32);

                // 大小 (8 bytes)
                ulong size = BitConverter.ToUInt64(pmtData, offset + 40);

                partitions.Add(new MtkPartition
                {
                    Name = name,
                    StartSector = startAddr / sectorSize,
                    SectorCount = size / sectorSize,
                    SectorSize = sectorSize
                });

                offset += entrySize;
            }

            return partitions;
        }

        /// <summary>
        /// 已知的 GPT 分区类型 GUID
        /// </summary>
        public static class PartitionTypes
        {
            public static readonly Guid EfiSystem = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
            public static readonly Guid BasicData = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
            public static readonly Guid LinuxFilesystem = new("0FC63DAF-8483-4772-8E79-3D69D8477DE4");
            public static readonly Guid AndroidMeta = new("19A710A2-B3CA-11E4-B026-10604B889DCF");
            public static readonly Guid AndroidBoot = new("49A4D17F-93A3-45C1-A0DE-F50B2EBE2599");
            public static readonly Guid AndroidRecovery = new("4177C722-9E92-4AAB-8644-43502BFD5506");
            public static readonly Guid AndroidSystem = new("38F428E6-D326-425D-9140-6E0EA133647C");
            public static readonly Guid AndroidCache = new("A893EF21-E428-470A-9E55-0668FD91A2D9");
            public static readonly Guid AndroidData = new("DC76DDA9-5AC1-491C-AF42-A82591580C0D");
        }
    }
}
