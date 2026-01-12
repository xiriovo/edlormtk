// ============================================================================
// MultiFlash TOOL - MediaTek DXCC Hardware Crypto Engine
// 联发科 DXCC 硬件加密引擎 | MTK DXCCハードウェア暗号エンジン | MTK DXCC 하드웨어 암호화 엔진
// ============================================================================
// [EN] Discretix CryptoCell (DXCC) - Hardware crypto for newer MTK chipsets
//      Key derivation, RPMB key generation, secure boot operations
// [中文] Discretix CryptoCell (DXCC) - 新款联发科芯片硬件加密
//       密钥派生、RPMB 密钥生成、安全启动操作
// [日本語] Discretix CryptoCell (DXCC) - 新型MTKチップセット用ハードウェア暗号
//         キー導出、RPMBキー生成、セキュアブート操作
// [한국어] Discretix CryptoCell (DXCC) - 최신 MTK 칩셋용 하드웨어 암호화
//         키 파생, RPMB 키 생성, 보안 부팅 작업
// [Español] Discretix CryptoCell (DXCC) - Crypto hardware para chipsets MTK nuevos
//           Derivación de claves, generación de claves RPMB, operaciones de arranque seguro
// [Русский] Discretix CryptoCell (DXCC) - Аппаратная криптография для новых чипсетов MTK
//           Получение ключей, генерация ключей RPMB, операции безопасной загрузки
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Security.Cryptography;

namespace tools.Modules.MTK.Hardware
{
    /// <summary>
    /// DXCC (Discretix CryptoCell) Hardware Crypto Engine
    /// DXCC 硬件加密引擎 | DXCCハードウェア暗号エンジン | DXCC 하드웨어 암호화 엔진
    /// Key derivation, RPMB key generation
    /// </summary>
    public class DxccEngine
    {
        #region 常量定义

        // 寄存器偏移
        private const int DX_HOST_IRR = 0xA00;
        private const int DX_HOST_ICR = 0xA08;
        private const int DX_DSCRPTR_QUEUE0_WORD0 = 0xE80;
        private const int DX_DSCRPTR_QUEUE0_WORD1 = 0xE84;
        private const int DX_DSCRPTR_QUEUE0_WORD2 = 0xE88;
        private const int DX_DSCRPTR_QUEUE0_WORD3 = 0xE8C;
        private const int DX_DSCRPTR_QUEUE0_WORD4 = 0xE90;
        private const int DX_DSCRPTR_QUEUE0_WORD5 = 0xE94;
        private const int DX_DSCRPTR_QUEUE0_CONTENT = 0xE9C;
        private const int DX_HOST_SEP_HOST_GPR4 = 0xAA0;

        // AES 常量
        private const int AES_BLOCK_SIZE = 16;
        private const int AES_IV_SIZE = 16;

        // 密钥类型
        public const int USER_KEY = 0;
        public const int ROOT_KEY = 1;
        public const int PROVISIONING_KEY = 2;
        public const int SESSION_KEY = 3;
        public const int PLATFORM_KEY = 5;
        public const int CUSTOMER_KEY = 6;

        // OTP 索引
        private const int SASI_SB_HASH_BOOT_KEY_256B = 2;
        private const int SASI_SB_HASH_BOOT_KEY_1_128B = 1;

        #endregion

        #region 字段

        private readonly uint _dxccBase;
        private readonly Func<uint, uint> _read32;
        private readonly Action<uint, uint> _write32;
        private readonly Action<uint, byte[]> _writeMem;
        private readonly Func<uint, int, byte[]> _readMem;
        private readonly uint _payloadAddr;

        #endregion

        public DxccEngine(
            uint dxccBase,
            Func<uint, uint> read32,
            Action<uint, uint> write32,
            Action<uint, byte[]> writeMem,
            Func<uint, int, byte[]> readMem,
            uint payloadAddr)
        {
            _dxccBase = dxccBase;
            _read32 = read32;
            _write32 = write32;
            _writeMem = writeMem;
            _readMem = readMem;
            _payloadAddr = payloadAddr;
        }

        #region TZCC 时钟控制

        /// <summary>
        /// 控制 TZCC 时钟
        /// </summary>
        public void TzccClock(bool enable)
        {
            if (enable)
            {
                _write32(0x1000108C, 0x18000000);
            }
            else
            {
                _write32(0x10001088, 0x8000000);
            }
        }

        #endregion

        #region OTP 读取

        /// <summary>
        /// 读取 OTP 字
        /// </summary>
        public uint? ReadOtpWord(uint otpAddress)
        {
            if (otpAddress > 0x24)
                return null;

            // 等待就绪
            while ((_read32(_dxccBase + (0x2AF * 4)) & 1) == 0) { }

            _write32(_dxccBase + (0x2A9 * 4), (4 * otpAddress) | 0x10000);

            // 等待完成
            while ((_read32(_dxccBase + (0x2AD * 4)) & 1) == 0) { }

            return _read32(_dxccBase + (0x2AB * 4));
        }

        /// <summary>
        /// 获取 LCS (Lifecycle State)
        /// </summary>
        public uint GetLcs()
        {
            while ((_read32(_dxccBase + (0x2AF * 4)) & 1) == 0) { }

            uint lcs = _read32(_dxccBase + (0x2B5 * 4));
            return lcs;
        }

        /// <summary>
        /// 获取公钥哈希
        /// </summary>
        public byte[]? GetPubKeyHash(int keyIndex = SASI_SB_HASH_BOOT_KEY_256B)
        {
            int start, length;
            if (keyIndex == SASI_SB_HASH_BOOT_KEY_256B)
            {
                start = 0x10;
                length = 8;
            }
            else if (keyIndex == SASI_SB_HASH_BOOT_KEY_1_128B)
            {
                start = 0x14;
                length = 4;
            }
            else
            {
                return null;
            }

            byte[] hashVal = new byte[length * 4];
            for (int idx = 0; idx < length; idx++)
            {
                uint? word = ReadOtpWord((uint)(start + idx));
                if (word == null) return null;
                BitConverter.GetBytes(word.Value).CopyTo(hashVal, idx * 4);
            }
            return hashVal;
        }

        #endregion

        #region 密钥生成

        /// <summary>
        /// 生成 RPMB 密钥
        /// </summary>
        public byte[]? GenerateRpmbKey(int level = 0)
        {
            byte[] rpmbIkey = System.Text.Encoding.ASCII.GetBytes("RPMB KEY");
            byte[] rpmbSalt = System.Text.Encoding.ASCII.GetBytes("SASI");

            // 调整密钥和盐
            for (int i = 0; i < rpmbIkey.Length; i++)
                rpmbIkey[i] = (byte)(rpmbIkey[i] + level);
            for (int i = 0; i < rpmbSalt.Length; i++)
                rpmbSalt[i] = (byte)(rpmbSalt[i] + level);

            int keyLength = level > 0 ? 0x10 : 0x20;

            TzccClock(true);
            uint dstAddr = _payloadAddr - 0x300;
            byte[]? rpmbKey = KeyDerivation(ROOT_KEY, rpmbIkey, rpmbSalt, keyLength, dstAddr);
            TzccClock(false);

            return rpmbKey;
        }

        /// <summary>
        /// 生成 iTrustee FBE 密钥
        /// </summary>
        public byte[]? GenerateITrusteeFbeKey(int keySize = 32, byte[]? appId = null)
        {
            appId ??= Array.Empty<byte>();
            byte[] fdeKey = new byte[keySize];
            uint dstAddr = _payloadAddr - 0x300;

            TzccClock(true);
            
            for (int ctr = 0; ctr < keySize / 16; ctr++)
            {
                // 构建 iTrustee 种子
                byte[] itrustee = new byte[20 + 16 + appId.Length + 1];
                System.Text.Encoding.ASCII.GetBytes("TrustedCorekeymaster").CopyTo(itrustee, 0);
                for (int i = 0; i < 16; i++)
                    itrustee[20 + i] = 0x07;
                appId.CopyTo(itrustee, 36);
                itrustee[^1] = (byte)ctr;

                byte[]? block = AesCmac(ROOT_KEY, 0, itrustee, itrustee.Length, dstAddr);
                if (block != null && block.Length >= 16)
                {
                    Array.Copy(block, 0, fdeKey, ctr * 16, Math.Min(16, keySize - ctr * 16));
                }
            }

            TzccClock(false);
            return fdeKey;
        }

        /// <summary>
        /// 生成 Provision 密钥
        /// </summary>
        public (byte[]? platKey, byte[]? provKey) GenerateProvisionKey()
        {
            byte[] platKeyLabel = System.Text.Encoding.ASCII.GetBytes("KEY PLAT");
            byte[] provKeyLabel = System.Text.Encoding.ASCII.GetBytes("PROVISION KEY");

            TzccClock(true);
            uint dstAddr = _payloadAddr - 0x300;

            byte[]? salt = GetPubKeyHash(SASI_SB_HASH_BOOT_KEY_256B);
            if (salt == null)
            {
                TzccClock(false);
                return (null, null);
            }

            byte[]? platKey = KeyDerivation(PLATFORM_KEY, platKeyLabel, salt, 0x10, dstAddr);

            // 等待就绪
            while ((_read32(_dxccBase + 0xAF4) & 1) == 0) { }

            byte[]? provKey = KeyDerivation(PROVISIONING_KEY, provKeyLabel, salt, 0x10, dstAddr);

            // 清理密钥寄存器
            _write32(_dxccBase + 0xAC0, 0);
            _write32(_dxccBase + 0xAC4, 0);
            _write32(_dxccBase + 0xAC8, 0);
            _write32(_dxccBase + 0xACC, 0);

            TzccClock(false);
            return (platKey, provKey);
        }

        /// <summary>
        /// 计算 SOC ID
        /// </summary>
        public byte[]? ComputeSocId()
        {
            byte[] key = new byte[] { 0x49 };
            byte[] salt = new byte[32];
            int keyLength = 0x10;

            TzccClock(true);
            uint dstAddr = _payloadAddr - 0x300;

            byte[]? pubKey = GetPubKeyHash(SASI_SB_HASH_BOOT_KEY_256B);
            if (pubKey == null)
            {
                TzccClock(false);
                return null;
            }

            byte[]? derivedKey = KeyDerivation(ROOT_KEY, key, salt, keyLength, dstAddr);
            if (derivedKey == null)
            {
                TzccClock(false);
                return null;
            }

            // 组合并计算 SHA256
            byte[] combined = new byte[pubKey.Length + derivedKey.Length];
            pubKey.CopyTo(combined, 0);
            derivedKey.CopyTo(combined, pubKey.Length);

            TzccClock(false);
            return SHA256.HashData(combined);
        }

        #endregion

        #region 密钥派生核心

        /// <summary>
        /// 密钥派生
        /// </summary>
        private byte[]? KeyDerivation(int aesKeyType, byte[] label, byte[] salt, int requestedLen, uint destAddr)
        {
            if (aesKeyType > PLATFORM_KEY || ((1 << (aesKeyType - 1)) & 0x17) == 0)
                return null;

            if (requestedLen > 0xFF || (requestedLen << 28) != 0)
                return null;

            if (label.Length == 0 || label.Length > 0x20)
                return null;

            int bufferLen = salt.Length + 3 + label.Length;
            int iterLength = (requestedLen + 0xF) >> 4;
            byte[] result = new byte[iterLength * 16];

            for (int i = 0; i < iterLength; i++)
            {
                // 构建缓冲区: counter(1) + label + 0x00 + salt + length(1)
                byte[] buffer = new byte[bufferLen];
                buffer[0] = (byte)(i + 1);
                Array.Copy(label, 0, buffer, 1, label.Length);
                buffer[1 + label.Length] = 0;
                Array.Copy(salt, 0, buffer, 2 + label.Length, salt.Length);
                buffer[bufferLen - 1] = (byte)((8 * requestedLen) & 0xFF);

                byte[]? block = AesCmac(aesKeyType, 0, buffer, bufferLen, destAddr);
                if (block != null)
                {
                    Array.Copy(block, 0, result, i * 16, Math.Min(16, result.Length - i * 16));
                }
            }

            return result[..requestedLen];
        }

        /// <summary>
        /// AES-CMAC 计算
        /// </summary>
        private byte[]? AesCmac(int aesKeyType, uint internalKey, byte[] dataIn, int bufferLen, uint destAddr)
        {
            uint sramAddr = destAddr;
            uint ivSramAddr = sramAddr;
            uint inputSramAddr = ivSramAddr + AES_IV_SIZE;
            int blockSize = (dataIn.Length / 0x20) * 0x20;
            uint outputSramAddr = inputSramAddr + (uint)blockSize;
            uint keySramAddr = outputSramAddr + (uint)blockSize;

            if (internalKey != 0)
            {
                // 写入内部密钥
                byte[] keyBytes = BitConverter.GetBytes(internalKey);
                _writeMem(keySramAddr, keyBytes);
            }

            // 写入输入数据
            _writeMem(inputSramAddr, dataIn[..bufferLen]);

            // 执行 CMAC
            if (AesCmacDriver(aesKeyType, keySramAddr, inputSramAddr, 0, bufferLen, sramAddr))
            {
                return _readMem(sramAddr, 16);
            }

            return null;
        }

        /// <summary>
        /// AES-CMAC 驱动
        /// </summary>
        private bool AesCmacDriver(int aesKeyType, uint pInternalKey, uint pDataIn, int dmaMode, int blockSize, uint pDataOut)
        {
            int keySizeInBytes;
            if (aesKeyType == ROOT_KEY)
            {
                keySizeInBytes = (_read32(_dxccBase + DX_HOST_SEP_HOST_GPR4) & 2) != 0 ? 0x20 : 0x10;
            }
            else
            {
                keySizeInBytes = 0x10;
            }

            ClearInterrupt();

            // 发送描述符序列
            uint[] desc1 = InitDescriptor();
            SetCipherMode(desc1, 7); // CMAC
            SetCipherConfig0(desc1, 0); // ENCRYPT
            SetKeySize(desc1, keySizeInBytes);
            SetDinConst(desc1, 0, AES_IV_SIZE);
            SetFlowMode(desc1, 0x20); // S_DIN_to_AES
            SetSetupMode(desc1, 1); // LOAD_STATE0
            SendDescriptor(desc1);

            // 加载密钥
            uint[] desc2 = InitDescriptor();
            if (aesKeyType == USER_KEY)
            {
                SetDinSram(desc2, pInternalKey, 16);
            }
            SetCipherDo(desc2, aesKeyType);
            SetCipherMode(desc2, 7); // CMAC
            SetCipherConfig0(desc2, 0); // ENCRYPT
            SetKeySize(desc2, keySizeInBytes);
            SetFlowMode(desc2, 0x20); // S_DIN_to_AES
            SetSetupMode(desc2, 4); // LOAD_KEY0
            desc2[4] |= (uint)(((aesKeyType >> 2) & 3) << 20);
            SendDescriptor(desc2);

            // 处理输入数据
            uint[] desc3 = InitDescriptor();
            SetDinSram(desc3, pDataIn, blockSize);
            SetFlowMode(desc3, 1); // DIN_AES_DOUT
            SendDescriptor(desc3);

            // 输出结果
            if (aesKeyType != PROVISIONING_KEY)
            {
                uint[] desc4 = InitDescriptor();
                SetCipherMode(desc4, 7); // CMAC
                SetCipherConfig0(desc4, 0); // ENCRYPT
                SetSetupMode(desc4, 8); // WRITE_STATE0
                SetFlowMode(desc4, 0x26); // S_AES_to_DOUT
                SetDoutSram(desc4, pDataOut, AES_BLOCK_SIZE);
                SetDinNoDma(desc4, 0, 0);
                SendDescriptor(desc4);
            }

            return WaitDescCompletion() == 0;
        }

        #endregion

        #region 描述符操作

        private uint[] InitDescriptor() => new uint[6];

        private void ClearInterrupt()
        {
            _write32(_dxccBase + DX_HOST_ICR, 4);
        }

        private uint WaitCrypto()
        {
            uint value;
            do
            {
                value = _read32(_dxccBase + DX_HOST_IRR);
            } while (value == 0);
            return value;
        }

        private void SendDescriptor(uint[] desc)
        {
            // 等待队列有空间
            while ((_read32(_dxccBase + DX_DSCRPTR_QUEUE0_CONTENT) << 0x1C) == 0) { }

            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD0, desc[0]);
            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD1, desc[1]);
            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD2, desc[2]);
            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD3, desc[3]);
            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD4, desc[4]);
            _write32(_dxccBase + DX_DSCRPTR_QUEUE0_WORD5, desc[5]);
        }

        private int WaitDescCompletion(uint destPtr = 0)
        {
            ClearInterrupt();

            uint[] data = InitDescriptor();
            data[0] = 0;
            data[1] = 0x8000011;
            data[2] = destPtr;
            data[3] = 0x8000012;
            data[4] = 0x100;
            data[5] = (destPtr >> 32) << 16;
            SendDescriptor(data);

            while ((WaitCrypto() & 4) == 0) { }

            uint value;
            do
            {
                value = _read32(_dxccBase + 0xBA0);
            } while (value == 0);

            if (value == 1)
            {
                ClearInterrupt();
                return 0;
            }
            return unchecked((int)0xF6000001);
        }

        private void SetCipherMode(uint[] desc, int mode)
        {
            desc[4] |= (uint)((mode & 0xF) << 10);
        }

        private void SetCipherConfig0(uint[] desc, int config)
        {
            desc[4] |= (uint)((config & 0x3) << 17);
        }

        private void SetKeySize(uint[] desc, int keySize)
        {
            desc[4] |= (uint)(((keySize >> 3) - 2) << 22);
        }

        private void SetFlowMode(uint[] desc, int flowMode)
        {
            desc[4] |= (uint)(flowMode & 0x3F);
        }

        private void SetSetupMode(uint[] desc, int setupMode)
        {
            desc[4] |= (uint)((setupMode & 0xF) << 24);
        }

        private void SetCipherDo(uint[] desc, int cipherDo)
        {
            desc[4] |= (uint)((cipherDo & 0x3) << 15);
        }

        private void SetDinConst(uint[] desc, uint val, int size)
        {
            desc[0] |= val & 0xFFFFFFFF;
            desc[1] |= (1 << 27); // DIN_CONST_VALUE
            desc[1] |= (1 << 0); // DMA_SRAM
            desc[1] |= (uint)((size & 0xFFFFFF) << 2);
        }

        private void SetDinSram(uint[] desc, uint addr, int size)
        {
            desc[0] |= addr & 0xFFFFFFFF;
            desc[1] |= (1 << 0); // DMA_SRAM
            desc[1] |= (uint)((size & 0xFFFFFF) << 2);
        }

        private void SetDinNoDma(uint[] desc, uint addr, int size)
        {
            desc[0] |= addr & 0xFFFFFFFF;
            desc[1] |= (uint)((size & 0xFFFFFF) << 2);
        }

        private void SetDoutSram(uint[] desc, uint addr, int size)
        {
            desc[2] |= addr & 0xFFFFFFFF;
            desc[3] |= (1 << 0); // DMA_SRAM
            desc[3] |= (uint)((size & 0xFFFFFF) << 2);
        }

        #endregion

        #region 安全禁用

        /// <summary>
        /// 禁用安全启动
        /// </summary>
        public void DisableSecurity()
        {
            uint lcs = GetLcs();
            if (lcs == 7)
                return;

            _write32(_dxccBase + 0xAC0, 0);
            _write32(_dxccBase + 0xAC4, 0);
            _write32(_dxccBase + 0xAC8, 0);
            _write32(_dxccBase + 0xACC, 0);
            _write32(_dxccBase + 0xAD8, 1);
        }

        #endregion

        #region 静态解密方法

        /// <summary>
        /// MTEE 数据解密
        /// </summary>
        public static byte[] MteeDecrypt(byte[] data)
        {
            byte[] key = Convert.FromHexString("B936C14D95A99585073E5607784A51F7444B60D6BFD6110F76D004CCB7E1950E");
            byte[] skey = SHA256.HashData(key);
            
            using var aes = Aes.Create();
            aes.Key = skey[..16];
            aes.IV = skey[16..32];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// 数据解扰
        /// </summary>
        public static byte[] Descramble(byte[] data)
        {
            byte[] key = Convert.FromHexString("5C0E349A27DC46034C7B6744A378BD17");
            byte[] iv = Convert.FromHexString("A0B0924686447109F2D51DCDDC93458A");

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB; // CTR 模式需要手动实现
            aes.Padding = PaddingMode.None;

            // 简化的 CTR 模式实现
            byte[] result = new byte[data.Length];
            byte[] counter = (byte[])iv.Clone();
            
            using var encryptor = aes.CreateEncryptor();
            
            for (int i = 0; i < data.Length; i += 16)
            {
                byte[] keystream = new byte[16];
                encryptor.TransformBlock(counter, 0, 16, keystream, 0);
                
                int blockLen = Math.Min(16, data.Length - i);
                for (int j = 0; j < blockLen; j++)
                {
                    result[i + j] = (byte)(data[i + j] ^ keystream[j]);
                }
                
                // 递增计数器
                for (int j = 15; j >= 0; j--)
                {
                    if (++counter[j] != 0)
                        break;
                }
            }
            
            return result;
        }

        #endregion
    }
}
