using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// Nothing Phone 设备认证策略
    /// 基于 bkerler/edl 项目的 nothing.py 移植
    /// 适用设备: Nothing Phone 1, 2, 2a
    /// </summary>
    public class NothingAuthStrategy : IAuthStrategy
    {
        public string Name => "Nothing Phone";

        private Action<string>? _log;

        // Nothing Phone 设备配置
        private static readonly Dictionary<string, (string DeviceName, string HashVerify)> DeviceConfigs = new()
        {
            { "20111", ("Nothing Phone 1", "16386b4035411a770b12507b2e30297c0c5471230b213e6a1e1e701c6a425150") },
            { "22111", ("Nothing Phone 2", "16386b4035411a770b12507b2e30297c0c5471230b213e6a1e1e701c6a425150") },
            { "23111", ("Nothing Phone 2a", "16386b4035411a770b12507b2e30297c0c5471230b213e6a1e1e701c6a425150") },
        };

        private const string DefaultHashVerify = "16386b4035411a770b12507b2e30297c0c5471230b213e6a1e1e701c6a425150";

        public string ProjId { get; private set; } = "22111";
        public string Serial { get; private set; } = "123456";

        public NothingAuthStrategy(Action<string>? log = null)
        {
            _log = log;
        }

        /// <summary>
        /// 检测是否是 Nothing Phone 设备
        /// </summary>
        public static bool DetectNothingDevice(List<string>? supportedFunctions)
        {
            if (supportedFunctions == null) return false;
            return supportedFunctions.Contains("ntprojectverify") || supportedFunctions.Contains("checkntfeature");
        }

        public async Task<bool> AuthenticateAsync(
            FirehoseClient firehose,
            string programmerPath,
            CancellationToken ct = default)
        {
            // 获取序列号
            Serial = firehose.ChipSerial ?? "123456";
            if (!string.IsNullOrEmpty(Serial))
            {
                if (Serial.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    Serial = Serial.Substring(2);

                if (long.TryParse(Serial, System.Globalization.NumberStyles.HexNumber, null, out long serialNum))
                {
                    Serial = serialNum.ToString();
                    _log?.Invoke($"[Nothing] 芯片序列号: {Serial}");
                }
            }

            _log?.Invoke("[Nothing] 开始 Nothing Phone 认证...");

            try
            {
                // 1. 发送 checkntfeature 命令
                _log?.Invoke("[Nothing] 发送 checkntfeature 命令...");
                string checkCmd = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data>\n  <checkntfeature />\n</data>\n";

                string response = await firehose.SendRawXmlAsync(checkCmd, ct);

                if (string.IsNullOrEmpty(response))
                {
                    _log?.Invoke("[Nothing] checkntfeature 无响应");
                    return false;
                }

                if (response.Contains("NAK"))
                {
                    _log?.Invoke("[Nothing] checkntfeature 返回 NAK，设备可能不支持");
                    return false;
                }

                await Task.Delay(300, ct);

                // 2. 生成并发送 ntprojectverify 命令
                _log?.Invoke("[Nothing] 发送 ntprojectverify 命令...");
                string verifyCmd = GenerateNtProjectVerifyCommand();

                response = await firehose.SendRawXmlAsync(verifyCmd, ct);

                if (!string.IsNullOrEmpty(response))
                {
                    string respLower = response.ToLower();
                    if (respLower.Contains("authenticated") && respLower.Contains("true"))
                    {
                        _log?.Invoke("[Nothing] ✓ Nothing Phone 认证成功!");
                        return true;
                    }

                    if (response.Contains("value=\"ACK\""))
                    {
                        _log?.Invoke("[Nothing] ✓ Nothing Phone 认证成功!");
                        return true;
                    }
                }

                _log?.Invoke($"[Nothing] 认证失败: {response}");
                return false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Nothing] 认证异常: {ex.Message}");
                return false;
            }
        }

        private string GenerateNtProjectVerifyCommand()
        {
            string token1 = GenerateRandomHex(32);
            string hashverify = DefaultHashVerify;
            if (DeviceConfigs.TryGetValue(ProjId, out var config))
                hashverify = config.HashVerify;

            string serialHex = ConvertSerialToHex(Serial);
            string authResp = token1 + ProjId + serialHex + hashverify;

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(authResp));
            string fullHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            string token2 = fullHash.Substring(0, 64);
            string token3 = hashverify;

            return $"<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data>\n    " +
                   $"<ntprojectverify  token1=\"{token1}\" token2=\"{token2}\" token3=\"{token3}\"/>\n</data>\n";
        }

        private static string ConvertSerialToHex(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return "0";
            if (serial.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return serial.Substring(2).ToLower();
            if (long.TryParse(serial, out long decSerial))
                return decSerial.ToString("x");
            return serial.ToLower();
        }

        private static string GenerateRandomHex(int byteLength)
        {
            byte[] bytes = new byte[byteLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
    }
}
