using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace tools.Modules.MTK.Resources
{
    /// <summary>
    /// MTK Payload 资源管理器
    /// 管理各种芯片专用和通用 Payload 文件
    /// </summary>
    public static class PayloadManager
    {
        // Payload 目录相对路径
        private static readonly string PayloadsDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "Modules", "MTK", "Payloads");

        private static readonly string LoadersDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "Modules", "MTK", "Loaders");

        // 缓存已加载的 Payload
        private static readonly Dictionary<string, byte[]> _cache = new();

        #region Payload 类型定义

        /// <summary>
        /// 通用 Payload 类型
        /// </summary>
        public static class GenericPayloads
        {
            public const string DumpPayload = "generic_dump_payload.bin";
            public const string LoaderPayload = "generic_loader_payload.bin";
            public const string PatcherPayload = "generic_patcher_payload.bin";
            public const string PreloaderDumpPayload = "generic_preloader_dump_payload.bin";
            public const string RebootPayload = "generic_reboot_payload.bin";
            public const string SramPayload = "generic_sram_payload.bin";
            public const string Stage1Payload = "generic_stage1_payload.bin";
            public const string UartDumpPayload = "generic_uart_dump_payload.bin";
            public const string DaXPayload = "da_x.bin";
            public const string DaXmlPayload = "da_xml.bin";
            public const string PlPayload = "pl.bin";
            public const string Stage2Payload = "stage2.bin";
        }

        /// <summary>
        /// DA 加载器类型
        /// </summary>
        public static class DaLoaders
        {
            public const string MTK_DA_V5 = "MTK_DA_V5.bin";
            public const string MTK_DA_V6 = "MTK_DA_V6.bin";
        }

        /// <summary>
        /// 支持的芯片型号 (有专用 Payload)
        /// </summary>
        public static readonly ushort[] SupportedChips = new ushort[]
        {
            0x2601, // MT2601
            0x6261, // MT6261
            0x6572, // MT6572
            0x6580, // MT6580
            0x6582, // MT6582
            0x6592, // MT6592
            0x6595, // MT6595
            0x6735, // MT6735
            0x6737, // MT6737
            0x6739, // MT6739
            0x6752, // MT6752
            0x6753, // MT6753
            0x6755, // MT6755
            0x6757, // MT6757
            0x6758, // MT6758
            0x6761, // MT6761
            0x6763, // MT6763
            0x6765, // MT6765
            0x6768, // MT6768
            0x6771, // MT6771
            0x6779, // MT6779
            0x6781, // MT6781
            0x6785, // MT6785
            0x6795, // MT6795
            0x6797, // MT6797
            0x6799, // MT6799
            0x6833, // MT6833
            0x6853, // MT6853
            0x6873, // MT6873
            0x6877, // MT6877
            0x6885, // MT6885
            0x6893, // MT6893
            0x8127, // MT8127
            0x8163, // MT8163
            0x8167, // MT8167
            0x8168, // MT8168
            0x8173, // MT8173
            0x8176, // MT8176
            0x8512, // MT8512
            0x8516, // MT8516
            0x8590, // MT8590
            0x8695, // MT8695
        };

        #endregion

        #region 加载方法

        /// <summary>
        /// 获取芯片专用 Payload
        /// </summary>
        /// <param name="hwCode">芯片硬件代码</param>
        /// <returns>Payload 数据，如果不存在则返回 null</returns>
        public static byte[]? GetChipPayload(ushort hwCode)
        {
            string fileName = $"mt{hwCode:x}_payload.bin";
            return LoadPayload(fileName);
        }

        /// <summary>
        /// 获取通用 Payload
        /// </summary>
        /// <param name="payloadName">Payload 名称 (使用 GenericPayloads 常量)</param>
        /// <returns>Payload 数据</returns>
        public static byte[]? GetGenericPayload(string payloadName)
        {
            return LoadPayload(payloadName);
        }

        /// <summary>
        /// 获取 DA 加载器
        /// </summary>
        /// <param name="loaderName">加载器名称 (使用 DaLoaders 常量)</param>
        /// <returns>DA 数据</returns>
        public static byte[]? GetDaLoader(string loaderName)
        {
            return LoadLoader(loaderName);
        }

        /// <summary>
        /// 检查芯片是否有专用 Payload
        /// </summary>
        public static bool HasChipPayload(ushort hwCode)
        {
            string fileName = $"mt{hwCode:x}_payload.bin";
            string filePath = Path.Combine(PayloadsDir, fileName);
            return File.Exists(filePath);
        }

        /// <summary>
        /// 获取最佳 Payload (优先芯片专用，否则使用通用)
        /// </summary>
        /// <param name="hwCode">芯片硬件代码</param>
        /// <returns>Payload 数据</returns>
        public static byte[]? GetBestPayload(ushort hwCode)
        {
            // 尝试获取芯片专用 Payload
            var chipPayload = GetChipPayload(hwCode);
            if (chipPayload != null)
                return chipPayload;

            // 回退到通用 Payload
            return GetGenericPayload(GenericPayloads.LoaderPayload);
        }

        /// <summary>
        /// 获取 Stage1 Payload (用于漏洞利用)
        /// </summary>
        public static byte[]? GetStage1Payload(ushort hwCode)
        {
            // 尝试芯片专用
            var chipPayload = GetChipPayload(hwCode);
            if (chipPayload != null)
                return chipPayload;

            // 使用通用 Stage1
            return GetGenericPayload(GenericPayloads.Stage1Payload);
        }

        /// <summary>
        /// 获取 Stage2 Payload (用于主功能)
        /// </summary>
        public static byte[]? GetStage2Payload()
        {
            return GetGenericPayload(GenericPayloads.Stage2Payload);
        }

        /// <summary>
        /// 获取 BROM Dump Payload
        /// </summary>
        public static byte[]? GetDumpPayload(ushort hwCode)
        {
            // 尝试芯片专用
            var chipPayload = GetChipPayload(hwCode);
            if (chipPayload != null)
                return chipPayload;

            return GetGenericPayload(GenericPayloads.DumpPayload);
        }

        /// <summary>
        /// 获取 Preloader Dump Payload
        /// </summary>
        public static byte[]? GetPreloaderDumpPayload()
        {
            return GetGenericPayload(GenericPayloads.PreloaderDumpPayload);
        }

        /// <summary>
        /// 获取 DA (Download Agent) 文件
        /// </summary>
        /// <param name="version">DA 版本 (5 或 6)</param>
        public static byte[]? GetDA(int version)
        {
            string loaderName = version switch
            {
                5 => DaLoaders.MTK_DA_V5,
                6 => DaLoaders.MTK_DA_V6,
                _ => DaLoaders.MTK_DA_V5
            };
            return GetDaLoader(loaderName);
        }

        #endregion

        #region 私有方法

        private static byte[]? LoadPayload(string fileName)
        {
            // 检查缓存
            if (_cache.TryGetValue(fileName, out var cached))
                return cached;

            string filePath = Path.Combine(PayloadsDir, fileName);
            if (!File.Exists(filePath))
                return null;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                _cache[fileName] = data;
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? LoadLoader(string fileName)
        {
            // 检查缓存
            string cacheKey = "loader_" + fileName;
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            string filePath = Path.Combine(LoadersDir, fileName);
            if (!File.Exists(filePath))
                return null;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                _cache[cacheKey] = data;
                return data;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取 Payload 目录路径
        /// </summary>
        public static string GetPayloadsPath() => PayloadsDir;

        /// <summary>
        /// 获取 Loader 目录路径
        /// </summary>
        public static string GetLoadersPath() => LoadersDir;

        #endregion

        #region Preloader 文件管理

        /// <summary>
        /// 获取 Preloader 文件目录
        /// </summary>
        public static string GetPreloadersPath()
        {
            return Path.Combine(LoadersDir, "Preloader");
        }

        /// <summary>
        /// 列出所有可用的 Preloader 文件
        /// </summary>
        public static string[] ListPreloaders()
        {
            string path = GetPreloadersPath();
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.GetFiles(path, "*.bin");
        }

        /// <summary>
        /// 加载指定的 Preloader 文件
        /// </summary>
        public static byte[]? LoadPreloader(string fileName)
        {
            string path = Path.Combine(GetPreloadersPath(), fileName);
            if (!File.Exists(path))
                return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
