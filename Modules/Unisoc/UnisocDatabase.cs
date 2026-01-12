// ============================================================================
// MultiFlash TOOL - Unisoc/Spreadtrum Chipset Database
// 展讯芯片数据库 | Unisocチップセットデータベース | Unisoc 칩셋 데이터베이스
// ============================================================================
// [EN] Database of Unisoc/Spreadtrum chipset information
//      Contains chip IDs, names, and hardware specifications
// [中文] 展讯芯片信息数据库
//       包含芯片 ID、名称和硬件规格
// [日本語] Unisoc/Spreadtrumチップセット情報データベース
//         チップID、名前、ハードウェア仕様を含む
// [한국어] Unisoc/Spreadtrum 칩셋 정보 데이터베이스
//         칩 ID, 이름, 하드웨어 사양 포함
// [Español] Base de datos de información de chipsets Unisoc/Spreadtrum
//           Contiene IDs de chips, nombres y especificaciones de hardware
// [Русский] База данных информации о чипсетах Unisoc/Spreadtrum
//           Содержит ID чипов, названия и характеристики оборудования
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;

namespace tools.Modules.Unisoc
{
    /// <summary>
    /// Unisoc (Spreadtrum) Chipset Database
    /// 展讯芯片数据库 | Unisocチップセットデータベース | Unisoc 칩셋 데이터베이스
    /// </summary>
    public static class UnisocDatabase
    {
        /// <summary>
        /// Chip Information / 芯片信息 / チップ情報 / 칩 정보
        /// </summary>
        public class ChipInfo
        {
            public string Name { get; set; } = "";
            public string Series { get; set; } = "";
            public string Description { get; set; } = "";
            public string Fdl1Address { get; set; } = "0x5000";
            public string Fdl2Address { get; set; } = "0x9efffe00";
            public bool SupportsExploit { get; set; }
        }

        /// <summary>
        /// 芯片数据库
        /// </summary>
        private static readonly Dictionary<string, ChipInfo> Chips = new(StringComparer.OrdinalIgnoreCase)
        {
            // SC 系列 (Legacy)
            ["SC7731"] = new ChipInfo
            {
                Name = "SC7731",
                Series = "SC",
                Description = "Spreadtrum SC7731 四核 Cortex-A7",
                Fdl1Address = "0x5000",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["SC7731E"] = new ChipInfo
            {
                Name = "SC7731E",
                Series = "SC",
                Description = "Spreadtrum SC7731E 四核 Cortex-A7",
                Fdl1Address = "0x5000",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["SC9832"] = new ChipInfo
            {
                Name = "SC9832",
                Series = "SC",
                Description = "Spreadtrum SC9832 四核 Cortex-A7",
                Fdl1Address = "0x5000",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["SC9832E"] = new ChipInfo
            {
                Name = "SC9832E",
                Series = "SC",
                Description = "Spreadtrum SC9832E 四核 Cortex-A53",
                Fdl1Address = "0x5000",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["SC9863A"] = new ChipInfo
            {
                Name = "SC9863A",
                Series = "SC",
                Description = "Unisoc SC9863A 八核 Cortex-A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },

            // T 系列 (Modern)
            ["T310"] = new ChipInfo
            {
                Name = "T310",
                Series = "Tiger",
                Description = "Unisoc Tiger T310 四核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T606"] = new ChipInfo
            {
                Name = "T606",
                Series = "Tiger",
                Description = "Unisoc Tiger T606 八核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T610"] = new ChipInfo
            {
                Name = "T610",
                Series = "Tiger",
                Description = "Unisoc Tiger T610 八核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T612"] = new ChipInfo
            {
                Name = "T612",
                Series = "Tiger",
                Description = "Unisoc Tiger T612 八核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T616"] = new ChipInfo
            {
                Name = "T616",
                Series = "Tiger",
                Description = "Unisoc Tiger T616 八核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T618"] = new ChipInfo
            {
                Name = "T618",
                Series = "Tiger",
                Description = "Unisoc Tiger T618 八核 Cortex-A75/A55",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T700"] = new ChipInfo
            {
                Name = "T700",
                Series = "Tiger",
                Description = "Unisoc Tiger T700 八核 5G",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = true
            },
            ["T760"] = new ChipInfo
            {
                Name = "T760",
                Series = "Tiger",
                Description = "Unisoc Tiger T760 八核 5G",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = false
            },
            ["T770"] = new ChipInfo
            {
                Name = "T770",
                Series = "Tiger",
                Description = "Unisoc Tiger T770 八核 5G",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = false
            },
            ["T820"] = new ChipInfo
            {
                Name = "T820",
                Series = "Tiger",
                Description = "Unisoc Tiger T820 八核 5G",
                Fdl1Address = "0x65000800",
                Fdl2Address = "0x9efffe00",
                SupportsExploit = false
            },
        };

        /// <summary>
        /// USB VID/PID 映射
        /// </summary>
        private static readonly Dictionary<(string VID, string PID), string> UsbDevices = new()
        {
            { ("1782", "4D00"), "Spreadtrum Download" },
            { ("1782", "4D01"), "Spreadtrum Diag" },
            { ("1782", "4D02"), "Spreadtrum ADB" },
            { ("1782", "4D10"), "Unisoc Download" },
            { ("1782", "4D11"), "Unisoc Diag" },
            { ("1782", "4D12"), "Unisoc ADB" },
        };

        /// <summary>
        /// 获取芯片信息
        /// </summary>
        public static ChipInfo? GetChipInfo(string chipName)
        {
            if (string.IsNullOrEmpty(chipName))
                return null;

            if (Chips.TryGetValue(chipName, out var info))
                return info;

            return null;
        }

        /// <summary>
        /// 根据 FDL1 地址获取芯片系列
        /// </summary>
        public static string GetSeriesByFdl1Address(string fdl1Address)
        {
            if (string.IsNullOrEmpty(fdl1Address))
                return "Unknown";

            fdl1Address = fdl1Address.Trim().ToLowerInvariant();

            if (fdl1Address == "0x5000" || fdl1Address == "0x00005000")
                return "SC (Legacy)";

            if (fdl1Address.StartsWith("0x65"))
                return "Tiger (Modern)";

            return "Unknown";
        }

        /// <summary>
        /// 获取所有支持的芯片
        /// </summary>
        public static IEnumerable<ChipInfo> GetAllChips()
        {
            return Chips.Values;
        }

        /// <summary>
        /// 识别 USB 设备
        /// </summary>
        public static string IdentifyUsbDevice(string vid, string pid)
        {
            if (UsbDevices.TryGetValue((vid.ToUpper(), pid.ToUpper()), out var name))
                return name;

            if (vid.ToUpper() == "1782")
                return "Spreadtrum/Unisoc Device";

            return "Unknown Device";
        }

        /// <summary>
        /// 是否是展讯/紫光展锐设备
        /// </summary>
        public static bool IsUnisocDevice(string vid)
        {
            return vid?.ToUpper() == "1782";
        }
    }
}
