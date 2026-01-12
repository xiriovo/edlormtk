using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Qualcomm.Authentication;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// OnePlus 设备策略
    /// 支持 OnePlus 5/5T/6/6T/7/7Pro/7T/8/8Pro/8T/9/9Pro/Nord/N10/N100 系列
    /// </summary>
    public class OnePlusDeviceStrategy : StandardDeviceStrategy
    {
        public override string Name => "OnePlus";

        private OnePlusAuthStrategy? _auth;

        public override async Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default)
        {
            log("[OnePlus] 开始 OnePlus 设备认证...");

            // 获取设备序列号 (从 Sahara 阶段传递)
            string serialHex = client.ChipSerial ?? "";
            string serial;

            if (string.IsNullOrEmpty(serialHex))
            {
                serial = "123456";
                log("[OnePlus] 未获取到芯片序列号，使用默认值");
            }
            else
            {
                // 转换为十进制 (OnePlus 认证使用十进制序列号)
                if (uint.TryParse(serialHex, System.Globalization.NumberStyles.HexNumber, null, out uint serialNum))
                {
                    serial = serialNum.ToString();
                    log($"[OnePlus] 芯片序列号: 0x{serialHex} ({serial})");
                }
                else
                {
                    serial = serialHex;
                    log($"[OnePlus] 芯片序列号: 0x{serial}");
                }
            }

            // 创建认证对象
            _auth = new OnePlusAuthStrategy(log);

            // 执行认证
            bool result = await _auth.AuthenticateAsync(client, programmerPath, ct);

            if (!result)
            {
                log("[OnePlus] OnePlus 认证失败，设备可能需要官方工具或正确的密钥");
            }

            return result;
        }
    }
}
