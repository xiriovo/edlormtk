using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// OnePlus 设备认证策略
    /// 支持 OnePlus 5/5T/6/6T/7/7Pro/7T/8/8Pro/8T/9/9Pro/Nord/N10/N100
    /// 基于 bkerler/edl 项目移植
    /// </summary>
    public class OnePlusAuthStrategy : IAuthStrategy
    {
        private readonly Action<string>? _log;
        private FirehoseClient? _client;
        private string _serial = "123456";
        private string _projId = "";
        private int _version = 1;

        public string Name => "OnePlus (Demacia/SetProjModel)";

        // 设备配置: projId -> (version, cm, paramMode)
        private static readonly Dictionary<string, (int Version, string? Cm, int ParamMode)> DeviceConfigs = new()
        {
            // OP5-7T 系列 (Version 1)
            { "16859", (1, null, 0) },  // OP5
            { "17801", (1, null, 0) },  // OP5T
            { "17819", (1, null, 0) },  // OP6
            { "18801", (1, null, 0) },  // OP6T
            { "18811", (1, null, 0) },  // OP6T T-Mo
            { "18857", (1, null, 0) },  // OP7
            { "18821", (1, null, 0) },  // OP7 Pro
            { "18825", (1, null, 0) },  // OP7 Pro 5G
            { "18827", (1, null, 0) },  // OP7 Pro 5G EE
            { "18831", (1, null, 0) },  // OP7 Pro T-Mo
            { "18865", (1, null, 0) },  // OP7T
            { "19801", (1, null, 0) },  // OP7T Pro
            { "19861", (1, null, 0) },  // OP7T Pro 5G
            { "19863", (1, null, 0) },  // OP7T T-Mo

            // OP8/9 系列 (Version 2)
            { "19821", (2, "0cffee8a", 0) },  // OP8
            { "19855", (2, "6d9215b4", 0) },  // OP8 T-Mo
            { "19811", (2, "40217c07", 0) },  // OP8 Pro
            { "19805", (2, "1a5ec176", 0) },  // OP8T
            { "20801", (2, "eacf50e7", 0) },  // Nord
            { "19815", (2, "9c151c7f", 0) },  // OP9 Pro
            { "19825", (2, "0898dcd6", 0) },  // OP9

            // N10/N100 系列 (Version 3)
            { "20885", (3, "3a403a71", 1) },  // N10 5G Metro
            { "20886", (3, "b8bd9e39", 1) },  // N10 5G Global
            { "20888", (3, "142f1bd7", 1) },  // N10 5G TMO
            { "20880", (3, "6ccf5913", 1) },  // N100 Metro
            { "20881", (3, "fa9ff378", 1) },  // N100 Global
        };

        // AES 密钥组件 (V1/V2)
        private static readonly byte[] AesKeyPrefix1 = { 0x10, 0x45, 0x63, 0x87, 0xE3, 0x7E, 0x23, 0x71 };
        private static readonly byte[] AesKeySuffix1 = { 0xA2, 0xD4, 0xA0, 0x74, 0x0F, 0xD3, 0x28, 0x96 };
        private static readonly byte[] AesIv1 = { 0x9D, 0x61, 0x4A, 0x1E, 0xAC, 0x81, 0xC9, 0xB2, 0xD3, 0x76, 0xD7, 0x49, 0x31, 0x03, 0x63, 0x79 };

        // Demacia AES 密钥组件
        private static readonly byte[] AesKeyPrefixDemacia = { 0x01, 0x63, 0xA0, 0xD1, 0xFD, 0xE2, 0x67, 0x11 };
        private static readonly byte[] AesKeySuffixDemacia = { 0x48, 0x27, 0xC2, 0x08, 0xFB, 0xB0, 0xE6, 0xF0 };
        private static readonly byte[] AesIvDemacia = { 0x96, 0xE0, 0x79, 0x0C, 0xAE, 0x2B, 0xB4, 0xAF, 0x68, 0x4C, 0x36, 0xCB, 0x0B, 0xEC, 0x49, 0xCE };

        // V3 AES 密钥组件
        private static readonly byte[] AesKeyPrefixV3 = { 0x46, 0xA5, 0x97, 0x30, 0xBB, 0x0D, 0x41, 0xE8 };
        private static readonly byte[] AesIvV3 = { 0xDC, 0x91, 0x0D, 0x88, 0xE3, 0xC6, 0xEE, 0x65, 0xF0, 0xC7, 0x44, 0xB4, 0x02, 0x30, 0xCE, 0x40 };

        // ProdKey
        private const string ProdKeyOld = "b2fad511325185e5";
        private const string ProdKeyNew = "7016147d58e8c038";
        private const string RandomPostfixV1 = "8MwDdWXZO7sj0PF3";
        private const string RandomPostfixV3 = "c75oVnz8yUgLZObh";

        public OnePlusAuthStrategy(Action<string>? log = null)
        {
            _log = log;
        }

        public async Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default)
        {
            _client = client;
            
            // 获取设备序列号
            string? serialHex = client.ChipSerial;
            if (!string.IsNullOrEmpty(serialHex) && uint.TryParse(serialHex, System.Globalization.NumberStyles.HexNumber, null, out uint serialNum))
            {
                _serial = serialNum.ToString();
            }

            _log?.Invoke($"[OnePlus] 开始认证，序列号: {_serial}");

            // 尝试获取 projId
            await ReadProjIdAsync(ct);

            if (string.IsNullOrEmpty(_projId))
            {
                _log?.Invoke("[OnePlus] 无法获取 projid，使用默认值 18821 (OnePlus 7 Pro)");
                _projId = "18821";
            }

            // 尝试主 projId
            if (await TryAuthenticateWithProjIdAsync(_projId, ct))
            {
                return true;
            }

            // 尝试备选 projId
            var alternatives = GetAlternativeProjIds(_projId);
            foreach (var altProjId in alternatives)
            {
                ct.ThrowIfCancellationRequested();
                _log?.Invoke($"[OnePlus] 尝试备选 projid: {altProjId}");
                if (await TryAuthenticateWithProjIdAsync(altProjId, ct))
                {
                    _projId = altProjId;
                    return true;
                }
            }

            _log?.Invoke("[OnePlus] ❌ 所有认证尝试均失败");
            return false;
        }

        private async Task ReadProjIdAsync(CancellationToken ct)
        {
            if (_client == null) return;

            try
            {
                // 尝试 getprjversion 命令
                _log?.Invoke("[OnePlus] 使用 getprjversion 命令...");

                string xml = "<?xml version=\"1.0\" ?><data><getprjversion /></data>";
                string? response = await _client.SendRawXmlAsync(xml, ct);

                if (!string.IsNullOrEmpty(response))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        response,
                        @"(?:prjversion|PrjVersion|projid)=""(\d+)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        string value = match.Groups[1].Value;
                        if (int.TryParse(value, out int projidNum) && projidNum >= 10000 && projidNum <= 99999)
                        {
                            _projId = value;
                            _log?.Invoke($"[OnePlus] ✓ 获取 projid: {value}");

                            if (DeviceConfigs.TryGetValue(value, out var config))
                            {
                                _version = config.Version;
                                _log?.Invoke($"[OnePlus] 设备版本: V{config.Version}");
                            }
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[OnePlus] getprjversion 异常: {ex.Message}");
            }

            // 基于芯片信息猜测
            GuessProjectIdFromChipInfo();
        }

        private void GuessProjectIdFromChipInfo()
        {
            if (_client == null) return;

            string hwid = _client.ChipHwId?.ToLower() ?? "";
            string pkHash = _client.ChipPkHash?.ToLower() ?? "";

            if (pkHash.StartsWith("2acf3a85") || pkHash.StartsWith("8aabc662"))
            {
                _log?.Invoke("[OnePlus] 检测到 SM8150 (基于 PK Hash)");
                _projId = "18821"; // OP7 Pro
            }
            else if (pkHash.StartsWith("c0c66e27"))
            {
                _projId = "18801"; // OP6T
            }
            else if (hwid.Contains("e1500a00") || hwid.Contains("000a50e1"))
            {
                _projId = "18821"; // SM8150 -> OP7 Pro
            }
            else if (hwid.Contains("e1b00800") || hwid.Contains("0008b0e1"))
            {
                _projId = "18801"; // SDM845 -> OP6T
            }
        }

        private static List<string> GetAlternativeProjIds(string primaryProjId)
        {
            var alternatives = new List<string>();

            // OP7 Pro 系列
            if (primaryProjId is "18821" or "18825" or "18827" or "18831")
            {
                alternatives.AddRange(new[] { "18821", "18825", "18827", "18831", "18865", "19801" });
            }
            // OP7T 系列
            else if (primaryProjId is "18865" or "19801" or "19863" or "19861")
            {
                alternatives.AddRange(new[] { "18865", "19801", "19863", "19861", "18821", "18825" });
            }
            // OP6 系列
            else if (primaryProjId is "18801" or "18811" or "17819" or "17801")
            {
                alternatives.AddRange(new[] { "18801", "18811", "17819", "17801" });
            }

            alternatives.Remove(primaryProjId);
            return alternatives;
        }

        private async Task<bool> TryAuthenticateWithProjIdAsync(string projId, CancellationToken ct)
        {
            if (_client == null) return false;

            var config = DeviceConfigs.GetValueOrDefault(projId, (1, null, 0));
            _version = config.Version;
            string modelId = config.Cm ?? projId;

            _log?.Invoke($"[OnePlus] 尝试: ProjId={projId}, Version={_version}");

            try
            {
                if (_version == 3)
                {
                    return await AuthenticateV3Async(modelId, ct);
                }
                else
                {
                    return await AuthenticateV1V2Async(modelId, ct);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[OnePlus] 认证异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> AuthenticateV1V2Async(string modelId, CancellationToken ct)
        {
            if (_client == null) return false;

            string pk = GenerateRandomPk();
            string prodKey = GetProdKey(_projId);

            // 1. Demacia
            _log?.Invoke("[OnePlus] 步骤1: demacia 验证...");
            var (demPk, demToken) = GenerateDemaciaToken(pk);
            string demCmd = $"demacia token=\"{demToken}\" pk=\"{demPk}\"";
            string? demResp = await _client.SendRawXmlAsync(demCmd, ct);

            if (!string.IsNullOrEmpty(demResp) && demResp.Contains("verify_res=\"0\""))
            {
                _log?.Invoke("[OnePlus] ✓ demacia 验证成功");
            }
            else
            {
                _log?.Invoke("[OnePlus] demacia 失败，继续尝试 setprojmodel...");
            }

            // 2. SetProjModel
            _log?.Invoke($"[OnePlus] 步骤2: setprojmodel (projid={modelId})...");
            var (projPk, projToken) = GenerateSetProjModelToken(modelId, pk, prodKey);
            string projCmd = $"setprojmodel token=\"{projToken}\" pk=\"{projPk}\"";
            string? projResp = await _client.SendRawXmlAsync(projCmd, ct);

            if (!string.IsNullOrEmpty(projResp) &&
                projResp.Contains("model_check=\"0\"") &&
                projResp.Contains("auth_token_verify=\"0\""))
            {
                _log?.Invoke("[OnePlus] ✅ setprojmodel 验证成功!");
                return true;
            }

            if (!string.IsNullOrEmpty(projResp) && projResp.Contains("value=\"ACK\""))
            {
                _log?.Invoke("[OnePlus] ✅ setprojmodel 返回 ACK");
                return true;
            }

            _log?.Invoke($"[OnePlus] setprojmodel 失败: {projResp}");
            return false;
        }

        private async Task<bool> AuthenticateV3Async(string modelId, CancellationToken ct)
        {
            if (_client == null) return false;

            // 1. 获取 device_timestamp
            _log?.Invoke("[OnePlus] 获取 device_timestamp...");
            string? startResp = await _client.SendRawXmlAsync("setprocstart", ct);

            if (string.IsNullOrEmpty(startResp) || !startResp.Contains("device_timestamp"))
            {
                _log?.Invoke($"[OnePlus] setprocstart 失败");
                return false;
            }

            string? timestamp = ExtractAttribute(startResp, "device_timestamp");
            if (string.IsNullOrEmpty(timestamp))
            {
                _log?.Invoke("[OnePlus] 无法解析 device_timestamp");
                return false;
            }

            _log?.Invoke($"[OnePlus] device_timestamp: {timestamp}");

            // 2. SetSwProjModel
            string pk = GenerateRandomPk();
            string prodKey = GetProdKey(_projId);

            var (swPk, swToken) = GenerateSetSwProjModelToken(modelId, pk, prodKey, timestamp);
            string swCmd = $"setswprojmodel token=\"{swToken}\" pk=\"{swPk}\"";
            string? swResp = await _client.SendRawXmlAsync(swCmd, ct);

            if (!string.IsNullOrEmpty(swResp) &&
                swResp.Contains("model_check=\"0\"") &&
                swResp.Contains("auth_token_verify=\"0\""))
            {
                _log?.Invoke("[OnePlus] ✅ setswprojmodel 验证成功!");
                return true;
            }

            _log?.Invoke($"[OnePlus] setswprojmodel 失败: {swResp}");
            return false;
        }

        private static string GenerateRandomPk()
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            using var rng = RandomNumberGenerator.Create();
            byte[] data = new byte[16];
            rng.GetBytes(data);

            var sb = new StringBuilder(16);
            foreach (byte b in data)
            {
                sb.Append(chars[b % chars.Length]);
            }
            return sb.ToString();
        }

        private static string GetProdKey(string projId)
        {
            return projId is "18825" or "18801" ? ProdKeyOld : ProdKeyNew;
        }

        private (string pk, string token) GenerateDemaciaToken(string pk)
        {
            string serial = _serial.PadLeft(10, '0');
            string hash1 = "2e7006834dafe8ad" + serial + "a6674c6b039707ff";

            byte[] hashBytes = ComputeSha256(Encoding.UTF8.GetBytes(hash1));
            byte[] data = new byte[48];
            Encoding.ASCII.GetBytes("907heavyworkload").CopyTo(data, 0);
            hashBytes.CopyTo(data, 16);

            byte[] padded = new byte[256];
            data.CopyTo(padded, 0);

            string token = EncryptAesCbc(padded, pk, true);
            return (pk, token);
        }

        private (string pk, string token) GenerateSetProjModelToken(string modelId, string pk, string prodKey)
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            string h1 = prodKey + modelId + RandomPostfixV1;
            string modelVerifyHashToken = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h1))).ToUpper();

            string version = "guacamoles_21_O.22_191107";
            string cf = "0";
            string h2 = "c4b95538c57df231" + modelId + cf + _serial + version + timestamp + modelVerifyHashToken + "5b0217457e49381b";
            string secret = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h2))).ToUpper();

            string[] items = { modelId, RandomPostfixV1, modelVerifyHashToken, version, cf, _serial, timestamp, secret };
            string dataStr = string.Join(",", items);

            byte[] padded = new byte[256];
            Encoding.UTF8.GetBytes(dataStr).CopyTo(padded, 0);

            string token = EncryptAesCbc(padded, pk, false);
            return (pk, token);
        }

        private (string pk, string token) GenerateSetSwProjModelToken(string modelId, string pk, string prodKey, string deviceTimestamp)
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            string h1 = prodKey + modelId + RandomPostfixV3;
            string modelVerifyHashToken = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h1))).ToUpper();

            string version = "billie8_14_E.01_201028";
            string h2 = prodKey + modelId + _serial + version + timestamp + modelVerifyHashToken + "8f7359c8a2951e8c";
            string secret = BytesToHex(ComputeSha256(Encoding.UTF8.GetBytes(h2))).ToUpper();

            string deviceId;
            try
            {
                deviceId = Convert.ToInt32(modelId, 16).ToString();
            }
            catch
            {
                deviceId = modelId;
            }

            string[] items = { modelId, RandomPostfixV3, modelVerifyHashToken, "0", "0", version, _serial, deviceId, timestamp, secret };
            string dataStr = string.Join(",", items);

            byte[] padded = new byte[512];
            Encoding.UTF8.GetBytes(dataStr).CopyTo(padded, 0);

            string token = EncryptAesCbcV3(padded, pk, deviceTimestamp);
            return (pk, token);
        }

        private static string EncryptAesCbc(byte[] data, string pk, bool isDemacia)
        {
            byte[] keyPrefix = isDemacia ? AesKeyPrefixDemacia : AesKeyPrefix1;
            byte[] keySuffix = isDemacia ? AesKeySuffixDemacia : AesKeySuffix1;
            byte[] iv = isDemacia ? AesIvDemacia : AesIv1;

            byte[] key = new byte[32];
            keyPrefix.CopyTo(key, 0);
            Encoding.UTF8.GetBytes(pk).CopyTo(key, 8);
            keySuffix.CopyTo(key, 24);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
            return BytesToHex(encrypted).ToUpper();
        }

        private static string EncryptAesCbcV3(byte[] data, string pk, string deviceTimestamp)
        {
            byte[] key = new byte[32];
            AesKeyPrefixV3.CopyTo(key, 0);
            Encoding.UTF8.GetBytes(pk).CopyTo(key, 8);

            long ts = long.Parse(deviceTimestamp);
            BitConverter.GetBytes(ts).CopyTo(key, 24);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = AesIvV3;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
            return BytesToHex(encrypted).ToUpper();
        }

        private static byte[] ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static string? ExtractAttribute(string xml, string attrName)
        {
            int start = xml.IndexOf(attrName + "=\"");
            if (start < 0) return null;

            start += attrName.Length + 2;
            int end = xml.IndexOf("\"", start);
            if (end < 0) return null;

            return xml.Substring(start, end - start);
        }
    }
}
