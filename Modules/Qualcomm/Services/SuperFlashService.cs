using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// Super 直刷服务 - 无需打包直接刷写子分区
    /// </summary>
    public class SuperFlashService
    {
        private readonly FirehoseClient _firehose;
        private readonly Action<string>? _log;
        private readonly Action<int>? _progress;

        public SuperFlashService(FirehoseClient firehose, Action<string>? log = null, Action<int>? progress = null)
        {
            _firehose = firehose ?? throw new ArgumentNullException(nameof(firehose));
            _log = log;
            _progress = progress;
        }

        /// <summary>
        /// 计算 Super 直刷布局
        /// </summary>
        public async Task<List<SuperFlashAction>?> PrepareDirectFlashActionsAsync(
            string jsonPath,
            string? imageRootDir = null)
        {
            var actions = new List<SuperFlashAction>();
            _log?.Invoke("[Super] 计算直刷布局...");

            string baseDir = imageRootDir ?? Path.GetDirectoryName(jsonPath) ?? "";

            string jsonContent;
            using (var reader = File.OpenText(jsonPath))
            {
                jsonContent = await reader.ReadToEndAsync();
            }

            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // 获取对齐参数
            long alignment = 1048576; // 默认 1MB
            if (root.TryGetProperty("block_devices", out var devices) && devices.GetArrayLength() > 0)
            {
                var dev = devices[0];
                if (dev.TryGetProperty("alignment", out var alignProp))
                {
                    string alignStr = alignProp.GetString()?.Replace("_", "") ?? "1048576";
                    long.TryParse(alignStr, out alignment);
                }
            }
            _log?.Invoke($"[Super] 对齐: {alignment} bytes");

            long currentByteOffset = 0;
            const int SECTOR_SIZE = 512;

            // 临时目录
            string tempRawDir = Path.Combine(Path.GetTempPath(), $"SuperRaw_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRawDir);

            try
            {
                // 处理 Super Metadata
                if (root.TryGetProperty("super_meta", out var meta))
                {
                    string metaPathRel = meta.GetProperty("path").GetString()?.Replace("/", "\\") ?? "";
                    string metaSizeStr = meta.GetProperty("size").GetString() ?? "0";
                    long.TryParse(metaSizeStr, out long metaSize);

                    string fullMetaPath = ResolvePath(baseDir, metaPathRel);

                    if (File.Exists(fullMetaPath))
                    {
                        actions.Add(new SuperFlashAction
                        {
                            PartitionName = "super_meta",
                            FilePath = fullMetaPath,
                            RelativeSectorOffset = 0,
                            SizeInBytes = metaSize,
                            DebugInfo = "Metadata (Offset 0)"
                        });
                        currentByteOffset += metaSize;
                    }
                    else
                    {
                        _log?.Invoke($"[Super] ❌ 找不到 super_meta: {fullMetaPath}");
                        return null;
                    }
                }
                else
                {
                    _log?.Invoke("[Super] ❌ JSON 缺少 super_meta 定义");
                    return null;
                }

                // 处理各分区
                if (root.TryGetProperty("partitions", out var partitions))
                {
                    foreach (var part in partitions.EnumerateArray())
                    {
                        if (!part.TryGetProperty("path", out var pathProp))
                            continue;

                        string partName = part.GetProperty("name").GetString() ?? "";
                        string relPath = pathProp.GetString()?.Replace("/", "\\") ?? "";
                        string fullPath = ResolvePath(baseDir, relPath);

                        // 对齐计算
                        long alignedStartOffset = AlignOffset(currentByteOffset, alignment);

                        if (File.Exists(fullPath))
                        {
                            string finalPath = fullPath;

                            // Sparse -> Raw 转换
                            if (SparseStream.IsSparseFile(fullPath))
                            {
                                _log?.Invoke($"[Super] 转换 Sparse: {partName}...");
                                string rawPath = Path.Combine(tempRawDir, partName + ".raw");

                                using var sparse = SparseStream.Open(fullPath, _log);
                                if (sparse.IsValid && sparse.ConvertToRaw(rawPath))
                                {
                                    finalPath = rawPath;
                                }
                                else
                                {
                                    _log?.Invoke($"[Super] ❌ 转换失败: {partName}");
                                    return null;
                                }
                            }

                            long fileSize = new FileInfo(finalPath).Length;

                            actions.Add(new SuperFlashAction
                            {
                                PartitionName = partName,
                                FilePath = finalPath,
                                RelativeSectorOffset = alignedStartOffset / SECTOR_SIZE,
                                SizeInBytes = fileSize,
                                DebugInfo = $"Offset: {alignedStartOffset} (Sector {alignedStartOffset / SECTOR_SIZE})"
                            });

                            currentByteOffset = alignedStartOffset + fileSize;
                        }
                        else
                        {
                            _log?.Invoke($"[Super] ⚠️ 文件缺失: {partName} ({relPath})");
                        }
                    }
                }

                _log?.Invoke($"[Super] ✅ 布局计算完成，{actions.Count} 个步骤");
                return actions;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Super] 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 执行 Super 直刷
        /// </summary>
        public async Task<bool> ExecuteDirectFlashAsync(
            List<SuperFlashAction> actions,
            PartitionInfo superPartition,
            Action<long, long>? overallProgress,
            CancellationToken ct)
        {
            if (actions == null || actions.Count == 0)
            {
                _log?.Invoke("[Super] 没有可刷写的分区");
                return false;
            }

            _log?.Invoke($"[Super] 开始直刷 {actions.Count} 个子分区...");

            long totalBytes = 0;
            foreach (var action in actions)
                totalBytes += action.SizeInBytes;

            long writtenBytes = 0;
            int successCount = 0;

            foreach (var action in actions)
            {
                ct.ThrowIfCancellationRequested();

                _log?.Invoke($"[Super] 刷写: {action.PartitionName} @ Sector {action.RelativeSectorOffset}");

                // 计算绝对扇区位置
                long absoluteSector = (long)superPartition.StartLba + action.RelativeSectorOffset;

                try
                {
                    bool success = await _firehose.FlashPartitionAsync(
                        action.FilePath,
                        absoluteSector.ToString(),
                        action.SizeInBytes / _firehose.SectorSize + 1,
                        superPartition.Lun.ToString(),
                        (current, total) =>
                        {
                            overallProgress?.Invoke(writtenBytes + current, totalBytes);
                        },
                        ct,
                        action.PartitionName);

                    if (success)
                    {
                        successCount++;
                        writtenBytes += action.SizeInBytes;
                        _log?.Invoke($"[Super] ✓ {action.PartitionName}");
                    }
                    else
                    {
                        _log?.Invoke($"[Super] ✗ {action.PartitionName} 失败");
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Super] ✗ {action.PartitionName} 异常: {ex.Message}");
                }
            }

            bool allSuccess = successCount == actions.Count;
            _log?.Invoke($"[Super] 完成 {successCount}/{actions.Count} 个分区");

            return allSuccess;
        }

        /// <summary>
        /// 智能刷写 - 从固件目录自动构建并刷写
        /// </summary>
        public async Task<bool> FlashFromDirectoryAsync(
            string rootDirectory,
            PartitionInfo superPartition,
            Action<long, long>? progress,
            CancellationToken ct)
        {
            _log?.Invoke($"[Super] 智能刷写模式: {Path.GetFileName(rootDirectory)}");

            if (!Directory.Exists(rootDirectory))
            {
                _log?.Invoke("[Super] ❌ 目录不存在");
                return false;
            }

            // 查找配置文件
            string? jsonPath = FindSuperDefJson(rootDirectory);
            if (string.IsNullOrEmpty(jsonPath))
            {
                _log?.Invoke("[Super] ❌ 未找到 super 分区定义文件");
                return false;
            }

            _log?.Invoke($"[Super] 配置文件: {Path.GetFileName(jsonPath)}");

            // 计算布局
            var actions = await PrepareDirectFlashActionsAsync(jsonPath, rootDirectory);
            if (actions == null || actions.Count == 0)
            {
                _log?.Invoke("[Super] ❌ 无法计算刷写布局");
                return false;
            }

            // 执行刷写
            return await ExecuteDirectFlashAsync(actions, superPartition, progress, ct);
        }

        #region 辅助方法

        private string ResolvePath(string baseDir, string relativePath)
        {
            string fullPath = Path.Combine(baseDir, relativePath);
            if (File.Exists(fullPath)) return fullPath;

            // 尝试去掉 IMAGES\ 前缀
            if (relativePath.StartsWith("IMAGES\\", StringComparison.OrdinalIgnoreCase))
            {
                string altPath = Path.Combine(baseDir, relativePath.Substring(7));
                if (File.Exists(altPath)) return altPath;
            }

            // 尝试添加 IMAGES\ 前缀
            string imagesPath = Path.Combine(baseDir, "IMAGES", relativePath);
            if (File.Exists(imagesPath)) return imagesPath;

            return fullPath;
        }

        private string? FindSuperDefJson(string rootDir)
        {
            // META 目录
            string metaDir = Path.Combine(rootDir, "META");
            if (Directory.Exists(metaDir))
            {
                var jsonFiles = Directory.GetFiles(metaDir, "*.json");
                foreach (var f in jsonFiles)
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("super_def", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
                // 其他 JSON
                foreach (var f in jsonFiles)
                {
                    string name = Path.GetFileName(f);
                    if (!name.Equals("config.json", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }

            // 根目录
            var rootJsons = Directory.GetFiles(rootDir, "*.json");
            foreach (var f in rootJsons)
            {
                if (Path.GetFileName(f).StartsWith("super_def", StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            return null;
        }

        private static long AlignOffset(long current, long alignment)
        {
            if (alignment == 0) return current;
            long remainder = current % alignment;
            if (remainder == 0) return current;
            return current + (alignment - remainder);
        }

        #endregion
    }

    /// <summary>
    /// Super 分区刷写动作
    /// </summary>
    public class SuperFlashAction
    {
        public string PartitionName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long RelativeSectorOffset { get; set; }
        public long SizeInBytes { get; set; }
        public string DebugInfo { get; set; } = "";
    }

    #region JSON 数据模型

    public class SuperDef
    {
        [JsonPropertyName("super_meta")]
        public SuperMeta? Meta { get; set; }

        [JsonPropertyName("block_devices")]
        public List<BlockDevice>? BlockDevices { get; set; }

        [JsonPropertyName("groups")]
        public List<PartitionGroup>? Groups { get; set; }

        [JsonPropertyName("partitions")]
        public List<SuperPartition>? Partitions { get; set; }
    }

    public class SuperMeta
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";
    }

    public class BlockDevice
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";

        [JsonPropertyName("alignment")]
        public string Alignment { get; set; } = "0";
    }

    public class PartitionGroup
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("maximum_size")]
        public string MaximumSize { get; set; } = "0";
    }

    public class SuperPartition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("group_name")]
        public string GroupName { get; set; } = "";

        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    #endregion
}
