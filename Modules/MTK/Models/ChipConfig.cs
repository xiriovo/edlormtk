using System;
using System.Collections.Generic;
using tools.Modules.MTK.Protocol;

namespace tools.Modules.MTK.Models
{
    /// <summary>
    /// 芯片配置
    /// </summary>
    public class ChipConfig
    {
        public ushort HwCode { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public uint Watchdog { get; set; }
        public uint Uart { get; set; }
        public uint BromPayloadAddr { get; set; }
        public uint DaPayloadAddr { get; set; }
        public uint? CqdmaBase { get; set; }
        public uint? GcpuBase { get; set; }
        public uint? SejBase { get; set; }
        public uint? DxccBase { get; set; }
        public uint? EfuseBase { get; set; }
        public uint? MeidAddr { get; set; }
        public uint? SocIdAddr { get; set; }
        public uint? ProvAddr { get; set; }
        public uint? ApDmaMem { get; set; }
        public uint Var1 { get; set; } = 0xA;
        public DAMode DaMode { get; set; } = DAMode.Legacy;
        public List<uint>? Blacklist { get; set; }

        // ===== 漏洞利用相关配置 =====

        /// <summary>
        /// BROM Register Access 配置 (用于 Kamakiri2)
        /// 格式: [(index, address), ...]
        /// </summary>
        public List<(int Index, uint Address)>? BromRegisterAccess { get; set; }

        /// <summary>
        /// Send Ptr 配置 (用于 Kamakiri2)
        /// 格式: [(index, address), ...]
        /// </summary>
        public List<(int Index, uint Address)>? SendPtr { get; set; }

        /// <summary>
        /// Secure Boot 漏洞地址
        /// </summary>
        public uint? SbcVulnAddr { get; set; }

        /// <summary>
        /// 是否支持 Kamakiri 漏洞
        /// </summary>
        public bool SupportsKamakiri => Var1 != 0;

        /// <summary>
        /// 是否支持 Kamakiri2 漏洞
        /// </summary>
        public bool SupportsKamakiri2 => BromRegisterAccess != null && BromRegisterAccess.Count > 0 &&
                                         SendPtr != null && SendPtr.Count > 0;

        /// <summary>
        /// 是否支持 GCPU (Amonet) 漏洞
        /// </summary>
        public bool SupportsGcpu => GcpuBase.HasValue && GcpuBase.Value != 0;

        /// <summary>
        /// 是否支持 CQDMA (Hashimoto) 漏洞
        /// </summary>
        public bool SupportsCqdma => CqdmaBase.HasValue && CqdmaBase.Value != 0 &&
                                     ApDmaMem.HasValue && ApDmaMem.Value != 0;

        /// <summary>
        /// 获取推荐的漏洞利用类型
        /// </summary>
        public string GetRecommendedExploit()
        {
            if (SupportsKamakiri2) return "Kamakiri2";
            if (SupportsKamakiri) return "Kamakiri";
            if (SupportsCqdma) return "Hashimoto (CQDMA)";
            if (SupportsGcpu) return "Amonet (GCPU)";
            return "None";
        }
    }

    /// <summary>
    /// 芯片配置数据库
    /// </summary>
    public static class ChipDatabase
    {
        private static readonly Dictionary<ushort, ChipConfig> _configs = new();

        static ChipDatabase()
        {
            InitializeConfigs();
        }

        private static void InitializeConfigs()
        {
            // MT6735
            Add(new ChipConfig
            {
                HwCode = 0x321,
                Name = "MT6735/MT6737",
                Description = "Helio P10",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10216000,
                SejBase = 0x1000A000,
                DxccBase = null,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                SocIdAddr = null,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6739
            Add(new ChipConfig
            {
                HwCode = 0x699,
                Name = "MT6739",
                Description = "Helio A22",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102AF8,
                SocIdAddr = 0x102B08,
                Var1 = 0x73,
                DaMode = DAMode.XFlash
            });

            // MT6761 (Helio A22)
            Add(new ChipConfig
            {
                HwCode = 0x766,
                Name = "MT6761",
                Description = "Helio A22",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102AF8,
                SocIdAddr = 0x102B08,
                Var1 = 0x73,
                DaMode = DAMode.XFlash
            });

            // MT6765 (Helio P35)
            Add(new ChipConfig
            {
                HwCode = 0x766,
                Name = "MT6765",
                Description = "Helio P35",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102AF8,
                SocIdAddr = 0x102B08,
                Var1 = 0x73,
                DaMode = DAMode.XFlash
            });

            // MT6768 (Helio G85)
            Add(new ChipConfig
            {
                HwCode = 0x707,
                Name = "MT6768",
                Description = "Helio G85",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102B38,
                SocIdAddr = 0x102B48,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT6771 (Helio P60)
            Add(new ChipConfig
            {
                HwCode = 0x788,
                Name = "MT6771",
                Description = "Helio P60",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102C78,
                SocIdAddr = 0x102C88,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT6779 (Helio P90)
            Add(new ChipConfig
            {
                HwCode = 0x813,
                Name = "MT6779",
                Description = "Helio P90",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102D98,
                SocIdAddr = 0x102DA8,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT6781 (Helio G96)
            Add(new ChipConfig
            {
                HwCode = 0x996,
                Name = "MT6781",
                Description = "Helio G96",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102E98,
                SocIdAddr = 0x102EA8,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6785 (Helio G90)
            Add(new ChipConfig
            {
                HwCode = 0x725,
                Name = "MT6785",
                Description = "Helio G90",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102D38,
                SocIdAddr = 0x102D48,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT6833 (Dimensity 700)
            Add(new ChipConfig
            {
                HwCode = 0x816,
                Name = "MT6833",
                Description = "Dimensity 700",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102F58,
                SocIdAddr = 0x102F68,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6853 (Dimensity 720)
            Add(new ChipConfig
            {
                HwCode = 0x886,
                Name = "MT6853",
                Description = "Dimensity 720",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102F98,
                SocIdAddr = 0x102FA8,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6873 (Dimensity 800)
            Add(new ChipConfig
            {
                HwCode = 0x873,
                Name = "MT6873",
                Description = "Dimensity 800",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102F78,
                SocIdAddr = 0x102F88,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6877 (Dimensity 900)
            Add(new ChipConfig
            {
                HwCode = 0x989,
                Name = "MT6877",
                Description = "Dimensity 900",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102FB8,
                SocIdAddr = 0x102FC8,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6885 (Dimensity 1000)
            Add(new ChipConfig
            {
                HwCode = 0x8A0,
                Name = "MT6885",
                Description = "Dimensity 1000+",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102FD8,
                SocIdAddr = 0x102FE8,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6893 (Dimensity 1200)
            Add(new ChipConfig
            {
                HwCode = 0x8C0,
                Name = "MT6893",
                Description = "Dimensity 1200",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = 0x10210000,
                EfuseBase = 0x11C10000,
                MeidAddr = 0x102FF8,
                SocIdAddr = 0x103008,
                Var1 = 0x28,
                DaMode = DAMode.Xml
            });

            // MT6580
            Add(new ChipConfig
            {
                HwCode = 0x580,
                Name = "MT6580",
                Description = "Quad-core A7",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10216000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6582
            Add(new ChipConfig
            {
                HwCode = 0x6582,
                Name = "MT6582",
                Description = "Quad-core A7",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10016000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6592
            Add(new ChipConfig
            {
                HwCode = 0x6592,
                Name = "MT6592",
                Description = "Octa-core A7",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10016000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6595
            Add(new ChipConfig
            {
                HwCode = 0x6595,
                Name = "MT6595",
                Description = "Octa-core A17+A7",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10016000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6752
            Add(new ChipConfig
            {
                HwCode = 0x6752,
                Name = "MT6752",
                Description = "Octa-core A53",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10016000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT6755 (Helio P10)
            Add(new ChipConfig
            {
                HwCode = 0x326,
                Name = "MT6755",
                Description = "Helio P10",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = null,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1022F0,
                Var1 = 0xA,
                DaMode = DAMode.XFlash
            });

            // MT6757 (Helio P20)
            Add(new ChipConfig
            {
                HwCode = 0x601,
                Name = "MT6757",
                Description = "Helio P20",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10050000,
                SejBase = 0x1000A000,
                DxccBase = null,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1025B8,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT6797 (Helio X20)
            Add(new ChipConfig
            {
                HwCode = 0x6797,
                Name = "MT6797",
                Description = "Helio X20/X25",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                CqdmaBase = 0x10212000,
                GcpuBase = 0x10210000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1023F0,
                Var1 = 0x25,
                DaMode = DAMode.XFlash
            });

            // MT8163
            Add(new ChipConfig
            {
                HwCode = 0x8163,
                Name = "MT8163",
                Description = "Quad-core A53 Tablet",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10210000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT8167
            Add(new ChipConfig
            {
                HwCode = 0x8167,
                Name = "MT8167",
                Description = "Quad-core A35 Tablet",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10210000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x102218,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });

            // MT8173
            Add(new ChipConfig
            {
                HwCode = 0x8173,
                Name = "MT8173",
                Description = "Dual A72 + Dual A53",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                GcpuBase = 0x10210000,
                SejBase = 0x1000A000,
                EfuseBase = 0x10206000,
                MeidAddr = 0x1008EC,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            });
        }

        private static void Add(ChipConfig config)
        {
            _configs[config.HwCode] = config;
        }

        /// <summary>
        /// 根据硬件代码获取芯片配置
        /// </summary>
        public static ChipConfig? GetConfig(ushort hwCode)
        {
            return _configs.TryGetValue(hwCode, out var config) ? config : null;
        }

        /// <summary>
        /// 获取所有支持的芯片
        /// </summary>
        public static IEnumerable<ChipConfig> GetAllConfigs() => _configs.Values;

        /// <summary>
        /// 获取默认芯片配置
        /// </summary>
        public static ChipConfig GetDefaultConfig()
        {
            return new ChipConfig
            {
                HwCode = 0,
                Name = "Unknown",
                Description = "Unknown MTK Device",
                Watchdog = 0x10007000,
                Uart = 0x11002000,
                BromPayloadAddr = 0x100A00,
                DaPayloadAddr = 0x201000,
                Var1 = 0xA,
                DaMode = DAMode.Legacy
            };
        }
    }
}
