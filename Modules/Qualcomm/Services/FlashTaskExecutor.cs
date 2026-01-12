// ============================================================================
// MultiFlash TOOL - Flash Task Executor
// 刷机任务执行器 | フラッシュタスク実行器 | 플래시 작업 실행기
// ============================================================================
// [EN] Orchestrates flash operations with progress tracking and retry logic
//      Supports parallel/sequential flashing, error recovery, status reporting
// [中文] 协调刷机操作，支持进度跟踪和重试逻辑
//       支持并行/顺序刷写、错误恢复、状态报告
// [日本語] 進捗追跡とリトライロジックでフラッシュ操作を調整
//         並列/順次フラッシュ、エラーリカバリ、ステータスレポートをサポート
// [한국어] 진행 추적 및 재시도 로직으로 플래시 작업 조정
//         병렬/순차 플래시, 오류 복구, 상태 보고 지원
// [Español] Orquesta operaciones de flash con seguimiento de progreso y lógica de reintento
//           Soporta flash paralelo/secuencial, recuperación de errores, informe de estado
// [Русский] Оркестрирует операции прошивки с отслеживанием прогресса и логикой повторов
//           Поддержка параллельной/последовательной прошивки, восстановления после ошибок
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Qualcomm.Strategies;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// Flash Task Executor - Orchestrates flash operations
    /// 刷机任务执行器 | フラッシュタスク実行器 | 플래시 작업 실행기
    /// </summary>
    public class FlashTaskExecutor
    {
        public FirehoseClient Client { get; }
        private readonly Action<string> _log;
        private readonly IDeviceStrategy _strategy;

        public int SectorSize { get; }

        // Sahara 芯片信息访问器
        public string ChipSerial => Client?.ChipSerial ?? "";
        public string ChipHwId => Client?.ChipHwId ?? "";
        public string ChipPkHash => Client?.ChipPkHash ?? "";

        // 进度事件
        public event Action<long, long>? ProgressChanged;
        public event Action<int, int>? TaskProgressChanged;
        public event Action<string>? StatusChanged;

        public FlashTaskExecutor(FirehoseClient client, IDeviceStrategy strategy, Action<string> log, int sectorSize)
        {
            Client = client;
            _strategy = strategy;
            _log = log;
            SectorSize = sectorSize;
        }

        /// <summary>
        /// 获取分区表
        /// </summary>
        public async Task<List<PartitionInfo>> GetPartitionsAsync(CancellationToken ct)
        {
            UpdateStatus("正在读取分区表 (GPT)...");
            return await _strategy.ReadGptAsync(Client, ct, _log);
        }

        /// <summary>
        /// 读取分区
        /// </summary>
        public async Task ReadPartitionAsync(PartitionInfo part, string savePath, CancellationToken ct)
        {
            UpdateStatus($"正在读取分区: {part.Name}");
            var sw = Stopwatch.StartNew();

            bool success = await _strategy.ReadPartitionAsync(Client, part, savePath,
                (c, t) => UpdateProgress(c, t), ct, _log);

            sw.Stop();
            if (!success) throw new Exception($"读取 {part.Name} 失败");
            UpdateProgress(100, 100);
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task ErasePartitionAsync(PartitionInfo part, CancellationToken ct)
        {
            UpdateStatus($"正在擦除分区: {part.Name}");
            UpdateProgress(0, 100);
            bool success = await _strategy.ErasePartitionAsync(Client, part, ct, _log);
            if (!success) throw new Exception($"擦除 {part.Name} 失败");
            UpdateProgress(100, 100);
        }

        /// <summary>
        /// 批量刷写任务
        /// </summary>
        public async Task ExecuteFlashTasksAsync(
            List<FlashPartitionInfo> tasks,
            bool protectLun5,
            List<string>? patchFiles,
            CancellationToken ct)
        {
            int successCount = 0;
            int failCount = 0;

            // 过滤无效任务
            var sortedTasks = tasks
                .Where(t => !string.IsNullOrEmpty(t.Filename) && File.Exists(t.Filename))
                .Where(t => !(protectLun5 && t.Lun == "5"))
                .OrderBy(t => int.TryParse(t.Lun, out int lun) ? lun : 99)
                .ThenBy(t => ParseStartSector(t.StartSector))
                .ToList();

            // 计算总数据量
            long totalBatchBytes = 0;
            foreach (var t in sortedTasks)
            {
                if (t.NumSectors > 0)
                    totalBatchBytes += t.NumSectors * SectorSize;
                else if (File.Exists(t.Filename))
                    totalBatchBytes += GetRealImageSize(t.Filename);
            }

            long processedBatchBytes = 0;
            UpdateProgress(0, totalBatchBytes);

            _log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _log($"[刷机] 开始批量写入 ({sortedTasks.Count} 个分区)");
            _log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            var totalSw = Stopwatch.StartNew();

            for (int i = 0; i < sortedTasks.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var task = sortedTasks[i];
                long currentTaskBytes = task.NumSectors > 0 
                    ? task.NumSectors * SectorSize 
                    : GetRealImageSize(task.Filename);

                UpdateStatus($"正在写入: {task.Name} ({i + 1}/{sortedTasks.Count})");
                TaskProgressChanged?.Invoke(i + 1, sortedTasks.Count);

                try
                {
                    var sw = Stopwatch.StartNew();

                    var partInfo = new PartitionInfo
                    {
                        Name = task.Name,
                        StartSector = ParseStartSector(task.StartSector),
                        NumSectors = task.NumSectors,
                        Lun = int.Parse(task.Lun),
                        SectorSize = SectorSize
                    };

                    bool result = await _strategy.WritePartitionAsync(
                        Client, partInfo, task.Filename,
                        (current, total) =>
                        {
                            UpdateProgress(processedBatchBytes + current, totalBatchBytes);
                        },
                        ct, _log);

                    sw.Stop();

                    if (result)
                    {
                        successCount++;
                        double mbps = (currentTaskBytes / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds;
                        _log($"[Success] {task.Name} ({FormatSize(currentTaskBytes)}, {mbps:F1} MB/s)");
                    }
                    else
                    {
                        failCount++;
                        _log($"[Fail] {task.Name} 写入失败");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _log($"[Error] {task.Name}: {ex.Message}");
                }

                processedBatchBytes += currentTaskBytes;
            }

            // 应用补丁
            if (patchFiles != null && patchFiles.Count > 0)
            {
                UpdateStatus("正在应用补丁...");
                foreach (var patch in patchFiles)
                {
                    if (File.Exists(patch))
                    {
                        _log($"[Patch] 应用补丁: {Path.GetFileName(patch)}");
                        string content = File.ReadAllText(patch);
                        Client.ApplyPatch(content);
                    }
                }
            }

            totalSw.Stop();

            double avgSpeed = totalBatchBytes / totalSw.Elapsed.TotalSeconds / 1024 / 1024;

            _log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _log($"[完成] 总用时: {FormatTimeSpan(totalSw.Elapsed)}");
            _log($"  📦 数据量: {FormatSize(processedBatchBytes)}");
            _log($"  ✅ 成功: {successCount}, ❌ 失败: {failCount}");
            _log($"  ⚡ 平均速度: {avgSpeed:F1} MB/s");
            _log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            UpdateStatus("刷机任务完成");
            UpdateProgress(100, 100);
        }

        /// <summary>
        /// 批量读取任务
        /// </summary>
        public async Task ExecuteReadTasksAsync(List<FlashPartitionInfo> tasks, string outputDirectory, CancellationToken ct)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            int total = tasks.Count;
            long totalBatchBytes = tasks.Sum(t => t.NumSectors * SectorSize);
            long processedBatchBytes = 0;

            UpdateProgress(0, totalBatchBytes);

            for (int i = 0; i < tasks.Count; i++)
            {
                if (ct.IsCancellationRequested) return;

                var task = tasks[i];
                string safeFileName = !string.IsNullOrWhiteSpace(task.Filename)
                    ? Path.GetFileName(task.Filename)
                    : $"{task.Name}.bin";

                string savePath = Path.Combine(outputDirectory, safeFileName);
                long taskBytes = task.NumSectors * SectorSize;

                UpdateStatus($"正在读取: {task.Name} ({i + 1}/{total})");
                TaskProgressChanged?.Invoke(i + 1, total);

                try
                {
                    var partInfo = new PartitionInfo
                    {
                        Name = task.Name,
                        StartSector = ParseStartSector(task.StartSector),
                        NumSectors = task.NumSectors,
                        Lun = int.Parse(task.Lun),
                        SectorSize = SectorSize
                    };

                    var sw = Stopwatch.StartNew();

                    bool success = await _strategy.ReadPartitionAsync(
                        Client, partInfo, savePath,
                        (c, t) => UpdateProgress(processedBatchBytes + c, totalBatchBytes),
                        ct, _log);

                    sw.Stop();

                    if (success)
                        _log($"[Success] {task.Name} -> {safeFileName}");
                    else
                        _log($"[Fail] 读取 {task.Name} 失败");
                }
                catch (Exception ex)
                {
                    _log($"[Error] {task.Name}: {ex.Message}");
                }

                processedBatchBytes += taskBytes;
            }

            UpdateStatus("批量读取完成");
            UpdateProgress(100, 100);
        }

        /// <summary>
        /// 批量擦除任务
        /// </summary>
        public async Task ExecuteEraseTasksAsync(List<FlashPartitionInfo> tasks, bool protectLun5, CancellationToken ct)
        {
            int total = tasks.Count;
            int current = 0;

            for (int i = 0; i < tasks.Count; i++)
            {
                if (ct.IsCancellationRequested) return;

                var task = tasks[i];
                UpdateStatus($"正在擦除: {task.Name} ({current + 1}/{total})");
                TaskProgressChanged?.Invoke(current + 1, total);

                if (protectLun5 && task.Lun == "5")
                {
                    _log($"[Skip] LUN5 保护: {task.Name}");
                    current++;
                    continue;
                }

                _log($"[Erase] 正在擦除 {task.Name}...");

                var partInfo = new PartitionInfo
                {
                    Name = task.Name,
                    StartSector = ParseStartSector(task.StartSector),
                    NumSectors = task.NumSectors,
                    Lun = int.Parse(task.Lun),
                    SectorSize = SectorSize
                };

                UpdateProgress(0, 100);
                if (await _strategy.ErasePartitionAsync(Client, partInfo, ct, _log))
                    _log($"[Success] {task.Name} 擦除成功");
                else
                    _log($"[Fail] {task.Name} 擦除失败");
                UpdateProgress(100, 100);

                current++;
            }

            UpdateStatus("擦除完成");
        }

        /// <summary>
        /// Super 分区直刷
        /// </summary>
        public async Task FlashSuperNoMergeAsync(
            string jsonPath,
            string imageSearchDir,
            bool protectLun5,
            CancellationToken ct)
        {
            _log("[流程] 开始 Super 分区无损直刷模式...");

            // 1. 读取设备 GPT
            _log("[1/4] 正在读取设备分区表 (GPT)...");

            var partitions = await GetPartitionsAsync(ct);
            var superPartition = partitions.FirstOrDefault(p =>
                p.Name.Equals("super", StringComparison.OrdinalIgnoreCase));

            if (superPartition == null)
            {
                _log("[错误] 设备分区表中未找到 'super' 分区！");
                return;
            }

            long superStartSector = superPartition.StartSector;
            _log($"[信息] Super 分区起始扇区: {superStartSector}");

            // 2. 计算 Super 内部布局
            _log("[2/4] 正在计算 Super 内部布局...");

            var superService = new SuperFlashService(Client, _log);
            var actions = await superService.PrepareDirectFlashActionsAsync(jsonPath, imageSearchDir);

            if (actions == null || actions.Count == 0)
            {
                _log("[错误] 布局计算失败或未找到有效分区");
                return;
            }

            // 3. 构造刷写任务列表
            _log($"[3/4] 生成刷写任务列表 ({actions.Count} 个子分区)...");

            var flashTasks = new List<FlashPartitionInfo>();

            foreach (var action in actions)
            {
                int deviceSectorSize = Client.SectorSize > 0 ? Client.SectorSize : 4096;
                long relativeOffsetInBytes = action.RelativeSectorOffset * 512;
                long relativeOffsetInDeviceSectors = relativeOffsetInBytes / deviceSectorSize;
                long finalAbsoluteSector = superStartSector + relativeOffsetInDeviceSectors;
                long numSectors = (action.SizeInBytes + deviceSectorSize - 1) / deviceSectorSize;

                var task = new FlashPartitionInfo(
                    "0",
                    action.PartitionName,
                    finalAbsoluteSector.ToString(),
                    numSectors,
                    action.FilePath,
                    0
                );

                flashTasks.Add(task);
                _log($"   -> {action.PartitionName.PadRight(15)} | AbsSector: {finalAbsoluteSector}");
            }

            // 4. 执行刷写
            _log("[4/4] 开始批量写入...");

            try
            {
                await ExecuteFlashTasksAsync(flashTasks, protectLun5, null, ct);
                _log("[完成] Super 分区直刷流程结束！");
            }
            finally
            {
                // 清理临时文件
                foreach (var action in actions)
                {
                    try
                    {
                        if (action.FilePath.StartsWith(Path.GetTempPath()))
                        {
                            if (File.Exists(action.FilePath))
                                File.Delete(action.FilePath);
                        }
                    }
                    catch { }
                }
            }
        }

        #region Helper Methods

        private void UpdateProgress(long current, long total)
        {
            ProgressChanged?.Invoke(current, total);
        }

        private void UpdateStatus(string msg)
        {
            StatusChanged?.Invoke(msg);
        }

        private static long ParseStartSector(string start)
        {
            if (string.IsNullOrWhiteSpace(start)) return 0;
            try
            {
                if (start.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt64(start, 16);
                if (long.TryParse(start, out long v)) return v;
            }
            catch { }
            return 0;
        }

        private static long GetRealImageSize(string filePath)
        {
            if (!File.Exists(filePath)) return 0;
            try
            {
                // 检查是否是 Sparse 格式
                if (SparseStream.IsSparseFile(filePath))
                {
                    using var sparse = SparseStream.Open(filePath);
                    return sparse.Length;
                }
                return new FileInfo(filePath).Length;
            }
            catch
            {
                return new FileInfo(filePath).Length;
            }
        }

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

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}时{ts.Minutes:D2}分{ts.Seconds:D2}秒";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}分{ts.Seconds:D2}秒";
            return $"{ts.Seconds}.{ts.Milliseconds / 100}秒";
        }

        #endregion
    }
}
