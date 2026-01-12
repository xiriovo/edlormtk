// ============================================================================
// MultiFlash TOOL - Unisoc Diagnostic Protocol
// 展讯诊断协议 | Unisoc診断プロトコル | Unisoc 진단 프로토콜
// ============================================================================
// [EN] Diagnostic channel protocol for Unisoc/Spreadtrum devices
//      Read/Write IMEI, AT commands, NV items, factory reset
// [中文] 展讯设备诊断通道协议
//       读写 IMEI、AT 命令、NV 项、恢复出厂设置
// [日本語] Unisoc/Spreadtrumデバイス用の診断チャネルプロトコル
//         IMEI読み書き、ATコマンド、NVアイテム、工場リセット
// [한국어] Unisoc/Spreadtrum 장치용 진단 채널 프로토콜
//         IMEI 읽기/쓰기, AT 명령, NV 항목, 공장 초기화
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

namespace tools.Modules.Unisoc.Diag
{
    /// <summary>
    /// Unisoc Diagnostic Protocol / 诊断通道协议 / 診断プロトコル / 진단 프로토콜
    /// [EN] For IMEI R/W, AT commands, NV items
    /// [中文] 用于读写 IMEI、AT 命令、NV 项
    /// </summary>
    public class DiagProtocol : IDisposable
    {
        // 帧定界符
        private const byte HDLC_FLAG = 0x7E;
        private const byte HDLC_ESCAPE = 0x7D;
        private const byte HDLC_ESCAPE_XOR = 0x20;

        // Diag 命令
        private const byte DIAG_CMD_VERSION = 0x00;
        private const byte DIAG_CMD_NV_READ = 0x07;
        private const byte DIAG_CMD_NV_WRITE = 0x08;
        private const byte DIAG_CMD_AT = 0x3E;
        private const byte DIAG_CMD_POWER_OFF = 0x0E;
        private const byte DIAG_CMD_RESET = 0x0F;

        // NV ID
        public const ushort NVID_IMEI1 = 0x0005;
        public const ushort NVID_IMEI2 = 0x0179;

        private SerialPort? _port;
        private bool _disposed;

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
        /// 打开诊断端口
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
                    ReadTimeout = 3000,
                    WriteTimeout = 3000
                };

                _port.Open();
                CurrentPort = portName;
                OnLog?.Invoke($"诊断端口 {portName} 已打开");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"打开诊断端口失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭端口
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
        /// 进入诊断模式
        /// </summary>
        public async Task<bool> EnterDiagModeAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("进入诊断模式...");

            // 发送诊断模式切换命令
            // 7E 00 00 00 00 08 00 FE 81 7E (正常诊断模式)
            var cmd = new byte[] { 0x7E, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0xFE, 0x81, 0x7E };
            await WriteAsync(cmd, ct);
            await Task.Delay(500, ct);

            OnLog?.Invoke("诊断模式命令已发送");
            return true;
        }

        /// <summary>
        /// 进入工厂测试模式
        /// </summary>
        public async Task<bool> EnterFactoryTestModeAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("进入工厂测试模式...");

            // 7E 00 00 00 00 08 00 FE 95 7E
            var cmd = new byte[] { 0x7E, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0xFE, 0x95, 0x7E };
            await WriteAsync(cmd, ct);
            await Task.Delay(500, ct);

            OnLog?.Invoke("工厂测试模式命令已发送");
            return true;
        }

        /// <summary>
        /// 读取 IMEI
        /// </summary>
        public async Task<string?> ReadImeiAsync(int imeiSlot = 1, CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            ushort nvId = imeiSlot == 1 ? NVID_IMEI1 : NVID_IMEI2;
            OnLog?.Invoke($"读取 IMEI{imeiSlot} (NV ID: 0x{nvId:X4})...");

            try
            {
                var cmd = BuildNvReadCommand(nvId);
                await WriteAsync(cmd, ct);

                var response = await ReadResponseAsync(3000, ct);
                if (response != null && response.Length > 8)
                {
                    // 解析 IMEI 数据
                    // 格式: 1A XX XX XX XX XX XX XX (8 bytes BCD)
                    string imei = ParseImeiFromBcd(response);
                    if (!string.IsNullOrEmpty(imei))
                    {
                        OnLog?.Invoke($"IMEI{imeiSlot}: {imei}");
                        return imei;
                    }
                }

                OnLog?.Invoke($"读取 IMEI{imeiSlot} 失败");
                return null;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"读取 IMEI 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入 IMEI
        /// </summary>
        public async Task<bool> WriteImeiAsync(string imei, int imeiSlot = 1, CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            if (string.IsNullOrEmpty(imei) || imei.Length != 15)
            {
                OnLog?.Invoke("无效的 IMEI 格式 (需要 15 位)");
                return false;
            }

            ushort nvId = imeiSlot == 1 ? NVID_IMEI1 : NVID_IMEI2;
            OnLog?.Invoke($"写入 IMEI{imeiSlot}: {imei}...");

            try
            {
                var bcdData = ConvertImeiToBcd(imei);
                var cmd = BuildNvWriteCommand(nvId, bcdData);
                await WriteAsync(cmd, ct);

                var response = await ReadResponseAsync(3000, ct);
                if (response != null && response.Length > 0)
                {
                    // 检查响应是否成功
                    OnLog?.Invoke($"IMEI{imeiSlot} 写入成功");
                    return true;
                }

                OnLog?.Invoke($"写入 IMEI{imeiSlot} 失败");
                return false;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"写入 IMEI 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送 AT 命令
        /// </summary>
        public async Task<string?> SendAtCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke($"发送 AT 命令: {command}");

            try
            {
                var cmd = BuildAtCommand(command);
                await WriteAsync(cmd, ct);

                var response = await ReadResponseAsync(5000, ct);
                if (response != null)
                {
                    string result = Encoding.ASCII.GetString(response).Trim('\0', '\r', '\n');
                    OnLog?.Invoke($"AT 响应: {result}");
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"AT 命令异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取软件版本
        /// </summary>
        public async Task<string?> GetSoftwareVersionAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            OnLog?.Invoke("获取软件版本...");

            try
            {
                var cmd = BuildCommand(DIAG_CMD_VERSION);
                await WriteAsync(cmd, ct);

                var response = await ReadResponseAsync(3000, ct);
                if (response != null && response.Length > 0)
                {
                    string version = Encoding.ASCII.GetString(response).Trim('\0');
                    OnLog?.Invoke($"软件版本: {version}");
                    return version;
                }

                return null;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"获取版本异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 恢复出厂设置
        /// </summary>
        public async Task<bool> FactoryResetAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("执行恢复出厂设置...");

            try
            {
                // AT+SPDIAG="AT+ETSRESET"
                var result = await SendAtCommandAsync("AT+SPDIAG=\"AT+ETSRESET\"", ct);
                return result != null;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"恢复出厂设置异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关机
        /// </summary>
        public async Task<bool> PowerOffAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("关机...");

            try
            {
                var cmd = BuildCommand(DIAG_CMD_POWER_OFF);
                await WriteAsync(cmd, ct);
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"关机异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重启
        /// </summary>
        public async Task<bool> RebootAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;

            OnLog?.Invoke("重启...");

            try
            {
                var cmd = BuildCommand(DIAG_CMD_RESET);
                await WriteAsync(cmd, ct);
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"重启异常: {ex.Message}");
                return false;
            }
        }

        #region 命令构建

        private byte[] BuildCommand(byte cmdType, byte[]? payload = null)
        {
            var ms = new MemoryStream();
            ms.WriteByte(HDLC_FLAG);

            // 写入命令类型
            WriteEscaped(ms, cmdType);

            // 写入负载
            if (payload != null)
            {
                foreach (byte b in payload)
                {
                    WriteEscaped(ms, b);
                }
            }

            // 计算 CRC
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

        private byte[] BuildNvReadCommand(ushort nvId)
        {
            var payload = new byte[2];
            payload[0] = (byte)(nvId & 0xFF);
            payload[1] = (byte)((nvId >> 8) & 0xFF);
            return BuildCommand(DIAG_CMD_NV_READ, payload);
        }

        private byte[] BuildNvWriteCommand(ushort nvId, byte[] data)
        {
            var payload = new byte[2 + data.Length];
            payload[0] = (byte)(nvId & 0xFF);
            payload[1] = (byte)((nvId >> 8) & 0xFF);
            Array.Copy(data, 0, payload, 2, data.Length);
            return BuildCommand(DIAG_CMD_NV_WRITE, payload);
        }

        private byte[] BuildAtCommand(string command)
        {
            var cmdBytes = Encoding.ASCII.GetBytes(command);
            return BuildCommand(DIAG_CMD_AT, cmdBytes);
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

        #region IMEI 转换

        private string ParseImeiFromBcd(byte[] data)
        {
            // IMEI BCD 格式: 1A D1 D2 D3 D4 D5 D6 D7 D8
            // 其中 1A = 0x1 + 0xA (0xA 是填充)
            // D1D2 = 第2-3位, D3D4 = 第4-5位...
            if (data.Length < 8) return "";

            var sb = new StringBuilder();

            // 第一个字节的高4位是第一位数字
            sb.Append((data[0] >> 4) & 0x0F);

            // 后续字节按 低4位+高4位 顺序
            for (int i = 1; i < 8; i++)
            {
                sb.Append(data[i] & 0x0F);
                sb.Append((data[i] >> 4) & 0x0F);
            }

            // 取前15位
            string result = sb.ToString();
            if (result.Length >= 15)
            {
                result = result.Substring(0, 15);
            }

            return result;
        }

        private byte[] ConvertImeiToBcd(string imei)
        {
            // IMEI 转 BCD 格式
            if (imei.Length != 15) return Array.Empty<byte>();

            var result = new byte[8];

            // 第一个字节: 第1位 + 0xA
            result[0] = (byte)((imei[0] - '0') | 0xA0);

            // 后续字节: 低4位 + 高4位
            for (int i = 0; i < 7; i++)
            {
                int low = imei[i * 2 + 1] - '0';
                int high = imei[i * 2 + 2] - '0';
                result[i + 1] = (byte)((high << 4) | low);
            }

            return result;
        }

        #endregion

        #region 通信

        private async Task WriteAsync(byte[] data, CancellationToken ct)
        {
            if (_port == null || !_port.IsOpen) return;

            await Task.Run(() =>
            {
                _port.Write(data, 0, data.Length);
                Thread.Sleep(15);
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

                    var deadline = DateTime.Now.AddMilliseconds(timeoutMs);

                    while (DateTime.Now < deadline)
                    {
                        if (ct.IsCancellationRequested) return null;

                        if (_port.BytesToRead == 0)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        int b = _port.ReadByte();
                        if (b == -1) break;

                        if (b == HDLC_FLAG)
                        {
                            if (inFrame && buffer.Count > 0)
                            {
                                // 帧结束
                                if (buffer.Count >= 2)
                                {
                                    return buffer.Take(buffer.Count - 2).ToArray();
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
                }
                catch (Exception)
                {
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
}
