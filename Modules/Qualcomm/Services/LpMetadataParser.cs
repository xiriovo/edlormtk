using System;
using System.Collections.Generic;
using System.Text;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// LP (Logical Partition) Metadata 解析器
    /// 用于解析 super 分区的动态分区表
    /// </summary>
    public static class LpMetadataParser
    {
        // LP Metadata Magic Numbers
        private const uint LP_GEOMETRY_MAGIC = 0x616C4467;   // "gDla"
        private const uint LP_HEADER_MAGIC = 0x414C5030;     // "0PLA"

        // Extent types
        private const uint LP_TARGET_TYPE_LINEAR = 0;
        private const uint LP_TARGET_TYPE_ZERO = 1;

        /// <summary>
        /// LP 几何信息
        /// </summary>
        public class LpGeometry
        {
            public uint Magic { get; set; }
            public uint StructSize { get; set; }
            public uint MetadataMaxSize { get; set; }
            public uint MetadataSlotCount { get; set; }
            public uint LogicalBlockSize { get; set; }
        }

        /// <summary>
        /// LP 头部信息
        /// </summary>
        public class LpHeader
        {
            public uint Magic { get; set; }
            public ushort MajorVersion { get; set; }
            public ushort MinorVersion { get; set; }
            public uint HeaderSize { get; set; }
            public uint TablesSize { get; set; }
            public uint PartitionsOffset { get; set; }
            public uint PartitionsNum { get; set; }
            public uint PartitionsEntrySize { get; set; }
            public uint ExtentsOffset { get; set; }
            public uint ExtentsNum { get; set; }
            public uint ExtentsEntrySize { get; set; }
            public uint GroupsOffset { get; set; }
            public uint GroupsNum { get; set; }
            public uint GroupsEntrySize { get; set; }
        }

        /// <summary>
        /// LP 分区条目
        /// </summary>
        public class LpPartition
        {
            public string Name { get; set; } = "";
            public uint Attributes { get; set; }
            public uint FirstExtentIndex { get; set; }
            public uint NumExtents { get; set; }
            public uint GroupIndex { get; set; }
        }

        /// <summary>
        /// LP Extent 条目
        /// </summary>
        public class LpExtent
        {
            public ulong NumSectors { get; set; }      // 512字节扇区数
            public uint TargetType { get; set; }       // 0=LINEAR, 1=ZERO
            public ulong TargetData { get; set; }      // 物理扇区偏移
            public uint TargetSource { get; set; }     // 块设备索引
        }

        /// <summary>
        /// Super 分区内的子分区位置
        /// </summary>
        public class SubPartitionLocation
        {
            public string Name { get; set; } = "";
            public long OffsetIn512Sectors { get; set; }  // super内偏移(512B扇区)
            public long OffsetInBytes { get; set; }       // super内偏移(字节)
            public long SizeIn512Sectors { get; set; }    // 大小(512B扇区)
            public long SizeInBytes { get; set; }         // 大小(字节)
            public long DeviceSector4096 { get; set; }    // 设备扇区(4096B)
        }

        /// <summary>
        /// 解析结果
        /// </summary>
        public class LpMetadata
        {
            public bool IsValid { get; set; }
            public LpGeometry? Geometry { get; set; }
            public LpHeader? Header { get; set; }
            public List<LpPartition> Partitions { get; set; } = new();
            public List<LpExtent> Extents { get; set; } = new();
            public List<SubPartitionLocation> SubPartitionLocations { get; set; } = new();
        }

        /// <summary>
        /// 解析 LP Metadata
        /// </summary>
        /// <param name="data">super 分区前 8KB 数据</param>
        /// <param name="superStartSector">super 分区起始扇区(4096B)</param>
        /// <returns>解析结果</returns>
        public static LpMetadata Parse(byte[] data, long superStartSector)
        {
            var result = new LpMetadata();

            if (data.Length < 8192)
            {
                return result;
            }

            // 1. 解析 Geometry (偏移 0)
            result.Geometry = ParseGeometry(data, 0);
            if (result.Geometry == null)
            {
                // 尝试偏移 4096 (backup)
                result.Geometry = ParseGeometry(data, 4096);
            }

            if (result.Geometry == null)
            {
                return result;
            }

            // 2. 解析 Header (偏移 0x1000 = 4096)
            result.Header = ParseHeader(data, 0x1000);
            if (result.Header == null)
            {
                return result;
            }

            // 3. 解析 Partitions 和 Extents
            int tablesBase = 0x1000 + (int)result.Header.HeaderSize;
            
            result.Partitions = ParsePartitions(data, tablesBase, result.Header);
            result.Extents = ParseExtents(data, tablesBase, result.Header);

            // 4. 计算子分区位置
            result.SubPartitionLocations = CalculateSubPartitionLocations(
                result.Partitions, result.Extents, superStartSector);

            result.IsValid = true;
            return result;
        }

        private static LpGeometry? ParseGeometry(byte[] data, int offset)
        {
            if (offset + 52 > data.Length)
                return null;

            uint magic = BitConverter.ToUInt32(data, offset);
            if (magic != LP_GEOMETRY_MAGIC)
                return null;

            return new LpGeometry
            {
                Magic = magic,
                StructSize = BitConverter.ToUInt32(data, offset + 4),
                MetadataMaxSize = BitConverter.ToUInt32(data, offset + 40),
                MetadataSlotCount = BitConverter.ToUInt32(data, offset + 44),
                LogicalBlockSize = BitConverter.ToUInt32(data, offset + 48),
            };
        }

        private static LpHeader? ParseHeader(byte[] data, int offset)
        {
            if (offset + 120 > data.Length)
                return null;

            uint magic = BitConverter.ToUInt32(data, offset);
            if (magic != LP_HEADER_MAGIC)
                return null;

            return new LpHeader
            {
                Magic = magic,
                MajorVersion = BitConverter.ToUInt16(data, offset + 4),
                MinorVersion = BitConverter.ToUInt16(data, offset + 6),
                HeaderSize = BitConverter.ToUInt32(data, offset + 8),
                TablesSize = BitConverter.ToUInt32(data, offset + 44),
                PartitionsOffset = BitConverter.ToUInt32(data, offset + 80),
                PartitionsNum = BitConverter.ToUInt32(data, offset + 84),
                PartitionsEntrySize = BitConverter.ToUInt32(data, offset + 88),
                ExtentsOffset = BitConverter.ToUInt32(data, offset + 92),
                ExtentsNum = BitConverter.ToUInt32(data, offset + 96),
                ExtentsEntrySize = BitConverter.ToUInt32(data, offset + 100),
                GroupsOffset = BitConverter.ToUInt32(data, offset + 104),
                GroupsNum = BitConverter.ToUInt32(data, offset + 108),
                GroupsEntrySize = BitConverter.ToUInt32(data, offset + 112),
            };
        }

        private static List<LpPartition> ParsePartitions(byte[] data, int tablesBase, LpHeader header)
        {
            var partitions = new List<LpPartition>();
            int offset = tablesBase + (int)header.PartitionsOffset;
            int entrySize = (int)header.PartitionsEntrySize;

            for (int i = 0; i < header.PartitionsNum; i++)
            {
                int entryOffset = offset + i * entrySize;
                if (entryOffset + 52 > data.Length)
                    break;

                // 名称是 36 字节
                string name = Encoding.ASCII.GetString(data, entryOffset, 36).TrimEnd('\0');
                if (string.IsNullOrEmpty(name))
                    continue;

                partitions.Add(new LpPartition
                {
                    Name = name,
                    Attributes = BitConverter.ToUInt32(data, entryOffset + 36),
                    FirstExtentIndex = BitConverter.ToUInt32(data, entryOffset + 40),
                    NumExtents = BitConverter.ToUInt32(data, entryOffset + 44),
                    GroupIndex = BitConverter.ToUInt32(data, entryOffset + 48),
                });
            }

            return partitions;
        }

        private static List<LpExtent> ParseExtents(byte[] data, int tablesBase, LpHeader header)
        {
            var extents = new List<LpExtent>();
            int offset = tablesBase + (int)header.ExtentsOffset;
            int entrySize = (int)header.ExtentsEntrySize;

            for (int i = 0; i < header.ExtentsNum; i++)
            {
                int entryOffset = offset + i * entrySize;
                if (entryOffset + 24 > data.Length)
                    break;

                extents.Add(new LpExtent
                {
                    NumSectors = BitConverter.ToUInt64(data, entryOffset),
                    TargetType = BitConverter.ToUInt32(data, entryOffset + 8),
                    TargetData = BitConverter.ToUInt64(data, entryOffset + 12),
                    TargetSource = BitConverter.ToUInt32(data, entryOffset + 20),
                });
            }

            return extents;
        }

        private static List<SubPartitionLocation> CalculateSubPartitionLocations(
            List<LpPartition> partitions, List<LpExtent> extents, long superStartSector)
        {
            var locations = new List<SubPartitionLocation>();

            foreach (var part in partitions)
            {
                if (part.NumExtents == 0)
                    continue;

                long totalSectors = 0;
                long firstOffset = -1;

                for (int i = 0; i < part.NumExtents; i++)
                {
                    int extIdx = (int)part.FirstExtentIndex + i;
                    if (extIdx >= extents.Count)
                        break;

                    var ext = extents[extIdx];
                    totalSectors += (long)ext.NumSectors;

                    if (i == 0 && ext.TargetType == LP_TARGET_TYPE_LINEAR)
                    {
                        firstOffset = (long)ext.TargetData;
                    }
                }

                if (firstOffset >= 0)
                {
                    long offsetInBytes = firstOffset * 512;
                    long sizeInBytes = totalSectors * 512;
                    // super 使用 4096 字节扇区，LP metadata 使用 512 字节扇区
                    long deviceSector = superStartSector + (firstOffset * 512 / 4096);

                    locations.Add(new SubPartitionLocation
                    {
                        Name = part.Name,
                        OffsetIn512Sectors = firstOffset,
                        OffsetInBytes = offsetInBytes,
                        SizeIn512Sectors = totalSectors,
                        SizeInBytes = sizeInBytes,
                        DeviceSector4096 = deviceSector,
                    });
                }
            }

            return locations;
        }

        /// <summary>
        /// 获取指定子分区的位置
        /// </summary>
        public static SubPartitionLocation? GetSubPartition(LpMetadata metadata, string name)
        {
            return metadata.SubPartitionLocations.Find(p => 
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取包含设备信息的子分区列表
        /// </summary>
        public static List<SubPartitionLocation> GetDeviceInfoPartitions(LpMetadata metadata)
        {
            var result = new List<SubPartitionLocation>();
            
            // 按优先级添加 (小分区优先)
            var priorities = new[] { "my_manifest_a", "my_region_a", "odm_a", "vendor_a" };
            
            foreach (var name in priorities)
            {
                var loc = GetSubPartition(metadata, name);
                if (loc != null)
                {
                    result.Add(loc);
                }
            }

            return result;
        }
    }
}
