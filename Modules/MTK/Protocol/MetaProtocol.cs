using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.MTK.Protocol
{
    /// <summary>
    /// META 模式类型
    /// </summary>
    public enum MetaMode
    {
        FastBoot,   // FASTBOOT 模式
        Meta,       // MAUI META 模式
        AdvMeta,    // Advanced META 模式
        Factory,    // Factory 菜单
        Ate,        // ATE Signaling Test
        AtNboot     // AT+NBOOT
    }

    /// <summary>
    /// MTK META 模式协议
    /// 用于进入各种特殊模式 (Fastboot, META, Factory 等)
    /// </summary>
    public class MetaProtocol : IDisposable
    {
        private SerialPort? _port;

        public event Action<string>? OnLog;

        // META 模式命令
        private static readonly byte[] CMD_FASTBOOT = System.Text.Encoding.ASCII.GetBytes("FASTBOOT");
        private static readonly byte[] CMD_META = System.Text.Encoding.ASCII.GetBytes("METAMETA");
        private static readonly byte[] CMD_ADVMETA = System.Text.Encoding.ASCII.GetBytes("ADVEMETA");
        private static readonly byte[] CMD_FACTORY = System.Text.Encoding.ASCII.GetBytes("FACTFACT");
        private static readonly byte[] CMD_ATE = System.Text.Encoding.ASCII.GetBytes("FACTORYM");
        private static readonly byte[] CMD_ATNBOOT = System.Text.Encoding.ASCII.GetBytes("AT+NBOOT");
        private static readonly byte[] CMD_READY = System.Text.Encoding.ASCII.GetBytes("READY");

        // 响应
        private static readonly byte[] RESP_ADVEMETA = System.Text.Encoding.ASCII.GetBytes("ATEMEVDX");
        private static readonly byte[] RESP_FASTBOOT = System.Text.Encoding.ASCII.GetBytes("TOOBTSAF");
        private static readonly byte[] RESP_META = System.Text.Encoding.ASCII.GetBytes("ATEMATEM");
        private static readonly byte[] RESP_FACTORY = System.Text.Encoding.ASCII.GetBytes("TCAFTCAF");
        private static readonly byte[] RESP_ATE = System.Text.Encoding.ASCII.GetBytes("MYROTCAF");

        public MetaProtocol()
        {
        }

        /// <summary>
        /// 连接到设备
        /// </summary>
        public bool Connect(string portName)
        {
            try
            {
                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };
                _port.Open();
                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 等待设备就绪并进入指定模式
        /// </summary>
        public async Task<bool> EnterModeAsync(MetaMode mode, int maxRetries = 30, CancellationToken ct = default)
        {
            byte[] modeCmd = mode switch
            {
                MetaMode.FastBoot => CMD_FASTBOOT,
                MetaMode.Meta => CMD_META,
                MetaMode.AdvMeta => CMD_ADVMETA,
                MetaMode.Factory => CMD_FACTORY,
                MetaMode.Ate => CMD_ATE,
                MetaMode.AtNboot => CMD_ATNBOOT,
                _ => CMD_META
            };

            Log($"Waiting for device to enter {mode} mode...");

            int retries = 0;
            while (retries < maxRetries && !ct.IsCancellationRequested)
            {
                try
                {
                    // 等待 READY 信号
                    byte[] response = ReadBytes(8, 1000);
                    
                    if (BytesEqual(response, CMD_READY) || 
                        (response.Length >= 5 && BytesEqual(response.AsSpan(0, 5).ToArray(), CMD_READY)))
                    {
                        Log("Device is READY, sending mode command...");
                        
                        // 发送模式命令
                        WriteBytes(modeCmd);
                        
                        // 等待确认响应
                        await Task.Delay(100, ct);
                        response = ReadBytes(8, 2000);

                        if (CheckResponse(response, mode))
                        {
                            Log($"Successfully entered {mode} mode");
                            
                            // META 模式需要额外的初始化
                            if (mode == MetaMode.Meta)
                            {
                                await InitMetaModeAsync(ct);
                            }
                            
                            return true;
                        }
                        else
                        {
                            Log($"Unexpected response: {BitConverter.ToString(response)}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // 继续重试
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                }

                retries++;
                await Task.Delay(300, ct);
            }

            Log("Failed to enter mode - timeout");
            return false;
        }

        /// <summary>
        /// 检查响应是否匹配模式
        /// </summary>
        private bool CheckResponse(byte[] response, MetaMode mode)
        {
            return mode switch
            {
                MetaMode.FastBoot => BytesEqual(response, RESP_FASTBOOT),
                MetaMode.Meta => BytesEqual(response, RESP_META),
                MetaMode.AdvMeta => BytesEqual(response, RESP_ADVEMETA),
                MetaMode.Factory => BytesEqual(response, RESP_FACTORY),
                MetaMode.Ate => BytesEqual(response, RESP_ATE),
                _ => false
            };
        }

        /// <summary>
        /// 初始化 META 模式
        /// </summary>
        private async Task InitMetaModeAsync(CancellationToken ct)
        {
            try
            {
                // 发送 META 初始化序列
                WriteBytes(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0xC0 });
                await Task.Delay(50, ct);
                
                WriteBytes(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0xC0 });
                await Task.Delay(50, ct);
                
                WriteBytes(new byte[] { 0x06, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0xC0, 0x00, 0x80, 0x00, 0x00 });
                
                // 读取 INFO 响应
                ReadBytes(13, 1000);
                
                // 发送断开命令
                WriteBytes(System.Text.Encoding.ASCII.GetBytes("DISCONNECT"));
                
                Log("META mode initialized");
            }
            catch (Exception ex)
            {
                Log($"META init error: {ex.Message}");
            }
        }

        /// <summary>
        /// 进入 Fastboot 模式
        /// </summary>
        public Task<bool> EnterFastbootAsync(CancellationToken ct = default)
        {
            return EnterModeAsync(MetaMode.FastBoot, 30, ct);
        }

        /// <summary>
        /// 进入 META 模式
        /// </summary>
        public Task<bool> EnterMetaAsync(CancellationToken ct = default)
        {
            return EnterModeAsync(MetaMode.Meta, 30, ct);
        }

        /// <summary>
        /// 进入 Factory 模式
        /// </summary>
        public Task<bool> EnterFactoryAsync(CancellationToken ct = default)
        {
            return EnterModeAsync(MetaMode.Factory, 30, ct);
        }

        #region 辅助方法

        private void WriteBytes(byte[] data)
        {
            _port?.Write(data, 0, data.Length);
        }

        private byte[] ReadBytes(int count, int timeout)
        {
            if (_port == null) return Array.Empty<byte>();
            
            _port.ReadTimeout = timeout;
            byte[] buffer = new byte[count];
            int read = 0;
            
            try
            {
                while (read < count)
                {
                    int r = _port.Read(buffer, read, count - read);
                    if (r == 0) break;
                    read += r;
                }
            }
            catch (TimeoutException)
            {
                // 返回已读取的数据
            }
            
            if (read < count)
            {
                byte[] result = new byte[read];
                Array.Copy(buffer, result, read);
                return result;
            }
            
            return buffer;
        }

        private bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[META] {message}");
        }

        public void Dispose()
        {
            _port?.Close();
            _port?.Dispose();
            _port = null;
        }

        #endregion
    }
}
