using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Qualcomm.Services;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// OPPO/Realme VIP 设备策略
    /// 支持伪装读写绕过防火墙限制
    /// </summary>
    public class OppoVipDeviceStrategy : StandardDeviceStrategy
    {
        public override string Name => "OPPO/Realme VIP";

        // 缓存每个 LUN 的第一个分区名称 (用于 gptmain 分段伪装)
        private readonly Dictionary<int, string> _lunFirstPartitions = new();

        public override async Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default)
        {
            log("[OPPO] 准备执行 VIP 签名验证...");

            string? finalDigest = digestPath;
            string? finalSig = signaturePath;

            // 自动查找认证文件
            if (string.IsNullOrEmpty(finalDigest) || string.IsNullOrEmpty(finalSig))
            {
                string? dir = Path.GetDirectoryName(programmerPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    if (string.IsNullOrEmpty(finalDigest))
                        finalDigest = FindAuthFile(dir, "digest");
                    if (string.IsNullOrEmpty(finalSig))
                        finalSig = FindAuthFile(dir, "signature");
                }
            }

            // 检查文件
            if (string.IsNullOrEmpty(finalDigest) || !File.Exists(finalDigest) ||
                string.IsNullOrEmpty(finalSig) || !File.Exists(finalSig))
            {
                log("[OPPO] ⚠️ 未找到 VIP 验证文件 (Digest/Signature)");
                return true; // 允许继续尝试
            }

            return await client.PerformVipAuthAsync(finalDigest, finalSig, ct);
        }

        /// <summary>
        /// OPPO 专用 GPT 读取 - 瀑布式策略
        /// </summary>
        public override async Task<List<PartitionInfo>> ReadGptAsync(
            FirehoseClient client,
            CancellationToken ct,
            Action<string> log)
        {
            var allPartitions = new List<PartitionInfo>();
            _lunFirstPartitions.Clear();

            int maxLun = (client.SectorSize == 4096) ? 5 : 0;
            int sectorsToRead = (client.SectorSize == 4096) ? 6 : 34;
            int lunRead = 0;

            for (int lun = 0; lun <= maxLun; lun++)
            {
                if (ct.IsCancellationRequested) break;

                byte[]? data = null;

                // 策略 1: PrimaryGPT + gpt_main{lun}.bin
                data = await TryReadGpt(client, lun, 0, sectorsToRead,
                    "PrimaryGPT", $"gpt_main{lun}.bin", ct);

                // 策略 2: BackupGPT + gpt_backup{lun}.bin
                if (data == null)
                {
                    data = await TryReadGpt(client, lun, 0, sectorsToRead,
                        "BackupGPT", $"gpt_backup{lun}.bin", ct);
                }

                // 策略 3: ssd 伪装
                if (data == null)
                {
                    data = await TryReadGpt(client, lun, 0, sectorsToRead,
                        "ssd", "ssd", ct);
                }

                // 策略 4: 读取 Backup GPT (磁盘末尾)
                if (data == null)
                {
                    log($"[GPT] LUN {lun}: Primary GPT 被拒绝，尝试 Backup GPT...");

                    string info = client.GetStorageInfo(lun);
                    long totalSectors = ParseTotalSectors(info);

                    if (totalSectors > 0)
                    {
                        int backupSectors = (client.SectorSize == 4096) ? 5 : sectorsToRead;
                        long startSector = totalSectors - backupSectors;

                        if (startSector > 0)
                        {
                            data = await TryReadGpt(client, lun, startSector, backupSectors,
                                "BackupGPT", $"gpt_backup{lun}.bin", ct);

                            if (data == null)
                            {
                                data = await TryReadGpt(client, lun, startSector, backupSectors,
                                    "ssd", "ssd", ct);
                            }
                        }
                    }
                }

                if (data != null && data.Length >= 512)
                {
                    // 使用专门的 GptParser 解析 (带日志输出)
                    var parts = GptParser.ParseWithLog(data, lun, client.SectorSize, log);
                    if (parts.Count > 0)
                    {
                        allPartitions.AddRange(parts);
                        lunRead++;

                        // 缓存首个分区名
                        var firstPart = parts.OrderBy(p => p.StartLba).FirstOrDefault();
                        if (firstPart != null)
                        {
                            _lunFirstPartitions[lun] = firstPart.Name;
                            log($"[GPT] LUN {lun}: {parts.Count} 个分区, 首分区: {firstPart.Name}");
                        }
                    }
                }

                await Task.Delay(50, ct);
            }

            log($"[GPT] 共读取 {lunRead} 个 LUN，{allPartitions.Count} 个分区");
            if (allPartitions.Count == 0)
                throw new Exception("VIP 模式读取分区表失败");

            return allPartitions;
        }

        /// <summary>
        /// OPPO 伪装读取分区 - 动态瀑布式策略
        /// 使用 FirehoseClient 的动态伪装生成器
        /// </summary>
        public override async Task<bool> ReadPartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string savePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            bool isUfs = client.StorageType.Contains("ufs");
            long gapSector = isUfs ? 6 : 34;
            long start = (long)part.StartLba;
            long end = start + (long)part.Sectors - 1;

            // 检查是否需要分段读取 (跨越 GPT Gap)
            bool needsSegmented = (start <= gapSector && end >= gapSector);

            if (needsSegmented)
            {
                log($"[VIP] {part.Name}: 检测到跨 GPT Gap，使用分段读取");
                string firstPartName = _lunFirstPartitions.GetValueOrDefault(part.Lun, part.Name);
                return await ReadSegmentedAsync(client, part, savePath, gapSector, firstPartName, progress, ct, log);
            }

            // 使用动态伪装策略
            var strategies = FirehoseClient.GetDynamicSpoofStrategies(
                part.Lun, 
                start, 
                part.Name, 
                start <= 33);

            foreach (var strategy in strategies)
            {
                if (ct.IsCancellationRequested) return false;

                try
                {
                    string label = string.IsNullOrEmpty(strategy.Label) ? part.Name : strategy.Label;
                    string filename = string.IsNullOrEmpty(strategy.Filename) ? $"{part.Name}.bin" : strategy.Filename;

                    log($"[VIP] {part.Name}: 尝试伪装 {label}/{filename}");

                    bool success = await client.ReadPartitionChunkedAsync(
                        savePath, 
                        part.StartLba.ToString(), 
                        (long)part.Sectors,
                        part.Lun.ToString(), 
                        progress, 
                        ct,
                        label, 
                        filename, 
                        append: false,
                        useAutoSpoof: false,
                        partitionName: part.Name);

                    if (success) 
                    {
                        log($"[VIP] ✓ {part.Name}: 伪装成功 ({label})");
                        return true;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIP] {part.Name} 策略 {strategy} 失败: {ex.Message}");
                }

                await Task.Delay(50, ct);
            }

            log($"[VIP] ❌ {part.Name}: 所有伪装策略均失败");
            return false;
        }

        /// <summary>
        /// OPPO 伪装写入分区 - 瀑布式策略
        /// </summary>
        public override async Task<bool> WritePartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string imagePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            var strategies = new List<(string filename, string label)>
            {
                ("gpt_backup0.bin", "BackupGPT"),
                ("gpt_backup0.bin", "gpt_backup0.bin"),
                ("gpt_main0.bin", "gpt_main0.bin"),
                ("ssd", "ssd"),
                (part.Name, part.Name)
            };

            foreach (var (spoofName, spoofLabel) in strategies)
            {
                if (ct.IsCancellationRequested) return false;

                try
                {
                    bool success = await client.FlashPartitionAsync(
                        imagePath,
                        part.StartLba.ToString(),
                        (long)part.Sectors,
                        part.Lun.ToString(),
                        progress, ct,
                        spoofLabel, spoofName);

                    if (success) return true;
                }
                catch (Exception ex)
                {
                    log($"[Write] {part.Name} 策略 {spoofName} 失败: {ex.Message}");
                }

                await Task.Delay(100, ct);
            }

            log($"[Write] ❌ 写入失败: {part.Name}");
            return false;
        }

        #region 辅助方法

        private async Task<byte[]?> TryReadGpt(FirehoseClient client, int lun,
            long startSector, int sectors, string label, string filename, CancellationToken ct)
        {
            try
            {
                // 记录尝试的伪装策略
                System.Diagnostics.Debug.WriteLine($"[GPT] 尝试 LUN{lun}: label={label}, filename={filename}, start={startSector}");
                
                var data = await client.ReadGptPacketAsync(lun, startSector, sectors, label, filename, ct);
                
                if (data != null && data.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[GPT] ✓ 成功读取 {data.Length} 字节");
                }
                
                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GPT] ✗ 失败: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> ReadSegmentedAsync(
            FirehoseClient client,
            PartitionInfo part,
            string savePath,
            long splitPoint,
            string firstPartName,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            long currentSector = (long)part.StartLba;
            long remainingSectors = (long)part.Sectors;
            long totalBytes = remainingSectors * client.SectorSize;
            long currentBytesRead = 0;

            if (File.Exists(savePath)) File.Delete(savePath);

            while (remainingSectors > 0)
            {
                if (ct.IsCancellationRequested) return false;

                string currentFilename = "gpt_main0.bin";
                string currentLabel = "gpt_main0.bin";
                long sectorsToRead = remainingSectors;

                if (currentSector < splitPoint)
                {
                    // Segment 1: 0 - (splitPoint-1)
                    sectorsToRead = Math.Min(remainingSectors, splitPoint - currentSector);
                }
                else if (currentSector == splitPoint)
                {
                    // Segment 2: Gap 扇区
                    sectorsToRead = 1;
                    currentFilename = firstPartName;
                    currentLabel = firstPartName;
                }
                // Segment 3: splitPoint+1 - End (保持 gpt_main0.bin)

                bool success = await client.ReadPartitionChunkedAsync(
                    savePath,
                    currentSector.ToString(),
                    sectorsToRead,
                    part.Lun.ToString(),
                    (c, t) => progress?.Invoke(currentBytesRead + c, totalBytes),
                    ct,
                    currentLabel, currentFilename,
                    append: true);

                if (!success) return false;

                currentSector += sectorsToRead;
                remainingSectors -= sectorsToRead;
                currentBytesRead += sectorsToRead * client.SectorSize;
            }

            return true;
        }

        private string? FindAuthFile(string dir, string baseName)
        {
            string[] extensions = { ".bin", ".mbn", ".elf" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(dir, baseName + ext);
                if (File.Exists(path)) return path;

                path = Path.Combine(dir, baseName.ToUpper() + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private long ParseTotalSectors(string info)
        {
            if (string.IsNullOrEmpty(info)) return 0;

            // JSON 格式: "total_blocks":124186624
            var matchJson = Regex.Match(info, @"""total_blocks""\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (matchJson.Success && long.TryParse(matchJson.Groups[1].Value, out long valJson))
                return valJson;

            // 文本格式: Total Logical Blocks: 0x766f000
            var matchHex = Regex.Match(info, @"Total\s+Logical\s+Blocks\s*:\s*0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase);
            if (matchHex.Success)
            {
                try { return Convert.ToInt64(matchHex.Groups[1].Value, 16); }
                catch { }
            }

            // 旧格式: num_partition_sectors: 123456
            var matchOld = Regex.Match(info, @"num_partition_sectors\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase);
            if (matchOld.Success && long.TryParse(matchOld.Groups[1].Value, out long valOld))
                return valOld;

            return 0;
        }

        #endregion
    }
}
