using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// 设备策略接口 - 统一不同厂商设备的操作
    /// </summary>
    public interface IDeviceStrategy
    {
        /// <summary>
        /// 策略名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 执行设备认证
        /// </summary>
        Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default);

        /// <summary>
        /// 读取 GPT 分区表
        /// </summary>
        Task<List<PartitionInfo>> ReadGptAsync(
            FirehoseClient client,
            CancellationToken ct,
            Action<string> log);

        /// <summary>
        /// 读取分区数据到文件
        /// </summary>
        Task<bool> ReadPartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string savePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log);

        /// <summary>
        /// 从文件写入分区
        /// </summary>
        Task<bool> WritePartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            string imagePath,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log);

        /// <summary>
        /// 从内存写入分区 (用于流水线优化)
        /// </summary>
        Task<bool> WritePartitionFromMemoryAsync(
            FirehoseClient client,
            PartitionInfo part,
            byte[] data,
            Action<long, long>? progress,
            CancellationToken ct,
            Action<string> log);

        /// <summary>
        /// 擦除分区
        /// </summary>
        Task<bool> ErasePartitionAsync(
            FirehoseClient client,
            PartitionInfo part,
            CancellationToken ct,
            Action<string> log);

        /// <summary>
        /// 重启设备
        /// </summary>
        Task<bool> ResetAsync(
            FirehoseClient client,
            string mode,
            Action<string> log);
    }
}
