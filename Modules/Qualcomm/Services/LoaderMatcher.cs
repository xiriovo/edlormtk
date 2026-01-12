using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// Loader 本地自动匹配服务
    /// 基于设备芯片信息自动选择正确的 Loader 和认证文件
    /// </summary>
    public class LoaderMatcher
    {
        private readonly string _loaderBaseDir;
        private readonly Action<string>? _log;

        // MSM ID -> Loader 目录映射
        private static readonly Dictionary<uint, string[]> MsmIdToLoaderDir = new()
        {
            // SM8xxx 旗舰系列
            { 0x001FA0E1, new[] { "SM8750_8E", "SM8750" } },                    // SM8750 (8 Elite)
            { 0x001EA0E1, new[] { "SM8650_8Gen3", "SM8650" } },                 // SM8650 (8 Gen3)
            { 0x001CA0E1, new[] { "SM8550_8Gen2_8+Gen2", "SM8550" } },          // SM8550 (8 Gen2)
            { 0x001BA0E1, new[] { "SM8475_8+Gen1", "SM8475" } },                // SM8475 (8+ Gen1)
            { 0x001AA0E1, new[] { "SM8450_8gen1", "SM8450" } },                 // SM8450 (8 Gen1)
            { 0x0018A0E1, new[] { "SM8350_888_888+", "SM8350" } },              // SM8350 (888/888+)
            { 0x0017A0E1, new[] { "SM8250", "SDM865" } },                       // SM8250 (865)
            { 0x0015A0E1, new[] { "SM8150", "SDM855" } },                       // SM8150 (855)
            
            // SM7xxx 中高端系列
            { 0x0019B0E1, new[] { "SM7675_7+Gen3", "SM7675" } },                // SM7675 (7+ Gen3)
            { 0x0018B0E1, new[] { "SM7475_7+Gen2", "SM7475" } },                // SM7475 (7+ Gen2)
            { 0x0011E0E1, new[] { "765G_765_768G_732_730G_730_678_675", "SM7250" } },
            { 0x000E70E1, new[] { "765G_765_768G_732_730G_730_678_675", "SM7150" } },
            
            // SM6xxx 中端系列
            { 0x0014C0E1, new[] { "SM6375_695_6sGen3", "SM6375" } },            // SM6375 (695)
            { 0x0012C0E1, new[] { "SM6225", "SM6225" } },                       // SM6225 (680)
            { 0x000F50E1, new[] { "SM6115_460_662", "SM6115" } },               // SM6115 (460)
            
            // SM4xxx 入门系列
            { 0x0012D0E1, new[] { "SM4350_480_Special", "SM4350" } },           // SM4350 (480)
            
            // SDM8xx 旧旗舰
            { 0x009470E1, new[] { "SDM845_Special", "SDM845" } },               // SDM845 (845)
            { 0x009400E1, new[] { "SDM845_Special", "MSM8994" } },              // MSM8994 (810)
            
            // SDM7xx 旧中高端
            { 0x000DB0E1, new[] { "710_670_712", "SDM710" } },                  // SDM710 (710)
            { 0x000910E1, new[] { "710_670_712", "SDM670" } },                  // SDM670 (670)
            { 0x0008C0E1, new[] { "710_670_712", "SDM660" } },                  // SDM660 (660)
        };

        // OEM ID -> 厂商标识
        private static readonly Dictionary<ushort, string> OemIdToVendor = new()
        {
            { 0x0051, "OPPO" },      // OPPO/OnePlus/Realme
            { 0x0072, "Xiaomi" },    // 小米
            { 0x0073, "Vivo" },      // Vivo/iQOO
            { 0x0017, "Lenovo" },    // 联想
            { 0x0040, "Lenovo" },    // 联想平板
        };

        public LoaderMatcher(string loaderBaseDir, Action<string>? log = null)
        {
            _loaderBaseDir = loaderBaseDir ?? "";
            _log = log;
        }

        /// <summary>
        /// 根据设备信息自动匹配 Loader
        /// </summary>
        public LoaderMatchResult FindLoader(uint msmId, ushort oemId, string? pkHash = null)
        {
            var result = new LoaderMatchResult();

            // 1. 确定厂商
            string vendor = "Unknown";
            if (OemIdToVendor.TryGetValue(oemId, out string? v))
                vendor = v;
            else if (!string.IsNullOrEmpty(pkHash))
                vendor = QualcommDatabase.GetVendorByPkHash(pkHash);

            result.Vendor = vendor;
            result.MsmId = msmId;
            result.OemId = oemId;

            // 2. 查找匹配的 Loader 目录
            string[]? possibleDirs = null;

            if (MsmIdToLoaderDir.TryGetValue(msmId, out string[]? dirs))
            {
                possibleDirs = dirs;
            }
            else
            {
                // 尝试只用低24位匹配
                uint msmIdLow = msmId & 0xFFFFFF;
                foreach (var kvp in MsmIdToLoaderDir)
                {
                    if ((kvp.Key & 0xFFFFFF) == msmIdLow)
                    {
                        possibleDirs = kvp.Value;
                        break;
                    }
                }
            }

            if (possibleDirs == null)
            {
                _log?.Invoke($"[LoaderMatcher] 未找到 MSM ID 0x{msmId:X} 的 Loader 映射");
                return result;
            }

            // 3. 在基础目录中搜索匹配的 Loader
            foreach (string dirName in possibleDirs)
            {
                string fullPath = Path.Combine(_loaderBaseDir, dirName);
                if (!Directory.Exists(fullPath))
                    continue;

                // 搜索 Loader 文件
                var loaderFiles = Directory.GetFiles(fullPath, "*.*")
                    .Where(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".melf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("Digest", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("Sign", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                if (loaderFiles.Count == 0)
                    continue;

                result.LoaderPath = loaderFiles[0];
                result.LoaderDir = fullPath;

                // 查找对应的 Digest 和 Sign 文件
                result.DigestPath = FindAuthFile(fullPath, "Digest");
                result.SignPath = FindAuthFile(fullPath, "Sign");

                result.RequiresVip = !string.IsNullOrEmpty(result.DigestPath) &&
                                     !string.IsNullOrEmpty(result.SignPath);

                result.MatchScore = 100;
                result.MatchType = "local_exact_match";
                result.MatchInfo = "本地 MSM ID 精确匹配";

                _log?.Invoke($"[LoaderMatcher] 匹配成功: {Path.GetFileName(result.LoaderPath)}");
                if (result.RequiresVip)
                    _log?.Invoke($"[LoaderMatcher] VIP 认证文件: {Path.GetFileName(result.DigestPath)}, {Path.GetFileName(result.SignPath)}");

                return result;
            }

            _log?.Invoke($"[LoaderMatcher] 未在 {_loaderBaseDir} 找到匹配的 Loader 目录");
            return result;
        }

        /// <summary>
        /// 根据芯片名称匹配 Loader
        /// </summary>
        public LoaderMatchResult FindLoaderByChipName(string chipName, ushort oemId = 0)
        {
            string[] keywords = chipName.ToUpperInvariant()
                .Replace("SNAPDRAGON", "")
                .Replace("QUALCOMM", "")
                .Split(new[] { ' ', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

            if (!Directory.Exists(_loaderBaseDir))
                return new LoaderMatchResult();

            var dirs = Directory.GetDirectories(_loaderBaseDir);
            foreach (string keyword in keywords)
            {
                foreach (string dir in dirs)
                {
                    string dirName = Path.GetFileName(dir).ToUpperInvariant();
                    if (dirName.Contains(keyword))
                    {
                        var loaderFiles = Directory.GetFiles(dir, "*.*")
                            .Where(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".melf", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                            .Where(f => !f.Contains("Digest", StringComparison.OrdinalIgnoreCase) &&
                                        !f.Contains("Sign", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .ToList();

                        if (loaderFiles.Count > 0)
                        {
                            var result = new LoaderMatchResult
                            {
                                LoaderPath = loaderFiles[0],
                                LoaderDir = dir,
                                OemId = oemId
                            };

                            result.DigestPath = FindAuthFile(dir, "Digest");
                            result.SignPath = FindAuthFile(dir, "Sign");
                            result.RequiresVip = !string.IsNullOrEmpty(result.DigestPath) &&
                                                 !string.IsNullOrEmpty(result.SignPath);

                            return result;
                        }
                    }
                }
            }

            return new LoaderMatchResult();
        }

        /// <summary>
        /// 查找认证文件 (Digest/Sign)
        /// </summary>
        private string? FindAuthFile(string dir, string keyword)
        {
            var files = Directory.GetFiles(dir, "*.*")
                .Where(f => f.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Where(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .ToList();

            return files.Count > 0 ? files[0] : null;
        }

        /// <summary>
        /// 获取所有可用的 Loader 列表
        /// </summary>
        public List<LoaderInfo> GetAvailableLoaders()
        {
            var loaders = new List<LoaderInfo>();

            if (!Directory.Exists(_loaderBaseDir))
                return loaders;

            foreach (string dir in Directory.GetDirectories(_loaderBaseDir))
            {
                var loaderFiles = Directory.GetFiles(dir, "*.*")
                    .Where(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".melf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("Digest", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("Sign", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (string file in loaderFiles)
                {
                    var info = new LoaderInfo
                    {
                        Path = file,
                        FileName = Path.GetFileName(file),
                        ChipName = Path.GetFileName(dir),
                        HasDigest = FindAuthFile(dir, "Digest") != null,
                        HasSign = FindAuthFile(dir, "Sign") != null,
                        FileSize = new FileInfo(file).Length
                    };
                    loaders.Add(info);
                }
            }

            return loaders;
        }
    }

    /// <summary>
    /// Loader 匹配结果
    /// </summary>
    public class LoaderMatchResult
    {
        public string? LoaderPath { get; set; }
        public string? LoaderDir { get; set; }
        public string? DigestPath { get; set; }
        public string? SignPath { get; set; }
        public bool RequiresVip { get; set; }
        public string Vendor { get; set; } = "Unknown";
        public uint MsmId { get; set; }
        public ushort OemId { get; set; }
        public int MatchScore { get; set; } = 0;
        public string? MatchType { get; set; }
        public string? MatchInfo { get; set; }

        public bool IsValid => !string.IsNullOrEmpty(LoaderPath) && File.Exists(LoaderPath);
        public bool IsPerfectMatch => MatchScore >= 100;
        public bool IsPartialMatch => MatchScore >= 50 && MatchScore < 100;
        public bool IsWeakMatch => MatchScore >= 30 && MatchScore < 50;
    }

    /// <summary>
    /// Loader 信息
    /// </summary>
    public class LoaderInfo
    {
        public string Path { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ChipName { get; set; } = "";
        public bool HasDigest { get; set; }
        public bool HasSign { get; set; }
        public long FileSize { get; set; }

        public bool RequiresVip => HasDigest && HasSign;
    }
}
