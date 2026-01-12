// ============================================================================
// MultiFlash TOOL - Qualcomm Sahara Protocol
// 高通 Sahara 协议 | Qualcomm Saharaプロトコル | 퀄컴 Sahara 프로토콜
// ============================================================================
// [EN] Sahara is the first-stage bootloader protocol for Qualcomm devices
//      Used to transfer programmer/loader images to device RAM
// [中文] Sahara 是高通设备的第一阶段引导加载程序协议
//       用于将 Programmer/Loader 镜像传输到设备 RAM
// [日本語] Saharaは、Qualcommデバイスの第1ステージブートローダープロトコルです
//         プログラマー/ローダーイメージをデバイスRAMに転送するために使用
// [한국어] Sahara는 퀄컴 장치의 1단계 부트로더 프로토콜입니다
//         프로그래머/로더 이미지를 장치 RAM으로 전송하는 데 사용
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm
{
    #region 协议枚举定义 / Protocol Enumerations / プロトコル列挙型 / 프로토콜 열거형

    /// <summary>
    /// Sahara Command ID / Sahara 命令 ID / Saharaコマンド ID / Sahara 명령 ID
    /// </summary>
    public enum SaharaCommand : uint
    {
        Hello = 0x01,
        HelloResponse = 0x02,
        ReadData = 0x03,            // 32位读取 (老设备)
        EndImageTransfer = 0x04,
        Done = 0x05,
        DoneResponse = 0x06,
        Reset = 0x07,               // 硬重置 (重启设备)
        ResetResponse = 0x08,
        MemoryDebug = 0x09,
        MemoryRead = 0x0A,
        CommandReady = 0x0B,        // 命令模式就绪
        SwitchMode = 0x0C,          // 切换模式
        Execute = 0x0D,             // 执行命令
        ExecuteData = 0x0E,         // 命令数据响应
        ExecuteResponse = 0x0F,     // 命令响应确认
        MemoryDebug64 = 0x10,
        MemoryRead64 = 0x11,
        ReadData64 = 0x12,          // 64位读取 (新设备)
        ResetStateMachine = 0x13    // 状态机重置 (软重置)
    }

    /// <summary>
    /// Sahara 模式
    /// </summary>
    public enum SaharaMode : uint
    {
        ImageTransferPending = 0x0,
        ImageTransferComplete = 0x1,
        MemoryDebug = 0x2,
        Command = 0x3               // 命令模式 (读取信息)
    }

    /// <summary>
    /// Sahara 执行命令 ID
    /// </summary>
    public enum SaharaExecCommand : uint
    {
        SerialNumRead = 0x01,       // 序列号
        MsmHwIdRead = 0x02,         // HWID (仅 V1/V2)
        OemPkHashRead = 0x03,       // PK Hash
        SblInfoRead = 0x06,         // SBL 信息 (V3)
        SblSwVersion = 0x07,        // SBL 版本 (V1/V2)
        PblSwVersion = 0x08,        // PBL 版本
        ChipIdV3Read = 0x0A,        // [关键] V3 芯片信息 (包含 HWID)
        SerialNumRead64 = 0x14      // 64位序列号
    }

    /// <summary>
    /// Sahara 状态码
    /// </summary>
    public enum SaharaStatus : uint
    {
        Success = 0x00,
        InvalidCommand = 0x01,
        ProtocolMismatch = 0x02,
        InvalidTargetProtocol = 0x03,
        InvalidHostProtocol = 0x04,
        InvalidPacketSize = 0x05,
        UnexpectedImageId = 0x06,
        InvalidHeaderSize = 0x07,
        InvalidDataSize = 0x08,
        InvalidImageType = 0x09,
        InvalidTransmitLength = 0x0A,
        InvalidReceiveLength = 0x0B,
        GeneralTransmitReceiveError = 0x0C,
        ReadDataError = 0x0D,
        UnsupportedNumProgramHeaders = 0x0E,
        InvalidProgramHeaderSize = 0x0F,
        MultipleSharedSegments = 0x10,
        UninitializedProgramHeaderLocation = 0x11,
        InvalidDestAddress = 0x12,
        InvalidImageHeaderDataSize = 0x13,
        InvalidElfHeader = 0x14,
        UnknownHostError = 0x15,
        ReceiveTimeout = 0x16,
        TransmitTimeout = 0x17,
        InvalidHostMode = 0x18,
        InvalidMemoryRead = 0x19,
        InvalidDataSizeRequest = 0x1A,
        MemoryDebugNotSupported = 0x1B,
        InvalidModeSwitch = 0x1C,
        CommandExecuteFailure = 0x1D,
        ExecuteCommandInvalidParam = 0x1E,
        AccessDenied = 0x1F,
        InvalidClientCommand = 0x20,
        HashTableAuthFailure = 0x21,    // Loader 签名不匹配
        HashVerificationFailure = 0x22, // 镜像被篡改
        HashTableNotFound = 0x23,       // 镜像未签名
        MaxErrors = 0x29
    }

    #endregion

    #region 协议结构体

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaHeader
    {
        public uint Command;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaHelloPacket
    {
        public uint Command;
        public uint Length;
        public uint Version;
        public uint VersionSupported;
        public uint MaxCommandPacketSize;
        public uint Mode;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaHelloResponse
    {
        public uint Command;
        public uint Length;
        public uint Version;
        public uint VersionSupported;
        public uint Status;
        public uint Mode;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaReadData
    {
        public uint Command;
        public uint Length;
        public uint ImageId;
        public uint DataOffset;
        public uint DataLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaReadData64
    {
        public uint Command;
        public uint Length;
        public ulong ImageId;
        public ulong DataOffset;
        public ulong DataLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaDonePacket
    {
        public uint Command;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaDoneResponse
    {
        public uint Command;
        public uint Length;
        public uint ImageTransferStatus;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaEndImageTransfer
    {
        public uint Command;
        public uint Length;
        public uint ImageId;
        public uint Status;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaSwitchMode
    {
        public uint Command;
        public uint Length;
        public uint Mode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaharaExecute
    {
        public uint Command;
        public uint Length;
        public uint ClientCommand;
    }

    #endregion

    /// <summary>
    /// Sahara 状态辅助类
    /// </summary>
    public static class SaharaStatusHelper
    {
        public static string GetErrorMessage(SaharaStatus status)
        {
            return status switch
            {
                SaharaStatus.Success => "成功",
                SaharaStatus.InvalidCommand => "无效命令",
                SaharaStatus.ProtocolMismatch => "协议不匹配",
                SaharaStatus.UnexpectedImageId => "镜像 ID 不匹配",
                SaharaStatus.ReceiveTimeout => "接收超时",
                SaharaStatus.TransmitTimeout => "发送超时",
                SaharaStatus.HashTableAuthFailure => "🔴 签名验证失败: Loader 与设备不匹配",
                SaharaStatus.HashVerificationFailure => "🔴 完整性校验失败: 镜像可能被篡改",
                SaharaStatus.HashTableNotFound => "🔴 找不到签名数据: 镜像未签名",
                SaharaStatus.CommandExecuteFailure => "命令执行失败",
                SaharaStatus.AccessDenied => "命令不支持",
                _ => $"未知错误 (0x{(uint)status:X2})"
            };
        }

        public static bool IsFatalError(SaharaStatus status)
        {
            return status switch
            {
                SaharaStatus.HashTableAuthFailure => true,
                SaharaStatus.HashVerificationFailure => true,
                SaharaStatus.HashTableNotFound => true,
                SaharaStatus.InvalidElfHeader => true,
                SaharaStatus.ProtocolMismatch => true,
                _ => false
            };
        }
    }

    /// <summary>
    /// Sahara 协议客户端 - 完整版 (支持 V1/V2/V3)
    /// </summary>
    public class SaharaClient : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string>? _log;
        private bool _disposed;

        // 配置
        private const int MAX_BUFFER_SIZE = 4096;
        // ⚠️ 参考官方 QSaharaServer：设备约 1 秒后发送 Hello
        // 使用更长的超时以确保不会错过
        private const int READ_TIMEOUT_MS = 30000;   // 30 秒
        private const int HELLO_TIMEOUT_MS = 30000;  // 30 秒

        // 协议状态
        public uint ProtocolVersion { get; private set; } = 2;
        public uint ProtocolVersionSupported { get; private set; } = 1;
        public SaharaMode CurrentMode { get; private set; } = SaharaMode.ImageTransferPending;
        public bool IsConnected { get; private set; }

        // 芯片信息 (Sahara 可读取)
        public string ChipSerial { get; private set; } = "";
        public string ChipHwId { get; private set; } = "";
        public string ChipPkHash { get; private set; } = "";
        
        private bool _chipInfoRead = false;
        private bool _doneSent = false;
        private bool _skipCommandMode = false;
        
        // 预读取的 Hello 数据 (由外部检测阶段传入)
        private byte[]? _pendingHelloData = null;

        public SaharaClient(SerialPortManager port, Action<string>? log = null)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _log = log;
        }
        
        /// <summary>
        /// 设置预读取的 Hello 数据 (由外部检测阶段传入)
        /// </summary>
        public void SetPendingHelloData(byte[] data)
        {
            _pendingHelloData = data;
        }

        /// <summary>
        /// 握手并上传 Loader
        /// </summary>
        public async Task<bool> HandshakeAndUploadAsync(string loaderPath, CancellationToken ct = default)
        {
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("Loader 文件不存在", loaderPath);

            byte[] fileBytes = await File.ReadAllBytesAsync(loaderPath, ct);
            _log?.Invoke($"[Sahara] 加载 Loader: {Path.GetFileName(loaderPath)} ({fileBytes.Length / 1024} KB)");

            // ⚠️ 关键修复：不再检查 BytesToRead，直接使用阻塞读取
            // BytesToRead 在某些 USB CDC 驱动实现中可能不准确
            // 参考 edl 工具：直接进入状态机循环，使用阻塞读取等待 Hello 包
            _log?.Invoke("[Sahara] 等待设备 Hello 包 (阻塞读取模式)...");
            
            return await HandshakeAndLoadInternalAsync(fileBytes, ct);
        }

        /// <summary>
        /// 内部握手和加载
        /// </summary>
        private async Task<bool> HandshakeAndLoadInternalAsync(byte[] fileBytes, CancellationToken ct)
        {
            bool done = false;
            int loopGuard = 0;
            int endImageTxCount = 0;
            int timeoutCount = 0;
            _doneSent = false;
            _totalSent = 0;
            _lastProgressLog = 0;
            var sw = Stopwatch.StartNew();

            while (!done && loopGuard++ < 1000)
            {
                ct.ThrowIfCancellationRequested();

                byte[]? header = null;
                
                // 检查是否有预读取的 Hello 数据 (首次循环)
                if (loopGuard == 1 && _pendingHelloData != null && _pendingHelloData.Length >= 8)
                {
                    _log?.Invoke($"[Sahara] 使用预读取的 Hello 数据 ({_pendingHelloData.Length} 字节)");
                    header = new byte[8];
                    Array.Copy(_pendingHelloData, 0, header, 0, 8);
                    
                    // 如果预读数据超过 8 字节，需要特殊处理（在处理 Hello 包时读取剩余部分）
                }
                else
                {
                    // 读取包头 (首次尝试时使用更长的超时)
                    int currentTimeout = (loopGuard == 1) ? READ_TIMEOUT_MS * 2 : READ_TIMEOUT_MS;
                    header = await ReadBytesAsync(8, currentTimeout, ct);
                }
                if (header == null)
                {
                    timeoutCount++;
                    int available = _port.BytesToRead;
                    _log?.Invoke($"[Sahara] 读取超时 ({timeoutCount}/5)，缓冲区: {available} 字节");
                    
                    if (timeoutCount >= 5)
                    {
                        _log?.Invoke("[Sahara] ❌ 多次读取超时，设备可能未响应");
                        _log?.Invoke("[Sahara] 请确保:");
                        _log?.Invoke("   1. 设备已进入 9008 EDL 模式");
                        _log?.Invoke("   2. 驱动已正确安装 (Qualcomm HS-USB QDLoader 9008)");
                        _log?.Invoke("   3. 端口未被其他程序占用");
                        _log?.Invoke("   4. 尝试重新插拔 USB 线");
                        return false;
                    }
                    
                    // 如果有部分数据，尝试读取
                    if (available > 0)
                    {
                        _log?.Invoke($"[Sahara] 尝试读取部分数据...");
                        var partial = await ReadBytesAsync(available, 1000, ct);
                        if (partial != null)
                        {
                            _log?.Invoke($"[Sahara] 部分数据 (Hex): {BitConverter.ToString(partial, 0, Math.Min(16, partial.Length))}");
                        }
                    }
                    
                    await Task.Delay(500, ct);
                    continue;
                }
                
                // 重置超时计数
                timeoutCount = 0;

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                // 调试：显示收到的命令
                if (cmdId != (uint)SaharaCommand.ReadData && cmdId != (uint)SaharaCommand.ReadData64)
                {
                    _log?.Invoke($"[Sahara] 收到: Cmd=0x{cmdId:X2} ({(SaharaCommand)cmdId}), Len={pktLen}");
                }

                if (pktLen < 8 || pktLen > MAX_BUFFER_SIZE * 4)
                {
                    _log?.Invoke($"[Sahara] ⚠️ 异常包: CmdId=0x{cmdId:X2}, Len={pktLen}");
                    PurgeBuffer();
                    await Task.Delay(50, ct);
                    continue;
                }

                switch ((SaharaCommand)cmdId)
                {
                    case SaharaCommand.Hello:
                        await HandleHelloAsync(pktLen, ct);
                        break;

                    case SaharaCommand.ReadData:
                        await HandleReadData32Async(pktLen, fileBytes, ct);
                        break;

                    case SaharaCommand.ReadData64:
                        await HandleReadData64Async(pktLen, fileBytes, ct);
                        break;

                    case SaharaCommand.EndImageTransfer:
                        var (success, isDone, newCount) = await HandleEndImageTransferAsync(pktLen, endImageTxCount, ct);
                        endImageTxCount = newCount;
                        if (!success) return false;
                        if (isDone) done = true;
                        break;

                    case SaharaCommand.DoneResponse:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log?.Invoke("[Sahara] ✅ Loader 加载成功");
                        done = true;
                        IsConnected = true;
                        break;

                    case SaharaCommand.CommandReady:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log?.Invoke("[Sahara] 收到 CmdReady，切换到传输模式");
                        SendSwitchMode(SaharaMode.ImageTransferPending);
                        break;

                    default:
                        if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                        _log?.Invoke($"[Sahara] 未知命令: 0x{cmdId:X2}");
                        break;
                }
            }

            return done;
        }

        /// <summary>
        /// 处理 Hello 包
        /// </summary>
        private async Task HandleHelloAsync(uint pktLen, CancellationToken ct)
        {
            byte[]? body = null;
            
            // 检查是否有预读取的 Hello 数据
            if (_pendingHelloData != null && _pendingHelloData.Length >= pktLen)
            {
                // 使用预读取数据
                body = new byte[pktLen - 8];
                Array.Copy(_pendingHelloData, 8, body, 0, (int)pktLen - 8);
                _pendingHelloData = null; // 清除，只使用一次
            }
            else
            {
                // 正常读取
                body = await ReadBytesAsync((int)pktLen - 8, 5000, ct);
                _pendingHelloData = null; // 清除
            }
            
            if (body == null) return;

            ProtocolVersion = BitConverter.ToUInt32(body, 0);
            uint deviceMode = body.Length >= 12 ? BitConverter.ToUInt32(body, 12) : 0;
            _log?.Invoke($"[Sahara] 收到 HELLO (版本={ProtocolVersion}, 模式={deviceMode})");

            // 尝试读取芯片信息 (仅首次，且设备处于传输模式)
            if (!_chipInfoRead && deviceMode == (uint)SaharaMode.ImageTransferPending)
            {
                _chipInfoRead = true;
                bool enteredCommandMode = await TryReadChipInfoSafeAsync(ct);
                
                if (enteredCommandMode)
                {
                    // 成功进入命令模式并读取了信息，已发送 SwitchMode
                    // 设备会重新发送 Hello，不要在这里发送 HelloResponse
                    _log?.Invoke("[Sahara] 等待设备重新发送 Hello...");
                    return;
                }
            }

            // 发送 HelloResponse 进入传输模式
            _log?.Invoke("[Sahara] 发送 HelloResponse (传输模式)");
            SendHelloResponse(SaharaMode.ImageTransferPending);
        }

        // 传输进度追踪
        private long _totalSent = 0;
        private long _lastProgressLog = 0;
        
        /// <summary>
        /// 处理 32 位读取请求
        /// </summary>
        private async Task HandleReadData32Async(uint pktLen, byte[] fileBytes, CancellationToken ct)
        {
            var body = await ReadBytesAsync(12, 5000, ct);
            if (body == null) return;

            uint imageId = BitConverter.ToUInt32(body, 0);
            uint offset = BitConverter.ToUInt32(body, 4);
            uint length = BitConverter.ToUInt32(body, 8);

            if (offset + length > fileBytes.Length)
            {
                _log?.Invoke($"[Sahara] ⚠️ 请求越界: offset={offset}, length={length}");
                return;
            }

            _port.Write(fileBytes, (int)offset, (int)length);
            
            // 进度显示
            _totalSent += length;
            if (_totalSent - _lastProgressLog > 100 * 1024) // 每 100KB 显示一次
            {
                int percent = (int)(_totalSent * 100 / fileBytes.Length);
                _log?.Invoke($"[Sahara] 传输进度: {_totalSent / 1024} KB / {fileBytes.Length / 1024} KB ({percent}%)");
                _lastProgressLog = _totalSent;
            }
        }

        /// <summary>
        /// 处理 64 位读取请求
        /// </summary>
        private async Task HandleReadData64Async(uint pktLen, byte[] fileBytes, CancellationToken ct)
        {
            var body = await ReadBytesAsync(24, 5000, ct);
            if (body == null) return;

            ulong imageId = BitConverter.ToUInt64(body, 0);
            ulong offset = BitConverter.ToUInt64(body, 8);
            ulong length = BitConverter.ToUInt64(body, 16);

            if ((long)offset + (long)length > fileBytes.Length)
            {
                _log?.Invoke($"[Sahara] ⚠️ 64位请求越界: offset={offset}, length={length}");
                return;
            }

            _port.Write(fileBytes, (int)offset, (int)length);
            
            // 进度显示
            _totalSent += (long)length;
            if (_totalSent - _lastProgressLog > 100 * 1024) // 每 100KB 显示一次
            {
                int percent = (int)(_totalSent * 100 / fileBytes.Length);
                _log?.Invoke($"[Sahara] 传输进度: {_totalSent / 1024} KB / {fileBytes.Length / 1024} KB ({percent}%)");
                _lastProgressLog = _totalSent;
            }
        }

        /// <summary>
        /// 处理镜像传输结束
        /// </summary>
        private async Task<(bool Success, bool IsDone, int NewCount)> HandleEndImageTransferAsync(uint pktLen, int endImageTxCount, CancellationToken ct)
        {
            endImageTxCount++;

            if (endImageTxCount > 10)
            {
                _log?.Invoke("[Sahara] 收到过多 EndImageTx 命令");
                return (false, false, endImageTxCount);
            }

            uint endStatus = 0;
            if (pktLen >= 16)
            {
                var body = await ReadBytesAsync(8, 5000, ct);
                if (body != null)
                {
                    endStatus = BitConverter.ToUInt32(body, 4);
                }
            }

            if (endStatus != 0)
            {
                var status = (SaharaStatus)endStatus;
                _log?.Invoke($"[Sahara] ❌ 传输失败: {SaharaStatusHelper.GetErrorMessage(status)}");
                return (false, false, endImageTxCount);
            }

            if (!_doneSent)
            {
                _log?.Invoke("[Sahara] 镜像传输完成，发送 Done");
                SendDone();
                _doneSent = true;
            }

            return (true, false, endImageTxCount);
        }

        /// <summary>
        /// [关键] 安全读取芯片信息 - 支持 V1/V2/V3
        /// </summary>
        private async Task<bool> TryReadChipInfoSafeAsync(CancellationToken ct)
        {
            if (_skipCommandMode)
            {
                _log?.Invoke("[Sahara] 跳过命令模式");
                return false;
            }

            try
            {
                // 发送 HelloResponse 请求进入命令模式
                _log?.Invoke($"[Sahara] 尝试进入命令模式 (v{ProtocolVersion})...");
                SendHelloResponse(SaharaMode.Command);

                // 等待响应
                var header = await ReadBytesAsync(8, 2000, ct);
                if (header == null)
                {
                    _log?.Invoke("[Sahara] 命令模式无响应");
                    return false;
                }

                uint cmdId = BitConverter.ToUInt32(header, 0);
                uint pktLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)cmdId == SaharaCommand.CommandReady)
                {
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _log?.Invoke("[Sahara] 设备接受命令模式");
                    
                    await ReadChipInfoCommandsAsync(ct);
                    
                    // 切换回传输模式
                    SendSwitchMode(SaharaMode.ImageTransferPending);
                    await Task.Delay(50, ct);

                    return true;
                }
                else if ((SaharaCommand)cmdId == SaharaCommand.ReadData ||
                         (SaharaCommand)cmdId == SaharaCommand.ReadData64)
                {
                    _log?.Invoke($"[Sahara] 设备拒绝命令模式 (v{ProtocolVersion})");
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    _skipCommandMode = true;
                    return false;
                }
                else
                {
                    if (pktLen > 8) await ReadBytesAsync((int)pktLen - 8, 1000, ct);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sahara] 芯片信息读取失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// [关键] 读取芯片信息 - V1/V2/V3 版本区分
        /// </summary>
        private async Task ReadChipInfoCommandsAsync(CancellationToken ct)
        {
            _log?.Invoke($"- Sahara version  : {ProtocolVersion}");

            // 1. 读取序列号 (cmd=0x01)
            var serialData = await ExecuteCommandSafeAsync(SaharaExecCommand.SerialNumRead, ct);
            if (serialData != null && serialData.Length >= 4)
            {
                uint serial = BitConverter.ToUInt32(serialData, 0);
                ChipSerial = serial.ToString("x8");
                _log?.Invoke($"- Chip Serial Number : {ChipSerial}");
            }

            // 2. 读取 HWID - V3 和 V1/V2 不同！
            if (ProtocolVersion < 3)
            {
                // V1/V2: 使用 cmd=0x02
                var hwidData = await ExecuteCommandSafeAsync(SaharaExecCommand.MsmHwIdRead, ct);
                if (hwidData != null && hwidData.Length >= 8)
                {
                    ProcessHwIdData(hwidData);
                }
            }
            else
            {
                // [关键] V3: cmd=0x02 不支持，使用 cmd=0x0A
                _log?.Invoke("[Sahara] V3 协议，使用 cmd=0x0A 读取芯片信息");
            }

            // 3. 读取 PK Hash (cmd=0x03)
            var pkhash = await ExecuteCommandSafeAsync(SaharaExecCommand.OemPkHashRead, ct);
            if (pkhash != null && pkhash.Length > 0)
            {
                int hashLen = Math.Min(pkhash.Length, 48);
                ChipPkHash = BitConverter.ToString(pkhash, 0, hashLen).Replace("-", "").ToLower();
                _log?.Invoke($"- OEM PKHASH : {ChipPkHash}");

                var pkInfo = QualcommDatabase.GetPkHashInfo(ChipPkHash);
                if (pkInfo != "Unknown" && pkInfo != "Custom OEM")
                {
                    _log?.Invoke($"- SecBoot : {pkInfo}");
                }
            }

            // 4. V3 专用: 读取扩展信息 (cmd=0x0A)
            if (string.IsNullOrEmpty(ChipHwId) || ProtocolVersion >= 3)
            {
                var extInfo = await ExecuteCommandSafeAsync(SaharaExecCommand.ChipIdV3Read, ct);
                if (extInfo != null && extInfo.Length >= 44)
                {
                    ProcessV3ExtendedInfo(extInfo);
                }
            }
        }

        /// <summary>
        /// 处理 V1/V2 HWID 数据
        /// </summary>
        private void ProcessHwIdData(byte[] hwidData)
        {
            ulong hwid = BitConverter.ToUInt64(hwidData, 0);
            ChipHwId = hwid.ToString("x16");

            uint msmId = (uint)(hwid & 0xFFFFFF);
            ushort oemId = (ushort)((hwid >> 32) & 0xFFFF);
            ushort modelId = (ushort)((hwid >> 48) & 0xFFFF);

            string chipName = QualcommDatabase.GetChipName(msmId);
            string vendor = QualcommDatabase.GetVendorName(oemId);

            _log?.Invoke($"- MSM HWID : 0x{msmId:x} | model_id:0x{modelId:x4} | oem_id:{oemId:X4} {vendor}");

            if (chipName != "Unknown")
                _log?.Invoke($"- CHIP : {chipName}");

            _log?.Invoke($"- HW_ID : {ChipHwId}");
        }

        /// <summary>
        /// [关键] 处理 V3 扩展信息 (cmd=0x0A 返回)
        /// </summary>
        private void ProcessV3ExtendedInfo(byte[] extInfo)
        {
            // V3 返回 84 字节数据
            // 偏移 0: Chip Identifier V3 (4字节)
            // 偏移 36: MSM_ID (4字节)
            // 偏移 40: OEM_ID (2字节)
            // 偏移 42: MODEL_ID (2字节)

            uint chipIdV3 = BitConverter.ToUInt32(extInfo, 0);
            if (chipIdV3 != 0)
                _log?.Invoke($"- Chip Identifier V3 : {chipIdV3:x8}");

            if (extInfo.Length >= 44)
            {
                uint rawMsm = BitConverter.ToUInt32(extInfo, 36);
                ushort rawOem = BitConverter.ToUInt16(extInfo, 40);
                ushort rawModel = BitConverter.ToUInt16(extInfo, 42);

                uint msmId = rawMsm & 0x00FFFFFF;

                // 检查备用 OEM_ID 位置
                if (rawOem == 0 && extInfo.Length >= 46)
                {
                    ushort altOemId = BitConverter.ToUInt16(extInfo, 44);
                    if (altOemId > 0 && altOemId < 0x1000)
                        rawOem = altOemId;
                }

                if (msmId != 0 || rawOem != 0)
                {
                    string chipName = QualcommDatabase.GetChipName(msmId);
                    string vendor = QualcommDatabase.GetVendorName(rawOem);

                    ChipHwId = $"00{msmId:x6}{rawOem:x4}{rawModel:x4}".ToLower();

                    _log?.Invoke($"- MSM HWID : 0x{msmId:x} | model_id:0x{rawModel:x4} | oem_id:{rawOem:X4} {vendor}");

                    if (chipName != "Unknown")
                        _log?.Invoke($"- CHIP : {chipName}");

                    _log?.Invoke($"- HW_ID : {ChipHwId}");
                }
            }
        }

        /// <summary>
        /// 安全执行命令
        /// </summary>
        private async Task<byte[]?> ExecuteCommandSafeAsync(SaharaExecCommand cmd, CancellationToken ct)
        {
            try
            {
                int timeout = cmd == SaharaExecCommand.SblInfoRead ? 5000 : 2000;

                // 发送 Execute (0x0D)
                var execPacket = new byte[12];
                BitConverter.GetBytes((uint)SaharaCommand.Execute).CopyTo(execPacket, 0);
                BitConverter.GetBytes((uint)12).CopyTo(execPacket, 4);
                BitConverter.GetBytes((uint)cmd).CopyTo(execPacket, 8);
                _port.Write(execPacket);

                // 读取响应头
                var header = await ReadBytesAsync(8, timeout, ct);
                if (header == null) return null;

                uint respCmd = BitConverter.ToUInt32(header, 0);
                uint respLen = BitConverter.ToUInt32(header, 4);

                if ((SaharaCommand)respCmd != SaharaCommand.ExecuteData)
                {
                    if (respLen > 8) await ReadBytesAsync((int)respLen - 8, 1000, ct);
                    return null;
                }

                // 读取响应体
                if (respLen <= 8) return null;
                var body = await ReadBytesAsync((int)respLen - 8, timeout, ct);
                if (body == null || body.Length < 8) return null;

                uint dataCmd = BitConverter.ToUInt32(body, 0);
                uint dataLen = BitConverter.ToUInt32(body, 4);

                if (dataCmd != (uint)cmd || dataLen == 0) return null;

                // 发送确认 (0x0F)
                var respPacket = new byte[12];
                BitConverter.GetBytes((uint)SaharaCommand.ExecuteResponse).CopyTo(respPacket, 0);
                BitConverter.GetBytes((uint)12).CopyTo(respPacket, 4);
                BitConverter.GetBytes((uint)cmd).CopyTo(respPacket, 8);
                _port.Write(respPacket);

                // 读取数据
                int dataTimeout = dataLen > 1000 ? 10000 : timeout;
                return await ReadBytesAsync((int)dataLen, dataTimeout, ct);
            }
            catch
            {
                return null;
            }
        }

        #region 发送方法

        private void SendHelloResponse(SaharaMode mode)
        {
            var resp = new SaharaHelloResponse
            {
                Command = (uint)SaharaCommand.HelloResponse,
                Length = 48,
                Version = 2,
                VersionSupported = 1,
                Status = (uint)SaharaStatus.Success,
                Mode = (uint)mode,
                Reserved1 = 0, Reserved2 = 0, Reserved3 = 0,
                Reserved4 = 0, Reserved5 = 0, Reserved6 = 0
            };
            _port.Write(StructToBytes(resp));
        }

        private void SendDone()
        {
            var done = new SaharaDonePacket
            {
                Command = (uint)SaharaCommand.Done,
                Length = 8
            };
            _port.Write(StructToBytes(done));
        }

        private void SendSwitchMode(SaharaMode mode)
        {
            var packet = new byte[12];
            BitConverter.GetBytes((uint)SaharaCommand.SwitchMode).CopyTo(packet, 0);
            BitConverter.GetBytes((uint)12).CopyTo(packet, 4);
            BitConverter.GetBytes((uint)mode).CopyTo(packet, 8);
            _port.Write(packet);
        }

        public void SendReset()
        {
            var packet = new byte[8];
            BitConverter.GetBytes((uint)SaharaCommand.ResetStateMachine).CopyTo(packet, 0);
            BitConverter.GetBytes((uint)8).CopyTo(packet, 4);
            _port.Write(packet);
        }

        #endregion

        #region 工具方法

        private async Task<byte[]?> ReadBytesAsync(int count, int timeoutMs, CancellationToken ct)
        {
            return await _port.TryReadExactAsync(count, timeoutMs, ct);
        }

        private void PurgeBuffer()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static byte[] StructToBytes<T>(T structure) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var bytes = new byte[size];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false);
                return bytes;
            }
            finally
            {
                handle.Free();
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
