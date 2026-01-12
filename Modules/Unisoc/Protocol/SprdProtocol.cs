// ============================================================================
// MultiFlash TOOL - SPRD Protocol Implementation
// SPRD (Spreadtrum/Unisoc) 下载协议实现
// SPRD (展讯/紫光展锐) ダウンロードプロトコル実装
// SPRD (스프레드트럼/유니SOC) 다운로드 프로토콜 구현
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Unisoc.Models;
using tools.Modules.Unisoc.Exploit;

namespace tools.Modules.Unisoc.Protocol
{
    /// <summary>
    /// SPRD Download Protocol / SPRD 下载协议 / SPRDダウンロードプロトコル
    /// 
    /// [EN] Spreadtrum/Unisoc device flash protocol implementation
    ///      Supports FDL1/FDL2 loading, partition read/write/erase
    /// 
    /// [中文] 展讯/紫光展锐设备刷机协议实现
    ///       支持 FDL1/FDL2 加载、分区读写擦除
    /// 
    /// [日本語] Spreadtrum/Unisocデバイスフラッシュプロトコル実装
    ///         FDL1/FDL2ロード、パーティション読み書き消去をサポート
    /// </summary>
    public class SprdProtocol : IDisposable
    {
        #region 协议常量

        // 帧定界符 (HDLC)
        private const byte HDLC_FLAG = 0x7E;
        private const byte HDLC_ESCAPE = 0x7D;
        private const byte HDLC_ESCAPE_XOR = 0x20;

        // BSL 命令类型 (Boot Stage Loader)
        private const byte CMD_CONNECT = 0x00;           // 连接握手
        private const byte CMD_DATA_START = 0x01;        // 数据传输开始
        private const byte CMD_DATA_MIDST = 0x02;        // 数据传输中
        private const byte CMD_DATA_END = 0x03;          // 数据传输结束
        private const byte CMD_DATA_EXEC = 0x04;         // 执行代码
        private const byte CMD_READ_FLASH = 0x05;        // 读取 Flash
        private const byte CMD_READ_CHIP_TYPE = 0x06;    // 读取芯片类型
        private const byte CMD_READ_NVITEM = 0x07;       // 读取 NV 项
        private const byte CMD_WRITE_NVITEM = 0x08;      // 写入 NV 项
        private const byte CMD_ERASE_FLASH = 0x09;       // 擦除 Flash
        private const byte CMD_REPARTITION = 0x0A;       // 重新分区
        private const byte CMD_READ_PARTITION = 0x0B;    // 读取分区
        private const byte CMD_WRITE_PARTITION = 0x0C;   // 写入分区
        private const byte CMD_ERASE_PARTITION = 0x0D;   // 擦除分区
        private const byte CMD_POWER_OFF = 0x0E;         // 关机
        private const byte CMD_RESET = 0x0F;             // 重启
        
        // 扩展命令
        private const byte CMD_GET_VERSION = 0x10;       // 获取版本
        private const byte CMD_READ_PARTITION_TABLE = 0x11; // 读取分区表
        private const byte CMD_CHANGE_BAUDRATE = 0x12;   // 改变波特率
        private const byte CMD_ENABLE_SECURE_BOOT = 0x13; // 启用安全启动
        private const byte CMD_READ_UID = 0x14;          // 读取 UID
        private const byte CMD_READ_SECTOR = 0x15;       // 读取扇区
        private const byte CMD_WRITE_SECTOR = 0x16;      // 写入扇区

        // 响应类型
        private const byte RSP_OK = 0x80;                // 成功
        private const byte RSP_ERROR = 0x81;             // 错误
        private const byte RSP_DATA = 0x82;              // 数据响应
        private const byte RSP_BUSY = 0x83;              // 繁忙
        private const byte RSP_VERIFY_ERROR = 0x84;      // 校验错误

        // 错误码
        private const ushort ERR_INVALID_CMD = 0x01;
        private const ushort ERR_INVALID_PARAM = 0x02;
        private const ushort ERR_FLASH_FAIL = 0x03;
        private const ushort ERR_CHECKSUM = 0x04;
        private const ushort ERR_TIMEOUT = 0x05;
        private const ushort ERR_PARTITION_NOT_FOUND = 0x06;

        #endregion

        private SerialPort? _port;
        private bool _disposed;
        private int _currentBaudRate = 115200;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _port?.IsOpen == true;

        /// <summary>
        /// 当前端口
        /// </summary>
        public string? CurrentPort { get; private set; }

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// 进度事件
        /// </summary>
        public event Action<long, long>? OnProgress;

        /// <summary>
        /// 打开串口
        /// </summary>
        public bool Open(string portName, int baudRate = 115200)
        {
            try
            {
                Close();

                _port = new SerialPort(portName)
                {
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };

                _port.Open();
                CurrentPort = portName;
                OnLog?.Invoke($"端口 {portName} 已打开 (波特率: {baudRate})");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"打开端口失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            if (_port != null)
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
                _port.Dispose();
                _port = null;
                CurrentPort = null;
            }
        }

        /// <summary>
        /// 发送握手命令
        /// </summary>
        public async Task<bool> HandshakeAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("发送握手命令...");

            // 发送连接命令
            var cmd = BuildCommand(CMD_CONNECT);
            await WriteAsync(cmd, ct);

            // 等待响应
            var response = await ReadResponseAsync(1000, ct);
            if (response != null && response.Length > 0 && response[0] == RSP_OK)
            {
                OnLog?.Invoke("握手成功");
                return true;
            }

            OnLog?.Invoke("握手失败");
            return false;
        }

        /// <summary>
        /// 发送 FDL 文件
        /// </summary>
        public async Task<bool> SendFdlAsync(byte[] fdlData, string address, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke($"发送 FDL 到地址 {address}...");

            try
            {
                // 解析地址
                uint addr = Convert.ToUInt32(address.Replace("0x", ""), 16);

                // 发送开始命令
                var startCmd = BuildDataStartCommand(addr, (uint)fdlData.Length);
                await WriteAsync(startCmd, ct);

                var response = await ReadResponseAsync(2000, ct);
                if (response == null || response.Length == 0 || response[0] != RSP_OK)
                {
                    OnLog?.Invoke("FDL 开始命令失败");
                    return false;
                }

                // 分块发送数据
                const int chunkSize = 4096;
                int offset = 0;
                while (offset < fdlData.Length)
                {
                    ct.ThrowIfCancellationRequested();

                    int remaining = fdlData.Length - offset;
                    int size = Math.Min(chunkSize, remaining);
                    var chunk = new byte[size];
                    Array.Copy(fdlData, offset, chunk, 0, size);

                    var dataCmd = BuildDataMidstCommand(chunk);
                    await WriteAsync(dataCmd, ct);

                    response = await ReadResponseAsync(2000, ct);
                    if (response == null || response.Length == 0 || response[0] != RSP_OK)
                    {
                        OnLog?.Invoke($"FDL 数据传输失败 (offset: {offset})");
                        return false;
                    }

                    offset += size;
                    OnProgress?.Invoke(offset, fdlData.Length);
                }

                // 发送结束命令
                var endCmd = BuildDataEndCommand();
                await WriteAsync(endCmd, ct);

                response = await ReadResponseAsync(2000, ct);
                if (response == null || response.Length == 0 || response[0] != RSP_OK)
                {
                    OnLog?.Invoke("FDL 结束命令失败");
                    return false;
                }

                // 发送执行命令
                var execCmd = BuildDataExecCommand();
                await WriteAsync(execCmd, ct);

                response = await ReadResponseAsync(5000, ct);
                if (response == null || response.Length == 0 || response[0] != RSP_OK)
                {
                    OnLog?.Invoke("FDL 执行命令失败");
                    return false;
                }

                OnLog?.Invoke("FDL 发送成功");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"FDL 发送异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取分区
        /// </summary>
        public async Task<byte[]?> ReadPartitionAsync(string partitionName, long offset, long size, CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke($"读取分区 {partitionName} (偏移: {offset}, 大小: {size})...");

            try
            {
                var result = new MemoryStream();
                const int chunkSize = 65536; // 64KB per read
                long remaining = size;
                long currentOffset = offset;

                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    int readSize = (int)Math.Min(chunkSize, remaining);

                    var cmd = BuildReadPartitionCommand(partitionName, currentOffset, readSize);
                    await WriteAsync(cmd, ct);

                    var response = await ReadResponseAsync(10000, ct);
                    if (response == null || response.Length < 2 || response[0] != RSP_DATA)
                    {
                        OnLog?.Invoke($"读取分区失败 (offset: {currentOffset})");
                        return null;
                    }

                    // 提取数据 (跳过响应头)
                    int dataLen = response.Length - 1;
                    result.Write(response, 1, dataLen);

                    currentOffset += readSize;
                    remaining -= readSize;
                    OnProgress?.Invoke(size - remaining, size);
                }

                OnLog?.Invoke($"分区读取完成, 大小: {result.Length}");
                return result.ToArray();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"读取分区异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(string partitionName, byte[] data, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke($"写入分区 {partitionName} (大小: {data.Length})...");

            try
            {
                const int chunkSize = 65536; // 64KB per write
                int offset = 0;

                while (offset < data.Length)
                {
                    ct.ThrowIfCancellationRequested();

                    int remaining = data.Length - offset;
                    int writeSize = Math.Min(chunkSize, remaining);

                    var chunk = new byte[writeSize];
                    Array.Copy(data, offset, chunk, 0, writeSize);

                    var cmd = BuildWritePartitionCommand(partitionName, offset, chunk);
                    await WriteAsync(cmd, ct);

                    var response = await ReadResponseAsync(10000, ct);
                    if (response == null || response.Length == 0 || response[0] != RSP_OK)
                    {
                        OnLog?.Invoke($"写入分区失败 (offset: {offset})");
                        return false;
                    }

                    offset += writeSize;
                    OnProgress?.Invoke(offset, data.Length);
                }

                OnLog?.Invoke("分区写入完成");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"写入分区异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke($"擦除分区 {partitionName}...");

            try
            {
                var cmd = BuildErasePartitionCommand(partitionName);
                await WriteAsync(cmd, ct);

                var response = await ReadResponseAsync(30000, ct);
                if (response != null && response.Length > 0 && response[0] == RSP_OK)
                {
                    OnLog?.Invoke("分区擦除完成");
                    return true;
                }

                OnLog?.Invoke("分区擦除失败");
                return false;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"擦除分区异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> ResetDeviceAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("重启设备...");

            var cmd = BuildCommand(CMD_RESET);
            await WriteAsync(cmd, ct);

            OnLog?.Invoke("重启命令已发送");
            return true;
        }

        /// <summary>
        /// 关机
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("关机...");

            var cmd = BuildCommand(CMD_POWER_OFF);
            await WriteAsync(cmd, ct);

            OnLog?.Invoke("关机命令已发送");
            return true;
        }

        /// <summary>
        /// 获取版本信息
        /// </summary>
        public async Task<string?> GetVersionAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke("获取版本信息...");

            var cmd = BuildCommand(CMD_GET_VERSION);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(3000, ct);
            if (response != null && response.Length > 1 && response[0] == RSP_DATA)
            {
                var version = Encoding.ASCII.GetString(response, 1, response.Length - 1).TrimEnd('\0');
                OnLog?.Invoke($"版本: {version}");
                return version;
            }

            return null;
        }

        /// <summary>
        /// 读取芯片类型
        /// </summary>
        public async Task<ChipInfo?> ReadChipTypeAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke("读取芯片类型...");

            var cmd = BuildCommand(CMD_READ_CHIP_TYPE);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(3000, ct);
            if (response != null && response.Length >= 5 && response[0] == RSP_DATA)
            {
                var chipId = BitConverter.ToUInt32(response, 1);
                var chipInfo = IdentifyChip(chipId);
                OnLog?.Invoke($"芯片: {chipInfo.Name} (ID: 0x{chipId:X8})");
                return chipInfo;
            }

            return null;
        }

        /// <summary>
        /// 读取 UID
        /// </summary>
        public async Task<byte[]?> ReadUidAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke("读取 UID...");

            var cmd = BuildCommand(CMD_READ_UID);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(3000, ct);
            if (response != null && response.Length > 1 && response[0] == RSP_DATA)
            {
                var uid = new byte[response.Length - 1];
                Array.Copy(response, 1, uid, 0, uid.Length);
                OnLog?.Invoke($"UID: {BitConverter.ToString(uid).Replace("-", "")}");
                return uid;
            }

            return null;
        }

        /// <summary>
        /// 读取分区表
        /// </summary>
        public async Task<List<PartitionEntry>?> ReadPartitionTableAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke("读取分区表...");

            var cmd = BuildCommand(CMD_READ_PARTITION_TABLE);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(5000, ct);
            if (response == null || response.Length < 2 || response[0] != RSP_DATA)
            {
                OnLog?.Invoke("读取分区表失败");
                return null;
            }

            var partitions = new List<PartitionEntry>();
            int offset = 1;

            while (offset < response.Length - 32)
            {
                // 每个分区条目: 32字节名称 + 8字节偏移 + 8字节大小
                var nameBytes = new byte[32];
                Array.Copy(response, offset, nameBytes, 0, 32);
                var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                
                if (string.IsNullOrEmpty(name)) break;

                var partOffset = BitConverter.ToInt64(response, offset + 32);
                var partSize = BitConverter.ToInt64(response, offset + 40);

                partitions.Add(new PartitionEntry
                {
                    Name = name,
                    Offset = partOffset,
                    Size = partSize
                });

                offset += 48;
            }

            OnLog?.Invoke($"找到 {partitions.Count} 个分区");
            foreach (var p in partitions)
            {
                OnLog?.Invoke($"  - {p.Name}: offset=0x{p.Offset:X}, size={FormatSize(p.Size)}");
            }

            return partitions;
        }

        /// <summary>
        /// 改变波特率
        /// </summary>
        public async Task<bool> ChangeBaudrateAsync(int newBaudRate, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke($"改变波特率到 {newBaudRate}...");

            var payload = BitConverter.GetBytes(newBaudRate);
            var cmd = BuildCommand(CMD_CHANGE_BAUDRATE, payload);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(2000, ct);
            if (response != null && response.Length > 0 && response[0] == RSP_OK)
            {
                // 等待设备切换
                await Task.Delay(100, ct);
                
                // 关闭并以新波特率重新打开
                var port = CurrentPort;
                Close();
                await Task.Delay(100, ct);
                
                if (Open(port!, newBaudRate))
                {
                    _currentBaudRate = newBaudRate;
                    OnLog?.Invoke($"波特率已切换到 {newBaudRate}");
                    return true;
                }
            }

            OnLog?.Invoke("波特率切换失败");
            return false;
        }

        /// <summary>
        /// 发送 FDL 并带 Exploit
        /// </summary>
        public async Task<bool> SendFdlWithExploitAsync(byte[] fdlData, string fdlAddress, 
            bool useExploit = false, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            // 检查是否使用 Exploit
            if (useExploit && RsaExploit.IsExploitSupported(fdlAddress))
            {
                OnLog?.Invoke($"🔓 使用 RSA Exploit (地址: {fdlAddress})");
                
                var exploitAddr = RsaExploit.GetExploitAddress(fdlAddress);
                var exploitData = RsaExploit.GetExploitPayload(exploitAddr!);
                
                if (exploitData != null)
                {
                    // 先发送 Exploit
                    OnLog?.Invoke("发送 Exploit Payload...");
                    var exploitResult = await SendFdlAsync(exploitData, exploitAddr!, ct);
                    if (!exploitResult)
                    {
                        OnLog?.Invoke("⚠️ Exploit 发送失败，尝试正常模式");
                    }
                    else
                    {
                        OnLog?.Invoke("✓ Exploit 注入成功");
                        await Task.Delay(200, ct);
                    }
                }
            }

            // 发送 FDL
            return await SendFdlAsync(fdlData, fdlAddress, ct);
        }

        /// <summary>
        /// 带重试的写入分区
        /// </summary>
        public async Task<bool> WritePartitionWithRetryAsync(string partitionName, byte[] data, 
            int maxRetries = 3, CancellationToken ct = default)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (retry > 0)
                {
                    OnLog?.Invoke($"重试 ({retry}/{maxRetries})...");
                    await Task.Delay(500, ct);
                }

                var result = await WritePartitionAsync(partitionName, data, ct);
                if (result) return true;
            }

            return false;
        }

        /// <summary>
        /// 批量写入分区
        /// </summary>
        public async Task<int> WritePartitionsAsync(Dictionary<string, byte[]> partitions, 
            CancellationToken ct = default)
        {
            int successCount = 0;
            int totalCount = partitions.Count;

            foreach (var (name, data) in partitions)
            {
                ct.ThrowIfCancellationRequested();
                
                OnLog?.Invoke($"写入分区 {name} ({successCount + 1}/{totalCount})...");
                
                var result = await WritePartitionWithRetryAsync(name, data, 3, ct);
                if (result)
                {
                    successCount++;
                    OnLog?.Invoke($"✓ {name} 写入成功");
                }
                else
                {
                    OnLog?.Invoke($"✗ {name} 写入失败");
                }
            }

            return successCount;
        }

        /// <summary>
        /// 读取 NV 项
        /// </summary>
        public async Task<byte[]?> ReadNvItemAsync(ushort nvId, CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke($"读取 NV 项 0x{nvId:X4}...");

            var payload = BitConverter.GetBytes(nvId);
            var cmd = BuildCommand(CMD_READ_NVITEM, payload);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(3000, ct);
            if (response != null && response.Length > 1 && response[0] == RSP_DATA)
            {
                var data = new byte[response.Length - 1];
                Array.Copy(response, 1, data, 0, data.Length);
                return data;
            }

            return null;
        }

        /// <summary>
        /// 写入 NV 项
        /// </summary>
        public async Task<bool> WriteNvItemAsync(ushort nvId, byte[] data, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke($"写入 NV 项 0x{nvId:X4}...");

            var payload = new byte[2 + data.Length];
            BitConverter.GetBytes(nvId).CopyTo(payload, 0);
            Array.Copy(data, 0, payload, 2, data.Length);

            var cmd = BuildCommand(CMD_WRITE_NVITEM, payload);
            await WriteAsync(cmd, ct);

            var response = await ReadResponseAsync(3000, ct);
            return response != null && response.Length > 0 && response[0] == RSP_OK;
        }

        #region 辅助方法

        private ChipInfo IdentifyChip(uint chipId)
        {
            // 常见芯片 ID 映射
            return chipId switch
            {
                0x7731 or 0x77310000 => new ChipInfo { Id = chipId, Name = "SC7731", Series = "SC" },
                0x7731E or 0x7731E000 => new ChipInfo { Id = chipId, Name = "SC7731E", Series = "SC" },
                0x9832 or 0x98320000 => new ChipInfo { Id = chipId, Name = "SC9832", Series = "SC" },
                0x9832E or 0x9832E000 => new ChipInfo { Id = chipId, Name = "SC9832E", Series = "SC" },
                0x9863 or 0x98630000 => new ChipInfo { Id = chipId, Name = "SC9863A", Series = "SC" },
                0x0606 or 0x06060000 => new ChipInfo { Id = chipId, Name = "T606", Series = "Tiger" },
                0x0610 or 0x06100000 => new ChipInfo { Id = chipId, Name = "T610", Series = "Tiger" },
                0x0618 or 0x06180000 => new ChipInfo { Id = chipId, Name = "T618", Series = "Tiger" },
                0x0700 or 0x07000000 => new ChipInfo { Id = chipId, Name = "T700", Series = "Tiger" },
                0x0760 or 0x07600000 => new ChipInfo { Id = chipId, Name = "T760", Series = "Tiger" },
                _ => new ChipInfo { Id = chipId, Name = $"Unknown (0x{chipId:X})", Series = "Unknown" }
            };
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

        #endregion

        #region 命令构建

        private byte[] BuildCommand(byte cmdType, byte[]? payload = null)
        {
            var ms = new MemoryStream();
            ms.WriteByte(HDLC_FLAG);
            
            // 写入命令类型 (需要转义)
            WriteEscaped(ms, cmdType);
            
            // 写入负载
            if (payload != null)
            {
                foreach (byte b in payload)
                {
                    WriteEscaped(ms, b);
                }
            }

            // 计算 CRC16
            var data = new byte[1 + (payload?.Length ?? 0)];
            data[0] = cmdType;
            if (payload != null)
                Array.Copy(payload, 0, data, 1, payload.Length);
            
            ushort crc = CalculateCrc16(data);
            WriteEscaped(ms, (byte)(crc & 0xFF));
            WriteEscaped(ms, (byte)((crc >> 8) & 0xFF));

            ms.WriteByte(HDLC_FLAG);
            return ms.ToArray();
        }

        private byte[] BuildDataStartCommand(uint address, uint size)
        {
            var payload = new byte[8];
            BitConverter.GetBytes(address).CopyTo(payload, 0);
            BitConverter.GetBytes(size).CopyTo(payload, 4);
            return BuildCommand(CMD_DATA_START, payload);
        }

        private byte[] BuildDataMidstCommand(byte[] data)
        {
            return BuildCommand(CMD_DATA_MIDST, data);
        }

        private byte[] BuildDataEndCommand()
        {
            return BuildCommand(CMD_DATA_END);
        }

        private byte[] BuildDataExecCommand()
        {
            return BuildCommand(CMD_DATA_EXEC);
        }

        private byte[] BuildReadPartitionCommand(string partitionName, long offset, int size)
        {
            var nameBytes = Encoding.ASCII.GetBytes(partitionName);
            var payload = new byte[nameBytes.Length + 1 + 8 + 4]; // name + null + offset + size
            Array.Copy(nameBytes, payload, nameBytes.Length);
            BitConverter.GetBytes(offset).CopyTo(payload, nameBytes.Length + 1);
            BitConverter.GetBytes(size).CopyTo(payload, nameBytes.Length + 1 + 8);
            return BuildCommand(CMD_READ_PARTITION, payload);
        }

        private byte[] BuildWritePartitionCommand(string partitionName, long offset, byte[] data)
        {
            var nameBytes = Encoding.ASCII.GetBytes(partitionName);
            var payload = new byte[nameBytes.Length + 1 + 8 + data.Length];
            Array.Copy(nameBytes, payload, nameBytes.Length);
            BitConverter.GetBytes(offset).CopyTo(payload, nameBytes.Length + 1);
            Array.Copy(data, 0, payload, nameBytes.Length + 1 + 8, data.Length);
            return BuildCommand(CMD_WRITE_PARTITION, payload);
        }

        private byte[] BuildErasePartitionCommand(string partitionName)
        {
            var nameBytes = Encoding.ASCII.GetBytes(partitionName + "\0");
            return BuildCommand(CMD_ERASE_PARTITION, nameBytes);
        }

        private void WriteEscaped(MemoryStream ms, byte b)
        {
            if (b == HDLC_FLAG || b == HDLC_ESCAPE)
            {
                ms.WriteByte(HDLC_ESCAPE);
                ms.WriteByte((byte)(b ^ HDLC_ESCAPE_XOR));
            }
            else
            {
                ms.WriteByte(b);
            }
        }

        private ushort CalculateCrc16(byte[] data)
        {
            ushort crc = 0;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ 0x8408);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        #endregion

        #region 通信

        private async Task WriteAsync(byte[] data, CancellationToken ct)
        {
            if (_port == null || !_port.IsOpen) return;

            await Task.Run(() =>
            {
                _port.Write(data, 0, data.Length);
            }, ct);
        }

        private async Task<byte[]?> ReadResponseAsync(int timeoutMs, CancellationToken ct)
        {
            if (_port == null || !_port.IsOpen) return null;

            return await Task.Run(() =>
            {
                try
                {
                    _port.ReadTimeout = timeoutMs;
                    var buffer = new List<byte>();
                    bool inFrame = false;
                    bool escaped = false;

                    while (true)
                    {
                        if (ct.IsCancellationRequested) return null;

                        int b = _port.ReadByte();
                        if (b == -1) break;

                        if (b == HDLC_FLAG)
                        {
                            if (inFrame && buffer.Count > 0)
                            {
                                // 帧结束，验证 CRC
                                if (buffer.Count >= 2)
                                {
                                    var result = buffer.Take(buffer.Count - 2).ToArray();
                                    return result;
                                }
                            }
                            inFrame = true;
                            buffer.Clear();
                            continue;
                        }

                        if (!inFrame) continue;

                        if (b == HDLC_ESCAPE)
                        {
                            escaped = true;
                            continue;
                        }

                        if (escaped)
                        {
                            buffer.Add((byte)(b ^ HDLC_ESCAPE_XOR));
                            escaped = false;
                        }
                        else
                        {
                            buffer.Add((byte)b);
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // 超时
                }
                catch (Exception)
                {
                    // 其他错误
                }

                return null;
            }, ct);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }

    /// <summary>
    /// 芯片信息
    /// </summary>
    public class ChipInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public string Series { get; set; } = "";
    }

    /// <summary>
    /// 分区条目
    /// </summary>
    public class PartitionEntry
    {
        public string Name { get; set; } = "";
        public long Offset { get; set; }
        public long Size { get; set; }
        public string SizeStr => FormatSize(Size);

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
    }
}
