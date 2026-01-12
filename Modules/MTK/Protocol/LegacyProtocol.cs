using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.MTK.Models;

namespace tools.Modules.MTK.Protocol
{
    /// <summary>
    /// MTK Legacy DA 协议实现
    /// 用于旧版 DA (DAMode = Legacy)
    /// </summary>
    public class LegacyProtocol : IDisposable
    {
        private SerialPort? _port;
        private readonly object _lock = new();

        public event Action<string>? OnLog;
        public event Action<int, int>? OnProgress;

        // 存储信息
        public EmmcInfo? EmmcInfo { get; private set; }
        public NandInfo? NandInfo { get; private set; }
        public uint SectorSize { get; private set; } = 512;
        public ulong TotalSectors { get; private set; }

        #region 连接管理

        /// <summary>
        /// 设置串口
        /// </summary>
        public void SetPort(SerialPort port)
        {
            _port = port;
        }

        /// <summary>
        /// 同步 DA
        /// </summary>
        public bool Sync()
        {
            try
            {
                // 发送同步字节
                WriteByte(LegacyCmd.SYNC);

                // 等待响应
                byte response = ReadByte();
                if (response == LegacyCmd.ACK)
                {
                    Log("Legacy DA sync successful");
                    return true;
                }

                Log($"Legacy DA sync failed, response: 0x{response:X2}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Legacy DA sync error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 设备信息

        /// <summary>
        /// 获取存储信息
        /// </summary>
        public bool GetStorageInfo()
        {
            try
            {
                // 获取 FAT 信息
                WriteByte(LegacyCmd.GET_FAT_INFO_CMD);
                WriteUInt32BE(0); // 地址
                WriteUInt32BE(16); // 读取 16 个 DWORD

                uint[] info = new uint[16];
                for (int i = 0; i < 16; i++)
                {
                    info[i] = ReadUInt32BE();
                }

                byte ack = ReadByte();
                if (ack != LegacyCmd.ACK)
                {
                    Log("GetStorageInfo failed");
                    return false;
                }

                // 解析存储信息
                SectorSize = info[0];
                TotalSectors = ((ulong)info[2] << 32) | info[1];

                Log($"Storage: SectorSize={SectorSize}, TotalSectors={TotalSectors}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"GetStorageInfo error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Flash 操作

        /// <summary>
        /// 读取 Flash
        /// </summary>
        public async Task<byte[]?> ReadFlashAsync(
            ulong startSector,
            ulong sectorCount,
            CancellationToken ct = default)
        {
            try
            {
                Log($"Legacy Read: sector={startSector}, count={sectorCount}");

                // 发送读取命令
                WriteByte(LegacyCmd.READ_CMD);

                // 发送参数: 起始扇区 (8字节), 扇区数 (8字节)
                WriteUInt64BE(startSector);
                WriteUInt64BE(sectorCount);

                // 读取数据
                byte[] result = new byte[sectorCount * SectorSize];
                uint offset = 0;

                while (offset < result.Length && !ct.IsCancellationRequested)
                {
                    // 读取一个扇区
                    byte[] sector = ReadBytes((int)SectorSize);
                    Array.Copy(sector, 0, result, offset, sector.Length);
                    offset += (uint)sector.Length;

                    // 发送 ACK
                    WriteByte(LegacyCmd.ACK);

                    // 报告进度
                    OnProgress?.Invoke((int)(offset / SectorSize), (int)sectorCount);
                }

                // 最终确认
                byte finalAck = ReadByte();
                if (finalAck != LegacyCmd.ACK)
                {
                    Log($"Read final ACK failed: 0x{finalAck:X2}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Log($"ReadFlash error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入 Flash
        /// </summary>
        public async Task<bool> WriteFlashAsync(
            ulong startSector,
            byte[] data,
            CancellationToken ct = default)
        {
            try
            {
                ulong sectorCount = (ulong)((data.Length + SectorSize - 1) / SectorSize);
                Log($"Legacy Write: sector={startSector}, count={sectorCount}");

                // 发送写入命令
                WriteByte(LegacyCmd.WRITE_CMD);

                // 发送参数
                WriteUInt64BE(startSector);
                WriteUInt64BE(sectorCount);

                // 等待确认
                byte ack = ReadByte();
                if (ack != LegacyCmd.ACK)
                {
                    Log($"Write command not ACKed: 0x{ack:X2}");
                    return false;
                }

                // 发送数据
                uint offset = 0;
                while (offset < data.Length && !ct.IsCancellationRequested)
                {
                    int toSend = Math.Min((int)SectorSize, data.Length - (int)offset);
                    byte[] sector = new byte[SectorSize];
                    Array.Copy(data, offset, sector, 0, toSend);

                    WriteBytes(sector);
                    offset += (uint)sector.Length;

                    // 等待 ACK
                    ack = ReadByte();
                    if (ack != LegacyCmd.ACK && ack != LegacyCmd.CONT_CHAR)
                    {
                        Log($"Write sector ACK failed: 0x{ack:X2}");
                        return false;
                    }

                    // 报告进度
                    OnProgress?.Invoke((int)(offset / SectorSize), (int)sectorCount);
                }

                Log("Write completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"WriteFlash error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 擦除 Flash
        /// </summary>
        public async Task<bool> EraseFlashAsync(
            ulong startSector,
            ulong sectorCount,
            CancellationToken ct = default)
        {
            try
            {
                Log($"Legacy Erase: sector={startSector}, count={sectorCount}");

                // 发送擦除命令
                WriteByte(LegacyCmd.ERASE_CMD);

                // 发送参数
                WriteUInt64BE(startSector);
                WriteUInt64BE(sectorCount);

                // 等待完成
                byte ack = ReadByte(30000); // 擦除可能需要较长时间
                if (ack != LegacyCmd.ACK)
                {
                    Log($"Erase failed: 0x{ack:X2}");
                    return false;
                }

                Log("Erase completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"EraseFlash error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 格式化 Flash
        /// </summary>
        public async Task<bool> FormatFlashAsync(CancellationToken ct = default)
        {
            try
            {
                Log("Legacy Format starting...");

                // 发送格式化命令
                WriteByte(LegacyCmd.FORMAT_CMD);

                // 等待完成 (可能需要很长时间)
                byte ack = ReadByte(600000); // 10分钟超时
                if (ack != LegacyCmd.ACK)
                {
                    Log($"Format failed: 0x{ack:X2}");
                    return false;
                }

                Log("Format completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"FormatFlash error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 分区表操作

        /// <summary>
        /// 读取 PMT (Partition Map Table)
        /// </summary>
        public async Task<byte[]?> ReadPmtAsync(CancellationToken ct = default)
        {
            try
            {
                Log("Reading PMT...");

                WriteByte(LegacyCmd.SDMMC_READ_PMT_CMD);

                // 读取长度
                uint length = ReadUInt32BE();
                if (length == 0 || length > 0x100000)
                {
                    Log($"Invalid PMT length: {length}");
                    return null;
                }

                // 读取数据
                byte[] pmt = ReadBytes((int)length);

                // 确认
                byte ack = ReadByte();
                if (ack != LegacyCmd.ACK)
                {
                    Log($"PMT read not ACKed: 0x{ack:X2}");
                }

                Log($"PMT read: {length} bytes");
                return pmt;
            }
            catch (Exception ex)
            {
                Log($"ReadPmt error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入 PMT
        /// </summary>
        public async Task<bool> WritePmtAsync(byte[] pmt, CancellationToken ct = default)
        {
            try
            {
                Log($"Writing PMT: {pmt.Length} bytes");

                WriteByte(LegacyCmd.SDMMC_WRITE_PMT_CMD);
                WriteUInt32BE((uint)pmt.Length);
                WriteBytes(pmt);

                byte ack = ReadByte();
                if (ack != LegacyCmd.ACK)
                {
                    Log($"PMT write not ACKed: 0x{ack:X2}");
                    return false;
                }

                Log("PMT written successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"WritePmt error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 寄存器操作

        /// <summary>
        /// 读取 32 位寄存器
        /// </summary>
        public uint? ReadReg32(uint address)
        {
            try
            {
                WriteByte(LegacyCmd.READ_REG32_CMD);
                WriteUInt32BE(address);

                uint value = ReadUInt32BE();

                byte ack = ReadByte();
                if (ack != LegacyCmd.ACK)
                    return null;

                return value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 写入 32 位寄存器
        /// </summary>
        public bool WriteReg32(uint address, uint value)
        {
            try
            {
                WriteByte(LegacyCmd.WRITE_REG32_CMD);
                WriteUInt32BE(address);
                WriteUInt32BE(value);

                return ReadByte() == LegacyCmd.ACK;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 控制命令

        /// <summary>
        /// 完成操作
        /// </summary>
        public bool Finish()
        {
            try
            {
                WriteByte(LegacyCmd.FINISH_CMD);
                return ReadByte() == LegacyCmd.ACK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 设置传输速度
        /// </summary>
        public bool SetSpeed(uint speed)
        {
            try
            {
                WriteByte(LegacyCmd.SPEED_CMD);
                WriteUInt32BE(speed);
                return ReadByte() == LegacyCmd.ACK;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 底层 I/O

        private void WriteByte(byte value)
        {
            lock (_lock)
            {
                _port?.Write(new[] { value }, 0, 1);
            }
        }

        private void WriteBytes(byte[] data)
        {
            lock (_lock)
            {
                _port?.Write(data, 0, data.Length);
            }
        }

        private void WriteUInt32BE(uint value)
        {
            WriteBytes(new[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            });
        }

        private void WriteUInt64BE(ulong value)
        {
            WriteBytes(new[]
            {
                (byte)((value >> 56) & 0xFF),
                (byte)((value >> 48) & 0xFF),
                (byte)((value >> 40) & 0xFF),
                (byte)((value >> 32) & 0xFF),
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            });
        }

        private byte ReadByte(int timeout = 5000)
        {
            lock (_lock)
            {
                if (_port == null) throw new InvalidOperationException("Port not set");
                _port.ReadTimeout = timeout;
                return (byte)_port.ReadByte();
            }
        }

        private byte[] ReadBytes(int count, int timeout = 5000)
        {
            lock (_lock)
            {
                if (_port == null) throw new InvalidOperationException("Port not set");
                _port.ReadTimeout = timeout;
                byte[] buffer = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int r = _port.Read(buffer, read, count - read);
                    if (r == 0) throw new TimeoutException("Read timeout");
                    read += r;
                }
                return buffer;
            }
        }

        private uint ReadUInt32BE(int timeout = 5000)
        {
            byte[] data = ReadBytes(4, timeout);
            return (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[Legacy] {message}");
            System.Diagnostics.Debug.WriteLine($"[Legacy] {message}");
        }

        /// <summary>
        /// 发送自定义命令 (用于扩展功能)
        /// </summary>
        /// <param name="command">命令数据</param>
        /// <param name="responseLength">期望的响应长度</param>
        /// <returns>响应数据</returns>
        public byte[]? SendCustomCommand(byte[] command, int responseLength)
        {
            lock (_lock)
            {
                try
                {
                    if (_port == null || !_port.IsOpen)
                        return null;

                    // 清空缓冲区
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();

                    // 发送命令
                    _port.Write(command, 0, command.Length);

                    // 读取响应
                    if (responseLength > 0)
                    {
                        var response = new byte[responseLength];
                        int totalRead = 0;
                        int timeout = 5000;
                        int startTime = Environment.TickCount;

                        while (totalRead < responseLength)
                        {
                            if (Environment.TickCount - startTime > timeout)
                            {
                                Log($"Custom command timeout: read {totalRead}/{responseLength}");
                                return totalRead > 0 ? response[..totalRead] : null;
                            }

                            if (_port.BytesToRead > 0)
                            {
                                int read = _port.Read(response, totalRead, responseLength - totalRead);
                                totalRead += read;
                            }
                            else
                            {
                                Thread.Sleep(1);
                            }
                        }

                        return response;
                    }

                    return Array.Empty<byte>();
                }
                catch (Exception ex)
                {
                    Log($"SendCustomCommand error: {ex.Message}");
                    return null;
                }
            }
        }

        public void Dispose()
        {
            _port = null;
        }

        #endregion
    }
}
