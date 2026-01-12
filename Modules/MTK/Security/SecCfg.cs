using System;
using System.IO;
using System.Security.Cryptography;
using tools.Modules.MTK.Hardware;

namespace tools.Modules.MTK.Security
{
    /// <summary>
    /// 锁定状态枚举
    /// </summary>
    public enum LockState : uint
    {
        LKS_DEFAULT = 0x01,
        LKS_MP_DEFAULT = 0x02,
        LKS_UNLOCK = 0x03,
        LKS_LOCK = 0x04,
        LKS_VERIFIED = 0x05,
        LKS_CUSTOM = 0x06
    }

    /// <summary>
    /// 关键锁定状态枚举
    /// </summary>
    public enum CriticalLockState : uint
    {
        LKCS_UNLOCK = 0x01,
        LKCS_LOCK = 0x02
    }

    /// <summary>
    /// MTK SecCfg 安全配置解析器
    /// 用于 Bootloader 解锁/锁定
    /// </summary>
    public class SecCfg
    {
        // 魔数
        private const uint MAGIC_V3 = 0x4D4D4D4D; // "MMMM"
        private const uint MAGIC_V4 = 0x4D4D4D4D;
        private const uint END_FLAG = 0x45454545;  // "EEEE"

        // 字段
        public uint Magic { get; private set; }
        public uint Version { get; private set; }
        public uint Size { get; private set; }
        public LockState LockState { get; set; }
        public CriticalLockState CriticalLockState { get; set; }
        public uint SbootRuntime { get; private set; }
        public uint EndFlag { get; private set; }
        public byte[] Hash { get; private set; } = new byte[32];

        // 检测到的硬件类型
        public string HwType { get; private set; } = "";

        // 是否有效
        public bool IsValid => Magic == MAGIC_V4 && EndFlag == END_FLAG;

        // 是否已解锁
        public bool IsUnlocked => LockState == LockState.LKS_UNLOCK;

        // SEJ 引擎引用
        private readonly SejEngine? _sej;

        public SecCfg(SejEngine? sej = null)
        {
            _sej = sej;
        }

        /// <summary>
        /// 静态解析方法
        /// </summary>
        public static SecCfg? Parse(byte[] data)
        {
            if (data == null || data.Length < 28)
                return null;

            var secCfg = new SecCfg();
            if (secCfg.ParseData(data))
                return secCfg;
            return null;
        }

        /// <summary>
        /// 解析 SecCfg 数据
        /// </summary>
        public bool ParseData(byte[] data)
        {
            if (data == null || data.Length < 28)
                return false;

            _rawData = data;

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            Magic = reader.ReadUInt32();
            Version = reader.ReadUInt32();
            Size = reader.ReadUInt32();
            LockState = (LockState)reader.ReadUInt32();
            CriticalLockState = (CriticalLockState)reader.ReadUInt32();
            SbootRuntime = reader.ReadUInt32();
            EndFlag = reader.ReadUInt32();

            if (!IsValid)
            {
                return false;
            }

            // 读取 Hash (在数据末尾)
            if (Size >= 0x20 && data.Length >= Size)
            {
                ms.Seek(Size - 0x20, SeekOrigin.Begin);
                Hash = reader.ReadBytes(0x20);
            }

            // 验证 Hash
            if (_sej != null)
            {
                VerifyHash(data);
            }

            return true;
        }

        // 原始数据保留
        private byte[]? _rawData;

        /// <summary>
        /// 验证 Hash 并检测硬件类型
        /// </summary>
        private void VerifyHash(byte[] data)
        {
            // 计算数据 Hash
            byte[] seccfgData = new byte[28];
            Array.Copy(BitConverter.GetBytes(Magic), 0, seccfgData, 0, 4);
            Array.Copy(BitConverter.GetBytes(Version), 0, seccfgData, 4, 4);
            Array.Copy(BitConverter.GetBytes(Size), 0, seccfgData, 8, 4);
            Array.Copy(BitConverter.GetBytes((uint)LockState), 0, seccfgData, 12, 4);
            Array.Copy(BitConverter.GetBytes((uint)CriticalLockState), 0, seccfgData, 16, 4);
            Array.Copy(BitConverter.GetBytes(SbootRuntime), 0, seccfgData, 20, 4);
            Array.Copy(BitConverter.GetBytes(END_FLAG), 0, seccfgData, 24, 4);

            byte[] expectedHash = SHA256.HashData(seccfgData);

            // 尝试不同的硬件解密方式
            // 1. SW 模式
            byte[] decHash = _sej!.AesCbcDecrypt(Hash);
            if (CompareBytes(expectedHash, decHash))
            {
                HwType = "SW";
                return;
            }

            // 2. 其他硬件模式需要更复杂的实现
            HwType = "Unknown";
        }

        /// <summary>
        /// 创建新的 SecCfg (解锁或锁定)
        /// </summary>
        public byte[]? Create(bool unlock)
        {
            if (!IsValid || string.IsNullOrEmpty(HwType))
            {
                return null;
            }

            // 设置新的锁定状态
            LockState newLockState;
            CriticalLockState newCriticalState;

            if (unlock)
            {
                newLockState = LockState.LKS_UNLOCK;
                newCriticalState = CriticalLockState.LKCS_UNLOCK;
            }
            else
            {
                newLockState = LockState.LKS_LOCK;
                newCriticalState = CriticalLockState.LKCS_LOCK;
            }

            // 构建新数据
            byte[] seccfgData = new byte[28];
            Array.Copy(BitConverter.GetBytes(Magic), 0, seccfgData, 0, 4);
            Array.Copy(BitConverter.GetBytes(Version), 0, seccfgData, 4, 4);
            Array.Copy(BitConverter.GetBytes(Size), 0, seccfgData, 8, 4);
            Array.Copy(BitConverter.GetBytes((uint)newLockState), 0, seccfgData, 12, 4);
            Array.Copy(BitConverter.GetBytes((uint)newCriticalState), 0, seccfgData, 16, 4);
            Array.Copy(BitConverter.GetBytes(SbootRuntime), 0, seccfgData, 20, 4);
            Array.Copy(BitConverter.GetBytes(END_FLAG), 0, seccfgData, 24, 4);

            // 计算新 Hash
            byte[] newHash = SHA256.HashData(seccfgData);

            // 加密 Hash
            byte[] encHash;
            if (HwType == "SW" && _sej != null)
            {
                encHash = _sej.AesCbcEncrypt(newHash);
            }
            else
            {
                // 不支持的硬件类型
                return null;
            }

            // 构建完整数据
            byte[] result = new byte[Size];
            Array.Copy(seccfgData, 0, result, 0, 28);
            Array.Copy(encHash, 0, result, (int)Size - 0x20, encHash.Length);

            return result;
        }

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public byte[]? Unlock()
        {
            if (LockState == LockState.LKS_UNLOCK)
            {
                return null; // 已经解锁
            }
            return Create(true);
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public byte[]? Lock()
        {
            if (LockState == LockState.LKS_LOCK)
            {
                return null; // 已经锁定
            }
            return Create(false);
        }

        /// <summary>
        /// 获取状态描述
        /// </summary>
        public string GetStatusDescription()
        {
            return $"LockState: {LockState}, CriticalLockState: {CriticalLockState}, HwType: {HwType}";
        }

        /// <summary>
        /// 导出为字节数组
        /// </summary>
        public byte[] ToBytes()
        {
            // 如果有原始数据，基于原始数据修改
            byte[] result;
            if (_rawData != null && _rawData.Length >= 28)
            {
                result = (byte[])_rawData.Clone();
            }
            else
            {
                // 创建新数据
                result = new byte[Math.Max((int)Size, 64)];
                BitConverter.GetBytes(MAGIC_V4).CopyTo(result, 0);
                BitConverter.GetBytes(Version).CopyTo(result, 4);
                BitConverter.GetBytes(Size).CopyTo(result, 8);
            }

            // 更新锁定状态
            BitConverter.GetBytes((uint)LockState).CopyTo(result, 12);
            BitConverter.GetBytes((uint)CriticalLockState).CopyTo(result, 16);
            BitConverter.GetBytes(SbootRuntime).CopyTo(result, 20);
            BitConverter.GetBytes(END_FLAG).CopyTo(result, 24);

            // 重新计算 Hash (如果有 SEJ)
            if (_sej != null && result.Length >= Size)
            {
                byte[] dataForHash = new byte[28];
                Array.Copy(result, 0, dataForHash, 0, 28);
                byte[] newHash = SHA256.HashData(dataForHash);
                byte[] encHash = _sej.AesCbcEncrypt(newHash);
                Array.Copy(encHash, 0, result, (int)Size - 0x20, Math.Min(encHash.Length, 0x20));
            }

            return result;
        }

        private static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
