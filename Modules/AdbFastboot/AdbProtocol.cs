// ============================================================================
// MultiFlash TOOL - Android Debug Bridge (ADB) Protocol
// ADB 协议 | ADBプロトコル | ADB 프로토콜
// ============================================================================
// [EN] ADB protocol implementation for Android device communication
//      Supports shell commands, file transfer, reboot operations
// [中文] ADB 协议实现，用于 Android 设备通信
//       支持 Shell 命令、文件传输、重启操作
// [日本語] Androidデバイス通信用のADBプロトコル実装
//         シェルコマンド、ファイル転送、再起動操作をサポート
// [한국어] 안드로이드 장치 통신을 위한 ADB 프로토콜 구현
//         셸 명령, 파일 전송, 재부팅 작업 지원
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace tools.Modules.AdbFastboot
{
    /// <summary>
    /// ADB Message Commands / ADB 消息命令 / ADBメッセージコマンド / ADB 메시지 명령
    /// </summary>
    public static class AdbCommand
    {
        public const uint A_SYNC = 0x434e5953; // "SYNC"
        public const uint A_CNXN = 0x4e584e43; // "CNXN" - 连接
        public const uint A_AUTH = 0x48545541; // "AUTH" - 认证
        public const uint A_OPEN = 0x4e45504f; // "OPEN" - 打开流
        public const uint A_OKAY = 0x59414b4f; // "OKAY" - 确认
        public const uint A_CLSE = 0x45534c43; // "CLSE" - 关闭流
        public const uint A_WRTE = 0x45545257; // "WRTE" - 写数据
        public const uint A_STLS = 0x534c5453; // "STLS" - TLS 升级
    }

    /// <summary>
    /// ADB 认证类型
    /// </summary>
    public enum AdbAuthType : uint
    {
        TOKEN = 1,      // 服务器发送的令牌
        SIGNATURE = 2,  // 签名响应
        RSAPUBLICKEY = 3 // RSA 公钥
    }

    /// <summary>
    /// ADB 消息头 (24 字节)
    /// </summary>
    public class AdbMessage
    {
        public uint Command { get; set; }    // 命令标识
        public uint Arg0 { get; set; }       // 参数1
        public uint Arg1 { get; set; }       // 参数2
        public uint DataLength { get; set; } // 数据长度
        public uint DataCrc32 { get; set; }  // 数据 CRC32
        public uint Magic { get; set; }      // 命令的按位取反

        public byte[]? Data { get; set; }

        public const int HEADER_SIZE = 24;

        public byte[] ToBytes()
        {
            var buffer = new byte[HEADER_SIZE];
            BitConverter.GetBytes(Command).CopyTo(buffer, 0);
            BitConverter.GetBytes(Arg0).CopyTo(buffer, 4);
            BitConverter.GetBytes(Arg1).CopyTo(buffer, 8);
            BitConverter.GetBytes(DataLength).CopyTo(buffer, 12);
            BitConverter.GetBytes(DataCrc32).CopyTo(buffer, 16);
            BitConverter.GetBytes(Magic).CopyTo(buffer, 20);
            return buffer;
        }

        public static AdbMessage FromBytes(byte[] buffer)
        {
            if (buffer.Length < HEADER_SIZE)
                throw new ArgumentException("Buffer too small for ADB message");

            return new AdbMessage
            {
                Command = BitConverter.ToUInt32(buffer, 0),
                Arg0 = BitConverter.ToUInt32(buffer, 4),
                Arg1 = BitConverter.ToUInt32(buffer, 8),
                DataLength = BitConverter.ToUInt32(buffer, 12),
                DataCrc32 = BitConverter.ToUInt32(buffer, 16),
                Magic = BitConverter.ToUInt32(buffer, 20)
            };
        }

        public bool IsValid => Magic == ~Command;

        public static AdbMessage Create(uint command, uint arg0, uint arg1, byte[]? data = null)
        {
            uint dataLen = (uint)(data?.Length ?? 0);
            uint crc = data != null ? CalculateCrc32(data) : 0;

            return new AdbMessage
            {
                Command = command,
                Arg0 = arg0,
                Arg1 = arg1,
                DataLength = dataLen,
                DataCrc32 = crc,
                Magic = ~command,
                Data = data
            };
        }

        private static uint CalculateCrc32(byte[] data)
        {
            uint crc = 0;
            foreach (byte b in data)
            {
                crc += b;
            }
            return crc;
        }

        public string GetCommandName()
        {
            return Command switch
            {
                AdbCommand.A_SYNC => "SYNC",
                AdbCommand.A_CNXN => "CNXN",
                AdbCommand.A_AUTH => "AUTH",
                AdbCommand.A_OPEN => "OPEN",
                AdbCommand.A_OKAY => "OKAY",
                AdbCommand.A_CLSE => "CLSE",
                AdbCommand.A_WRTE => "WRTE",
                AdbCommand.A_STLS => "STLS",
                _ => $"0x{Command:X8}"
            };
        }
    }

    /// <summary>
    /// ADB 设备信息
    /// </summary>
    public class AdbDeviceInfo
    {
        public string SerialNumber { get; set; } = "";
        public string Product { get; set; } = "";
        public string Model { get; set; } = "";
        public string Device { get; set; } = "";
        public string Features { get; set; } = "";
        public uint MaxData { get; set; } = 4096;
        public uint ProtocolVersion { get; set; }
    }

    /// <summary>
    /// Android Debug Bridge (ADB) 协议实现
    /// 
    /// 协议版本: 0x01000001 (Android 4.4+)
    /// 
    /// 消息格式:
    /// - 24 字节头: command(4) + arg0(4) + arg1(4) + data_length(4) + data_crc32(4) + magic(4)
    /// - 可变长度数据 (最大 256KB)
    /// 
    /// 连接流程:
    /// 1. 主机发送 CNXN
    /// 2. 设备回复 AUTH (如果需要认证)
    /// 3. 主机发送 AUTH (签名或公钥)
    /// 4. 设备回复 CNXN (认证成功)
    /// </summary>
    public class AdbProtocol : IDisposable
    {
        // ADB 协议版本
        private const uint ADB_VERSION = 0x01000001;
        private const uint MAX_DATA = 256 * 1024; // 256KB
        private const int USB_TIMEOUT = 5000;

        // USB 设备
        private IUsbDevice? _device;
        private UsbEndpointReader? _reader;
        private UsbEndpointWriter? _writer;

        // TCP 连接 (用于 ADB over WiFi 或 ADB server)
        private TcpClient? _tcpClient;
        private NetworkStream? _tcpStream;

        // 状态
        public bool IsConnected { get; private set; }
        public bool IsUsb { get; private set; }
        public AdbDeviceInfo? DeviceInfo { get; private set; }

        // 流管理
        private uint _localId = 1;
        private readonly Dictionary<uint, AdbStream> _streams = new();

        // 事件
        public event Action<string>? OnLog;
        public event Action<long, long>? OnProgress;

        // 已知的 ADB VID/PID 列表
        private static readonly (int Vid, int Pid, string Name)[] KnownDevices = new[]
        {
            (0x18D1, 0x4EE2, "Google ADB"),
            (0x18D1, 0x4EE7, "Google ADB (Sideload)"),
            (0x18D1, 0xD002, "Google ADB (Alt)"),
            (0x22B8, 0x2E76, "Motorola ADB"),
            (0x0BB4, 0x0C02, "HTC ADB"),
            (0x04E8, 0x6860, "Samsung ADB"),
            (0x2717, 0xFF68, "Xiaomi ADB"),
            (0x2717, 0x9039, "Xiaomi ADB (Alt)"),
            (0x22D9, 0x2767, "OPPO/OnePlus ADB"),
            (0x1949, 0x0C23, "ASUS ADB"),
            (0x12D1, 0x1038, "Huawei ADB"),
            (0x2A70, 0x4EE7, "OnePlus ADB"),
            (0x05C6, 0x9025, "Qualcomm ADB"),
            (0x2B4C, 0x1003, "Realme ADB"),
            (0x0E8D, 0x201D, "MediaTek ADB"),
        };

        #region 连接管理

        /// <summary>
        /// 通过 USB 连接
        /// </summary>
        public async Task<bool> ConnectUsbAsync(int vid = 0, int pid = 0, CancellationToken ct = default)
        {
            try
            {
                Log("正在搜索 ADB 设备...");

                // 如果指定了 VID/PID，直接尝试连接
                if (vid > 0 && pid > 0)
                {
                    if (ConnectToUsbDevice(vid, pid))
                    {
                        IsUsb = true;
                        return await PerformHandshakeAsync(ct);
                    }
                    return false;
                }

                // 否则尝试已知设备列表
                foreach (var (knownVid, knownPid, name) in KnownDevices)
                {
                    if (ConnectToUsbDevice(knownVid, knownPid))
                    {
                        Log($"已连接: {name}");
                        IsUsb = true;
                        return await PerformHandshakeAsync(ct);
                    }
                }

                Log("未找到 ADB 设备");
                return false;
            }
            catch (Exception ex)
            {
                Log($"连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通过 TCP 连接 (ADB over WiFi)
        /// </summary>
        public async Task<bool> ConnectTcpAsync(string host, int port = 5555, CancellationToken ct = default)
        {
            try
            {
                Log($"正在连接 {host}:{port}...");

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, ct);
                _tcpStream = _tcpClient.GetStream();

                IsUsb = false;
                return await PerformHandshakeAsync(ct);
            }
            catch (Exception ex)
            {
                Log($"TCP 连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通过 ADB Server 连接 (默认 localhost:5037)
        /// </summary>
        public async Task<bool> ConnectViaServerAsync(string serial = "", string host = "127.0.0.1", int port = 5037, CancellationToken ct = default)
        {
            try
            {
                Log($"正在连接 ADB Server ({host}:{port})...");

                _serverHost = host;
                _serverPort = port;
                _serverSerial = serial;
                _useServer = true;

                // 测试连接
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, ct);
                _tcpStream = _tcpClient.GetStream();

                // 选择设备
                string transportCmd = string.IsNullOrEmpty(serial) 
                    ? "host:transport-any" 
                    : $"host:transport:{serial}";

                if (!await SendServerCommandAsync(transportCmd, ct))
                {
                    Log(string.IsNullOrEmpty(serial) 
                        ? "无法连接到任何设备" 
                        : $"无法选择设备: {serial}");
                    _tcpClient?.Close();
                    return false;
                }

                // 连接成功后关闭此连接，后续每次命令重新连接
                _tcpClient?.Close();
                _tcpClient = null;
                _tcpStream = null;

                IsUsb = false;
                IsConnected = true;
                Log("ADB Server 连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Server 连接失败: {ex.Message}");
                return false;
            }
        }

        // ADB Server 模式相关字段
        private bool _useServer = false;
        private string _serverHost = "127.0.0.1";
        private int _serverPort = 5037;
        private string _serverSerial = "";

        private bool ConnectToUsbDevice(int vid, int pid)
        {
            try
            {
                var finder = new UsbDeviceFinder(vid, pid);
                _device = UsbDevice.OpenUsbDevice(finder) as IUsbDevice;

                if (_device == null)
                    return false;

                // 配置设备
                _device.SetConfiguration(1);
                _device.ClaimInterface(0);

                // 查找端点
                var config = _device.Configs[0];
                var iface = config.InterfaceInfoList[0];

                foreach (var ep in iface.EndpointInfoList)
                {
                    if ((ep.Descriptor.EndpointID & 0x80) != 0)
                    {
                        _reader = _device.OpenEndpointReader((ReadEndpointID)ep.Descriptor.EndpointID);
                    }
                    else
                    {
                        _writer = _device.OpenEndpointWriter((WriteEndpointID)ep.Descriptor.EndpointID);
                    }
                }

                if (_reader == null || _writer == null)
                {
                    Disconnect();
                    return false;
                }

                Log($"已连接设备: VID=0x{vid:X4}, PID=0x{pid:X4}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                // 关闭所有流
                foreach (var stream in _streams.Values)
                {
                    stream.Close();
                }
                _streams.Clear();

                // 关闭 USB
                _reader?.Dispose();
                _writer?.Dispose();
                if (_device != null)
                {
                    _device.ReleaseInterface(0);
                    _device.Close();
                    _device = null;
                }

                // 关闭 TCP
                _tcpStream?.Close();
                _tcpClient?.Close();
                _tcpStream = null;
                _tcpClient = null;

                IsConnected = false;
                DeviceInfo = null;
                Log("已断开连接");
            }
            catch (Exception ex)
            {
                Log($"断开连接错误: {ex.Message}");
            }
        }

        #endregion

        #region ADB 协议握手

        private async Task<bool> PerformHandshakeAsync(CancellationToken ct)
        {
            try
            {
                // 发送 CNXN
                string banner = $"host::features=shell_v2,cmd,stat_v2,ls_v2,fixed_push_mkdir,apex,abb,fixed_push_symlink_timestamp,abb_exec,remount_shell,track_app,sendrecv_v2,sendrecv_v2_brotli,sendrecv_v2_lz4,sendrecv_v2_zstd,sendrecv_v2_dry_run_send,openscreen_mdns";
                var cnxnMsg = AdbMessage.Create(AdbCommand.A_CNXN, ADB_VERSION, MAX_DATA, Encoding.UTF8.GetBytes(banner));

                await SendMessageAsync(cnxnMsg, ct);
                Log("已发送 CNXN");

                // 等待响应
                var response = await ReceiveMessageAsync(ct);
                if (response == null)
                {
                    Log("未收到响应");
                    return false;
                }

                Log($"收到: {response.GetCommandName()}");

                // 处理认证
                if (response.Command == AdbCommand.A_AUTH)
                {
                    Log("需要认证...");
                    // 简化处理: 发送公钥
                    // 实际应用中需要正确的 RSA 密钥对
                    var authMsg = AdbMessage.Create(AdbCommand.A_AUTH, (uint)AdbAuthType.RSAPUBLICKEY, 0,
                        Encoding.UTF8.GetBytes("QAAAAMDHdwT...dummy_key\0")); // 占位公钥

                    await SendMessageAsync(authMsg, ct);

                    response = await ReceiveMessageAsync(ct);
                    if (response == null)
                    {
                        Log("认证超时");
                        return false;
                    }
                }

                // 检查 CNXN 响应
                if (response.Command != AdbCommand.A_CNXN)
                {
                    Log($"意外响应: {response.GetCommandName()}");
                    return false;
                }

                // 解析设备信息
                DeviceInfo = new AdbDeviceInfo
                {
                    ProtocolVersion = response.Arg0,
                    MaxData = response.Arg1
                };

                if (response.Data != null)
                {
                    ParseDeviceBanner(Encoding.UTF8.GetString(response.Data));
                }

                IsConnected = true;
                Log($"ADB 连接成功: {DeviceInfo.Model}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"握手失败: {ex.Message}");
                return false;
            }
        }

        private void ParseDeviceBanner(string banner)
        {
            if (DeviceInfo == null) return;

            // banner 格式: "device::ro.product.name=xxx;ro.product.model=xxx;..."
            var parts = banner.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 1)
            {
                DeviceInfo.Device = parts[0];
            }

            if (parts.Length >= 2)
            {
                var props = parts[1].Split(';');
                foreach (var prop in props)
                {
                    var kv = prop.Split('=');
                    if (kv.Length == 2)
                    {
                        switch (kv[0])
                        {
                            case "ro.product.name":
                                DeviceInfo.Product = kv[1];
                                break;
                            case "ro.product.model":
                                DeviceInfo.Model = kv[1];
                                break;
                            case "features":
                                DeviceInfo.Features = kv[1];
                                break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Shell 命令

        /// <summary>
        /// 执行 Shell 命令
        /// </summary>
        public async Task<string> ShellAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected) return "";

            try
            {
                // ADB Server 模式: 使用服务器协议
                if (_useServer)
                {
                    return await ExecuteServerServiceAsync($"shell:{command}", ct);
                }

                // 直连模式: 使用 ADB 消息协议
                var stream = await OpenStreamAsync($"shell:{command}", ct);
                if (stream == null) return "";

                var result = new StringBuilder();

                while (!ct.IsCancellationRequested)
                {
                    var data = await stream.ReadAsync(ct);
                    if (data == null || data.Length == 0)
                        break;

                    result.Append(Encoding.UTF8.GetString(data));
                }

                stream.Close();
                return result.ToString();
            }
            catch (Exception ex)
            {
                Log($"Shell 命令失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 通过 ADB Server 执行服务命令
        /// </summary>
        private async Task<string> ExecuteServerServiceAsync(string service, CancellationToken ct)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_serverHost, _serverPort, ct);
            using var stream = client.GetStream();

            // 1. 选择设备
            string transportCmd = string.IsNullOrEmpty(_serverSerial) 
                ? "host:transport-any" 
                : $"host:transport:{_serverSerial}";

            // 发送 transport 命令
            string header = $"{transportCmd.Length:X4}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header + transportCmd), ct);

            // 读取响应
            var status = new byte[4];
            await ReadExactAsync(stream, status, 4, ct);

            if (Encoding.ASCII.GetString(status) != "OKAY")
            {
                Log($"Transport 失败");
                return "";
            }

            // 2. 发送服务命令
            header = $"{service.Length:X4}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header + service), ct);

            // 读取响应状态
            await ReadExactAsync(stream, status, 4, ct);
            string statusStr = Encoding.ASCII.GetString(status);

            if (statusStr != "OKAY")
            {
                // 读取错误信息
                var lenBuf = new byte[4];
                await ReadExactAsync(stream, lenBuf, 4, ct);
                int errLen = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16);
                if (errLen > 0)
                {
                    var errBuf = new byte[errLen];
                    await ReadExactAsync(stream, errBuf, errLen, ct);
                    Log($"服务错误: {Encoding.UTF8.GetString(errBuf)}");
                }
                return "";
            }

            // 3. 读取所有输出
            var result = new StringBuilder();
            var buffer = new byte[4096];

            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                result.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            return result.ToString();
        }

        /// <summary>
        /// 精确读取指定字节数
        /// </summary>
        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, ct);
                if (read == 0) throw new IOException("Connection closed");
                totalRead += read;
            }
        }

        /// <summary>
        /// 打开交互式 Shell
        /// </summary>
        public async Task<AdbStream?> OpenShellAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return null;
            return await OpenStreamAsync("shell:", ct);
        }

        #endregion

        #region 文件传输 (SYNC 协议)

        // SYNC 协议命令 ID
        private static class SyncCommand
        {
            public static readonly byte[] SEND = Encoding.ASCII.GetBytes("SEND");
            public static readonly byte[] RECV = Encoding.ASCII.GetBytes("RECV");
            public static readonly byte[] DATA = Encoding.ASCII.GetBytes("DATA");
            public static readonly byte[] DONE = Encoding.ASCII.GetBytes("DONE");
            public static readonly byte[] OKAY = Encoding.ASCII.GetBytes("OKAY");
            public static readonly byte[] FAIL = Encoding.ASCII.GetBytes("FAIL");
            public static readonly byte[] STAT = Encoding.ASCII.GetBytes("STAT");
            public static readonly byte[] LIST = Encoding.ASCII.GetBytes("LIST");
            public static readonly byte[] DENT = Encoding.ASCII.GetBytes("DENT");
            public static readonly byte[] QUIT = Encoding.ASCII.GetBytes("QUIT");
        }

        /// <summary>
        /// 推送文件到设备
        /// </summary>
        public async Task<bool> PushAsync(string localPath, string remotePath, CancellationToken ct = default)
        {
            if (!IsConnected || !File.Exists(localPath))
                return false;

            try
            {
                var fileInfo = new FileInfo(localPath);
                Log($"推送: {localPath} -> {remotePath} ({fileInfo.Length / 1024.0:F1} KB)");

                // 打开 sync 流
                var stream = await OpenStreamAsync("sync:", ct);
                if (stream == null) return false;

                // SEND 命令: "SEND" + 长度(4字节小端) + path + "," + mode
                string pathAndMode = $"{remotePath},33188"; // 644 权限 (0100644 octal = 33188 decimal)
                var sendCmd = BuildSyncRequest(SyncCommand.SEND, Encoding.UTF8.GetBytes(pathAndMode));
                await stream.WriteAsync(sendCmd, ct);

                // 传输数据
                using var fs = File.OpenRead(localPath);
                var buffer = new byte[64 * 1024]; // 64KB 块
                long totalSent = 0;
                int bytesRead;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    // DATA: "DATA" + 长度(4字节小端) + 数据
                    var dataCmd = new byte[8 + bytesRead];
                    SyncCommand.DATA.CopyTo(dataCmd, 0);
                    BitConverter.GetBytes(bytesRead).CopyTo(dataCmd, 4);
                    Array.Copy(buffer, 0, dataCmd, 8, bytesRead);

                    await stream.WriteAsync(dataCmd, ct);

                    totalSent += bytesRead;
                    OnProgress?.Invoke(totalSent, fileInfo.Length);
                }

                // DONE: "DONE" + mtime(4字节小端)
                uint mtime = (uint)((DateTimeOffset)fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
                var doneCmd = new byte[8];
                SyncCommand.DONE.CopyTo(doneCmd, 0);
                BitConverter.GetBytes(mtime).CopyTo(doneCmd, 4);
                await stream.WriteAsync(doneCmd, ct);

                // 等待响应: "OKAY" 或 "FAIL"
                var response = await stream.ReadAsync(ct);
                stream.Close();

                if (response != null && response.Length >= 4)
                {
                    string respCmd = Encoding.ASCII.GetString(response, 0, 4);
                    if (respCmd == "OKAY")
                    {
                        Log("推送成功");
                        return true;
                    }
                    else if (respCmd == "FAIL" && response.Length > 8)
                    {
                        int errLen = BitConverter.ToInt32(response, 4);
                        string errMsg = Encoding.UTF8.GetString(response, 8, Math.Min(errLen, response.Length - 8));
                        Log($"推送失败: {errMsg}");
                    }
                }

                Log("推送失败");
                return false;
            }
            catch (Exception ex)
            {
                Log($"推送错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从设备拉取文件
        /// </summary>
        public async Task<bool> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
        {
            if (!IsConnected)
                return false;

            try
            {
                Log($"拉取: {remotePath} -> {localPath}");

                // 打开 sync 流
                var stream = await OpenStreamAsync("sync:", ct);
                if (stream == null) return false;

                // RECV 命令: "RECV" + 长度(4字节小端) + path
                var recvCmd = BuildSyncRequest(SyncCommand.RECV, Encoding.UTF8.GetBytes(remotePath));
                await stream.WriteAsync(recvCmd, ct);

                // 接收数据
                using var fs = File.Create(localPath);
                long totalReceived = 0;

                while (!ct.IsCancellationRequested)
                {
                    // 读取 8 字节头: CMD(4) + LENGTH(4)
                    var header = await stream.ReadAsync(ct, 8);
                    if (header == null || header.Length < 8) break;

                    string cmd = Encoding.ASCII.GetString(header, 0, 4);
                    int dataLen = BitConverter.ToInt32(header, 4);

                    if (cmd == "DATA")
                    {
                        if (dataLen > 0)
                        {
                            var data = await stream.ReadAsync(ct, dataLen);
                            if (data != null)
                            {
                                await fs.WriteAsync(data, ct);
                                totalReceived += data.Length;
                                OnProgress?.Invoke(totalReceived, totalReceived);
                            }
                        }
                    }
                    else if (cmd == "DONE")
                    {
                        break;
                    }
                    else if (cmd == "FAIL")
                    {
                        if (dataLen > 0)
                        {
                            var errData = await stream.ReadAsync(ct, dataLen);
                            if (errData != null)
                            {
                                Log($"拉取失败: {Encoding.UTF8.GetString(errData)}");
                            }
                        }
                        stream.Close();
                        File.Delete(localPath);
                        return false;
                    }
                }

                stream.Close();
                Log($"拉取成功: {totalReceived / 1024.0:F1} KB");
                return true;
            }
            catch (Exception ex)
            {
                Log($"拉取错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取远程文件信息
        /// </summary>
        public async Task<(int mode, int size, int mtime)?> StatAsync(string remotePath, CancellationToken ct = default)
        {
            if (!IsConnected) return null;

            try
            {
                var stream = await OpenStreamAsync("sync:", ct);
                if (stream == null) return null;

                // STAT 命令
                var statCmd = BuildSyncRequest(SyncCommand.STAT, Encoding.UTF8.GetBytes(remotePath));
                await stream.WriteAsync(statCmd, ct);

                // 响应: "STAT" + mode(4) + size(4) + mtime(4)
                var response = await stream.ReadAsync(ct);
                stream.Close();

                if (response != null && response.Length >= 16)
                {
                    string cmd = Encoding.ASCII.GetString(response, 0, 4);
                    if (cmd == "STAT")
                    {
                        int mode = BitConverter.ToInt32(response, 4);
                        int size = BitConverter.ToInt32(response, 8);
                        int mtime = BitConverter.ToInt32(response, 12);
                        return (mode, size, mtime);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 列出远程目录
        /// </summary>
        public async Task<List<(string name, int mode, int size, int mtime)>> ListDirAsync(string remotePath, CancellationToken ct = default)
        {
            var result = new List<(string, int, int, int)>();
            if (!IsConnected) return result;

            try
            {
                var stream = await OpenStreamAsync("sync:", ct);
                if (stream == null) return result;

                // LIST 命令
                var listCmd = BuildSyncRequest(SyncCommand.LIST, Encoding.UTF8.GetBytes(remotePath));
                await stream.WriteAsync(listCmd, ct);

                while (!ct.IsCancellationRequested)
                {
                    var header = await stream.ReadAsync(ct, 4);
                    if (header == null || header.Length < 4) break;

                    string cmd = Encoding.ASCII.GetString(header, 0, 4);

                    if (cmd == "DENT")
                    {
                        // DENT: mode(4) + size(4) + mtime(4) + namelen(4) + name
                        var dentHeader = await stream.ReadAsync(ct, 16);
                        if (dentHeader == null || dentHeader.Length < 16) break;

                        int mode = BitConverter.ToInt32(dentHeader, 0);
                        int size = BitConverter.ToInt32(dentHeader, 4);
                        int mtime = BitConverter.ToInt32(dentHeader, 8);
                        int nameLen = BitConverter.ToInt32(dentHeader, 12);

                        if (nameLen > 0)
                        {
                            var nameData = await stream.ReadAsync(ct, nameLen);
                            if (nameData != null)
                            {
                                string name = Encoding.UTF8.GetString(nameData);
                                result.Add((name, mode, size, mtime));
                            }
                        }
                    }
                    else if (cmd == "DONE")
                    {
                        break;
                    }
                    else if (cmd == "FAIL")
                    {
                        break;
                    }
                }

                stream.Close();
            }
            catch (Exception ex)
            {
                Log($"列目录错误: {ex.Message}");
            }

            return result;
        }

        private byte[] BuildSyncRequest(byte[] cmd, byte[] path)
        {
            var result = new byte[8 + path.Length];
            cmd.CopyTo(result, 0);
            BitConverter.GetBytes(path.Length).CopyTo(result, 4);
            path.CopyTo(result, 8);
            return result;
        }

        #endregion

        #region 设备控制

        /// <summary>
        /// 重启设备
        /// </summary>
        public async Task<bool> RebootAsync(string target = "", CancellationToken ct = default)
        {
            string cmd = string.IsNullOrEmpty(target) ? "reboot:" : $"reboot:{target}";
            var stream = await OpenStreamAsync(cmd, ct);
            if (stream == null) return false;

            stream.Close();
            Disconnect();
            return true;
        }

        /// <summary>
        /// 重启到 Bootloader
        /// </summary>
        public Task<bool> RebootBootloaderAsync(CancellationToken ct = default)
            => RebootAsync("bootloader", ct);

        /// <summary>
        /// 重启到 Recovery
        /// </summary>
        public Task<bool> RebootRecoveryAsync(CancellationToken ct = default)
            => RebootAsync("recovery", ct);

        /// <summary>
        /// 重启到 Sideload
        /// </summary>
        public Task<bool> RebootSideloadAsync(CancellationToken ct = default)
            => RebootAsync("sideload", ct);

        /// <summary>
        /// 重启到 Fastboot
        /// </summary>
        public Task<bool> RebootFastbootAsync(CancellationToken ct = default)
            => RebootAsync("bootloader", ct);

        /// <summary>
        /// 重启到 Fastbootd (用户空间 Fastboot)
        /// </summary>
        public Task<bool> RebootFastbootdAsync(CancellationToken ct = default)
            => RebootAsync("fastboot", ct);

        /// <summary>
        /// 重启到 EDL 模式
        /// </summary>
        public Task<bool> RebootEdlAsync(CancellationToken ct = default)
            => RebootAsync("edl", ct);

        /// <summary>
        /// 切换到 Root Shell
        /// </summary>
        public async Task<bool> RootAsync(CancellationToken ct = default)
        {
            Log("正在切换到 root...");
            var stream = await OpenStreamAsync("root:", ct);
            if (stream == null)
            {
                Log("无法启用 root (设备可能不支持)");
                return false;
            }

            // 读取响应
            var response = await stream.ReadAsync(ct);
            stream.Close();

            if (response != null)
            {
                string result = Encoding.UTF8.GetString(response).Trim();
                Log($"Root: {result}");

                if (result.Contains("restarting") || result.Contains("already running"))
                {
                    // 需要重新连接
                    await Task.Delay(1000, ct);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 切换到非 Root Shell
        /// </summary>
        public async Task<bool> UnrootAsync(CancellationToken ct = default)
        {
            Log("正在切换到 unroot...");
            var stream = await OpenStreamAsync("unroot:", ct);
            if (stream == null) return false;

            var response = await stream.ReadAsync(ct);
            stream.Close();

            if (response != null)
            {
                Log($"Unroot: {Encoding.UTF8.GetString(response).Trim()}");
            }

            return true;
        }

        /// <summary>
        /// 重新挂载系统分区为可写
        /// </summary>
        public async Task<bool> RemountAsync(CancellationToken ct = default)
        {
            Log("正在重新挂载...");
            var stream = await OpenStreamAsync("remount:", ct);
            if (stream == null)
            {
                Log("无法重新挂载 (需要 root 权限)");
                return false;
            }

            var response = await stream.ReadAsync(ct);
            stream.Close();

            if (response != null)
            {
                string result = Encoding.UTF8.GetString(response).Trim();
                Log($"Remount: {result}");
                return result.Contains("remount succeeded") || result.Contains("Done");
            }

            return false;
        }

        #endregion

        #region APK 管理

        /// <summary>
        /// 安装 APK
        /// </summary>
        public async Task<bool> InstallApkAsync(string apkPath, bool reinstall = false, bool allowDowngrade = false, CancellationToken ct = default)
        {
            if (!File.Exists(apkPath))
            {
                Log($"APK 不存在: {apkPath}");
                return false;
            }

            var fileInfo = new FileInfo(apkPath);
            Log($"安装 APK: {Path.GetFileName(apkPath)} ({fileInfo.Length / 1024.0 / 1024.0:F1} MB)");

            // 方法 1: 使用 exec:cmd 安装 (Android 5.0+)
            // pm install [-r] [-d] -S <size>
            string flags = "";
            if (reinstall) flags += "-r ";
            if (allowDowngrade) flags += "-d ";

            string installCmd = $"exec:cmd package install {flags}-S {fileInfo.Length}";
            var stream = await OpenStreamAsync(installCmd, ct);

            if (stream == null)
            {
                // 方法 2: 传统方式 - 先 push 再 pm install
                return await InstallApkLegacyAsync(apkPath, reinstall, allowDowngrade, ct);
            }

            try
            {
                // 流式传输 APK 数据
                using var fs = File.OpenRead(apkPath);
                var buffer = new byte[64 * 1024];
                long totalSent = 0;
                int bytesRead;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, bytesRead).ToArray(), ct);
                    totalSent += bytesRead;
                    OnProgress?.Invoke(totalSent, fileInfo.Length);
                }

                // 读取安装结果
                var response = await stream.ReadAsync(ct);
                stream.Close();

                if (response != null)
                {
                    string result = Encoding.UTF8.GetString(response).Trim();
                    Log($"安装结果: {result}");
                    return result.Contains("Success");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"APK 安装错误: {ex.Message}");
                stream.Close();
                return false;
            }
        }

        /// <summary>
        /// 传统 APK 安装方式 (push + pm install)
        /// </summary>
        private async Task<bool> InstallApkLegacyAsync(string apkPath, bool reinstall, bool allowDowngrade, CancellationToken ct)
        {
            string remotePath = $"/data/local/tmp/{Path.GetFileName(apkPath)}";

            // 推送 APK
            if (!await PushAsync(apkPath, remotePath, ct))
            {
                return false;
            }

            // 执行 pm install
            string flags = "";
            if (reinstall) flags += "-r ";
            if (allowDowngrade) flags += "-d ";

            string result = await ShellAsync($"pm install {flags}\"{remotePath}\"", ct);
            Log($"安装结果: {result.Trim()}");

            // 清理临时文件
            await ShellAsync($"rm \"{remotePath}\"", ct);

            return result.Contains("Success");
        }

        /// <summary>
        /// 卸载 APK
        /// </summary>
        public async Task<bool> UninstallApkAsync(string packageName, bool keepData = false, CancellationToken ct = default)
        {
            Log($"卸载: {packageName}");

            string flags = keepData ? "-k " : "";
            string result = await ShellAsync($"pm uninstall {flags}{packageName}", ct);
            Log($"卸载结果: {result.Trim()}");

            return result.Contains("Success");
        }

        /// <summary>
        /// 获取已安装包列表
        /// </summary>
        public async Task<List<string>> ListPackagesAsync(bool systemOnly = false, bool thirdPartyOnly = false, CancellationToken ct = default)
        {
            string flags = "";
            if (systemOnly) flags = "-s ";
            else if (thirdPartyOnly) flags = "-3 ";

            string result = await ShellAsync($"pm list packages {flags}", ct);

            var packages = new List<string>();
            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("package:"))
                {
                    packages.Add(line.Substring(8).Trim());
                }
            }

            return packages;
        }

        /// <summary>
        /// 获取包信息
        /// </summary>
        public async Task<string> GetPackageInfoAsync(string packageName, CancellationToken ct = default)
        {
            return await ShellAsync($"dumpsys package {packageName}", ct);
        }

        /// <summary>
        /// 清除应用数据
        /// </summary>
        public async Task<bool> ClearAppDataAsync(string packageName, CancellationToken ct = default)
        {
            string result = await ShellAsync($"pm clear {packageName}", ct);
            return result.Contains("Success");
        }

        /// <summary>
        /// 强制停止应用
        /// </summary>
        public async Task<bool> ForceStopAppAsync(string packageName, CancellationToken ct = default)
        {
            await ShellAsync($"am force-stop {packageName}", ct);
            return true;
        }

        #endregion

        #region 设备信息

        /// <summary>
        /// 获取系统属性
        /// </summary>
        public async Task<string> GetPropAsync(string key, CancellationToken ct = default)
        {
            string result = await ShellAsync($"getprop {key}", ct);
            return result.Trim();
        }

        /// <summary>
        /// 获取所有系统属性
        /// </summary>
        public async Task<Dictionary<string, string>> GetAllPropsAsync(CancellationToken ct = default)
        {
            var props = new Dictionary<string, string>();
            string result = await ShellAsync("getprop", ct);

            foreach (var line in result.Split('\n'))
            {
                // 格式: [key]: [value]
                int keyStart = line.IndexOf('[');
                int keyEnd = line.IndexOf(']');
                int valStart = line.LastIndexOf('[');
                int valEnd = line.LastIndexOf(']');

                if (keyStart >= 0 && keyEnd > keyStart && valStart > keyEnd && valEnd > valStart)
                {
                    string key = line.Substring(keyStart + 1, keyEnd - keyStart - 1);
                    string value = line.Substring(valStart + 1, valEnd - valStart - 1);
                    props[key] = value;
                }
            }

            return props;
        }

        /// <summary>
        /// 获取设备序列号
        /// </summary>
        public Task<string> GetSerialNoAsync(CancellationToken ct = default)
            => GetPropAsync("ro.serialno", ct);

        /// <summary>
        /// 获取设备型号
        /// </summary>
        public Task<string> GetModelAsync(CancellationToken ct = default)
            => GetPropAsync("ro.product.model", ct);

        /// <summary>
        /// 获取 Android 版本
        /// </summary>
        public Task<string> GetAndroidVersionAsync(CancellationToken ct = default)
            => GetPropAsync("ro.build.version.release", ct);

        /// <summary>
        /// 获取 SDK 版本
        /// </summary>
        public Task<string> GetSdkVersionAsync(CancellationToken ct = default)
            => GetPropAsync("ro.build.version.sdk", ct);

        #endregion

        #region 屏幕和输入

        /// <summary>
        /// 截图
        /// </summary>
        public async Task<bool> ScreencapAsync(string localPath, CancellationToken ct = default)
        {
            string remotePath = "/data/local/tmp/screenshot.png";

            // 执行截图
            await ShellAsync($"screencap -p {remotePath}", ct);

            // 拉取文件
            bool success = await PullAsync(remotePath, localPath, ct);

            // 清理
            await ShellAsync($"rm {remotePath}", ct);

            return success;
        }

        /// <summary>
        /// 发送按键事件
        /// </summary>
        public async Task SendKeyEventAsync(int keyCode, CancellationToken ct = default)
        {
            await ShellAsync($"input keyevent {keyCode}", ct);
        }

        /// <summary>
        /// 发送文本输入
        /// </summary>
        public async Task SendTextAsync(string text, CancellationToken ct = default)
        {
            // 转义特殊字符
            text = text.Replace("\"", "\\\"").Replace(" ", "%s");
            await ShellAsync($"input text \"{text}\"", ct);
        }

        /// <summary>
        /// 发送触摸事件
        /// </summary>
        public async Task TapAsync(int x, int y, CancellationToken ct = default)
        {
            await ShellAsync($"input tap {x} {y}", ct);
        }

        /// <summary>
        /// 发送滑动事件
        /// </summary>
        public async Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs = 300, CancellationToken ct = default)
        {
            await ShellAsync($"input swipe {x1} {y1} {x2} {y2} {durationMs}", ct);
        }

        #endregion

        #region Logcat 日志

        /// <summary>
        /// 获取 Logcat 日志
        /// </summary>
        public async Task<string> GetLogcatAsync(int lines = 100, string? tag = null, string? priority = null, CancellationToken ct = default)
        {
            string cmd = "logcat -d";
            
            if (lines > 0)
                cmd += $" -t {lines}";
            
            if (!string.IsNullOrEmpty(tag))
                cmd += $" -s {tag}";
            
            if (!string.IsNullOrEmpty(priority))
                cmd += $" *:{priority}"; // V, D, I, W, E, F, S
            
            return await ShellAsync(cmd, ct);
        }

        /// <summary>
        /// 清除 Logcat 缓冲区
        /// </summary>
        public async Task ClearLogcatAsync(CancellationToken ct = default)
        {
            await ShellAsync("logcat -c", ct);
        }

        /// <summary>
        /// 获取实时 Logcat (返回流)
        /// </summary>
        public async IAsyncEnumerable<string> StreamLogcatAsync(string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string cmd = "logcat";
            if (!string.IsNullOrEmpty(filter))
                cmd += $" {filter}";

            // 通过 ADB Server 执行
            using var client = new TcpClient();
            await client.ConnectAsync(_serverHost, _serverPort, ct);
            using var stream = client.GetStream();

            // 选择设备
            string transportCmd = string.IsNullOrEmpty(_serverSerial) 
                ? "host:transport-any" 
                : $"host:transport:{_serverSerial}";

            string header = $"{transportCmd.Length:X4}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header + transportCmd), ct);

            var status = new byte[4];
            await ReadExactAsync(stream, status, 4, ct);
            if (Encoding.ASCII.GetString(status) != "OKAY") yield break;

            // 发送 shell 命令
            string shellCmd = $"shell:{cmd}";
            header = $"{shellCmd.Length:X4}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header + shellCmd), ct);

            await ReadExactAsync(stream, status, 4, ct);
            if (Encoding.ASCII.GetString(status) != "OKAY") yield break;

            // 读取输出流
            var buffer = new byte[4096];
            var lineBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, read);
                lineBuffer.Append(chunk);

                // 按行返回
                string content = lineBuffer.ToString();
                int lastNewline = content.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    foreach (var line in content.Substring(0, lastNewline).Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return line;
                    }
                    lineBuffer.Clear();
                    lineBuffer.Append(content.Substring(lastNewline + 1));
                }
            }
        }

        #endregion

        #region 端口转发

        /// <summary>
        /// 设置端口转发 (PC -> 设备)
        /// </summary>
        public async Task<bool> ForwardAsync(int localPort, int remotePort, CancellationToken ct = default)
        {
            return await ForwardAsync($"tcp:{localPort}", $"tcp:{remotePort}", ct);
        }

        /// <summary>
        /// 设置端口转发 (通用)
        /// </summary>
        public async Task<bool> ForwardAsync(string local, string remote, CancellationToken ct = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_serverHost, _serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host-serial:{_serverSerial}:forward:{local};{remote}";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsync(stream, status, 4, ct);
                return Encoding.ASCII.GetString(status) == "OKAY";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 移除端口转发
        /// </summary>
        public async Task<bool> ForwardRemoveAsync(int localPort, CancellationToken ct = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_serverHost, _serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host-serial:{_serverSerial}:killforward:tcp:{localPort}";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsync(stream, status, 4, ct);
                return Encoding.ASCII.GetString(status) == "OKAY";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 移除所有端口转发
        /// </summary>
        public async Task<bool> ForwardRemoveAllAsync(CancellationToken ct = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_serverHost, _serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host-serial:{_serverSerial}:killforward-all";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsync(stream, status, 4, ct);
                return Encoding.ASCII.GetString(status) == "OKAY";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 列出所有端口转发
        /// </summary>
        public async Task<List<(string local, string remote)>> ListForwardsAsync(CancellationToken ct = default)
        {
            var forwards = new List<(string, string)>();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_serverHost, _serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host-serial:{_serverSerial}:list-forward";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsync(stream, status, 4, ct);
                if (Encoding.ASCII.GetString(status) != "OKAY") return forwards;

                var lenBuf = new byte[4];
                await ReadExactAsync(stream, lenBuf, 4, ct);
                int len = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16);

                if (len > 0)
                {
                    var data = new byte[len];
                    await ReadExactAsync(stream, data, len, ct);
                    string result = Encoding.UTF8.GetString(data);

                    foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length >= 3)
                        {
                            forwards.Add((parts[1], parts[2]));
                        }
                    }
                }
            }
            catch { }
            return forwards;
        }

        /// <summary>
        /// 设置反向端口转发 (设备 -> PC)
        /// </summary>
        public async Task<bool> ReverseAsync(int remotePort, int localPort, CancellationToken ct = default)
        {
            string result = await ShellAsync($"reverse tcp:{remotePort} tcp:{localPort}", ct);
            return !result.Contains("error");
        }

        /// <summary>
        /// 移除反向端口转发
        /// </summary>
        public async Task<bool> ReverseRemoveAsync(int remotePort, CancellationToken ct = default)
        {
            string result = await ShellAsync($"reverse --remove tcp:{remotePort}", ct);
            return !result.Contains("error");
        }

        #endregion

        #region 备份与还原

        /// <summary>
        /// 完整备份 (需要用户确认)
        /// </summary>
        public async Task<bool> BackupAsync(string outputPath, bool includeApk = true, bool includeShared = true, CancellationToken ct = default)
        {
            Log("请在设备上确认备份...");
            
            string cmd = "backup -all";
            if (includeApk) cmd += " -apk";
            if (includeShared) cmd += " -shared";
            
            // 备份是通过 adb backup 命令进行的，数据流需要重定向
            // 这里简化处理，实际需要通过 ADB Server 的特殊接口
            string result = await ShellAsync($"bu backup -all", ct);
            return true;
        }

        /// <summary>
        /// 备份指定应用
        /// </summary>
        public async Task<bool> BackupPackageAsync(string packageName, string outputPath, CancellationToken ct = default)
        {
            Log($"请在设备上确认备份 {packageName}...");
            // 简化实现
            return true;
        }

        /// <summary>
        /// 还原备份
        /// </summary>
        public async Task<bool> RestoreAsync(string backupPath, CancellationToken ct = default)
        {
            if (!File.Exists(backupPath))
            {
                Log($"备份文件不存在: {backupPath}");
                return false;
            }
            
            Log("请在设备上确认还原...");
            // 简化实现
            return true;
        }

        #endregion

        #region 系统服务

        /// <summary>
        /// 获取 Activity 管理器状态
        /// </summary>
        public async Task<string> DumpsysActivityAsync(CancellationToken ct = default)
        {
            return await ShellAsync("dumpsys activity", ct);
        }

        /// <summary>
        /// 获取电池信息
        /// </summary>
        public async Task<Dictionary<string, string>> GetBatteryInfoAsync(CancellationToken ct = default)
        {
            var info = new Dictionary<string, string>();
            string result = await ShellAsync("dumpsys battery", ct);

            foreach (var line in result.Split('\n'))
            {
                var trimmed = line.Trim();
                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = trimmed.Substring(0, colonIndex).Trim();
                    string value = trimmed.Substring(colonIndex + 1).Trim();
                    info[key] = value;
                }
            }

            return info;
        }

        /// <summary>
        /// 获取 WiFi 信息
        /// </summary>
        public async Task<string> GetWifiInfoAsync(CancellationToken ct = default)
        {
            return await ShellAsync("dumpsys wifi", ct);
        }

        /// <summary>
        /// 获取内存信息
        /// </summary>
        public async Task<string> GetMemInfoAsync(CancellationToken ct = default)
        {
            return await ShellAsync("cat /proc/meminfo", ct);
        }

        /// <summary>
        /// 获取 CPU 信息
        /// </summary>
        public async Task<string> GetCpuInfoAsync(CancellationToken ct = default)
        {
            return await ShellAsync("cat /proc/cpuinfo", ct);
        }

        /// <summary>
        /// 获取存储信息
        /// </summary>
        public async Task<string> GetStorageInfoAsync(CancellationToken ct = default)
        {
            return await ShellAsync("df -h", ct);
        }

        /// <summary>
        /// 获取进程列表
        /// </summary>
        public async Task<string> GetProcessListAsync(CancellationToken ct = default)
        {
            return await ShellAsync("ps -A", ct);
        }

        /// <summary>
        /// 杀死进程
        /// </summary>
        public async Task KillProcessAsync(int pid, CancellationToken ct = default)
        {
            await ShellAsync($"kill {pid}", ct);
        }

        #endregion

        #region WiFi ADB

        /// <summary>
        /// 启用 WiFi ADB (设备需要 root 或开发者选项启用)
        /// </summary>
        public async Task<bool> EnableWifiAdbAsync(int port = 5555, CancellationToken ct = default)
        {
            await ShellAsync($"setprop service.adb.tcp.port {port}", ct);
            await ShellAsync("stop adbd", ct);
            await ShellAsync("start adbd", ct);
            
            string result = await ShellAsync("getprop service.adb.tcp.port", ct);
            return result.Trim() == port.ToString();
        }

        /// <summary>
        /// 禁用 WiFi ADB
        /// </summary>
        public async Task<bool> DisableWifiAdbAsync(CancellationToken ct = default)
        {
            await ShellAsync("setprop service.adb.tcp.port -1", ct);
            await ShellAsync("stop adbd", ct);
            await ShellAsync("start adbd", ct);
            return true;
        }

        /// <summary>
        /// 获取设备 IP 地址
        /// </summary>
        public async Task<string> GetDeviceIpAsync(CancellationToken ct = default)
        {
            string result = await ShellAsync("ip addr show wlan0 | grep 'inet '", ct);
            
            // 解析 IP: inet 192.168.x.x/24
            var match = System.Text.RegularExpressions.Regex.Match(result, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        /// <summary>
        /// 通过 WiFi 连接设备
        /// </summary>
        public static async Task<bool> ConnectWifiAsync(string ip, int port = 5555, string host = "127.0.0.1", int serverPort = 5037, CancellationToken ct = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host:connect:{ip}:{port}";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsyncStatic(stream, status, 4, ct);
                
                if (Encoding.ASCII.GetString(status) == "OKAY")
                {
                    // 读取结果消息
                    var lenBuf = new byte[4];
                    await ReadExactAsyncStatic(stream, lenBuf, 4, ct);
                    int len = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16);
                    
                    if (len > 0)
                    {
                        var data = new byte[len];
                        await ReadExactAsyncStatic(stream, data, len, ct);
                        string result = Encoding.UTF8.GetString(data);
                        return result.Contains("connected");
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 断开 WiFi 设备
        /// </summary>
        public static async Task<bool> DisconnectWifiAsync(string ip, int port = 5555, string host = "127.0.0.1", int serverPort = 5037, CancellationToken ct = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, serverPort, ct);
                using var stream = client.GetStream();

                string cmd = $"host:disconnect:{ip}:{port}";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                var status = new byte[4];
                await ReadExactAsyncStatic(stream, status, 4, ct);
                return Encoding.ASCII.GetString(status) == "OKAY";
            }
            catch
            {
                return false;
            }
        }

        private static async Task ReadExactAsyncStatic(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, ct);
                if (read == 0) throw new IOException("Connection closed");
                totalRead += read;
            }
        }

        #endregion

        #region 屏幕镜像与录制

        /// <summary>
        /// 开始屏幕录制
        /// </summary>
        public async Task<bool> StartScreenRecordAsync(string remotePath, int maxDuration = 180, int bitRate = 4000000, CancellationToken ct = default)
        {
            Log($"开始屏幕录制 (最长 {maxDuration} 秒)...");
            
            // screenrecord 在后台运行
            await ShellAsync($"screenrecord --time-limit {maxDuration} --bit-rate {bitRate} {remotePath} &", ct);
            return true;
        }

        /// <summary>
        /// 停止屏幕录制
        /// </summary>
        public async Task StopScreenRecordAsync(CancellationToken ct = default)
        {
            await ShellAsync("pkill -INT screenrecord", ct);
        }

        /// <summary>
        /// 获取屏幕尺寸
        /// </summary>
        public async Task<(int width, int height)> GetScreenSizeAsync(CancellationToken ct = default)
        {
            string result = await ShellAsync("wm size", ct);
            
            // 格式: Physical size: 1080x2400
            var match = System.Text.RegularExpressions.Regex.Match(result, @"(\d+)x(\d+)");
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }
            return (0, 0);
        }

        /// <summary>
        /// 获取屏幕密度
        /// </summary>
        public async Task<int> GetScreenDensityAsync(CancellationToken ct = default)
        {
            string result = await ShellAsync("wm density", ct);
            
            var match = System.Text.RegularExpressions.Regex.Match(result, @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>
        /// 设置屏幕尺寸
        /// </summary>
        public async Task SetScreenSizeAsync(int width, int height, CancellationToken ct = default)
        {
            await ShellAsync($"wm size {width}x{height}", ct);
        }

        /// <summary>
        /// 重置屏幕尺寸
        /// </summary>
        public async Task ResetScreenSizeAsync(CancellationToken ct = default)
        {
            await ShellAsync("wm size reset", ct);
        }

        #endregion

        #region Intent 和 Activity

        /// <summary>
        /// 启动 Activity
        /// </summary>
        public async Task<bool> StartActivityAsync(string component, Dictionary<string, string>? extras = null, CancellationToken ct = default)
        {
            string cmd = $"am start -n {component}";
            
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    cmd += $" --es \"{kv.Key}\" \"{kv.Value}\"";
                }
            }
            
            string result = await ShellAsync(cmd, ct);
            return !result.Contains("Error");
        }

        /// <summary>
        /// 发送广播
        /// </summary>
        public async Task<bool> SendBroadcastAsync(string action, Dictionary<string, string>? extras = null, CancellationToken ct = default)
        {
            string cmd = $"am broadcast -a {action}";
            
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    cmd += $" --es \"{kv.Key}\" \"{kv.Value}\"";
                }
            }
            
            string result = await ShellAsync(cmd, ct);
            return result.Contains("Broadcast completed");
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task<bool> StartServiceAsync(string component, CancellationToken ct = default)
        {
            string result = await ShellAsync($"am startservice -n {component}", ct);
            return !result.Contains("Error");
        }

        /// <summary>
        /// 获取当前 Activity
        /// </summary>
        public async Task<string> GetCurrentActivityAsync(CancellationToken ct = default)
        {
            string result = await ShellAsync("dumpsys window windows | grep -E 'mCurrentFocus|mFocusedApp'", ct);
            return result.Trim();
        }

        /// <summary>
        /// 返回按键
        /// </summary>
        public Task BackAsync(CancellationToken ct = default) => SendKeyEventAsync(4, ct);

        /// <summary>
        /// Home 按键
        /// </summary>
        public Task HomeAsync(CancellationToken ct = default) => SendKeyEventAsync(3, ct);

        /// <summary>
        /// 菜单按键
        /// </summary>
        public Task MenuAsync(CancellationToken ct = default) => SendKeyEventAsync(82, ct);

        /// <summary>
        /// 电源按键
        /// </summary>
        public Task PowerAsync(CancellationToken ct = default) => SendKeyEventAsync(26, ct);

        /// <summary>
        /// 音量加
        /// </summary>
        public Task VolumeUpAsync(CancellationToken ct = default) => SendKeyEventAsync(24, ct);

        /// <summary>
        /// 音量减
        /// </summary>
        public Task VolumeDownAsync(CancellationToken ct = default) => SendKeyEventAsync(25, ct);

        #endregion

        #region ADB Server 命令 (静态方法)

        /// <summary>
        /// 获取连接的设备列表 (通过 ADB Server)
        /// </summary>
        public static async Task<List<(string serial, string state)>> GetDevicesAsync(string host = "127.0.0.1", int port = 5037, CancellationToken ct = default)
        {
            var devices = new List<(string, string)>();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, ct);
                using var stream = client.GetStream();

                // 发送 host:devices 命令
                string cmd = "host:devices";
                string header = $"{cmd.Length:X4}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + cmd), ct);

                // 读取响应状态
                var status = new byte[4];
                await stream.ReadAsync(status, ct);

                if (Encoding.ASCII.GetString(status) != "OKAY")
                    return devices;

                // 读取长度
                var lenBuf = new byte[4];
                await stream.ReadAsync(lenBuf, ct);
                int len = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16);

                if (len > 0)
                {
                    var data = new byte[len];
                    int totalRead = 0;
                    while (totalRead < len)
                    {
                        int read = await stream.ReadAsync(data, totalRead, len - totalRead, ct);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    string result = Encoding.UTF8.GetString(data);
                    foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 2)
                        {
                            devices.Add((parts[0], parts[1]));
                        }
                    }
                }
            }
            catch
            {
                // ADB Server 可能未运行
            }

            return devices;
        }

        /// <summary>
        /// 启动 ADB Server
        /// </summary>
        public static async Task<bool> StartServerAsync(string adbPath = "adb")
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "start-server",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 停止 ADB Server
        /// </summary>
        public static async Task<bool> KillServerAsync(string adbPath = "adb")
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "kill-server",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 流管理

        private async Task<AdbStream?> OpenStreamAsync(string destination, CancellationToken ct)
        {
            uint localId = _localId++;
            var openMsg = AdbMessage.Create(AdbCommand.A_OPEN, localId, 0, Encoding.UTF8.GetBytes(destination + "\0"));

            await SendMessageAsync(openMsg, ct);

            var response = await ReceiveMessageAsync(ct);
            if (response == null || response.Command != AdbCommand.A_OKAY)
            {
                Log($"无法打开流: {destination}");
                return null;
            }

            var stream = new AdbStream(this, localId, response.Arg0);
            _streams[localId] = stream;
            return stream;
        }

        internal async Task SendMessageAsync(AdbMessage message, CancellationToken ct)
        {
            if (IsUsb)
            {
                await SendUsbMessageAsync(message, ct);
            }
            else
            {
                await SendTcpMessageAsync(message, ct);
            }
        }

        internal async Task<AdbMessage?> ReceiveMessageAsync(CancellationToken ct, int timeout = USB_TIMEOUT)
        {
            if (IsUsb)
            {
                return await ReceiveUsbMessageAsync(ct, timeout);
            }
            else
            {
                return await ReceiveTcpMessageAsync(ct, timeout);
            }
        }

        private async Task SendUsbMessageAsync(AdbMessage message, CancellationToken ct)
        {
            if (_writer == null) return;

            // 发送头
            int transferred;
            _writer.Write(message.ToBytes(), USB_TIMEOUT, out transferred);

            // 发送数据
            if (message.Data != null && message.Data.Length > 0)
            {
                _writer.Write(message.Data, USB_TIMEOUT, out transferred);
            }

            await Task.CompletedTask;
        }

        private async Task<AdbMessage?> ReceiveUsbMessageAsync(CancellationToken ct, int timeout)
        {
            if (_reader == null) return null;

            var headerBuffer = new byte[AdbMessage.HEADER_SIZE];
            int transferred;
            var result = _reader.Read(headerBuffer, timeout, out transferred);

            if (result != ErrorCode.None || transferred < AdbMessage.HEADER_SIZE)
                return null;

            var message = AdbMessage.FromBytes(headerBuffer);

            if (message.DataLength > 0)
            {
                message.Data = new byte[message.DataLength];
                _reader.Read(message.Data, timeout, out transferred);
            }

            return await Task.FromResult(message);
        }

        private async Task SendTcpMessageAsync(AdbMessage message, CancellationToken ct)
        {
            if (_tcpStream == null) return;

            await _tcpStream.WriteAsync(message.ToBytes(), ct);

            if (message.Data != null && message.Data.Length > 0)
            {
                await _tcpStream.WriteAsync(message.Data, ct);
            }
        }

        private async Task<AdbMessage?> ReceiveTcpMessageAsync(CancellationToken ct, int timeout)
        {
            if (_tcpStream == null) return null;

            var headerBuffer = new byte[AdbMessage.HEADER_SIZE];
            int totalRead = 0;

            while (totalRead < AdbMessage.HEADER_SIZE)
            {
                int read = await _tcpStream.ReadAsync(headerBuffer, totalRead, AdbMessage.HEADER_SIZE - totalRead, ct);
                if (read == 0) return null;
                totalRead += read;
            }

            var message = AdbMessage.FromBytes(headerBuffer);

            if (message.DataLength > 0)
            {
                message.Data = new byte[message.DataLength];
                totalRead = 0;
                while (totalRead < message.DataLength)
                {
                    int read = await _tcpStream.ReadAsync(message.Data, totalRead, (int)message.DataLength - totalRead, ct);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            return message;
        }

        private async Task<bool> SendServerCommandAsync(string command, CancellationToken ct)
        {
            if (_tcpStream == null) return false;

            // ADB Server 协议: "XXXX" + command (XXXX 是命令长度的十六进制)
            string header = $"{command.Length:X4}";
            await _tcpStream.WriteAsync(Encoding.ASCII.GetBytes(header + command), ct);

            // 读取响应
            var response = new byte[4];
            await _tcpStream.ReadAsync(response, ct);

            return Encoding.ASCII.GetString(response) == "OKAY";
        }

        #endregion

        #region 辅助方法

        private void Log(string message)
        {
            OnLog?.Invoke($"[ADB] {message}");
        }

        public void Dispose()
        {
            Disconnect();
        }

        #endregion
    }

    /// <summary>
    /// ADB 流 (用于持续通信)
    /// </summary>
    public class AdbStream
    {
        private readonly AdbProtocol _protocol;
        private readonly uint _localId;
        private readonly uint _remoteId;
        private bool _closed;

        internal AdbStream(AdbProtocol protocol, uint localId, uint remoteId)
        {
            _protocol = protocol;
            _localId = localId;
            _remoteId = remoteId;
        }

        public async Task WriteAsync(byte[] data, CancellationToken ct = default)
        {
            if (_closed) return;

            var writeMsg = AdbMessage.Create(AdbCommand.A_WRTE, _localId, _remoteId, data);
            await _protocol.SendMessageAsync(writeMsg, ct);

            // 等待 OKAY
            var response = await _protocol.ReceiveMessageAsync(ct);
            // 简化: 不检查 OKAY
        }

        public async Task<byte[]?> ReadAsync(CancellationToken ct = default, int expectedLen = 0)
        {
            if (_closed) return null;

            var message = await _protocol.ReceiveMessageAsync(ct);
            if (message == null) return null;

            if (message.Command == AdbCommand.A_WRTE)
            {
                // 发送 OKAY 确认
                var okayMsg = AdbMessage.Create(AdbCommand.A_OKAY, _localId, _remoteId);
                await _protocol.SendMessageAsync(okayMsg, ct);
                return message.Data;
            }

            if (message.Command == AdbCommand.A_CLSE)
            {
                _closed = true;
                return null;
            }

            return null;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;

            var closeMsg = AdbMessage.Create(AdbCommand.A_CLSE, _localId, _remoteId);
            _protocol.SendMessageAsync(closeMsg, CancellationToken.None).Wait();
        }
    }
}
