using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;
using tools.Modules.Qualcomm.SuperDef;

namespace tools.Modules.Qualcomm
{
    /// <summary>
    /// 高通刷机服务 - 整合 Sahara 和 Firehose 协议
    /// </summary>
    public class QualcommService : IDisposable
    {
        private SerialPortManager? _portManager;
        private SaharaClient? _sahara;
        private FirehoseClient? _firehose;
        private SuperFlasher? _superFlasher;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly object _lock = new();

        /// <summary>
        /// 日志回调
        /// </summary>
        public Action<string>? OnLog { get; set; }

        /// <summary>
        /// 进度回调 (当前, 总计)
        /// </summary>
        public Action<long, long>? OnProgress { get; set; }

        /// <summary>
        /// 设备状态变化回调
        /// </summary>
        public Action<string>? OnStatusChanged { get; set; }

        /// <summary>
        /// 当前串口名
        /// </summary>
        public string? CurrentPort => _portManager?.PortName;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _portManager?.IsOpen ?? false;

        /// <summary>
        /// 存储类型 (ufs/emmc)
        /// </summary>
        public string StorageType { get; set; } = "ufs";

        /// <summary>
        /// 扇区大小
        /// </summary>
        public int SectorSize { get; set; } = 4096;

        /// <summary>
        /// 连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, string loaderPath, CancellationToken ct = default)
        {
            lock (_lock)
            {
                // 清理之前的连接
                Disconnect();

                _portManager = new SerialPortManager
                {
                    BaudRate = 115200,
                    ReadTimeout = 10000,
                    WriteTimeout = 10000
                };
            }

            try
            {
                OnLog?.Invoke($"[高通] 连接端口: {portName}");
                OnStatusChanged?.Invoke("正在连接...");

                // 打开串口 (⚠️ 不清空缓冲区，保留设备 Hello 包)
                if (!await _portManager.OpenAsync(portName, 3, discardBuffer: false, ct))
                {
                    OnLog?.Invoke("[高通] ❌ 无法打开端口");
                    OnStatusChanged?.Invoke("连接失败");
                    return false;
                }

                OnLog?.Invoke("[高通] ✅ 端口已打开");

                // Sahara 握手
                _sahara = new SaharaClient(_portManager, OnLog);
                if (!await _sahara.HandshakeAndUploadAsync(loaderPath, ct))
                {
                    OnLog?.Invoke("[高通] ❌ Sahara 握手失败");
                    OnStatusChanged?.Invoke("握手失败");
                    return false;
                }

                // 等待设备进入 Firehose 模式
                await Task.Delay(1000, ct);

                // 初始化 Firehose
                _firehose = new FirehoseClient(_portManager, OnLog, OnProgress);

                if (!await _firehose.ConfigureAsync(StorageType, null, 0, ct))
                {
                    OnLog?.Invoke("[高通] ⚠️ Firehose 配置失败，尝试继续...");
                }

                OnStatusChanged?.Invoke("已连接");
                OnLog?.Invoke("[高通] ✅ 设备连接成功");
                return true;
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("[高通] 操作已取消");
                OnStatusChanged?.Invoke("已取消");
                Disconnect();
                throw;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[高通] ❌ 连接失败: {ex.Message}");
                OnStatusChanged?.Invoke("连接错误");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _firehose?.Dispose();
                _firehose = null;

                _sahara?.Dispose();
                _sahara = null;

                _portManager?.Close();
                _portManager?.Dispose();
                _portManager = null;

                OnStatusChanged?.Invoke("未连接");
            }
        }

        /// <summary>
        /// 读取分区表
        /// </summary>
        public async Task<List<PartitionInfo>> ReadGptAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            return await _firehose!.ReadGptPartitionsAsync(useVipMode: false, ct: ct) ?? new List<PartitionInfo>();
        }

        /// <summary>
        /// 读取所有 LUN 的分区表
        /// </summary>
        public async Task<List<PartitionInfo>> ReadAllGptAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            return await _firehose!.ReadGptPartitionsAsync(useVipMode: false, ct: ct) ?? new List<PartitionInfo>();
        }

        /// <summary>
        /// 读取分区到文件
        /// </summary>
        public async Task<bool> ReadPartitionAsync(PartitionInfo partition, string savePath, CancellationToken ct = default)
        {
            EnsureConnected();

            OnStatusChanged?.Invoke($"正在读取: {partition.Name}");
            var result = await _firehose!.ReadPartitionAsync(partition, savePath, ct);
            OnStatusChanged?.Invoke(result ? "读取完成" : "读取失败");

            return result;
        }

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(PartitionInfo partition, string imagePath, CancellationToken ct = default)
        {
            EnsureConnected();

            if (!File.Exists(imagePath))
            {
                OnLog?.Invoke($"[高通] ❌ 文件不存在: {imagePath}");
                return false;
            }

            OnStatusChanged?.Invoke($"正在写入: {partition.Name}");
            var result = await _firehose!.WritePartitionAsync(partition, imagePath, false, ct);
            OnStatusChanged?.Invoke(result ? "写入完成" : "写入失败");

            return result;
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(PartitionInfo partition, CancellationToken ct = default)
        {
            EnsureConnected();

            OnStatusChanged?.Invoke($"正在擦除: {partition.Name}");
            var result = await _firehose!.ErasePartitionAsync(partition, ct);
            OnStatusChanged?.Invoke(result ? "擦除完成" : "擦除失败");

            return result;
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(string mode = "reset", CancellationToken ct = default)
        {
            EnsureConnected();

            OnLog?.Invoke($"[高通] 重启设备: {mode}");
            OnStatusChanged?.Invoke("正在重启...");

            var result = await _firehose!.ResetAsync(mode, ct);
            
            // 重启后断开连接
            Disconnect();

            return result;
        }

        #region Super直刷

        /// <summary>
        /// Super刷写进度回调
        /// </summary>
        public Action<SuperFlashProgress>? OnSuperProgress { get; set; }

        /// <summary>
        /// 检查固件是否支持Super Meta模式
        /// </summary>
        public bool IsSuperMetaSupported(string firmwareDir, out string? nvId)
        {
            nvId = null;
            if (_firehose == null) return false;

            _superFlasher ??= new SuperFlasher(_firehose, OnLog, OnSuperProgress);
            return _superFlasher.IsSuperMetaSupported(firmwareDir, out nvId);
        }

        /// <summary>
        /// 获取Super分区信息摘要
        /// </summary>
        public string? GetSuperSummary(string firmwareDir, string? nvId = null)
        {
            if (_firehose == null) return null;

            _superFlasher ??= new SuperFlasher(_firehose, OnLog, OnSuperProgress);
            return _superFlasher.GetSuperSummary(firmwareDir, nvId);
        }

        /// <summary>
        /// 使用Super Meta模式刷写固件
        /// </summary>
        /// <param name="firmwareDir">固件目录 (包含META/super_def.json)</param>
        /// <param name="nvId">NV ID (如 10010111), 可选</param>
        /// <param name="flashSlotB">是否同时刷写B槽位</param>
        /// <param name="ct">取消令牌</param>
        public async Task<SuperFlashResult> FlashSuperAsync(
            string firmwareDir,
            string? nvId = null,
            bool flashSlotB = false,
            CancellationToken ct = default)
        {
            EnsureConnected();

            OnStatusChanged?.Invoke("Super直刷模式");
            OnLog?.Invoke("[高通] 🚀 启动Super直刷模式...");

            _superFlasher ??= new SuperFlasher(_firehose!, OnLog, OnSuperProgress);
            return await _superFlasher.FlashSuperAsync(firmwareDir, nvId, flashSlotB, ct);
        }

        /// <summary>
        /// 智能刷写 - 自动检测并选择最佳刷写模式
        /// </summary>
        /// <param name="firmwareDir">固件目录</param>
        /// <param name="useSuperMeta">是否强制使用Super Meta模式</param>
        /// <param name="ct">取消令牌</param>
        public async Task<bool> SmartFlashAsync(
            string firmwareDir,
            bool useSuperMeta = true,
            CancellationToken ct = default)
        {
            EnsureConnected();

            // 检查是否支持Super Meta模式
            if (useSuperMeta && IsSuperMetaSupported(firmwareDir, out var nvId))
            {
                OnLog?.Invoke($"[高通] 📦 检测到Super Meta支持 (NV={nvId})");
                var result = await FlashSuperAsync(firmwareDir, nvId, false, ct);
                return result.Success;
            }

            // 回退到传统模式
            OnLog?.Invoke("[高通] 使用传统刷写模式...");
            // TODO: 实现传统刷写逻辑
            return false;
        }

        #endregion

        /// <summary>
        /// 停止当前操作
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// 创建取消令牌
        /// </summary>
        public CancellationToken CreateCancellationToken()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        private void EnsureConnected()
        {
            if (_firehose == null || _portManager?.IsOpen != true)
            {
                throw new InvalidOperationException("设备未连接");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
