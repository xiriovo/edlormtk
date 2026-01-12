using System;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.Models;

namespace tools.Modules.MTK.Hardware
{
    /// <summary>
    /// SEJ (Security Engine for JTAG protection) 硬件加密引擎
    /// 用于 AES 加密/解密、RPMB 密钥生成等
    /// </summary>
    public class SejEngine
    {
        private readonly PreloaderProtocol? _preloader;
        private readonly ChipConfig _chipConfig;
        private readonly Func<uint, uint>? _read32;
        private readonly Action<uint, uint>? _write32;

        // SEJ 寄存器偏移
        private const uint HACC_CON = 0x0000;
        private const uint HACC_ACON = 0x0004;
        private const uint HACC_ACON2 = 0x0008;
        private const uint HACC_ACONK = 0x000C;
        private const uint HACC_ASRC0 = 0x0010;
        private const uint HACC_ASRC1 = 0x0014;
        private const uint HACC_ASRC2 = 0x0018;
        private const uint HACC_ASRC3 = 0x001C;
        private const uint HACC_AKEY0 = 0x0020;
        private const uint HACC_AKEY1 = 0x0024;
        private const uint HACC_AKEY2 = 0x0028;
        private const uint HACC_AKEY3 = 0x002C;
        private const uint HACC_AKEY4 = 0x0030;
        private const uint HACC_AKEY5 = 0x0034;
        private const uint HACC_AKEY6 = 0x0038;
        private const uint HACC_AKEY7 = 0x003C;
        private const uint HACC_ACFG0 = 0x0040;
        private const uint HACC_AOUT0 = 0x0050;
        private const uint HACC_AOUT1 = 0x0054;
        private const uint HACC_AOUT2 = 0x0058;
        private const uint HACC_AOUT3 = 0x005C;
        private const uint HACC_SW_OTP0 = 0x0060;
        private const uint HACC_SECINIT0 = 0x0080;
        private const uint HACC_SECINIT1 = 0x0084;
        private const uint HACC_SECINIT2 = 0x0088;
        private const uint HACC_MKJ = 0x00A0;

        // AES 模式
        private const int AES_CBC_MODE = 1;
        private const int AES_SW_KEY = 0;
        private const int AES_HW_KEY = 1;
        private const int AES_HW_WRAP_KEY = 2;

        /// <summary>
        /// 使用 PreloaderProtocol 构造
        /// </summary>
        public SejEngine(PreloaderProtocol preloader, ChipConfig chipConfig)
        {
            _preloader = preloader;
            _chipConfig = chipConfig;

            if (!chipConfig.SejBase.HasValue || chipConfig.SejBase.Value == 0)
            {
                throw new ArgumentException("Chip does not have SEJ base address configured");
            }
        }

        /// <summary>
        /// 使用函数委托构造 (用于 XFlash DA 模式)
        /// </summary>
        public SejEngine(Func<uint, uint> read32, Action<uint, uint> write32, ChipConfig chipConfig)
        {
            _read32 = read32;
            _write32 = write32;
            _chipConfig = chipConfig;

            if (!chipConfig.SejBase.HasValue || chipConfig.SejBase.Value == 0)
            {
                throw new ArgumentException("Chip does not have SEJ base address configured");
            }
        }

        private uint SejBase => _chipConfig.SejBase!.Value;

        /// <summary>
        /// 读取 SEJ 寄存器
        /// </summary>
        private uint ReadReg(uint offset)
        {
            if (_read32 != null)
                return _read32(SejBase + offset);
            return _preloader!.Read32(SejBase + offset, 1);
        }

        /// <summary>
        /// 写入 SEJ 寄存器
        /// </summary>
        private void WriteReg(uint offset, uint value)
        {
            if (_write32 != null)
                _write32(SejBase + offset, value);
            else
                _preloader!.Write32(SejBase + offset, value);
        }

        /// <summary>
        /// 初始化 SEJ
        /// </summary>
        public void Init()
        {
            // 清除配置
            WriteReg(HACC_ACON, 0);
            WriteReg(HACC_ACONK, 0);

            // 设置默认配置
            WriteReg(HACC_ACON, 0x18000000); // AES-128, CBC mode
        }

        /// <summary>
        /// 设置 AES 密钥
        /// </summary>
        public void SetKey(byte[] key)
        {
            if (key.Length != 16 && key.Length != 32)
                throw new ArgumentException("Key must be 16 or 32 bytes");

            for (int i = 0; i < key.Length / 4; i++)
            {
                uint word = BitConverter.ToUInt32(key, i * 4);
                WriteReg(HACC_AKEY0 + (uint)(i * 4), word);
            }
        }

        /// <summary>
        /// 设置 IV (初始化向量)
        /// </summary>
        public void SetIv(byte[] iv)
        {
            if (iv.Length != 16)
                throw new ArgumentException("IV must be 16 bytes");

            for (int i = 0; i < 4; i++)
            {
                uint word = BitConverter.ToUInt32(iv, i * 4);
                WriteReg(HACC_ACFG0 + (uint)(i * 4), word);
            }
        }

        /// <summary>
        /// 设置源数据
        /// </summary>
        private void SetSource(byte[] data)
        {
            if (data.Length != 16)
                throw new ArgumentException("Source data must be 16 bytes");

            for (int i = 0; i < 4; i++)
            {
                uint word = BitConverter.ToUInt32(data, i * 4);
                WriteReg(HACC_ASRC0 + (uint)(i * 4), word);
            }
        }

        /// <summary>
        /// 获取输出数据
        /// </summary>
        private byte[] GetOutput()
        {
            byte[] output = new byte[16];
            for (int i = 0; i < 4; i++)
            {
                uint word = ReadReg(HACC_AOUT0 + (uint)(i * 4));
                BitConverter.GetBytes(word).CopyTo(output, i * 4);
            }
            return output;
        }

        /// <summary>
        /// 执行 AES 操作
        /// </summary>
        private void Execute(bool encrypt)
        {
            uint acon = ReadReg(HACC_ACON);
            if (encrypt)
            {
                acon |= (1 << 4); // 加密模式
            }
            else
            {
                acon &= ~(1u << 4); // 解密模式
            }
            acon |= 1; // 启动

            WriteReg(HACC_ACON, acon);

            // 等待完成
            while ((ReadReg(HACC_ACON) & 0x8000) == 0)
            {
                System.Threading.Thread.Sleep(1);
            }
        }

        /// <summary>
        /// AES-128-CBC 加密
        /// </summary>
        public byte[] AesCbcEncrypt(byte[] data, byte[]? key = null, byte[]? iv = null)
        {
            Init();

            if (key != null)
                SetKey(key);

            if (iv != null)
                SetIv(iv);
            else
                SetIv(new byte[16]);

            byte[] result = new byte[data.Length];
            byte[] currentIv = iv ?? new byte[16];

            for (int i = 0; i < data.Length; i += 16)
            {
                byte[] block = new byte[16];
                int len = Math.Min(16, data.Length - i);
                Array.Copy(data, i, block, 0, len);

                // CBC: XOR with previous ciphertext (or IV)
                for (int j = 0; j < 16; j++)
                {
                    block[j] ^= currentIv[j];
                }

                SetSource(block);
                Execute(true);
                currentIv = GetOutput();

                Array.Copy(currentIv, 0, result, i, len);
            }

            return result;
        }

        /// <summary>
        /// AES-128-CBC 解密
        /// </summary>
        public byte[] AesCbcDecrypt(byte[] data, byte[]? key = null, byte[]? iv = null)
        {
            Init();

            if (key != null)
                SetKey(key);

            if (iv != null)
                SetIv(iv);
            else
                SetIv(new byte[16]);

            byte[] result = new byte[data.Length];
            byte[] currentIv = iv ?? new byte[16];

            for (int i = 0; i < data.Length; i += 16)
            {
                byte[] block = new byte[16];
                int len = Math.Min(16, data.Length - i);
                Array.Copy(data, i, block, 0, len);

                SetSource(block);
                Execute(false);
                byte[] decrypted = GetOutput();

                // CBC: XOR with previous ciphertext (or IV)
                for (int j = 0; j < 16; j++)
                {
                    result[i + j] = (byte)(decrypted[j] ^ currentIv[j]);
                }

                currentIv = block;
            }

            return result;
        }

        /// <summary>
        /// 生成 RPMB 密钥
        /// </summary>
        public byte[] GenerateRpmbKey(byte[] meid, byte[]? otp = null)
        {
            if (meid.Length != 16)
                throw new ArgumentException("MEID must be 16 bytes");

            otp ??= new byte[32];

            // RPMB 密钥生成使用硬件密钥
            Init();

            // 使用 OTP 作为密钥
            SetKey(otp[..16]);

            // 加密 MEID
            SetSource(meid);
            Execute(true);

            return GetOutput();
        }

        /// <summary>
        /// 生成 MTEE 密钥
        /// </summary>
        public byte[] GenerateMteeKey(byte[]? otp = null)
        {
            otp ??= new byte[32];

            Init();
            SetKey(otp[..16]);
            SetSource(new byte[16]); // 全零输入
            Execute(true);

            return GetOutput();
        }
    }
}
