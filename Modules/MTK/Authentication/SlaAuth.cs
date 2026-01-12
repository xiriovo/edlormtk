// ============================================================================
// MultiFlash TOOL - MediaTek SLA Authentication
// 联发科 SLA 认证 | MTK SLA認証 | MTK SLA 인증
// ============================================================================
// [EN] Serial Link Authorization (SLA) bypass for MTK BROM/Preloader
//      RSA signature verification with multiple key support
// [中文] 联发科 BROM/Preloader 串口链路授权 (SLA) 绕过
//       多密钥支持的 RSA 签名验证
// [日本語] MTK BROM/Preloader用シリアルリンク認証（SLA）バイパス
//         複数キーサポートによるRSA署名検証
// [한국어] MTK BROM/Preloader용 시리얼 링크 인증(SLA) 우회
//         다중 키 지원 RSA 서명 검증
// [Español] Bypass de autorización de enlace serial (SLA) para MTK BROM/Preloader
//           Verificación de firma RSA con soporte de múltiples claves
// [Русский] Обход авторизации последовательного канала (SLA) для MTK BROM/Preloader
//           Проверка подписи RSA с поддержкой нескольких ключей
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;

namespace tools.Modules.MTK.Authentication
{
    /// <summary>
    /// SLA Key / SLA 密钥 / SLAキー / SLA 키
    /// </summary>
    public class SlaKey
    {
        public string Name { get; set; } = "";
        public BigInteger N { get; set; }  // Modulus / 模数
        public BigInteger E { get; set; }  // Public exponent / 公钥指数
        public BigInteger D { get; set; }  // Private exponent / 私钥指数
    }

    /// <summary>
    /// SLA Authentication Module / SLA 认证模块
    /// SLA認証モジュール / SLA 인증 모듈
    /// </summary>
    public class SlaAuth
    {
        private readonly List<SlaKey> _bromKeys = new();
        private readonly List<RSA> _daKeys = new();

        public SlaAuth()
        {
            InitializeBromKeys();
        }

        /// <summary>
        /// 初始化 BROM SLA 密钥
        /// 注意：这些密钥需要从实际设备或固件中提取
        /// </summary>
        private void InitializeBromKeys()
        {
            // 示例密钥结构 - 实际使用时需要替换为真实密钥
            // 这些是公开的测试密钥，不适用于生产设备

            // Key 1 - Generic
            _bromKeys.Add(new SlaKey
            {
                Name = "Generic",
                N = BigInteger.Parse("00" +
                    "C43469A90B2480EB1BB865EB16C8F293D72E5EEF" +
                    "D2BEC93BFC9BA789DECBFCEA8E1C0D88ABE80C45" +
                    "B1F6B8AF8D2C9D9C4F9C3D3D80B8D92B1A0E6F0D" +
                    "0E3E2D1C0B0A090807060504030201", System.Globalization.NumberStyles.HexNumber),
                E = BigInteger.Parse("010001", System.Globalization.NumberStyles.HexNumber),
                D = BigInteger.Parse("00" +
                    "D2BEC93BFC9BA789DECBFCEA8E1C0D88ABE80C45" +
                    "C43469A90B2480EB1BB865EB16C8F293D72E5EEF" +
                    "B1F6B8AF8D2C9D9C4F9C3D3D80B8D92B1A0E6F0D" +
                    "0E3E2D1C0B0A090807060504030201", System.Globalization.NumberStyles.HexNumber)
            });
        }

        /// <summary>
        /// 获取 BROM 密钥列表
        /// </summary>
        public IEnumerable<SlaKey> GetBromKeys() => _bromKeys;

        /// <summary>
        /// 签名挑战数据
        /// </summary>
        /// <param name="challenge">挑战数据</param>
        /// <param name="hwCode">硬件代码 (用于选择密钥)</param>
        /// <returns>签名数据或 null (如果无法签名)</returns>
        public byte[]? SignChallenge(byte[] challenge, ushort hwCode)
        {
            if (challenge == null || challenge.Length == 0)
                return null;

            // 尝试使用所有 BROM 密钥
            foreach (var key in _bromKeys)
            {
                try
                {
                    return GenerateBromResponse(challenge, key);
                }
                catch
                {
                    continue;
                }
            }

            // 如果 BROM 密钥都失败，尝试 DA 密钥
            foreach (var rsaKey in _daKeys)
            {
                try
                {
                    return GenerateDaSignature(challenge, rsaKey);
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// 生成 BROM SLA 响应
        /// 根据官方 SP Flash Tool flash.dll 逆向分析:
        /// 1. 字节交换输入 (每2字节交换)
        /// 2. 只使用前16字节进行 RSA 签名 (PKCS1 padding)
        /// 3. 字节交换输出
        /// </summary>
        public byte[] GenerateBromResponse(byte[] challenge, SlaKey key)
        {
            // 官方只使用前16字节
            const int SIGN_LENGTH = 16;
            
            // 交换字节对 (每2字节交换) - 输入
            byte[] swapped = new byte[Math.Min(challenge.Length, SIGN_LENGTH)];
            int swapLen = Math.Min(challenge.Length, SIGN_LENGTH);
            for (int i = 0; i < swapLen - 1; i += 2)
            {
                swapped[i] = challenge[i + 1];
                swapped[i + 1] = challenge[i];
            }
            // 处理奇数长度情况
            if (swapLen % 2 == 1)
            {
                swapped[swapLen - 1] = challenge[swapLen - 1];
            }

            // RSA 签名 (使用私钥指数 D 和模数 N) - 只签名前16字节
            byte[] signature = CustomizedSign(key.N, key.D, swapped);

            // 再次交换字节对 - 输出
            byte[] result = new byte[signature.Length];
            for (int i = 0; i < signature.Length - 1; i += 2)
            {
                result[i] = signature[i + 1];
                result[i + 1] = signature[i];
            }
            // 处理奇数长度情况
            if (signature.Length % 2 == 1)
            {
                result[signature.Length - 1] = signature[signature.Length - 1];
            }

            return result;
        }

        /// <summary>
        /// 生成 BROM SLA 响应 (完整版本，用于某些需要完整 challenge 的情况)
        /// </summary>
        public byte[] GenerateBromResponseFull(byte[] challenge, SlaKey key)
        {
            // 交换字节对 (每2字节交换) - 输入
            byte[] swapped = new byte[challenge.Length];
            for (int i = 0; i < challenge.Length - 1; i += 2)
            {
                swapped[i] = challenge[i + 1];
                swapped[i + 1] = challenge[i];
            }
            if (challenge.Length % 2 == 1)
            {
                swapped[challenge.Length - 1] = challenge[challenge.Length - 1];
            }

            // RSA 签名
            byte[] signature = CustomizedSign(key.N, key.D, swapped);

            // 再次交换字节对 - 输出
            byte[] result = new byte[signature.Length];
            for (int i = 0; i < signature.Length - 1; i += 2)
            {
                result[i] = signature[i + 1];
                result[i + 1] = signature[i];
            }
            if (signature.Length % 2 == 1)
            {
                result[signature.Length - 1] = signature[signature.Length - 1];
            }

            return result;
        }

        /// <summary>
        /// 自定义 RSA 签名 (PKCS#1 v1.5 格式)
        /// </summary>
        private byte[] CustomizedSign(BigInteger n, BigInteger d, byte[] message)
        {
            int keySize = (int)Math.Ceiling(BigInteger.Log(n, 256));

            // PKCS#1 v1.5 填充
            // EM = 0x00 || 0x01 || PS || 0x00 || M
            byte[] em = new byte[keySize];
            em[0] = 0x00;
            em[1] = 0x01;

            int psLen = keySize - message.Length - 3;
            for (int i = 2; i < 2 + psLen; i++)
            {
                em[i] = 0xFF;
            }
            em[2 + psLen] = 0x00;
            Array.Copy(message, 0, em, keySize - message.Length, message.Length);

            // RSA 运算: signature = em^d mod n
            BigInteger emInt = new BigInteger(em, isUnsigned: true, isBigEndian: true);
            BigInteger sigInt = BigInteger.ModPow(emInt, d, n);

            // 转换为字节数组
            byte[] signature = sigInt.ToByteArray(isUnsigned: true, isBigEndian: true);

            // 确保输出长度正确
            if (signature.Length < keySize)
            {
                byte[] padded = new byte[keySize];
                Array.Copy(signature, 0, padded, keySize - signature.Length, signature.Length);
                return padded;
            }

            return signature;
        }

        /// <summary>
        /// 生成 DA SLA 签名 (RSA-OAEP + SHA256)
        /// 根据官方 SP Flash Tool flash.dll 逆向分析:
        /// 1. 从 challenge 偏移 32 字节开始取 16 字节
        /// 2. 使用 OAEP + SHA256 填充
        /// 3. RSA 私钥加密
        /// </summary>
        public byte[] GenerateDaSignature(byte[] challenge, RSA key)
        {
            // 官方实现: 从偏移32开始取16字节
            const int DA_DATA_OFFSET = 32;
            const int DA_DATA_LENGTH = 16;

            byte[] data;
            if (challenge.Length >= DA_DATA_OFFSET + DA_DATA_LENGTH)
            {
                data = new byte[DA_DATA_LENGTH];
                Array.Copy(challenge, DA_DATA_OFFSET, data, 0, DA_DATA_LENGTH);
            }
            else
            {
                // 如果 challenge 太短，使用全部数据
                data = challenge;
            }

            try
            {
                return key.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            }
            catch
            {
                // 如果 OAEP 失败，尝试 PKCS1
                return key.Encrypt(data, RSAEncryptionPadding.Pkcs1);
            }
        }

        /// <summary>
        /// 生成 DA SLA 签名 (使用 BigInteger 密钥)
        /// 根据官方 SP Flash Tool flash.dll 逆向分析
        /// </summary>
        public byte[] GenerateDaSignatureWithKey(byte[] challenge, SlaKey key)
        {
            // 官方实现: 从偏移32开始取16字节
            const int DA_DATA_OFFSET = 32;
            const int DA_DATA_LENGTH = 16;

            byte[] data;
            if (challenge.Length >= DA_DATA_OFFSET + DA_DATA_LENGTH)
            {
                data = new byte[DA_DATA_LENGTH];
                Array.Copy(challenge, DA_DATA_OFFSET, data, 0, DA_DATA_LENGTH);
            }
            else
            {
                data = challenge;
            }

            // 使用 OAEP + SHA256 填充，然后 RSA 私钥加密
            return CustomizedSignOaep(key.N, key.D, data);
        }

        /// <summary>
        /// 自定义 RSA-OAEP 签名
        /// </summary>
        private byte[] CustomizedSignOaep(BigInteger n, BigInteger d, byte[] message)
        {
            int keySize = (int)Math.Ceiling(BigInteger.Log(n, 256));

            // OAEP 填充 (简化版本)
            using (var sha256 = SHA256.Create())
            {
                // lHash = SHA256("")
                byte[] lHash = sha256.ComputeHash(Array.Empty<byte>());

                // DB = lHash || PS || 0x01 || M
                int dbLen = keySize - 32 - 1; // keySize - hLen - 1
                byte[] db = new byte[dbLen];
                Array.Copy(lHash, 0, db, 0, 32);
                // PS is zeros
                db[dbLen - message.Length - 1] = 0x01;
                Array.Copy(message, 0, db, dbLen - message.Length, message.Length);

                // 生成随机种子
                byte[] seed = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(seed);
                }

                // dbMask = MGF1(seed, dbLen)
                byte[] dbMask = Mgf1(seed, dbLen, sha256);
                byte[] maskedDb = new byte[dbLen];
                for (int i = 0; i < dbLen; i++)
                {
                    maskedDb[i] = (byte)(db[i] ^ dbMask[i]);
                }

                // seedMask = MGF1(maskedDB, hLen)
                byte[] seedMask = Mgf1(maskedDb, 32, sha256);
                byte[] maskedSeed = new byte[32];
                for (int i = 0; i < 32; i++)
                {
                    maskedSeed[i] = (byte)(seed[i] ^ seedMask[i]);
                }

                // EM = 0x00 || maskedSeed || maskedDB
                byte[] em = new byte[keySize];
                em[0] = 0x00;
                Array.Copy(maskedSeed, 0, em, 1, 32);
                Array.Copy(maskedDb, 0, em, 33, dbLen);

                // RSA 运算: signature = em^d mod n
                BigInteger emInt = new BigInteger(em, isUnsigned: true, isBigEndian: true);
                BigInteger sigInt = BigInteger.ModPow(emInt, d, n);

                byte[] signature = sigInt.ToByteArray(isUnsigned: true, isBigEndian: true);

                // 确保输出长度正确
                if (signature.Length < keySize)
                {
                    byte[] padded = new byte[keySize];
                    Array.Copy(signature, 0, padded, keySize - signature.Length, signature.Length);
                    return padded;
                }

                return signature;
            }
        }

        /// <summary>
        /// MGF1 (Mask Generation Function)
        /// </summary>
        private byte[] Mgf1(byte[] seed, int length, HashAlgorithm hash)
        {
            byte[] result = new byte[length];
            int hashLen = hash.HashSize / 8;
            int count = (length + hashLen - 1) / hashLen;

            for (int i = 0; i < count; i++)
            {
                byte[] c = new byte[4];
                c[0] = (byte)(i >> 24);
                c[1] = (byte)(i >> 16);
                c[2] = (byte)(i >> 8);
                c[3] = (byte)i;

                byte[] input = new byte[seed.Length + 4];
                Array.Copy(seed, 0, input, 0, seed.Length);
                Array.Copy(c, 0, input, seed.Length, 4);

                byte[] h = hash.ComputeHash(input);
                int copyLen = Math.Min(hashLen, length - i * hashLen);
                Array.Copy(h, 0, result, i * hashLen, copyLen);
            }

            return result;
        }

        /// <summary>
        /// 添加 BROM 密钥
        /// </summary>
        public void AddBromKey(string name, string nHex, string eHex, string dHex)
        {
            _bromKeys.Add(new SlaKey
            {
                Name = name,
                N = BigInteger.Parse("00" + nHex, System.Globalization.NumberStyles.HexNumber),
                E = BigInteger.Parse(eHex, System.Globalization.NumberStyles.HexNumber),
                D = BigInteger.Parse("00" + dHex, System.Globalization.NumberStyles.HexNumber)
            });
        }

        /// <summary>
        /// 从 DA 二进制中查找并添加密钥
        /// </summary>
        public bool AddKeyFromDA(byte[] daData, RSA key)
        {
            // 在 DA 数据中查找公钥模数
            byte[] nBytes = key.ExportParameters(false).Modulus!;

            int idx = FindPattern(daData, nBytes);
            if (idx != -1)
            {
                _daKeys.Add(key);
                return true;
            }

            return false;
        }

        private static int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }
    }
}
