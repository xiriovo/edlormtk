using System;

namespace tools.Modules.MTK.Models
{
    /// <summary>
    /// eMMC 存储信息
    /// </summary>
    public class EmmcInfo
    {
        public byte Type { get; set; }
        public uint BlockSize { get; set; }
        public ulong Boot1Size { get; set; }
        public ulong Boot2Size { get; set; }
        public ulong RpmbSize { get; set; }
        public ulong UserSize { get; set; }
        public ulong Gp1Size { get; set; }
        public ulong Gp2Size { get; set; }
        public ulong Gp3Size { get; set; }
        public ulong Gp4Size { get; set; }
        public byte[] Cid { get; set; } = new byte[16];
        public string? FwVersion { get; set; }

        public string GetManufacturer()
        {
            if (Cid == null || Cid.Length < 1) return "Unknown";
            return Cid[0] switch
            {
                0x15 => "Samsung",
                0x45 => "SanDisk",
                0x90 => "SK Hynix",
                0xFE => "Micron",
                0x13 => "Micron",
                0x11 => "Toshiba",
                _ => $"Unknown (0x{Cid[0]:X2})"
            };
        }

        public override string ToString()
        {
            return $"eMMC: {GetManufacturer()}, User: {UserSize / 1024 / 1024 / 1024}GB";
        }
    }

    /// <summary>
    /// UFS 存储信息
    /// </summary>
    public class UfsInfo
    {
        public byte Type { get; set; }
        public uint BlockSize { get; set; }
        public ulong Lu0Size { get; set; }
        public ulong Lu1Size { get; set; }
        public ulong Lu2Size { get; set; }
        public byte LuCount { get; set; }
        public byte[] Cid { get; set; } = new byte[16];
        public string? FwVersion { get; set; }
        public ushort SpecVersion { get; set; }

        public string GetManufacturer()
        {
            if (Cid == null || Cid.Length < 1) return "Unknown";
            return Cid[0] switch
            {
                0xCE => "Samsung",
                0x45 => "SanDisk",
                0xAD => "SK Hynix",
                0x2C => "Micron",
                0x98 => "Toshiba/Kioxia",
                _ => $"Unknown (0x{Cid[0]:X2})"
            };
        }

        public override string ToString()
        {
            return $"UFS {SpecVersion / 256}.{SpecVersion % 256}: {GetManufacturer()}, LU0: {Lu0Size / 1024 / 1024 / 1024}GB";
        }
    }

    /// <summary>
    /// NAND 存储信息
    /// </summary>
    public class NandInfo
    {
        public byte Type { get; set; }
        public uint PageSize { get; set; }
        public uint BlockSize { get; set; }
        public uint SpareSize { get; set; }
        public ulong TotalSize { get; set; }
        public byte[] Id { get; set; } = new byte[8];
        public string? DeviceName { get; set; }

        public override string ToString()
        {
            return $"NAND: {DeviceName ?? "Unknown"}, Size: {TotalSize / 1024 / 1024}MB";
        }
    }

    /// <summary>
    /// NOR 存储信息
    /// </summary>
    public class NorInfo
    {
        public byte Type { get; set; }
        public uint PageSize { get; set; }
        public ulong TotalSize { get; set; }
        public byte[] Id { get; set; } = new byte[8];
        public string? DeviceName { get; set; }

        public override string ToString()
        {
            return $"NOR: {DeviceName ?? "Unknown"}, Size: {TotalSize / 1024 / 1024}MB";
        }
    }

    /// <summary>
    /// RAM 信息
    /// </summary>
    public class RamInfo
    {
        public RamType Type { get; set; }
        public uint BaseAddress { get; set; }
        public ulong Size { get; set; }

        public override string ToString()
        {
            return $"{Type}: Base=0x{BaseAddress:X}, Size={Size / 1024 / 1024}MB";
        }
    }

    /// <summary>
    /// RAM 类型
    /// </summary>
    public enum RamType
    {
        Unknown,
        Sram,
        Dram
    }

    /// <summary>
    /// MTK 分区信息
    /// </summary>
    public class MtkPartition
    {
        public string Name { get; set; } = "";
        public ulong StartSector { get; set; }
        public ulong SectorCount { get; set; }
        public uint SectorSize { get; set; } = 512;
        public byte PartitionType { get; set; }
        public Guid TypeGuid { get; set; }
        public Guid UniqueGuid { get; set; }
        public ulong Attributes { get; set; }

        // 计算属性
        public ulong StartOffset => StartSector * SectorSize;
        public ulong Size => SectorCount * SectorSize;
        public string SizeFormatted => FormatSize(Size);

        // UI 绑定
        public bool IsSelected { get; set; }
        public string? CustomFilePath { get; set; }
        public bool IsSparse { get; set; }

        private static string FormatSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            double size = bytes;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }

        public override string ToString()
        {
            return $"{Name}: Start=0x{StartOffset:X}, Size={SizeFormatted}";
        }
    }
}
