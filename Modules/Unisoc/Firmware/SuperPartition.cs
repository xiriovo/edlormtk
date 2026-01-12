using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Unisoc.Firmware
{
    /// <summary>
    /// Super 分区处理器
    /// 用于处理 Android 动态分区 (super.img)
    /// </summary>
    public class SuperPartition : IDisposable
    {
        #region 结构定义

        // LP Metadata 魔数
        private const uint LP_METADATA_MAGIC = 0x414C5030; // "0PLA"
        private const uint LP_METADATA_HEADER_MAGIC = 0x20000; 

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LpMetadataGeometry
        {
            public uint magic;                    // LP_METADATA_GEOMETRY_MAGIC
            public uint struct_size;              // sizeof(LpMetadataGeometry)
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] checksum;
            public uint metadata_max_size;
            public uint metadata_slot_count;
            public uint logical_block_size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LpMetadataHeader
        {
            public uint magic;
            public ushort major_version;
            public ushort minor_version;
            public uint header_size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] header_checksum;
            public uint tables_size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] tables_checksum;
            public LpMetadataTableDescriptor partitions;
            public LpMetadataTableDescriptor extents;
            public LpMetadataTableDescriptor groups;
            public LpMetadataTableDescriptor block_devices;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LpMetadataTableDescriptor
        {
            public uint offset;
            public uint num_entries;
            public uint entry_size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LpMetadataPartition
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
            public byte[] name;
            public uint attributes;
            public uint first_extent_index;
            public uint num_extents;
            public uint group_index;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct LpMetadataExtent
        {
            public ulong num_sectors;
            public ulong target_type;
            public ulong target_data;
            public ulong target_source;
        }

        #endregion

        private bool _disposed;

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// 进度事件
        /// </summary>
        public event Action<long, long>? OnProgress;

        /// <summary>
        /// 子分区列表
        /// </summary>
        public List<SubPartition> SubPartitions { get; } = new();

        /// <summary>
        /// 是否是 Super 分区
        /// </summary>
        public static bool IsSuperPartition(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(filePath));
                
                // 检查是否是 sparse 格式
                uint magic = reader.ReadUInt32();
                if (magic == 0xED26FF3A)
                {
                    // Sparse 格式，需要先解压
                    return false; // 返回 false，让调用者先解压
                }

                // 跳到 LP Metadata 位置 (通常在 4096 或 8192 偏移)
                reader.BaseStream.Seek(4096, SeekOrigin.Begin);
                magic = reader.ReadUInt32();
                
                if (magic == LP_METADATA_MAGIC || magic == LP_METADATA_HEADER_MAGIC)
                    return true;

                reader.BaseStream.Seek(8192, SeekOrigin.Begin);
                magic = reader.ReadUInt32();
                
                return magic == LP_METADATA_MAGIC || magic == LP_METADATA_HEADER_MAGIC;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析 Super 分区
        /// </summary>
        public bool Parse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                OnLog?.Invoke($"文件不存在: {filePath}");
                return false;
            }

            SubPartitions.Clear();

            try
            {
                using var reader = new BinaryReader(File.OpenRead(filePath));

                // 查找 LP Metadata
                long metadataOffset = FindMetadataOffset(reader);
                if (metadataOffset < 0)
                {
                    OnLog?.Invoke("未找到 LP Metadata");
                    return false;
                }

                OnLog?.Invoke($"LP Metadata 偏移: 0x{metadataOffset:X}");
                reader.BaseStream.Seek(metadataOffset, SeekOrigin.Begin);

                // 读取 Geometry
                var geoBytes = reader.ReadBytes(Marshal.SizeOf<LpMetadataGeometry>());
                var geometry = BytesToStruct<LpMetadataGeometry>(geoBytes);

                OnLog?.Invoke($"Metadata 大小: {geometry.metadata_max_size}");
                OnLog?.Invoke($"逻辑块大小: {geometry.logical_block_size}");

                // 读取 Header
                var headerBytes = reader.ReadBytes(Marshal.SizeOf<LpMetadataHeader>());
                var header = BytesToStruct<LpMetadataHeader>(headerBytes);

                OnLog?.Invoke($"版本: {header.major_version}.{header.minor_version}");
                OnLog?.Invoke($"分区数: {header.partitions.num_entries}");

                // 读取分区表
                var partitionsOffset = metadataOffset + header.header_size + header.partitions.offset;
                reader.BaseStream.Seek(partitionsOffset, SeekOrigin.Begin);

                for (int i = 0; i < header.partitions.num_entries; i++)
                {
                    var partBytes = reader.ReadBytes((int)header.partitions.entry_size);
                    var part = BytesToStruct<LpMetadataPartition>(partBytes);

                    string name = Encoding.ASCII.GetString(part.name).TrimEnd('\0');
                    if (string.IsNullOrEmpty(name)) continue;

                    // 读取 extents 获取大小
                    long size = 0;
                    long currentPos = reader.BaseStream.Position;

                    var extentsOffset = metadataOffset + header.header_size + header.extents.offset;
                    reader.BaseStream.Seek(extentsOffset + part.first_extent_index * header.extents.entry_size, SeekOrigin.Begin);

                    for (int j = 0; j < part.num_extents; j++)
                    {
                        var extBytes = reader.ReadBytes((int)header.extents.entry_size);
                        var ext = BytesToStruct<LpMetadataExtent>(extBytes);
                        size += (long)ext.num_sectors * 512;
                    }

                    reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);

                    SubPartitions.Add(new SubPartition
                    {
                        Name = name,
                        Size = size,
                        Attributes = part.attributes,
                        GroupIndex = part.group_index
                    });
                }

                OnLog?.Invoke($"解析完成: {SubPartitions.Count} 个子分区");
                foreach (var sp in SubPartitions)
                {
                    OnLog?.Invoke($"  - {sp.Name}: {sp.SizeStr}");
                }

                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"解析失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 提取子分区
        /// </summary>
        public async Task<bool> ExtractSubPartitionAsync(string superPath, string partitionName, 
            string outputPath, CancellationToken ct = default)
        {
            var partition = SubPartitions.FirstOrDefault(p => 
                p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

            if (partition == null)
            {
                OnLog?.Invoke($"未找到子分区: {partitionName}");
                return false;
            }

            OnLog?.Invoke($"提取子分区: {partitionName} ({partition.SizeStr})");

            // 这里需要实际实现从 super.img 中提取子分区
            // 暂时使用 lpunpack 工具的逻辑
            OnLog?.Invoke("⚠️ 提取功能需要 lpunpack 支持");
            
            return await Task.FromResult(false);
        }

        /// <summary>
        /// 打包 Super 分区
        /// </summary>
        public async Task<bool> PackSuperPartitionAsync(string outputPath, 
            Dictionary<string, string> partitionFiles, long superSize = 0, CancellationToken ct = default)
        {
            OnLog?.Invoke($"打包 Super 分区: {partitionFiles.Count} 个子分区");

            // 这里需要实际实现打包逻辑
            // 暂时使用 lpmake 工具的逻辑
            OnLog?.Invoke("⚠️ 打包功能需要 lpmake 支持");
            
            return await Task.FromResult(false);
        }

        private long FindMetadataOffset(BinaryReader reader)
        {
            // 常见的 metadata 偏移位置
            long[] offsets = { 4096, 8192, 0x1000, 0x2000 };

            foreach (var offset in offsets)
            {
                if (reader.BaseStream.Length < offset + 4) continue;

                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                uint magic = reader.ReadUInt32();

                if (magic == LP_METADATA_MAGIC || magic == LP_METADATA_HEADER_MAGIC ||
                    magic == 0x0 || magic == 0x20) // 某些格式的前导
                {
                    // 验证是否有效
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                    return offset;
                }
            }

            return -1;
        }

        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SubPartitions.Clear();
        }
    }

    /// <summary>
    /// 子分区信息
    /// </summary>
    public class SubPartition
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public uint Attributes { get; set; }
        public uint GroupIndex { get; set; }
        
        public string SizeStr => FormatSize(Size);
        
        public bool IsReadOnly => (Attributes & 1) != 0;
        public bool IsSlotted => Name.EndsWith("_a") || Name.EndsWith("_b");

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
}
