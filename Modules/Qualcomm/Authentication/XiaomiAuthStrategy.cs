using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// 小米认证策略 (参考 edlclient xiaomi.py)
    /// 支持设备: Redmi A1, Poco F1, Redmi 5/6/7 Pro, 7A, 8, 8A, 8A Dual, 8A Pro, Y2, S2
    /// </summary>
    public class XiaomiAuthStrategy : IAuthStrategy
    {
        private readonly Action<string>? _log;
        private readonly Func<string, string>? _manualSignCallback;

        public string Name => "Xiaomi (MiAuth Bypass)";

        // 预置签名列表 - Base64 格式 (来自 edlclient xiaomi.py)
        // 发送方式: 先发 sig 命令，再发签名数据，检查 authenticated 响应
        private static readonly string[] AuthSignsBase64 = new[]
        {
            // 签名 1 (来自 edlclient)
            "k246jlc8rQfBZ2RLYSF4Ndha1P3bfYQKK3IlQy/NoTp8GSz6l57RZRfmlwsbB99sUW/sgfaWj89//dvDl6Fiwso" +
            "+XXYSSqF2nxshZLObdpMLTMZ1GffzOYd2d/ToryWChoK8v05ZOlfn4wUyaZJT4LHMXZ0NVUryvUbVbxjW5SkLpKDKwkMfnxnEwaOddmT" +
            "/q0ip4RpVk4aBmDW4TfVnXnDSX9tRI+ewQP4hEI8K5tfZ0mfyycYa0FTGhJPcTTP3TQzy1Krc1DAVLbZ8IqGBrW13YWN" +
            "/cMvaiEzcETNyA4N3kOaEXKWodnkwucJv2nEnJWTKNHY9NS9f5Cq3OPs4pQ==",
            
            // 签名 2 (来自 edlclient)
            "vzXWATo51hZr4Dh+a5sA/Q4JYoP4Ee3oFZSGbPZ2tBsaMupn" +
            "+6tPbZDkXJRLUzAqHaMtlPMKaOHrEWZysCkgCJqpOPkUZNaSbEKpPQ6uiOVJpJwA" +
            "/PmxuJ72inzSPevriMAdhQrNUqgyu4ATTEsOKnoUIuJTDBmzCeuh/34SOjTdO4Pc+s3ORfMD0TX+WImeUx4c9xVdSL/xirPl" +
            "/BouhfuwFd4qPPyO5RqkU/fevEoJWGHaFjfI302c9k7EpfRUhq1z+wNpZblOHuj0B3/7VOkK8KtSvwLkmVF" +
            "/t9ECiry6G5iVGEOyqMlktNlIAbr2MMYXn6b4Y3GDCkhPJ5LUkQ=="
        };

        // 预置签名列表 - Hex 格式 (旧版兼容)
        private static readonly string[] AuthSignsHex = new[]
        {
            // Sig 1: 通用签名
            "BF35D6013A39D6166BE0387E6B9B00FD0E096283F811EDE81594866CF676B41B1A32EA67FBAB4F6D90E45C944B53302A1DA32D94F30A68E1EB116672B02920089AA938F91464D6926C42A93D0EAE88E549A49C00FCF9B1B89EF68A7CD23DEBEB88C01D850ACD52A832BB80134C4B0E2A7A1422E2530C19B309EBA1FF7E123A34DD3B83DCFACDCE45F303D135FE58899E531E1CF7155D48BFF18AB3E5FC1A2E85FBB015DE2A3CFC8EE51AA453F7DEBC4A095861DA1637C8DF4D9CF64EC4A5F45486AD73FB036965B94E1EE8F4077FFB54E90AF0AB52BF02E499517FB7D1028ABCBA1B98951843B2A8C964B4D94801BAF630C6179FA6F86371830A484F2792D491",
            // Sig 2
            "600000010800936E3A8E573CAD07C167644B61217835D85AD4FDDB7D840A2B7225432FCDA13A7C192CFA979ED16517E6970B1B07DF6C516FEC81F6968FCF7FFDDBC397A162C2CA3E5D76124AA1769F1B2164B39B76930B4CC67519F7F339877677F4E8AF25828682BCBF4E593A57E7E30532699253E0B1CC5D9D0D554AF2BD46D56F18D6E5290BA4A0CAC2431F9F19C4C1A39D7664FFAB48A9E11A559386819835B84DF5675E70D25FDB5123E7B040FE21108F0AE6D7D9D267F2C9C61AD054C68493DC4D33F74D0CF2D4AADCD430152DB67C22A181AD6D7761637F70CBDA884CDC11337203837790E6845CA5A8767930B9C26FDA71272564CA34763D352F5FE4",
            // Sig 3
            "936E3A8E573CAD07C167644B61217835D85AD4FDDB7D840A2B7225432FCDA13A7C192CFA979ED16517E6970B1B07DF6C516FEC81F6968FCF7FFDDBC397A162C2CA3E5D76124AA1769F1B2164B39B76930B4CC67519F7F339877677F4E8AF25828682BCBF4E59600000110532699253E0B1CC5D9D0D554AF2BD46D56F18D6E5290BA4A0CAC2431F9F19C4C1A39D7664FFAB48A9E11A559386819835B84DF5675E70D25FDB5123E7B040FE21108F0AE6D7D9D267F2C9C61AD054C68493DC4D33F74D0CF2D4AADCD430152DB67C22A181AD6D7761637F70CBDA884CDC11337203837790E6845CA5A8767930B9C26FDA71272564CA34763D352F5FE42AB738FB38A5"
        };

        public XiaomiAuthStrategy(Action<string>? log = null, Func<string, string>? manualSignCallback = null)
        {
            _log = log;
            _manualSignCallback = manualSignCallback;
        }

        public async Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default)
        {
            _log?.Invoke("[MiAuth] 正在尝试小米免授权认证...");

            try
            {
                // 1. Ping 设备确保连接活跃
                client.Ping();
                await Task.Delay(100, ct);

                // 2. 尝试 edlclient 方式 (Base64 签名 + xmlsend)
                _log?.Invoke("[MiAuth] 尝试 edlclient 签名方式...");
                int index = 1;
                foreach (var base64Sign in AuthSignsBase64)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        // 先发送 sig 命令
                        string sigCmd = "<?xml version=\"1.0\" ?><data><sig TargetName=\"sig\" size_in_bytes=\"256\" verbose=\"1\"/></data>";
                        var sigResponse = await client.SendRawXmlAsync(sigCmd, ct);
                        
                        if (sigResponse == null || sigResponse.Contains("NAK"))
                        {
                            index++;
                            continue;
                        }
                        
                        // 发送签名数据
                        byte[] signData = Convert.FromBase64String(base64Sign);
                        var authResponse = await client.SendRawBytesAndGetResponseAsync(signData, ct);
                        
                        if (authResponse != null && 
                            (authResponse.ToLower().Contains("authenticated") || 
                             authResponse.Contains("ACK")))
                        {
                            // 验证认证是否真正成功
                            await Task.Delay(200, ct);
                            if (await client.PingAsync(ct))
                            {
                                _log?.Invoke($"[MiAuth] ✅ edlclient 签名 #{index} 验证成功！设备已解锁。");
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 继续尝试下一个
                    }
                    index++;
                }

                // 3. 尝试旧版 Hex 签名方式
                _log?.Invoke("[MiAuth] 尝试旧版 Hex 签名方式...");
                index = 1;
                foreach (var hexSign in AuthSignsHex)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (client.SendSignature(HexStringToBytes(hexSign)))
                        {
                            await Task.Delay(200, ct);
                            if (await client.PingAsync(ct))
                            {
                                _log?.Invoke($"[MiAuth] ✅ Hex 签名 #{index} 验证成功！设备已解锁。");
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 继续尝试下一个
                    }
                    index++;
                }

                _log?.Invoke("[MiAuth] 内置签名尝试失败，尝试获取 Blob...");

                // 4. 尝试获取 Blob (Challenge)
                string? blob = await client.SendXmlCommandWithAttributeResponseAsync(
                    "<?xml version=\"1.0\" ?><data><sig TargetName=\"req\" /></data>",
                    "value",
                    10,
                    ct
                );

                if (string.IsNullOrEmpty(blob))
                {
                    _log?.Invoke("[MiAuth] ❌ 无法获取 Blob，认证失败。");
                    return false;
                }

                // Base64 转 Hex
                string displayBlob = blob;
                if (IsBase64String(blob))
                {
                    try
                    {
                        byte[] blobBytes = Convert.FromBase64String(PadBase64(blob));
                        displayBlob = BitConverter.ToString(blobBytes).Replace("-", "");
                    }
                    catch { }
                }

                _log?.Invoke($"[MiAuth] Blob: {displayBlob.Substring(0, Math.Min(displayBlob.Length, 32))}...");

                // 5. 如果有手动签名回调，请求用户输入
                if (_manualSignCallback != null)
                {
                    string? userSignHex = _manualSignCallback(displayBlob);

                    if (!string.IsNullOrEmpty(userSignHex))
                    {
                        try
                        {
                            if (client.SendSignature(HexStringToBytes(userSignHex.Trim())))
                            {
                                _log?.Invoke("[MiAuth] ✅ 手动签名验证成功！");
                                return true;
                            }
                            _log?.Invoke("[MiAuth] ❌ 手动签名验证失败。");
                        }
                        catch (Exception ex)
                        {
                            _log?.Invoke($"[MiAuth] 签名格式错误: {ex.Message}");
                        }
                    }
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[MiAuth] 异常: {ex.Message}");
                return false;
            }
        }

        private static bool IsBase64String(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
                    return false;
            }
            return (s.Length % 4 == 0) || s.Length > 20;
        }

        private static string PadBase64(string s)
        {
            int pad = 4 - (s.Length % 4);
            if (pad < 4) return s + new string('=', pad);
            return s;
        }

        private static byte[] HexStringToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
            hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim();
            if (hex.Length % 2 != 0) throw new ArgumentException("Hex string length must be even.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }
}
