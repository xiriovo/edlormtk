// ============================================================================
// MultiFlash TOOL - MediaTek Preloader Protocol
// 联发科 Preloader 协议 | MTK Preloaderプロトコル | MTK Preloader 프로토콜
// ============================================================================
// [EN] Low-level protocol for communicating with MTK Preloader/BROM
//      Handles DA loading, handshake, authentication, flash operations
// [中文] 与联发科 Preloader/BROM 通信的底层协议
//       处理 DA 加载、握手、认证、Flash 操作
// [日本語] MTK Preloader/BROMとの通信用低レベルプロトコル
//         DAロード、ハンドシェイク、認証、フラッシュ操作を処理
// [한국어] MTK Preloader/BROM과 통신하기 위한 저수준 프로토콜
//         DA 로딩, 핸드셰이크, 인증, 플래시 작업 처리
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.MTK.DA;
using tools.Modules.MTK.Authentication;

namespace tools.Modules.MTK.Protocol
{
    /// <summary>
    /// Target Config - Security State / 目标配置 - 安全状态 / ターゲット設定 / 대상 설정
    /// </summary>
    public class TargetConfig
    {
        public uint RawValue { get; set; }
        public bool SbcEnabled { get; set; }    // Secure Boot Check
        public bool SlaEnabled { get; set; }    // SLA 认证
        public bool DaaEnabled { get; set; }    // DAA 认证
        public bool SwJtagEnabled { get; set; } // JTAG
        public bool EppEnabled { get; set; }    // EPP 参数
        public bool CertRequired { get; set; }  // 需要证书
        public bool MemReadAuth { get; set; }   // 内存读取需认证
        public bool MemWriteAuth { get; set; }  // 内存写入需认证
        public bool CmdC8Blocked { get; set; }  // C8 命令被阻止
    }

    /// <summary>
    /// 设备信息
    /// </summary>
    public class DeviceInfo
    {
        public ushort HwCode { get; set; }
        public ushort HwSubCode { get; set; }
        public ushort HwVersion { get; set; }
        public ushort SwVersion { get; set; }
        public byte BlVersion { get; set; }
        public byte BromVersion { get; set; }
        public bool IsBrom { get; set; }
        public uint PlCap0 { get; set; }
        public uint PlCap1 { get; set; }
        public byte[] MeId { get; set; } = Array.Empty<byte>();
        public byte[] SocId { get; set; } = Array.Empty<byte>();
        public TargetConfig TargetConfig { get; set; } = new();

        public bool SupportsXFlash => (PlCap0 & 0x1) != 0 && BlVersion > 1;
    }

    /// <summary>
    /// MTK Preloader 协议实现
    /// </summary>
    public class PreloaderProtocol : IDisposable
    {
        private SerialPort? _port;
        private readonly object _lock = new();

        public event Action<string>? OnLog;
        public DeviceInfo DeviceInfo { get; } = new();
        public bool IsConnected => _port?.IsOpen == true;
        public SerialPort? Port => _port;

        #region 连接管理

        /// <summary>
        /// 打开串口并握手
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
        {
            try
            {
                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    ReadBufferSize = 65536,
                    WriteBufferSize = 65536
                };
                _port.Open();

                // 执行握手
                if (!await HandshakeAsync(ct))
                {
                    _port.Close();
                    return false;
                }

                // 获取设备信息
                await GetDeviceInfoAsync(ct);

                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 握手序列: 0xA0 -> 0x5F, 0x0A -> 0xF5, 0x50 -> 0xAF, 0x05 -> 0xFA
        /// 参考: mtkclient Port.py 和 SP Flash Tool 官方实现
        /// </summary>
        private async Task<bool> HandshakeAsync(CancellationToken ct)
        {
            byte[] sequence = { 0xA0, 0x0A, 0x50, 0x05 };
            
            // Preloader 模式需要先发送 0xA0 进行同步
            // (某些设备在 BROM 模式下不需要这个)
            bool isPreloaderMode = false;
            
            for (int retry = 0; retry < 100 && !ct.IsCancellationRequested; retry++)
            {
                int idx = 0;
                try
                {
                    // 清空接收缓冲区
                    if (_port?.BytesToRead > 0)
                    {
                        _port.DiscardInBuffer();
                    }
                    
                    // 对于 Preloader 模式，先发送 0xA0 进行同步 (来自 mtkclient)
                    if (retry > 0 && !isPreloaderMode)
                    {
                        WriteByte(0xA0);
                        await Task.Delay(5, ct);
                    }
                    
                    while (idx < sequence.Length)
                    {
                        WriteByte(sequence[idx]);
                        
                        // 使用适当的超时时间 (100-200ms)，某些设备响应较慢
                        byte response = ReadByte(150);

                        byte expected = (byte)(~sequence[idx] & 0xFF);
                        if (response == expected)
                        {
                            idx++;
                        }
                        else
                        {
                            // 收到意外响应，可能是 Preloader 模式
                            if (response == 0xA0 || response == sequence[idx])
                            {
                                isPreloaderMode = true;
                            }
                            idx = 0;
                        }
                    }

                    Log("Handshake successful");
                    return true;
                }
                catch (TimeoutException)
                {
                    // 握手超时，设备可能未准备好
                    await Task.Delay(50, ct);
                }
                catch (Exception ex)
                {
                    Log($"Handshake attempt {retry + 1} failed: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }

            Log("Handshake failed after 100 retries");
            return false;
        }
        
        /// <summary>
        /// USB 握手 (用于 USB 连接模式)
        /// 参考: mtkclient Port.py run_handshake
        /// </summary>
        public bool UsbHandshake(Func<byte[], bool> usbWrite, Func<int, byte[]> usbRead)
        {
            byte[] sequence = { 0xA0, 0x0A, 0x50, 0x05 };
            int idx = 0;
            
            try
            {
                while (idx < sequence.Length)
                {
                    // 发送单个字节
                    if (usbWrite(new[] { sequence[idx] }))
                    {
                        // 读取响应
                        byte[] response = usbRead(64);  // 读取最大包大小
                        if (response.Length > 0)
                        {
                            // 检查最后一个字节
                            byte lastByte = response[response.Length - 1];
                            byte expected = (byte)(~sequence[idx] & 0xFF);
                            
                            if (lastByte == expected)
                            {
                                idx++;
                            }
                            else
                            {
                                idx = 0;
                            }
                        }
                    }
                }
                
                Log("USB Handshake successful");
                return true;
            }
            catch (Exception ex)
            {
                Log($"USB Handshake failed: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _port?.Close();
            _port?.Dispose();
            _port = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        #endregion

        #region 设备信息

        private async Task GetDeviceInfoAsync(CancellationToken ct)
        {
            // GET_HW_CODE (0xFD)
            if (Echo(PreloaderCmd.GET_HW_CODE))
            {
                uint val = ReadUInt32BE();
                DeviceInfo.HwCode = (ushort)((val >> 16) & 0xFFFF);
                DeviceInfo.HwVersion = (ushort)(val & 0xFFFF);
                Log($"HW Code: 0x{DeviceInfo.HwCode:X4}, HW Ver: 0x{DeviceInfo.HwVersion:X4}");
            }

            // GET_BL_VER (0xFE) - 判断是 BROM 还是 Preloader
            if (Echo(PreloaderCmd.GET_BL_VER))
            {
                byte blVer = ReadByte();
                if (blVer == PreloaderCmd.GET_BL_VER)
                {
                    DeviceInfo.IsBrom = true;
                    Log("BROM mode detected");
                }
                else
                {
                    DeviceInfo.BlVersion = blVer;
                    DeviceInfo.IsBrom = blVer <= 2;
                    Log($"Bootloader Version: {DeviceInfo.BlVersion}, IsBrom: {DeviceInfo.IsBrom}");
                }
            }

            // GET_HW_SW_VER (0xFC)
            if (Echo(PreloaderCmd.GET_HW_SW_VER))
            {
                byte[] data = ReadBytes(8);
                DeviceInfo.HwSubCode = (ushort)((data[0] << 8) | data[1]);
                DeviceInfo.HwVersion = (ushort)((data[2] << 8) | data[3]);
                DeviceInfo.SwVersion = (ushort)((data[4] << 8) | data[5]);
                Log($"HW SubCode: 0x{DeviceInfo.HwSubCode:X4}, SW Ver: 0x{DeviceInfo.SwVersion:X4}");
            }

            // GET_TARGET_CONFIG (0xD8)
            if (Echo(PreloaderCmd.GET_TARGET_CONFIG))
            {
                byte[] data = ReadBytes(6);
                uint config = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                ushort status = (ushort)((data[4] << 8) | data[5]);

                DeviceInfo.TargetConfig = new TargetConfig
                {
                    RawValue = config,
                    SbcEnabled = (config & 0x1) != 0,
                    SlaEnabled = (config & 0x2) != 0,
                    DaaEnabled = (config & 0x4) != 0,
                    SwJtagEnabled = (config & 0x6) != 0,
                    EppEnabled = (config & 0x8) != 0,
                    CertRequired = (config & 0x10) != 0,
                    MemReadAuth = (config & 0x20) != 0,
                    MemWriteAuth = (config & 0x40) != 0,
                    CmdC8Blocked = (config & 0x80) != 0
                };

                Log($"Target Config: 0x{config:X8}");
                Log($"  SBC: {DeviceInfo.TargetConfig.SbcEnabled}, SLA: {DeviceInfo.TargetConfig.SlaEnabled}, DAA: {DeviceInfo.TargetConfig.DaaEnabled}");
            }

            // GET_PL_CAP (0xFB)
            try
            {
                byte[] capData = SendCmd(PreloaderCmd.GET_PL_CAP, 8);
                if (capData.Length >= 8)
                {
                    DeviceInfo.PlCap0 = (uint)((capData[0] << 24) | (capData[1] << 16) | (capData[2] << 8) | capData[3]);
                    DeviceInfo.PlCap1 = (uint)((capData[4] << 24) | (capData[5] << 16) | (capData[6] << 8) | capData[7]);
                    Log($"PL Cap: 0x{DeviceInfo.PlCap0:X8}, 0x{DeviceInfo.PlCap1:X8}");
                }
            }
            catch { /* 可选命令 */ }

            // GET_ME_ID (0xE1)
            try
            {
                if (Echo(PreloaderCmd.GET_ME_ID))
                {
                    int len = ReadInt32BE();
                    if (len > 0 && len <= 32)
                    {
                        DeviceInfo.MeId = ReadBytes(len);
                        ushort status = ReadUInt16BE();
                        Log($"ME ID: {BitConverter.ToString(DeviceInfo.MeId).Replace("-", "")}");
                    }
                }
            }
            catch { /* 可选命令 */ }

            // GET_SOC_ID (0xE7)
            try
            {
                if (Echo(PreloaderCmd.GET_SOC_ID))
                {
                    int len = ReadInt32BE();
                    if (len > 0 && len <= 64)
                    {
                        DeviceInfo.SocId = ReadBytes(len);
                        ushort status = ReadUInt16BE();
                        Log($"SOC ID: {BitConverter.ToString(DeviceInfo.SocId).Replace("-", "")}");
                    }
                }
            }
            catch { /* 可选命令 */ }
        }

        #endregion

        #region 内存操作

        /// <summary>
        /// 读取 16 位内存
        /// </summary>
        public ushort Read16(uint address, int count = 1)
        {
            if (!Echo(PreloaderCmd.READ16))
                throw new Exception("READ16 command failed");

            WriteUInt32BE(address);
            WriteUInt32BE((uint)count);
            ushort status = ReadUInt16BE();
            if (status != 0)
                throw new Exception($"READ16 status error: 0x{status:X4}");

            return ReadUInt16BE();
        }

        /// <summary>
        /// 读取 32 位内存
        /// </summary>
        public uint Read32(uint address, int count = 1)
        {
            if (!Echo(PreloaderCmd.READ32))
                throw new Exception("READ32 command failed");

            WriteUInt32BE(address);
            WriteUInt32BE((uint)count);
            ushort status = ReadUInt16BE();
            if (status != 0)
                throw new Exception($"READ32 status error: 0x{status:X4}");

            return ReadUInt32BE();
        }

        /// <summary>
        /// 写入 16 位内存
        /// </summary>
        public bool Write16(uint address, ushort value)
        {
            if (!Echo(PreloaderCmd.WRITE16))
                return false;

            WriteUInt32BE(address);
            WriteUInt32BE(1);
            ushort status = ReadUInt16BE();
            if (status != 0)
                return false;

            WriteUInt16BE(value);
            status = ReadUInt16BE();
            return status == 0;
        }

        /// <summary>
        /// 写入 32 位内存
        /// </summary>
        public bool Write32(uint address, uint value)
        {
            if (!Echo(PreloaderCmd.WRITE32))
                return false;

            WriteUInt32BE(address);
            WriteUInt32BE(1);
            ushort status = ReadUInt16BE();
            if (status != 0)
                return false;

            WriteUInt32BE(value);
            status = ReadUInt16BE();
            return status == 0;
        }

        /// <summary>
        /// 禁用看门狗定时器
        /// </summary>
        public bool DisableWatchdog(uint watchdogAddr, uint value = 0x22000000)
        {
            Log($"Disabling watchdog at 0x{watchdogAddr:X8}");
            return Write32(watchdogAddr, value);
        }

        #endregion

        #region DA 操作

        /// <summary>
        /// 发送 DA 到设备
        /// </summary>
        public bool SendDA(uint address, uint size, uint sigLen, byte[] data)
        {
            Log($"Sending DA to 0x{address:X8}, size=0x{size:X}, sigLen=0x{sigLen:X}");

            // 准备数据和校验和
            var (checksum, payload) = PrepareData(data, sigLen, size);

            // SEND_DA (0xD7)
            if (!Echo(PreloaderCmd.SEND_DA))
            {
                Log("SEND_DA command failed");
                return false;
            }

            // 发送地址 (big-endian)
            if (!Echo(ToBE32(address)))
            {
                Log("SEND_DA address failed");
                return false;
            }

            // 发送长度
            if (!Echo(ToBE32((uint)payload.Length)))
            {
                Log("SEND_DA size failed");
                return false;
            }

            // 发送签名长度
            if (!Echo(ToBE32(sigLen)))
            {
                Log("SEND_DA sig_len failed");
                return false;
            }

            // 检查状态
            ushort status = ReadUInt16BE();
            if (status == StatusCode.SLA_REQUIRED)
            {
                Log("SLA authentication required...");
                if (!HandleSLA())
                {
                    Log("SLA authentication failed");
                    return false;
                }
                status = 0;
            }

            if (status > 0xFF)
            {
                Log($"SEND_DA status error: 0x{status:X4}");
                return false;
            }

            // 上传数据
            if (!UploadData(payload, checksum))
            {
                Log("Upload DA data failed");
                return false;
            }

            Log("DA sent successfully");
            return true;
        }

        /// <summary>
        /// 跳转到 DA
        /// </summary>
        public bool JumpDA(uint address)
        {
            Log($"Jumping to 0x{address:X8}");

            if (!Echo(PreloaderCmd.JUMP_DA))
            {
                Log("JUMP_DA command failed");
                return false;
            }

            WriteBytes(ToBE32(address));

            uint resAddr = ReadUInt32BE();
            if (resAddr != address)
            {
                Log($"Jump address mismatch: expected 0x{address:X8}, got 0x{resAddr:X8}");
                return false;
            }

            ushort status = ReadUInt16BE();
            if (status != 0)
            {
                Log($"JUMP_DA status error: 0x{status:X4}");
                return false;
            }

            Log("Jump successful");
            return true;
        }

        /// <summary>
        /// 跳转到 DA (64位)
        /// </summary>
        public bool JumpDA64(uint address)
        {
            Log($"Jumping to 0x{address:X8} (64-bit)");

            if (!Echo(PreloaderCmd.JUMP_DA64))
            {
                Log("JUMP_DA64 command failed");
                return false;
            }

            WriteBytes(ToBE32(address));

            uint resAddr = ReadUInt32BE();
            if (resAddr != address)
            {
                Log($"Jump address mismatch");
                return false;
            }

            // 64位标志
            Echo(0x01);

            ushort status = ReadUInt16BE();
            if (status != 0)
            {
                Log($"JUMP_DA64 status error: 0x{status:X4}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// DA1 同步
        /// </summary>
        public bool SyncDA1()
        {
            try
            {
                // 等待 0xC0
                byte sync = ReadByte(5000);
                if (sync != 0xC0)
                {
                    Log($"DA1 sync failed, received: 0x{sync:X2}");
                    return false;
                }

                // 回复 0xC0
                WriteByte(0xC0);

                // 等待 0x0C
                byte resp = ReadByte(1000);
                if (resp != 0x0C)
                {
                    Log($"DA1 sync step 2 failed: 0x{resp:X2}");
                    return false;
                }

                // 发送 ACK
                WriteByte(PreloaderCmd.ACK);

                Log("DA1 sync completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"DA1 sync error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 认证操作

        /// <summary>
        /// 发送证书
        /// </summary>
        public bool SendCert(byte[] cert)
        {
            var (checksum, data) = PrepareData(cert, 0, (uint)cert.Length);

            if (!Echo(PreloaderCmd.SEND_CERT))
                return false;

            if (!Echo(ToBE32((uint)data.Length)))
                return false;

            ushort status = ReadUInt16BE();
            if (status > 0xFF)
            {
                Log($"Send cert error: 0x{status:X4}");
                return false;
            }

            return UploadData(data, checksum);
        }

        /// <summary>
        /// 发送认证数据
        /// </summary>
        public bool SendAuth(byte[] auth)
        {
            var (checksum, data) = PrepareData(auth, 0, (uint)auth.Length);

            if (!Echo(PreloaderCmd.SEND_AUTH))
                return false;

            WriteUInt32BE((uint)data.Length);
            uint rlen = ReadUInt32BE();
            if (rlen != data.Length)
                return false;

            ushort status = ReadUInt16BE();
            if (status == StatusCode.NO_AUTH_NEEDED)
            {
                Log("No auth needed");
                return true;
            }

            if (status > 0xFF)
            {
                Log($"Send auth error: 0x{status:X4}");
                return false;
            }

            // 上传数据
            int pos = 0;
            while (pos < data.Length)
            {
                int size = Math.Min(64, data.Length - pos);
                byte[] chunk = new byte[size];
                Array.Copy(data, pos, chunk, 0, size);
                WriteBytes(chunk);
                pos += size;
            }
            WriteBytes(Array.Empty<byte>());
            Thread.Sleep(35);

            ushort crc = ReadUInt16BE();
            status = ReadUInt16BE();
            return status <= 0xFF;
        }

        /// <summary>
        /// 处理 SLA 认证
        /// </summary>
        public bool HandleSLA()
        {
            Log("Handling SLA authentication...");

            // 获取 SLA 密钥
            var slaAuth = new SlaAuth();

            if (!Echo(PreloaderCmd.SLA))
                return false;

            ushort status = ReadUInt16BE();
            if (status == StatusCode.SLA_PASS)
            {
                Log("SLA already passed");
                return true;
            }

            if (status > 0xFF)
            {
                Log($"SLA status error: 0x{status:X4}");
                return false;
            }

            // 读取挑战长度和数据
            int challengeLen = ReadInt32BE();
            byte[] challenge = ReadBytes(challengeLen);

            Log($"SLA challenge received, length: {challengeLen}");

            // 尝试每个密钥
            foreach (var key in slaAuth.GetBromKeys())
            {
                try
                {
                    byte[] response = slaAuth.GenerateBromResponse(challenge, key);

                    WriteUInt32LE((uint)response.Length);
                    uint rlen = ReadUInt32BE();
                    if (rlen != response.Length)
                        continue;

                    status = ReadUInt16BE();
                    if (status > 0xFF)
                        continue;

                    WriteBytes(response);

                    uint result = ReadUInt32BE();
                    if (result <= 0xFF)
                    {
                        Log("SLA authentication successful");
                        return true;
                    }
                }
                catch
                {
                    continue;
                }
            }

            Log("SLA authentication failed - no matching key");
            return false;
        }

        /// <summary>
        /// 获取 SLA 挑战数据
        /// </summary>
        public byte[]? GetSlaChallenge()
        {
            try
            {
                Log("Getting SLA challenge...");

                if (!Echo(PreloaderCmd.SLA))
                    return null;

                ushort status = ReadUInt16BE();
                if (status == StatusCode.SLA_PASS)
                {
                    Log("SLA already passed");
                    return null;
                }

                if (status > 0xFF)
                {
                    Log($"SLA status error: 0x{status:X4}");
                    return null;
                }

                // 读取挑战长度和数据
                int challengeLen = ReadInt32BE();
                if (challengeLen <= 0 || challengeLen > 1024)
                {
                    Log($"Invalid challenge length: {challengeLen}");
                    return null;
                }

                byte[] challenge = ReadBytes(challengeLen);
                Log($"SLA challenge received, length: {challengeLen}");
                return challenge;
            }
            catch (Exception ex)
            {
                Log($"GetSlaChallenge error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 发送 SLA 响应
        /// </summary>
        public bool SendSlaResponse(byte[] response)
        {
            try
            {
                Log($"Sending SLA response, length: {response.Length}");

                WriteUInt32LE((uint)response.Length);
                uint rlen = ReadUInt32BE();
                if (rlen != response.Length)
                {
                    Log($"Response length mismatch: expected {response.Length}, got {rlen}");
                    return false;
                }

                ushort status = ReadUInt16BE();
                if (status > 0xFF)
                {
                    Log($"SLA response status error: 0x{status:X4}");
                    return false;
                }

                WriteBytes(response);

                uint result = ReadUInt32BE();
                if (result <= 0xFF)
                {
                    Log("SLA authentication successful");
                    return true;
                }
                else
                {
                    Log($"SLA authentication failed: 0x{result:X}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"SendSlaResponse error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 工具方法

        private (ushort checksum, byte[] data) PrepareData(byte[] data, uint sigLen, uint maxSize)
        {
            // 分离数据和签名
            int dataLen = (int)(data.Length - sigLen);
            byte[] payload;

            if (sigLen > 0 && dataLen > 0)
            {
                payload = new byte[Math.Min(maxSize, data.Length)];
                Array.Copy(data, 0, payload, 0, Math.Min(dataLen, payload.Length));
                if (sigLen > 0 && payload.Length > dataLen)
                {
                    int sigCopyLen = (int)Math.Min(sigLen, payload.Length - dataLen);
                    Array.Copy(data, dataLen, payload, dataLen, sigCopyLen);
                }
            }
            else
            {
                payload = new byte[Math.Min(maxSize > 0 ? maxSize : data.Length, data.Length)];
                Array.Copy(data, 0, payload, 0, payload.Length);
            }

            // 确保偶数长度
            if (payload.Length % 2 != 0)
            {
                byte[] newPayload = new byte[payload.Length + 1];
                Array.Copy(payload, newPayload, payload.Length);
                payload = newPayload;
            }

            // 计算校验和 (XOR)
            ushort checksum = 0;
            for (int i = 0; i < payload.Length; i += 2)
            {
                checksum ^= (ushort)((payload[i] << 8) | payload[i + 1]);
            }

            return (checksum, payload);
        }

        private bool UploadData(byte[] data, ushort expectedChecksum)
        {
            // 分块发送
            int pos = 0;
            while (pos < data.Length)
            {
                int size = Math.Min(64, data.Length - pos);
                byte[] chunk = new byte[size];
                Array.Copy(data, pos, chunk, 0, size);
                WriteBytes(chunk);
                pos += size;
            }

            // 发送空包结束
            WriteBytes(Array.Empty<byte>());
            
            // 动态延迟：基于数据大小计算，最小 35ms，最大 500ms
            int delayMs = Math.Max(35, Math.Min(500, data.Length / 1000 + 35));
            Thread.Sleep(delayMs);

            try
            {
                ushort checksum = ReadUInt16BE();
                ushort status = ReadUInt16BE();

                if (checksum != expectedChecksum && checksum != 0)
                {
                    Log($"Checksum mismatch: expected 0x{expectedChecksum:X4}, got 0x{checksum:X4}");
                }

                return status <= 0xFF;
            }
            catch (Exception ex)
            {
                Log($"Upload data response error: {ex.Message}");
                return false;
            }
        }

        private byte[] SendCmd(byte cmd, int responseLen)
        {
            if (!Echo(cmd))
                return Array.Empty<byte>();
            return ReadBytes(responseLen);
        }

        public bool Echo(byte cmd)
        {
            WriteByte(cmd);
            try
            {
                byte response = ReadByte();
                return response == cmd;
            }
            catch
            {
                return false;
            }
        }

        public bool Echo(byte[] data)
        {
            WriteBytes(data);
            try
            {
                byte[] response = ReadBytes(data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    if (response[i] != data[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 公开的读取字节方法
        /// </summary>
        public byte[] ReadBytes(int count)
        {
            return ReadBytes(count, 5000);
        }

        #endregion

        #region 底层 IO

        private void WriteByte(byte b)
        {
            lock (_lock)
            {
                _port?.Write(new byte[] { b }, 0, 1);
            }
        }

        private void WriteBytes(byte[] data)
        {
            lock (_lock)
            {
                if (data.Length > 0)
                    _port?.Write(data, 0, data.Length);
            }
        }

        private void WriteUInt16BE(ushort value)
        {
            WriteBytes(new byte[] { (byte)(value >> 8), (byte)(value & 0xFF) });
        }

        private void WriteUInt32BE(uint value)
        {
            WriteBytes(ToBE32(value));
        }

        private void WriteUInt32LE(uint value)
        {
            WriteBytes(BitConverter.GetBytes(value));
        }

        private byte ReadByte(int timeout = 1000)
        {
            lock (_lock)
            {
                if (_port == null) throw new InvalidOperationException("Port not open");
                _port.ReadTimeout = timeout;
                return (byte)_port.ReadByte();
            }
        }

        private byte[] ReadBytes(int count, int timeout = 1000)
        {
            lock (_lock)
            {
                if (_port == null) throw new InvalidOperationException("Port not open");
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

        private ushort ReadUInt16BE(int timeout = 1000)
        {
            byte[] data = ReadBytes(2, timeout);
            return (ushort)((data[0] << 8) | data[1]);
        }

        private uint ReadUInt32BE(int timeout = 1000)
        {
            byte[] data = ReadBytes(4, timeout);
            return (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        }

        private int ReadInt32BE(int timeout = 1000)
        {
            return (int)ReadUInt32BE(timeout);
        }

        private static byte[] ToBE32(uint value)
        {
            return new byte[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[Preloader] {message}");
            System.Diagnostics.Debug.WriteLine($"[Preloader] {message}");
        }

        #endregion

        #region 漏洞利用支持方法

        /// <summary>
        /// 写入原始数据
        /// </summary>
        public void Write(byte[] data)
        {
            WriteBytes(data);
        }

        /// <summary>
        /// 公开的读取原始字节
        /// </summary>
        public byte[] ReadRawBytes(int count)
        {
            return ReadBytes(count, 5000);
        }

        /// <summary>
        /// 读取 16 位值 (大端)
        /// </summary>
        public ushort ReadWord()
        {
            return ReadUInt16BE(5000);
        }

        /// <summary>
        /// 读取 32 位值 (大端)
        /// </summary>
        public uint ReadDword()
        {
            return ReadUInt32BE(5000);
        }

        /// <summary>
        /// BROM Register Access 命令 (0xDA)
        /// 用于 Kamakiri2 漏洞利用
        /// </summary>
        public byte[] BromRegisterAccess(uint address, int length, byte[]? writeData = null, bool checkResult = true)
        {
            // 发送命令
            WriteByte(PreloaderCmd.BROM_REGISTER_ACCESS);
            
            // 读取状态
            var status = ReadUInt16BE();
            if (status != 0 && checkResult)
                throw new Exception($"BROM_REGISTER_ACCESS init failed: 0x{status:X}");

            // 发送地址 (大端)
            WriteUInt32BE(address);

            // 发送长度 (大端)
            WriteUInt32BE((uint)length);

            // 读取回显
            var addrEcho = ReadUInt32BE();
            var lenEcho = ReadUInt32BE();

            if (writeData != null)
            {
                // 写入模式
                WriteBytes(writeData);
                
                // 读取状态
                status = ReadUInt16BE();
                if (status != 0 && checkResult)
                    throw new Exception($"BROM_REGISTER_ACCESS write failed: 0x{status:X}");
                
                return Array.Empty<byte>();
            }
            else
            {
                // 读取模式
                byte[] result = ReadBytes(length);
                
                // 读取状态
                status = ReadUInt16BE();
                if (status != 0 && checkResult)
                    throw new Exception($"BROM_REGISTER_ACCESS read failed: 0x{status:X}");
                
                return result;
            }
        }

        /// <summary>
        /// 比较字节数组
        /// </summary>
        private static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        #endregion

        #region 额外命令

        /// <summary>
        /// 跳转到分区
        /// </summary>
        public bool JumpToPartition(string partitionName)
        {
            if (!Echo(0x71)) // JUMP_TO_PARTITION
                return false;

            byte[] nameBytes = new byte[64];
            var nameData = System.Text.Encoding.UTF8.GetBytes(partitionName);
            Array.Copy(nameData, nameBytes, Math.Min(nameData.Length, 64));
            WriteBytes(nameBytes);

            ushort status = ReadUInt16BE();
            return status <= 0xFF;
        }

        /// <summary>
        /// 发送分区数据
        /// </summary>
        public bool SendPartitionData(string partitionName, byte[] data)
        {
            if (!Echo(0x70)) // SEND_PARTITION_DATA
                return false;

            // 计算校验和
            uint checksum = 0;
            for (int i = 0; i < data.Length / 4; i++)
            {
                checksum += BitConverter.ToUInt32(data, i * 4);
            }
            checksum &= 0xFFFFFFFF;

            byte[] nameBytes = new byte[64];
            var nameData = System.Text.Encoding.UTF8.GetBytes(partitionName);
            Array.Copy(nameData, nameBytes, Math.Min(nameData.Length, 64));
            WriteBytes(nameBytes);

            WriteUInt32BE((uint)data.Length);
            ushort status = ReadUInt16BE();
            if (status > 0xFF)
                return false;

            // 发送数据
            int pos = 0;
            int length = data.Length;
            while (length > 0)
            {
                int dsize = Math.Min(length, 0x200);
                WriteBytes(data.AsSpan(pos, dsize).ToArray());
                pos += dsize;
                length -= dsize;
            }

            // 发送校验和
            WriteUInt32BE(checksum);
            return true;
        }

        /// <summary>
        /// I2C 初始化
        /// </summary>
        public bool I2cInit()
        {
            return Echo(PreloaderCmd.I2C_INIT);
        }

        /// <summary>
        /// I2C 去初始化
        /// </summary>
        public bool I2cDeinit()
        {
            return Echo(PreloaderCmd.I2C_DEINIT);
        }

        /// <summary>
        /// I2C 写入
        /// </summary>
        public bool I2cWrite8(byte dev, byte reg, byte data)
        {
            if (!Echo(PreloaderCmd.I2C_WRITE8))
                return false;

            WriteByte(dev);
            WriteByte(reg);
            WriteByte(data);
            ushort status = ReadUInt16BE();
            return status == 0;
        }

        /// <summary>
        /// I2C 读取
        /// </summary>
        public byte? I2cRead8(byte dev, byte reg)
        {
            if (!Echo(PreloaderCmd.I2C_READ8))
                return null;

            WriteByte(dev);
            WriteByte(reg);
            byte data = ReadByte();
            ushort status = ReadUInt16BE();
            if (status == 0)
                return data;
            return null;
        }

        /// <summary>
        /// PWR 读取 16位
        /// </summary>
        public ushort? PwrRead16(uint addr)
        {
            if (!Echo(PreloaderCmd.PWR_READ16))
                return null;

            WriteUInt32BE(addr);
            ushort data = ReadUInt16BE();
            ushort status = ReadUInt16BE();
            if (status == 0)
                return data;
            return null;
        }

        /// <summary>
        /// PWR 写入 16位
        /// </summary>
        public bool PwrWrite16(uint addr, ushort value)
        {
            if (!Echo(PreloaderCmd.PWR_WRITE16))
                return false;

            WriteUInt32BE(addr);
            WriteUInt16BE(value);
            ushort status = ReadUInt16BE();
            return status == 0;
        }

        /// <summary>
        /// 获取 MAUI 固件版本
        /// </summary>
        public byte[]? GetMauiFwVersion()
        {
            if (!Echo(0xBF)) // GET_MAUI_FW_VER
                return null;

            // 读取版本数据
            byte[] version = ReadBytes(64);
            ushort status = ReadUInt16BE();
            if (status == 0)
                return version;
            return null;
        }

        /// <summary>
        /// 运行扩展命令 (0xC8)
        /// </summary>
        public bool RunExtCmd(byte cmd = 0xB1)
        {
            WriteByte(PreloaderCmd.CMD_C8);
            if (ReadByte() != PreloaderCmd.CMD_C8)
                return false;

            WriteByte(cmd);
            if (ReadByte() != cmd)
                return false;

            ReadByte();  // 额外读取
            ReadUInt16BE(); // 状态
            return true;
        }

        #endregion
    }
}
