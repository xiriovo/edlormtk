using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.SuperDef
{
    /// <summary>
    /// Super分区刷写结果
    /// </summary>
    public class SuperFlashResult
    {
        public bool Success { get; set; }
        public int TotalPartitions { get; set; }
        public int FlashedPartitions { get; set; }
        public int FailedPartitions { get; set; }
        public long TotalBytes { get; set; }
        public List<string> FailedPartitionNames { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Super分区刷写进度
    /// </summary>
    public class SuperFlashProgress
    {
        public string CurrentPartition { get; set; } = "";
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public long CurrentBytes { get; set; }
        public long TotalBytes { get; set; }
        public double OverallProgress => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100 : 0;
    }

    /// <summary>
    /// 高通Super直刷器 - 使用Super Meta模式
    /// </summary>
    public class SuperFlasher
    {
        private readonly FirehoseClient _firehose;
        private readonly Action<string>? _log;
        private readonly Action<SuperFlashProgress>? _progress;
        private readonly SuperDefParser _parser = new();

        public SuperFlasher(
            FirehoseClient firehose,
            Action<string>? log = null,
            Action<SuperFlashProgress>? progress = null)
        {
            _firehose = firehose ?? throw new ArgumentNullException(nameof(firehose));
            _log = log;
            _progress = progress;
        }

        /// <summary>
        /// 执行Super直刷
        /// </summary>
        /// <param name="firmwareDir">固件目录</param>
        /// <param name="nvId">NV ID (如 10010111), 可选</param>
        /// <param name="flashSlotB">是否同时刷写B槽位</param>
        /// <param name="ct">取消令牌</param>
        public async Task<SuperFlashResult> FlashSuperAsync(
            string firmwareDir,
            string? nvId = null,
            bool flashSlotB = false,
            CancellationToken ct = default)
        {
            var result = new SuperFlashResult();

            try
            {
                // 1. 查找并解析 super_def
                _log?.Invoke("[Super] 解析 super_def.json...");
                var def = _parser.ParseFromFirmware(firmwareDir, nvId);
                if (def == null)
                {
                    result.ErrorMessage = "未找到 super_def.json 或解析失败";
                    _log?.Invoke($"[Super] ❌ {result.ErrorMessage}");
                    return result;
                }

                _log?.Invoke($"[Super] ✅ 找到Super定义: NV={def.NvId}, 分区数={def.Partitions?.Count ?? 0}");

                // 2. 获取需要刷写的分区
                var partitions = _parser.GetFlashablePartitions(def, flashSlotB);
                if (partitions.Count == 0)
                {
                    result.ErrorMessage = "没有找到需要刷写的分区";
                    _log?.Invoke($"[Super] ❌ {result.ErrorMessage}");
                    return result;
                }

                result.TotalPartitions = partitions.Count;
                _log?.Invoke($"[Super] 📦 准备刷写 {partitions.Count} 个分区");

                // 计算总大小
                foreach (var p in partitions)
                {
                    var imgPath = Path.Combine(firmwareDir, p.Path!);
                    if (File.Exists(imgPath))
                        result.TotalBytes += new FileInfo(imgPath).Length;
                }

                // 3. 逐个刷写子分区
                int index = 0;
                long bytesWritten = 0;

                foreach (var partition in partitions)
                {
                    ct.ThrowIfCancellationRequested();
                    index++;

                    var imgPath = Path.Combine(firmwareDir, partition.Path!);
                    if (!File.Exists(imgPath))
                    {
                        _log?.Invoke($"[Super] ⚠️ 跳过不存在的文件: {partition.Path}");
                        continue;
                    }

                    var fileSize = new FileInfo(imgPath).Length;
                    _log?.Invoke($"[Super] [{index}/{partitions.Count}] 刷写 {partition.Name} ({fileSize / 1024 / 1024}MB)...");

                    // 更新进度
                    _progress?.Invoke(new SuperFlashProgress
                    {
                        CurrentPartition = partition.Name!,
                        CurrentIndex = index,
                        TotalCount = partitions.Count,
                        CurrentBytes = bytesWritten,
                        TotalBytes = result.TotalBytes
                    });

                    // 查找目标分区
                    var targetPartition = _firehose.FindPartition(partition.Name!);
                    if (targetPartition == null)
                    {
                        _log?.Invoke($"[Super] ⚠️ 未在设备上找到分区: {partition.Name}");
                        result.FailedPartitions++;
                        result.FailedPartitionNames.Add(partition.Name!);
                        continue;
                    }

                    // 刷写分区
                    bool success = await _firehose.FlashPartitionAsync(
                        imgPath,
                        targetPartition.StartSector.ToString(),
                        targetPartition.NumSectors,
                        targetPartition.Lun.ToString(),
                        (current, total) =>
                        {
                            _progress?.Invoke(new SuperFlashProgress
                            {
                                CurrentPartition = partition.Name!,
                                CurrentIndex = index,
                                TotalCount = partitions.Count,
                                CurrentBytes = bytesWritten + current,
                                TotalBytes = result.TotalBytes
                            });
                        },
                        ct,
                        partition.Name);

                    if (success)
                    {
                        result.FlashedPartitions++;
                        bytesWritten += fileSize;
                        _log?.Invoke($"[Super] ✅ {partition.Name} 刷写完成");
                    }
                    else
                    {
                        result.FailedPartitions++;
                        result.FailedPartitionNames.Add(partition.Name!);
                        _log?.Invoke($"[Super] ❌ {partition.Name} 刷写失败");
                    }
                }

                // 4. 刷写 super_meta
                var metaPath = _parser.GetSuperMetaPath(firmwareDir, def);
                if (!string.IsNullOrEmpty(metaPath))
                {
                    _log?.Invoke("[Super] 📝 更新 Super 元数据...");

                    var superPartition = _firehose.FindPartition("super");
                    if (superPartition != null)
                    {
                        var metaFileInfo = new FileInfo(metaPath);
                        long metaSectors = (metaFileInfo.Length + _firehose.SectorSize - 1) / _firehose.SectorSize;

                        bool metaSuccess = await _firehose.FlashPartitionAsync(
                            metaPath,
                            superPartition.StartSector.ToString(),
                            metaSectors,
                            superPartition.Lun.ToString(),
                            null,
                            ct,
                            "super_meta");

                        if (metaSuccess)
                        {
                            _log?.Invoke("[Super] ✅ Super 元数据更新完成");
                        }
                        else
                        {
                            _log?.Invoke("[Super] ⚠️ Super 元数据更新失败");
                        }
                    }
                }

                result.Success = result.FailedPartitions == 0;
                _log?.Invoke($"[Super] 🎉 刷写完成: {result.FlashedPartitions}/{result.TotalPartitions} 成功");

                return result;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "操作已取消";
                _log?.Invoke("[Super] ⚠️ 刷写已取消");
                throw;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _log?.Invoke($"[Super] ❌ 刷写异常: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 检查固件是否支持Super Meta模式
        /// </summary>
        public bool IsSuperMetaSupported(string firmwareDir, out string? nvId)
        {
            nvId = null;

            var metaDir = Path.Combine(firmwareDir, "META");
            if (!Directory.Exists(metaDir)) return false;

            // 查找 super_def.*.json
            var files = Directory.GetFiles(metaDir, "super_def.*.json");
            if (files.Length == 0)
            {
                files = Directory.GetFiles(metaDir, "super_def.json");
            }

            if (files.Length == 0) return false;

            // 提取 NV ID
            var fileName = Path.GetFileNameWithoutExtension(files[0]);
            if (fileName.StartsWith("super_def.") && fileName != "super_def")
            {
                nvId = fileName.Replace("super_def.", "");
            }

            return true;
        }

        /// <summary>
        /// 获取Super分区信息摘要
        /// </summary>
        public string? GetSuperSummary(string firmwareDir, string? nvId = null)
        {
            var def = _parser.ParseFromFirmware(firmwareDir, nvId);
            if (def == null) return null;

            var partitions = _parser.GetFlashablePartitions(def, false);
            long totalSize = partitions.Sum(p =>
            {
                var path = Path.Combine(firmwareDir, p.Path ?? "");
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            });

            return $"Super Meta模式: {partitions.Count}个分区, 总计{totalSize / 1024 / 1024}MB";
        }
    }
}
