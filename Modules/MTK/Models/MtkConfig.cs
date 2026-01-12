using System;
using System.Collections.Generic;
using tools.Modules.MTK.DA;
using tools.Modules.MTK.Protocol;

namespace tools.Modules.MTK.Models
{
    /// <summary>
    /// MTK 运行时配置
    /// </summary>
    public class MtkConfig
    {
        // 设备信息
        public ushort HwCode { get; set; }
        public ushort HwSubCode { get; set; }
        public ushort HwVersion { get; set; }
        public ushort SwVersion { get; set; }

        // 芯片配置
        public ChipConfig ChipConfig { get; set; } = new();

        // 目标配置 (安全状态)
        public TargetConfig TargetConfig { get; set; } = new();

        // DA 配置
        public string? LoaderPath { get; set; }
        public byte[]? PreloaderData { get; set; }
        public Dictionary<uint, List<DAEntry>> DaSetup { get; set; } = new();
        public DAEntry? SelectedDaEntry { get; set; }
        public byte[]? Da1Data { get; set; }
        public byte[]? Da2Data { get; set; }
        public byte[]? EmiData { get; set; }

        // 设备 ID
        public byte[]? MeId { get; set; }
        public byte[]? SocId { get; set; }

        // 存储信息
        public StorageType StorageType { get; set; } = StorageType.Unknown;
        public EmmcInfo? EmmcInfo { get; set; }
        public UfsInfo? UfsInfo { get; set; }
        public NandInfo? NandInfo { get; set; }
        public NorInfo? NorInfo { get; set; }

        // 分区表
        public List<MtkPartition> Partitions { get; set; } = new();

        // 事件
        public event Action<string>? OnLog;
        public event Action<int, int>? OnProgress;
        public event Action<string>? OnStateChanged;

        /// <summary>
        /// 根据 HwCode 初始化芯片配置
        /// </summary>
        public void InitHwCode(ushort hwCode)
        {
            HwCode = hwCode;
            ChipConfig = ChipDatabase.GetConfig(hwCode) ?? new ChipConfig { HwCode = hwCode };
        }

        /// <summary>
        /// 是否需要认证
        /// </summary>
        public bool NeedsAuth => TargetConfig.SlaEnabled || TargetConfig.DaaEnabled;

        /// <summary>
        /// 是否启用了安全启动
        /// </summary>
        public bool HasSecureBoot => TargetConfig.SbcEnabled;

        public void Log(string msg) => OnLog?.Invoke(msg);
        public void ReportProgress(int current, int total) => OnProgress?.Invoke(current, total);
        public void ReportState(string state) => OnStateChanged?.Invoke(state);
    }

    /// <summary>
    /// 存储类型
    /// </summary>
    public enum StorageType
    {
        Unknown,
        Emmc,
        Ufs,
        Nand,
        Nor
    }
}
