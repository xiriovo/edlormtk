using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// 标准设备策略 - 无需特殊认证的高通设备
    /// </summary>
    public class StandardDeviceStrategy : IDeviceStrategy
    {
        public virtual string Name => "Standard Qualcomm";

        public virtual Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default)
        {
            // 标准设备无需额外认证
            return Task.FromResult(true);
        }

        public virtual async Task<List<PartitionInfo>> ReadGptAsync(
            FirehoseClient client,
            CancellationToken ct,
            Action<string> log)
        {
            var allPartitions = new List<PartitionInfo>();
            int maxLun = (client.SectorSize == 4096) ? 5 : 0;
            int sectorsToRead = (client.SectorSize == 4096) ? 34 : 34;  // 读取完整 GPT (34 扇区)
            int lunRead = 0;

            for (int lun = 0; lun <= maxLun; lun++)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    log($"[GPT] 读取 LUN{lun} (普通模式)...");
                    
                    // 使用普通读取模式 (小米/联想/标准设备)
                    var data = await client.ReadSectorsAsync(lun, 0, sectorsToRead, ct);

                    if (data != null && data.Length >= 512)
                    {
                        log($"[GPT] LUN{lun} 读取成功 ({data.Length} 字节)");
                        var parts = client.ParseGptPartitions(data, lun);
                        if (parts.Count > 0)
                        {
                            allPartitions.AddRange(parts);
                            lunRead++;
                            log($"[GPT] LUN{lun}: {parts.Count} 个分区");
                        }
                    }
                    else
                    {
                        log($"[GPT] LUN{lun} 无数据");
                    }
                }
                catch (Exception ex)
                {
                    log($"[GPT] LUN{lun} 读取异常: {ex.Message}");
                }

                await Task.Delay(10, ct);  // 减少延迟
            }

            log($"[GPT] 共读取 {lunRead} 个 LUN，{allPartitions.Count} 个分区");
            if (allPartitions.Count == 0)
                throw new Exception("未读取到任何有效分区信息 (请检查设备连接和认证状态)");

            return allPartitions;
        }

        public virtual async Task<bool> ReadPartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string savePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            return await client.ReadPartitionAsync(
                savePath,
                part.StartLba.ToString(),
                (long)part.Sectors,
                part.Lun.ToString(),
                progress, ct, part.Name);
        }

        public virtual async Task<bool> WritePartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string imagePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            return await client.FlashPartitionAsync(
                imagePath,
                part.StartLba.ToString(),
                (long)part.Sectors,
                part.Lun.ToString(),
                progress, ct, part.Name);
        }

        public virtual async Task<bool> WritePartitionFromMemoryAsync(
            FirehoseClient client,
            PartitionInfo part,
            byte[] data,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log)
        {
            if (data == null || data.Length == 0)
            {
                log?.Invoke($"[警告] {part.Name} 数据为空，无法写入");
                return false;
            }

            return await client.FlashPartitionFromMemoryAsync(
                data,
                part.StartLba.ToString(),
                (long)part.Sectors,
                part.Lun.ToString(),
                progress, ct, part.Name);
        }

        public virtual Task<bool> ErasePartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            CancellationToken ct,
            Action<string> log)
        {
            return Task.FromResult(client.ErasePartition(
                part.StartLba.ToString(),
                (long)part.Sectors,
                part.Lun.ToString()));
        }

        public virtual Task<bool> ResetAsync(
            FirehoseClient client,
            string mode,
            Action<string> log)
        {
            return Task.FromResult(client.Reset(mode));
        }
    }
}
