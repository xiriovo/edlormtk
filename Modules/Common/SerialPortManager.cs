using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Common
{
    /// <summary>
    /// 串口管理器 - 解决端口占用问题
    /// 使用单例模式确保串口资源正确管理
    /// </summary>
    public class SerialPortManager : IDisposable
    {
        private SerialPort? _port;
        private readonly object _lock = new();
        private bool _disposed;
        private string _currentPortName = "";

        // 串口配置 (9008 EDL 模式用 USB CDC 模拟串口)
        public int BaudRate { get; set; } = 115200;  // USB CDC 模式下此值被忽略
        public int ReadTimeout { get; set; } = 10000; // 官方工具: 10秒
        public int WriteTimeout { get; set; } = 10000; // 官方工具: 10秒
        public int ReadBufferSize { get; set; } = 4 * 1024 * 1024; // 4MB (优化: 匹配最大payload)
        public int WriteBufferSize { get; set; } = 4 * 1024 * 1024; // 4MB (优化: 匹配最大payload)

        /// <summary>
        /// 当前串口是否打开
        /// </summary>
        public bool IsOpen => _port?.IsOpen ?? false;

        /// <summary>
        /// 当前端口名
        /// </summary>
        public string PortName => _currentPortName;

        /// <summary>
        /// 打开串口 (带重试机制)
        /// </summary>
        /// <param name="portName">端口名称</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="discardBuffer">是否清空缓冲区 (Sahara 协议必须设为 false，否则会丢失 Hello 包)</param>
        public bool Open(string portName, int maxRetries = 3, bool discardBuffer = false)
        {
            lock (_lock)
            {
                // 如果已打开同一端口，直接返回
                if (_port?.IsOpen == true && _currentPortName == portName)
                    return true;

                // 关闭之前的端口
                CloseInternal();

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // ⚠️ 重要：Sahara 协议不能调用 ForceReleasePort！
                        // 因为它会短暂打开端口，导致设备发送 Hello 包后又立即关闭，
                        // Hello 包会被丢弃，后续正式打开时设备不会再发送。
                        // 只有在明确需要清空缓冲区时才调用
                        if (discardBuffer)
                        {
                            // 尝试强制释放端口 (仅非 Sahara 场景)
                            ForceReleasePort(portName);
                            Thread.Sleep(100);
                        }

                        // ⚠️ 参考 temp_source_full/Qualcomm/AutoFlasher.cs 的串口配置
                        // port = new SerialPort(portName, 115200);
                        // port.ReadTimeout = 5000;
                        // port.WriteTimeout = 5000;
                        _port = new SerialPort
                        {
                            PortName = portName,
                            BaudRate = BaudRate,
                            DataBits = 8,
                            Parity = Parity.None,
                            StopBits = StopBits.One,
                            Handshake = Handshake.None,
                            // ⚠️ 参考实现使用 5000ms (5秒)
                            ReadTimeout = 5000,
                            WriteTimeout = 5000,
                            ReadBufferSize = ReadBufferSize,
                            WriteBufferSize = WriteBufferSize
                            // ⚠️ 注意：参考实现没有设置 DtrEnable/RtsEnable，使用默认值
                        };

                        _port.Open();
                        _currentPortName = portName;

                        // ⚠️ 重要：Sahara 协议设备连接后立即发送 Hello 包
                        // 如果清空缓冲区会丢失 Hello 包导致握手失败
                        if (discardBuffer)
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }

                        return true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 端口被占用，等待后重试
                        Thread.Sleep(500 * (i + 1));
                    }
                    catch (IOException)
                    {
                        // IO 错误，等待后重试
                        Thread.Sleep(300 * (i + 1));
                    }
                    catch (Exception)
                    {
                        if (i == maxRetries - 1)
                            throw;
                        Thread.Sleep(200);
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 异步打开串口
        /// </summary>
        /// <param name="portName">端口名称</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="discardBuffer">是否清空缓冲区 (Sahara 协议必须设为 false)</param>
        /// <param name="ct">取消令牌</param>
        public async Task<bool> OpenAsync(string portName, int maxRetries = 3, bool discardBuffer = false, CancellationToken ct = default)
        {
            return await Task.Run(() => Open(portName, maxRetries, discardBuffer), ct);
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            if (_port != null)
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        // 先清空缓冲区
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                        }
                        catch { }

                        // 禁用 DTR/RTS
                        try
                        {
                            _port.DtrEnable = false;
                            _port.RtsEnable = false;
                        }
                        catch { }

                        // 等待数据发送完成
                        Thread.Sleep(50);

                        _port.Close();
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        _port.Dispose();
                    }
                    catch { }
                    _port = null;
                    _currentPortName = "";
                }
            }
        }

        /// <summary>
        /// 强制释放端口 (尝试关闭占用该端口的句柄)
        /// </summary>
        private static void ForceReleasePort(string portName)
        {
            try
            {
                // 尝试短暂打开再关闭，有时可以释放残留句柄
                using var tempPort = new SerialPort(portName);
                tempPort.Open();
                tempPort.Close();
            }
            catch
            {
                // 忽略错误，端口可能已被占用
            }
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        public void Write(byte[] data, int offset, int count)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("串口未打开");

            _port.Write(data, offset, count);
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        public void Write(byte[] data)
        {
            Write(data, 0, data.Length);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("串口未打开");

            return _port.Read(buffer, offset, count);
        }

        /// <summary>
        /// 读取指定长度数据 (阻塞直到读取完成或超时)
        /// </summary>
        public byte[] ReadExact(int length, int timeout = 10000)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("串口未打开");

            var buffer = new byte[length];
            int totalRead = 0;
            var startTime = DateTime.Now;

            while (totalRead < length)
            {
                if ((DateTime.Now - startTime).TotalMilliseconds > timeout)
                    throw new TimeoutException($"读取超时: 期望 {length} 字节, 实际读取 {totalRead} 字节");

                int bytesToRead = Math.Min(_port.BytesToRead, length - totalRead);
                if (bytesToRead > 0)
                {
                    int read = _port.Read(buffer, totalRead, bytesToRead);
                    totalRead += read;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            return buffer;
        }

        /// <summary>
        /// 异步读取指定长度数据
        /// </summary>
        public async Task<byte[]> ReadExactAsync(int length, int timeout = 10000, CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("串口未打开");

            var buffer = new byte[length];
            int totalRead = 0;
            var startTime = DateTime.Now;

            while (totalRead < length)
            {
                ct.ThrowIfCancellationRequested();

                if ((DateTime.Now - startTime).TotalMilliseconds > timeout)
                    throw new TimeoutException($"读取超时: 期望 {length} 字节, 实际读取 {totalRead} 字节");

                int bytesToRead = Math.Min(_port.BytesToRead, length - totalRead);
                if (bytesToRead > 0)
                {
                    int read = _port.Read(buffer, totalRead, bytesToRead);
                    totalRead += read;
                }
                else
                {
                    await Task.Delay(1, ct);
                }
            }

            return buffer;
        }

        /// <summary>
        /// 读取精确长度的数据 (超时返回null而非抛异常)
        /// 
        /// ⚠️ 参考 temp_source_full/Qualcomm/SaharaProtocol.cs 的 ReadBytesBlocking 方法
        /// 使用轮询 BytesToRead + 短超时读取
        /// </summary>
        public async Task<byte[]?> TryReadExactAsync(int length, int timeout = 10000, CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    var buffer = new byte[length];
                    int totalRead = 0;
                    int startTime = Environment.TickCount;
                    int originalTimeout = _port.ReadTimeout;
                    
                    // ⚠️ 参考实现：单次超时 = Math.Max(100, timeout / 10)
                    _port.ReadTimeout = Math.Max(100, timeout / 10);

                    try
                    {
                        while (totalRead < length && (Environment.TickCount - startTime) < timeout)
                        {
                            if (ct.IsCancellationRequested)
                                return null;

                            // ⚠️ 关键：先检查 BytesToRead
                            int bytesAvailable = _port.BytesToRead;
                            
                            if (bytesAvailable > 0)
                            {
                                int toRead = Math.Min(length - totalRead, bytesAvailable);
                                try
                                {
                                    int read = _port.Read(buffer, totalRead, toRead);
                                    if (read > 0)
                                        totalRead += read;
                                }
                                catch (TimeoutException)
                                {
                                    // 继续循环
                                }
                            }
                            else
                            {
                                // ⚠️ 参考实现：没数据就尝试阻塞读取一小段时间
                                try
                                {
                                    int read = _port.Read(buffer, totalRead, length - totalRead);
                                    if (read > 0)
                                        totalRead += read;
                                }
                                catch (TimeoutException)
                                {
                                    // 超时后继续循环
                                    if (totalRead == 0 && (Environment.TickCount - startTime) > timeout / 2)
                                        break; // 过半时间还没数据，放弃
                                    Thread.Sleep(10);
                                }
                            }
                        }
                    }
                    finally
                    {
                        try { _port.ReadTimeout = originalTimeout; } catch { }
                    }

                    return totalRead == length ? buffer : null;
                }
                catch { return null; }
            }, ct);
        }

        /// <summary>
        /// 读取任意长度数据 (返回已读取的数据，即使不够指定长度)
        /// 
        /// ⚠️ 参考 temp_source_full/Qualcomm/SaharaProtocol.cs 的 WaitForHelloWithRetry 方法
        /// 关键：使用轮询 BytesToRead 而不是纯阻塞读取
        /// </summary>
        public async Task<byte[]?> TryReadAnyAsync(int maxLength, int timeout = 10000, CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    var buffer = new byte[maxLength];
                    int totalRead = 0;
                    int startTime = Environment.TickCount;
                    int originalTimeout = _port.ReadTimeout;
                    
                    // ⚠️ 参考实现：使用短超时 (2秒)
                    _port.ReadTimeout = 2000;

                    try
                    {
                        // ⚠️ 关键：使用轮询模式而不是纯阻塞
                        // 参考 WaitForHelloWithRetry 的实现
                        while ((Environment.TickCount - startTime) < timeout)
                        {
                            if (ct.IsCancellationRequested)
                                break;

                            // ⚠️ 关键：先检查 BytesToRead
                            int bytesAvailable = _port.BytesToRead;
                            
                            if (bytesAvailable > 0)
                            {
                                // 有数据可读
                                int toRead = Math.Min(maxLength - totalRead, bytesAvailable);
                                try
                                {
                                    int read = _port.Read(buffer, totalRead, toRead);
                                    if (read > 0)
                                    {
                                        totalRead += read;
                                        
                                        // 如果已读取包头 (8字节)，继续读取剩余数据
                                        if (totalRead >= 8)
                                        {
                                            // 短暂等待可能的后续数据
                                            Thread.Sleep(50);
                                            
                                            // 读取剩余数据
                                            int remaining = _port.BytesToRead;
                                            if (remaining > 0 && totalRead < maxLength)
                                            {
                                                int more = _port.Read(buffer, totalRead, Math.Min(remaining, maxLength - totalRead));
                                                totalRead += more;
                                            }
                                            break; // 返回数据
                                        }
                                    }
                                }
                                catch (TimeoutException)
                                {
                                    // 读取超时，继续循环
                                }
                            }
                            else
                            {
                                // ⚠️ 参考实现：没数据就等 50ms 再检查
                                Thread.Sleep(50);
                            }
                        }
                    }
                    finally
                    {
                        try { _port.ReadTimeout = originalTimeout; } catch { }
                    }

                    if (totalRead > 0)
                    {
                        var result = new byte[totalRead];
                        Array.Copy(buffer, 0, result, 0, totalRead);
                        return result;
                    }
                    return null;
                }
                catch (Exception) 
                { 
                    return null; 
                }
            }, ct);
        }

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        public void DiscardInBuffer()
        {
            _port?.DiscardInBuffer();
        }

        /// <summary>
        /// 清空发送缓冲区
        /// </summary>
        public void DiscardOutBuffer()
        {
            _port?.DiscardOutBuffer();
        }

        /// <summary>
        /// 获取可用字节数
        /// </summary>
        public int BytesToRead => _port?.BytesToRead ?? 0;

        /// <summary>
        /// 获取所有可用串口
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// 检测端口是否可用
        /// </summary>
        public static bool IsPortAvailable(string portName)
        {
            try
            {
                using var port = new SerialPort(portName);
                port.Open();
                port.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Close();
                }
                _disposed = true;
            }
        }

        ~SerialPortManager()
        {
            Dispose(false);
        }
    }
}
