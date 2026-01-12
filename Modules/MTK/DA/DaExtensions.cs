using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.Models;
using tools.Modules.MTK.Hardware;
using tools.Modules.MTK.Security;

namespace tools.Modules.MTK.DA
{
    #region XFlash 扩展命令

    /// <summary>
    /// XFlash 扩展命令
    /// </summary>
    public static class XFlashExtCmd
    {
        public const uint CUSTOM_ACK = 0x0F0000;
        public const uint CUSTOM_READMEM = 0x0F0001;
        public const uint CUSTOM_READREGISTER = 0x0F0002;
        public const uint CUSTOM_WRITEMEM = 0x0F0003;
        public const uint CUSTOM_WRITEREGISTER = 0x0F0004;
        public const uint CUSTOM_SET_STORAGE = 0x0F0005;
        public const uint CUSTOM_RPMB_SET_KEY = 0x0F0006;
        public const uint CUSTOM_RPMB_PROG_KEY = 0x0F0007;
        public const uint CUSTOM_RPMB_INIT = 0x0F0008;
        public const uint CUSTOM_RPMB_READ = 0x0F0009;
        public const uint CUSTOM_RPMB_WRITE = 0x0F000A;
        public const uint CUSTOM_SEJ_HW = 0x0F000B;
    }

    /// <summary>
    /// Legacy 扩展命令
    /// </summary>
    public static class LegacyExtCmd
    {
        public const byte CUSTOM_READ = 0x29;
        public const byte CUSTOM_WRITE = 0x2A;
        public const byte ACK = 0x5A;
        public const byte NACK = 0xA5;
    }

    #endregion

    /// <summary>
    /// XFlash DA 扩展功能
    /// 提供 RPMB、密钥提取、内存读写等高级功能
    /// </summary>
    public class XFlashExtension
    {
        private readonly XFlashProtocol _xflash;
        private readonly ChipConfig _chipConfig;
        private byte[]? _da2Data;
        private uint _da2Address;

        public event Action<string>? OnLog;

        public XFlashExtension(XFlashProtocol xflash, ChipConfig chipConfig)
        {
            _xflash = xflash;
            _chipConfig = chipConfig;
        }

        /// <summary>
        /// 设置 DA2 数据 (用于补丁)
        /// </summary>
        public void SetDa2(byte[] da2Data, uint da2Address)
        {
            _da2Data = da2Data;
            _da2Address = da2Address;
        }

        #region 内存操作

        /// <summary>
        /// 读取内存 (需要已补丁的 DA)
        /// </summary>
        public byte[]? ReadMem(uint address, uint length)
        {
            try
            {
                Log($"ReadMem: 0x{address:X8}, length: 0x{length:X}");
                
                // 使用 XFlash 自定义命令读取内存
                // 命令格式: CMD(4) + ADDR(4) + LEN(4)
                var cmd = new byte[12];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_READMEM).CopyTo(cmd, 0);
                BitConverter.GetBytes(address).CopyTo(cmd, 4);
                BitConverter.GetBytes(length).CopyTo(cmd, 8);
                
                // 通过 XFlash 发送自定义命令
                return _xflash.SendCustomCommand(cmd, (int)length);
            }
            catch (Exception ex)
            {
                Log($"ReadMem error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入内存 (需要已补丁的 DA)
        /// </summary>
        public bool WriteMem(uint address, byte[] data)
        {
            try
            {
                Log($"WriteMem: 0x{address:X8}, length: 0x{data.Length:X}");
                
                // 使用 XFlash 自定义命令写入内存
                // 命令格式: CMD(4) + ADDR(4) + LEN(4) + DATA
                var cmd = new byte[12 + data.Length];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_WRITEMEM).CopyTo(cmd, 0);
                BitConverter.GetBytes(address).CopyTo(cmd, 4);
                BitConverter.GetBytes((uint)data.Length).CopyTo(cmd, 8);
                data.CopyTo(cmd, 12);
                
                var result = _xflash.SendCustomCommand(cmd, 4);
                return result != null && result.Length >= 4 && BitConverter.ToUInt32(result, 0) == 0;
            }
            catch (Exception ex)
            {
                Log($"WriteMem error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取寄存器
        /// </summary>
        public uint? ReadRegister(uint address)
        {
            var data = ReadMem(address, 4);
            if (data == null || data.Length < 4)
                return null;
            return BitConverter.ToUInt32(data, 0);
        }

        /// <summary>
        /// 写入寄存器
        /// </summary>
        public bool WriteRegister(uint address, uint value)
        {
            return WriteMem(address, BitConverter.GetBytes(value));
        }

        #endregion

        #region RPMB 操作

        /// <summary>
        /// RPMB 错误描述
        /// </summary>
        public static readonly string[] RpmbErrors = new[]
        {
            "Success",
            "General failure",
            "Authentication failure",
            "Counter failure",
            "Address failure",
            "Write failure",
            "Read failure",
            "Authentication key not yet programmed"
        };

        /// <summary>
        /// 初始化 RPMB
        /// </summary>
        public bool RpmbInit()
        {
            try
            {
                Log("Initializing RPMB...");
                
                var cmd = new byte[4];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_RPMB_INIT).CopyTo(cmd, 0);
                
                var result = _xflash.SendCustomCommand(cmd, 4);
                if (result != null && result.Length >= 4)
                {
                    uint status = BitConverter.ToUInt32(result, 0);
                    if (status == 0)
                    {
                        Log("RPMB initialized successfully");
                        return true;
                    }
                    Log($"RPMB init failed: {(status < RpmbErrors.Length ? RpmbErrors[status] : $"Error {status}")}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"RPMB init error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取 RPMB 数据
        /// </summary>
        public byte[]? RpmbRead(uint address, uint length)
        {
            try
            {
                Log($"RPMB Read: addr=0x{address:X}, len=0x{length:X}");
                
                // 命令格式: CMD(4) + ADDR(4) + LEN(4)
                var cmd = new byte[12];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_RPMB_READ).CopyTo(cmd, 0);
                BitConverter.GetBytes(address).CopyTo(cmd, 4);
                BitConverter.GetBytes(length).CopyTo(cmd, 8);
                
                return _xflash.SendCustomCommand(cmd, (int)length + 4);
            }
            catch (Exception ex)
            {
                Log($"RPMB read error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入 RPMB 数据
        /// </summary>
        public bool RpmbWrite(uint address, byte[] data)
        {
            try
            {
                Log($"RPMB Write: addr=0x{address:X}, len=0x{data.Length:X}");
                
                // 命令格式: CMD(4) + ADDR(4) + LEN(4) + DATA
                var cmd = new byte[12 + data.Length];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_RPMB_WRITE).CopyTo(cmd, 0);
                BitConverter.GetBytes(address).CopyTo(cmd, 4);
                BitConverter.GetBytes((uint)data.Length).CopyTo(cmd, 8);
                data.CopyTo(cmd, 12);
                
                var result = _xflash.SendCustomCommand(cmd, 4);
                return result != null && result.Length >= 4 && BitConverter.ToUInt32(result, 0) == 0;
            }
            catch (Exception ex)
            {
                Log($"RPMB write error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置 RPMB 密钥
        /// </summary>
        public bool RpmbSetKey(byte[] key)
        {
            if (key.Length != 32)
            {
                Log("RPMB key must be 32 bytes");
                return false;
            }

            try
            {
                Log("Setting RPMB key...");
                
                // 命令格式: CMD(4) + KEY(32)
                var cmd = new byte[36];
                BitConverter.GetBytes(XFlashExtCmd.CUSTOM_RPMB_SET_KEY).CopyTo(cmd, 0);
                key.CopyTo(cmd, 4);
                
                var result = _xflash.SendCustomCommand(cmd, 4);
                if (result != null && result.Length >= 4)
                {
                    uint status = BitConverter.ToUInt32(result, 0);
                    if (status == 0)
                    {
                        Log("RPMB key set successfully");
                        return true;
                    }
                    Log($"RPMB set key failed: {(status < RpmbErrors.Length ? RpmbErrors[status] : $"Error {status}")}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"RPMB set key error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 密钥提取

        /// <summary>
        /// 提取设备密钥
        /// </summary>
        public DeviceKeys? ExtractKeys()
        {
            try
            {
                Log("Extracting device keys...");
                
                var keys = new DeviceKeys();

                // 尝试读取 MEID
                if (_chipConfig.MeidAddr.HasValue)
                {
                    keys.MeId = ReadMem(_chipConfig.MeidAddr.Value, 16);
                    if (keys.MeId != null)
                        Log($"MEID: {BitConverter.ToString(keys.MeId).Replace("-", "")}");
                }

                // 尝试读取 SOC ID
                if (_chipConfig.SocIdAddr.HasValue)
                {
                    keys.SocId = ReadMem(_chipConfig.SocIdAddr.Value, 32);
                    if (keys.SocId != null)
                        Log($"SOCID: {BitConverter.ToString(keys.SocId).Replace("-", "")}");
                }

                // 尝试读取 Provision Key
                if (_chipConfig.ProvAddr.HasValue)
                {
                    keys.ProvKey = ReadMem(_chipConfig.ProvAddr.Value, 16);
                }

                return keys;
            }
            catch (Exception ex)
            {
                Log($"Extract keys error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 生成 RPMB 密钥
        /// </summary>
        public byte[]? GenerateRpmbKey()
        {
            try
            {
                var keys = ExtractKeys();
                if (keys?.MeId == null)
                {
                    Log("Cannot generate RPMB key: MEID not available");
                    return null;
                }

                // 使用 DXCC 或 SEJ 生成 RPMB 密钥
                if (_chipConfig.DxccBase.HasValue)
                {
                    Log("Generating RPMB key using DXCC...");
                    // 创建 DXCC 引擎包装器
                    var dxcc = new DxccEngine(
                        _chipConfig.DxccBase.Value,
                        Read32,
                        Write32,
                        WriteMemory,
                        ReadMemory,
                        _da2Address
                    );
                    return dxcc.GenerateRpmbKey();
                }
                else if (_chipConfig.SejBase.HasValue)
                {
                    Log("Generating RPMB key using SEJ...");
                    // 创建 SEJ 引擎包装器
                    var sej = new SejEngine(
                        Read32,
                        Write32,
                        _chipConfig
                    );
                    return sej.GenerateRpmbKey(keys.MeId);
                }

                Log("No hardware crypto engine available");
                return null;
            }
            catch (Exception ex)
            {
                Log($"Generate RPMB key error: {ex.Message}");
                return null;
            }
        }

        #region 硬件读写包装

        private uint Read32(uint address)
        {
            var data = ReadMem(address, 4);
            if (data == null || data.Length < 4)
                return 0;
            return BitConverter.ToUInt32(data, 0);
        }

        private void Write32(uint address, uint value)
        {
            WriteMem(address, BitConverter.GetBytes(value));
        }

        private void WriteMemory(uint address, byte[] data)
        {
            WriteMem(address, data);
        }

        private byte[] ReadMemory(uint address, int length)
        {
            return ReadMem(address, (uint)length) ?? Array.Empty<byte>();
        }

        #endregion

        #endregion

        #region SecCfg 操作

        private const int SECCFG_SIZE = 0x4000; // 16KB

        /// <summary>
        /// 读取 SecCfg
        /// </summary>
        public SecCfg? ReadSecCfg()
        {
            try
            {
                Log("Reading SecCfg...");
                
                // 从 seccfg 分区读取数据
                var data = _xflash.ReadPartitionData("seccfg", 0, SECCFG_SIZE);
                if (data == null || data.Length == 0)
                {
                    Log("Failed to read seccfg partition");
                    return null;
                }
                
                // 解析 SecCfg
                var secCfg = SecCfg.Parse(data);
                if (secCfg != null)
                {
                    Log($"SecCfg: LockState={secCfg.LockState}, CriticalLock={secCfg.CriticalLockState}");
                }
                return secCfg;
            }
            catch (Exception ex)
            {
                Log($"Read SecCfg error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入 SecCfg
        /// </summary>
        public bool WriteSecCfg(SecCfg secCfg)
        {
            try
            {
                Log("Writing SecCfg...");
                
                var data = secCfg.ToBytes();
                return _xflash.WritePartitionData("seccfg", 0, data);
            }
            catch (Exception ex)
            {
                Log($"Write SecCfg error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解锁 Bootloader
        /// </summary>
        public bool UnlockBootloader()
        {
            try
            {
                Log("Unlocking bootloader...");

                var secCfg = ReadSecCfg();
                if (secCfg == null)
                {
                    Log("Failed to read SecCfg");
                    return false;
                }

                if (secCfg.LockState == Security.LockState.LKS_UNLOCK)
                {
                    Log("Bootloader is already unlocked");
                    return true;
                }

                // 修改锁定状态
                secCfg.LockState = Security.LockState.LKS_UNLOCK;
                secCfg.CriticalLockState = Security.CriticalLockState.LKCS_UNLOCK;
                
                // 写回 SecCfg
                if (!WriteSecCfg(secCfg))
                {
                    Log("Failed to write SecCfg");
                    return false;
                }

                Log("Bootloader unlocked successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Unlock bootloader error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 锁定 Bootloader
        /// </summary>
        public bool LockBootloader()
        {
            try
            {
                Log("Locking bootloader...");

                var secCfg = ReadSecCfg();
                if (secCfg == null)
                {
                    Log("Failed to read SecCfg");
                    return false;
                }

                if (secCfg.LockState == Security.LockState.LKS_LOCK)
                {
                    Log("Bootloader is already locked");
                    return true;
                }

                secCfg.LockState = Security.LockState.LKS_LOCK;
                secCfg.CriticalLockState = Security.CriticalLockState.LKCS_LOCK;
                
                // 写回 SecCfg
                if (!WriteSecCfg(secCfg))
                {
                    Log("Failed to write SecCfg");
                    return false;
                }

                Log("Bootloader locked successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Lock bootloader error: {ex.Message}");
                return false;
            }
        }

        #endregion

        private void Log(string message)
        {
            OnLog?.Invoke($"[XFlashExt] {message}");
        }
    }

    /// <summary>
    /// Legacy DA 扩展功能
    /// </summary>
    public class LegacyExtension
    {
        private readonly LegacyProtocol _legacy;
        private readonly ChipConfig _chipConfig;

        public event Action<string>? OnLog;

        public LegacyExtension(LegacyProtocol legacy, ChipConfig chipConfig)
        {
            _legacy = legacy;
            _chipConfig = chipConfig;
        }

        /// <summary>
        /// 补丁 DA2
        /// </summary>
        public byte[] PatchDa2(byte[] da2)
        {
            var patched = new byte[da2.Length];
            Array.Copy(da2, patched, da2.Length);

            // 查找并补丁安全检查
            // 补丁 READ_REG16_CMD 检查
            int checkAddr = FindBinary(patched, new byte[] { 0x08, 0xB5, 0x4F, 0xF4, 0x50, 0x42 });
            if (checkAddr >= 0)
            {
                // 替换为直接返回
                var patch = new byte[] { 0x08, 0xB5, 0x00, 0x20, 0x08, 0xBD };
                Array.Copy(patch, 0, patched, checkAddr, patch.Length);
                Log("Legacy DA2 patched (address check)");
            }

            // 补丁 CMD F0 (自定义读取命令)
            int checkAddr2 = FindBinary(patched, new byte[] { 0x30, 0xB5, 0x85, 0xB0, 0x03, 0xAB });
            if (checkAddr2 >= 0)
            {
                // 替换为自定义读取实现
                var cmdF0 = HexToBytes(
                    "70B54AF2C864C8F2040463 6A9847636A05469847064" +
                    "64FF0000128680 5F10405A36A9847A6F1010600 2EF6D15A202369BDE87040 1847");
                if (cmdF0.Length <= patched.Length - checkAddr2)
                {
                    Array.Copy(cmdF0, 0, patched, checkAddr2, cmdF0.Length);
                    Log("Legacy DA2 patched (CMD F0)");
                }
            }

            return patched;
        }

        /// <summary>
        /// 自定义内存读取 (使用 CMD 0xF0)
        /// </summary>
        public byte[]? CustomRead(uint address, uint length)
        {
            try
            {
                Log($"Custom read: 0x{address:X8}, len=0x{length:X}");
                
                // 发送自定义读取命令 0xF0
                // 格式: CMD(1) + ADDR(4) + LEN(4)
                var cmd = new byte[9];
                cmd[0] = LegacyExtCmd.CUSTOM_READ;
                BitConverter.GetBytes(address).CopyTo(cmd, 1);
                BitConverter.GetBytes(length).CopyTo(cmd, 5);
                
                return _legacy.SendCustomCommand(cmd, (int)length);
            }
            catch (Exception ex)
            {
                Log($"Custom read error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 自定义内存写入 (使用 CMD 0xF1)
        /// </summary>
        public bool CustomWrite(uint address, byte[] data)
        {
            try
            {
                Log($"Custom write: 0x{address:X8}, len=0x{data.Length:X}");
                
                // 发送自定义写入命令 0xF1
                // 格式: CMD(1) + ADDR(4) + LEN(4) + DATA
                var cmd = new byte[9 + data.Length];
                cmd[0] = LegacyExtCmd.CUSTOM_WRITE;
                BitConverter.GetBytes(address).CopyTo(cmd, 1);
                BitConverter.GetBytes((uint)data.Length).CopyTo(cmd, 5);
                data.CopyTo(cmd, 9);
                
                var result = _legacy.SendCustomCommand(cmd, 1);
                return result != null && result.Length > 0 && result[0] == LegacyExtCmd.ACK;
            }
            catch (Exception ex)
            {
                Log($"Custom write error: {ex.Message}");
                return false;
            }
        }

        private int FindBinary(byte[] data, byte[] pattern)
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

        private byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[LegacyExt] {message}");
        }
    }

    /// <summary>
    /// 设备密钥数据
    /// </summary>
    public class DeviceKeys
    {
        public byte[]? MeId { get; set; }
        public byte[]? SocId { get; set; }
        public byte[]? ProvKey { get; set; }
        public byte[]? RpmbKey { get; set; }
        public byte[]? FbeKey { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (MeId != null) sb.AppendLine($"MEID: {BitConverter.ToString(MeId).Replace("-", "")}");
            if (SocId != null) sb.AppendLine($"SOCID: {BitConverter.ToString(SocId).Replace("-", "")}");
            if (ProvKey != null) sb.AppendLine($"ProvKey: {BitConverter.ToString(ProvKey).Replace("-", "")}");
            if (RpmbKey != null) sb.AppendLine($"RpmbKey: {BitConverter.ToString(RpmbKey).Replace("-", "")}");
            if (FbeKey != null) sb.AppendLine($"FbeKey: {BitConverter.ToString(FbeKey).Replace("-", "")}");
            return sb.ToString();
        }
    }
}
