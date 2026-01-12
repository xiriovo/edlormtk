using System;

namespace tools.Modules.Unisoc.Models
{
    /// <summary>
    /// Unisoc (展讯) 设备信息
    /// </summary>
    public class UnisocDeviceInfo
    {
        /// <summary>
        /// 端口号 (如 COM3)
        /// </summary>
        public string Port { get; set; } = "---";

        /// <summary>
        /// 芯片型号 (如 SC9863A, T618)
        /// </summary>
        public string ChipName { get; set; } = "---";

        /// <summary>
        /// Boot 版本
        /// </summary>
        public string BootVersion { get; set; } = "---";

        /// <summary>
        /// 协议版本 (SPRD/SPRD2)
        /// </summary>
        public string Protocol { get; set; } = "---";

        /// <summary>
        /// 设备模式 (Download/Diag/Normal)
        /// </summary>
        public string Mode { get; set; } = "---";

        /// <summary>
        /// USB Vendor ID
        /// </summary>
        public string VendorId { get; set; } = "";

        /// <summary>
        /// USB Product ID
        /// </summary>
        public string ProductId { get; set; } = "";

        /// <summary>
        /// 是否已加载 FDL
        /// </summary>
        public bool FdlLoaded { get; set; }

        /// <summary>
        /// FDL1 地址
        /// </summary>
        public string Fdl1Address { get; set; } = "";

        /// <summary>
        /// FDL2 地址
        /// </summary>
        public string Fdl2Address { get; set; } = "";

        /// <summary>
        /// 是否支持 RSA 漏洞利用
        /// </summary>
        public bool SupportsExploit { get; set; }

        /// <summary>
        /// Exploit 地址
        /// </summary>
        public string ExploitAddress { get; set; } = "";
    }

    /// <summary>
    /// Unisoc 分区信息
    /// </summary>
    public class UnisocPartitionInfo
    {
        /// <summary>
        /// 分区ID
        /// </summary>
        public string FileId { get; set; } = "";

        /// <summary>
        /// 分区名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 扇区偏移
        /// </summary>
        public long Sector { get; set; }

        /// <summary>
        /// 扇区数量
        /// </summary>
        public long Length { get; set; }

        /// <summary>
        /// 分区大小 (字节)
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 文件路径
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 格式化大小显示
        /// </summary>
        public string SizeStr => FormatSize(Size);

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
    /// PAC 固件信息
    /// </summary>
    public class PacFirmwareInfo
    {
        /// <summary>
        /// PAC 版本 (BP_R1.0.0 / BP_R2.0.1)
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName { get; set; } = "";

        /// <summary>
        /// 固件名称
        /// </summary>
        public string FirmwareName { get; set; } = "";

        /// <summary>
        /// 固件大小
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 分区数量
        /// </summary>
        public int PartitionCount { get; set; }

        /// <summary>
        /// 分区列表起始位置
        /// </summary>
        public int PartitionsListStart { get; set; }

        /// <summary>
        /// Flash 类型
        /// </summary>
        public int FlashType { get; set; }

        /// <summary>
        /// 是否包含 super 分区
        /// </summary>
        public bool ContainsSuper { get; set; }
    }
}
