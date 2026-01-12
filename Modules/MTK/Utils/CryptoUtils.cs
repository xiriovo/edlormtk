using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace tools.Modules.MTK.Utils
{
    /// <summary>
    /// MTK 加密工具类
    /// </summary>
    public static class CryptoUtils
    {
        #region AES 操作

        /// <summary>
        /// AES-128-CBC 加密
        /// </summary>
        public static byte[] AesEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// AES-128-CBC 解密
        /// </summary>
        public static byte[] AesDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// AES-128-ECB 加密
        /// </summary>
        public static byte[] AesEcbEncrypt(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// AES-128-ECB 解密
        /// </summary>
        public static byte[] AesEcbDecrypt(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// AES-CMAC (RFC 4493)
        /// </summary>
        public static byte[] AesCmac(byte[] data, byte[] key)
        {
            // 生成子密钥
            var L = AesEcbEncrypt(new byte[16], key);
            var K1 = GenerateSubkey(L);
            var K2 = GenerateSubkey(K1);

            // 填充数据
            int blockCount = (data.Length + 15) / 16;
            bool complete = data.Length % 16 == 0 && data.Length > 0;
            
            byte[] lastBlock = new byte[16];
            if (complete)
            {
                Array.Copy(data, (blockCount - 1) * 16, lastBlock, 0, 16);
                XorBytes(lastBlock, K1);
            }
            else
            {
                int remaining = data.Length % 16;
                if (remaining > 0)
                    Array.Copy(data, blockCount * 16 - 16, lastBlock, 0, remaining);
                lastBlock[remaining] = 0x80;
                XorBytes(lastBlock, K2);
                if (blockCount == 0) blockCount = 1;
            }

            // CBC-MAC
            byte[] X = new byte[16];
            for (int i = 0; i < blockCount - 1; i++)
            {
                byte[] block = new byte[16];
                Array.Copy(data, i * 16, block, 0, 16);
                XorBytes(X, block);
                X = AesEcbEncrypt(X, key);
            }
            
            XorBytes(X, lastBlock);
            return AesEcbEncrypt(X, key);
        }

        private static byte[] GenerateSubkey(byte[] L)
        {
            byte[] K = new byte[16];
            byte msb = (byte)((L[0] & 0x80) != 0 ? 1 : 0);
            
            for (int i = 0; i < 15; i++)
            {
                K[i] = (byte)((L[i] << 1) | (L[i + 1] >> 7));
            }
            K[15] = (byte)(L[15] << 1);
            
            if (msb != 0)
            {
                K[15] ^= 0x87;
            }
            
            return K;
        }

        private static void XorBytes(byte[] dest, byte[] src)
        {
            for (int i = 0; i < 16; i++)
            {
                dest[i] ^= src[i];
            }
        }

        #endregion

        #region RSA 操作

        /// <summary>
        /// RSA 签名 (不带填充，用于 SLA)
        /// </summary>
        public static byte[] RsaSignRaw(byte[] data, byte[] n, byte[] d)
        {
            // 简化实现 - 实际需要大数运算库
            using var rsa = RSA.Create();
            var parameters = new RSAParameters
            {
                Modulus = n,
                D = d,
                // 其他参数需要根据实际密钥设置
            };
            
            try
            {
                rsa.ImportParameters(parameters);
                return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                // RSA 原始操作需要自定义实现
                return RsaRawOp(data, d, n);
            }
        }

        /// <summary>
        /// RSA 原始运算 (m^e mod n)
        /// </summary>
        private static byte[] RsaRawOp(byte[] message, byte[] exponent, byte[] modulus)
        {
            // 使用 BigInteger 进行模幂运算
            var m = new System.Numerics.BigInteger(message, true, true);
            var e = new System.Numerics.BigInteger(exponent, true, true);
            var n = new System.Numerics.BigInteger(modulus, true, true);
            
            var result = System.Numerics.BigInteger.ModPow(m, e, n);
            
            byte[] resultBytes = result.ToByteArray(true, true);
            
            // 确保结果长度与模数相同
            if (resultBytes.Length < modulus.Length)
            {
                byte[] padded = new byte[modulus.Length];
                Array.Copy(resultBytes, 0, padded, modulus.Length - resultBytes.Length, resultBytes.Length);
                return padded;
            }
            
            return resultBytes;
        }

        #endregion

        #region Hash 操作

        /// <summary>
        /// SHA-1 哈希
        /// </summary>
        public static byte[] Sha1(byte[] data)
        {
            using var sha1 = SHA1.Create();
            return sha1.ComputeHash(data);
        }

        /// <summary>
        /// SHA-256 哈希
        /// </summary>
        public static byte[] Sha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }

        /// <summary>
        /// HMAC-SHA256
        /// </summary>
        public static byte[] HmacSha256(byte[] data, byte[] key)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// MD5 哈希
        /// </summary>
        public static byte[] Md5(byte[] data)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(data);
        }

        #endregion

        #region 字节操作

        /// <summary>
        /// 十六进制字符串转字节数组
        /// </summary>
        public static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// 字节数组转十六进制字符串
        /// </summary>
        public static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// 反转字节数组
        /// </summary>
        public static byte[] Reverse(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = data[data.Length - 1 - i];
            }
            return result;
        }

        /// <summary>
        /// XOR 两个字节数组
        /// </summary>
        public static byte[] Xor(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            byte[] result = new byte[len];
            for (int i = 0; i < len; i++)
            {
                result[i] = (byte)(a[i] ^ b[i]);
            }
            return result;
        }

        /// <summary>
        /// 填充到指定长度
        /// </summary>
        public static byte[] Pad(byte[] data, int length, byte padByte = 0)
        {
            if (data.Length >= length) return data;
            
            byte[] result = new byte[length];
            Array.Copy(data, result, data.Length);
            for (int i = data.Length; i < length; i++)
            {
                result[i] = padByte;
            }
            return result;
        }

        #endregion

        #region MTK 特定

        /// <summary>
        /// 计算 MTK AUTH 哈希
        /// </summary>
        public static byte[] ComputeAuthHash(byte[] challenge, byte[] key)
        {
            // MTK 使用特定的哈希算法组合
            byte[] combined = new byte[challenge.Length + key.Length];
            Array.Copy(challenge, combined, challenge.Length);
            Array.Copy(key, 0, combined, challenge.Length, key.Length);
            return Sha256(combined);
        }

        /// <summary>
        /// 生成随机字节
        /// </summary>
        public static byte[] GenerateRandom(int length)
        {
            byte[] random = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(random);
            return random;
        }

        #endregion
    }
}
