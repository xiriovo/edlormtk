// ============================================================================
// MultiFlash TOOL - MediaTek XFlash Protocol
// 联发科 XFlash 协议 | MTK XFlashプロトコル | MTK XFlash 프로토콜
// ============================================================================
// [EN] XFlash DA protocol for newer MediaTek chipsets
//      Supports high-speed transfers, custom commands, RPMB operations
// [中文] 新款联发科芯片的 XFlash DA 协议
//       支持高速传输、自定义命令、RPMB 操作
// [日本語] 新型MediaTekチップセット用XFlash DAプロトコル
//         高速転送、カスタムコマンド、RPMB操作をサポート
// [한국어] 최신 MediaTek 칩셋용 XFlash DA 프로토콜
//         고속 전송, 커스텀 명령, RPMB 작업 지원
// [Español] Protocolo XFlash DA para chipsets MediaTek más nuevos
//           Soporta transferencias de alta velocidad, comandos personalizados, operaciones RPMB
// [Русский] Протокол XFlash DA для новых чипсетов MediaTek
//           Поддержка высокоскоростных передач, пользовательских команд, операций RPMB
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace tools.Modules.MTK.Protocol
{
    #region Data Models / 数据模型 / データモデル / 데이터 모델

    /// <summary>
    /// RAM Information / RAM 信息 / RAM情報 / RAM 정보
    /// </summary>
    public class RamInfo
    {
        public uint Type { get; set; }
        public ulong BaseAddress { get; set; }
        public ulong Size { get; set; }
    }

    /// <summary>
    /// 芯片 ID 信息
    /// </summary>
    public class ChipId
    {
        public ushort HwCode { get; set; }
        public ushort HwSubCode { get; set; }
        public ushort HwVersion { get; set; }
        public ushort SwVersion { get; set; }
        public ushort ChipEvolution { get; set; }
    }

    /// <summary>
    /// eMMC 信息
    /// </summary>
    public class EmmcInfo
    {
        public uint Type { get; set; }
        public uint BlockSize { get; set; } = 512;
        public ulong Boot1Size { get; set; }
        public ulong Boot2Size { get; set; }
        public ulong RpmbSize { get; set; }
        public ulong Gp1Size { get; set; }
        public ulong Gp2Size { get; set; }
        public ulong Gp3Size { get; set; }
        public ulong Gp4Size { get; set; }
        public ulong UserSize { get; set; }
        public byte[] Cid { get; set; } = new byte[16];
        public ulong FwVersion { get; set; }
    }

    /// <summary>
    /// UFS 信息
    /// </summary>
    public class UfsInfo
    {
        public uint Type { get; set; }
        public uint BlockSize { get; set; }
        public ulong Lu0Size { get; set; }
        public ulong Lu1Size { get; set; }
        public ulong Lu2Size { get; set; }
        public byte[] Cid { get; set; } = new byte[16];
        public byte[] FwVersion { get; set; } = new byte[4];
        public byte[] Serial { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// NAND 信息
    /// </summary>
    public class NandInfo
    {
        public uint Type { get; set; }
        public uint PageSize { get; set; }
        public uint BlockSize { get; set; }
        public uint SpareSize { get; set; }
        public ulong TotalSize { get; set; }
        public ulong AvailableSize { get; set; }
        public bool BmtExist { get; set; }
        public byte[] NandId { get; set; } = new byte[12];
    }

    /// <summary>
    /// NOR 信息
    /// </summary>
    public class NorInfo
    {
        public uint Type { get; set; }
        public uint PageSize { get; set; }
        public ulong AvailableSize { get; set; }
    }

    /// <summary>
    /// 存储信息
    /// </summary>
    public class StorageInfo
    {
        public string Type { get; set; } = "unknown";
        public ulong TotalSize { get; set; }
        public ulong Boot1Size { get; set; }
        public ulong Boot2Size { get; set; }
        public ulong RpmbSize { get; set; }
        public uint BlockSize { get; set; }
        public byte[] Cid { get; set; } = Array.Empty<byte>();

        public EmmcInfo? Emmc { get; set; }
        public UfsInfo? Ufs { get; set; }
        public NandInfo? Nand { get; set; }
        public NorInfo? Nor { get; set; }
    }

    /// <summary>
    /// 分区信息
    /// </summary>
    public class MtkPartitionInfo
    {
        public string Name { get; set; } = "";
        public ulong StartSector { get; set; }
        public ulong SectorCount { get; set; }
        public uint SectorSize { get; set; } = 512;
        public Guid TypeGuid { get; set; }
        public Guid UniqueGuid { get; set; }
        public ulong Attributes { get; set; }

        public ulong Size => SectorCount * SectorSize;
        public ulong Offset => StartSector * SectorSize;
    }

    /// <summary>
    /// 数据包长度信息
    /// </summary>
    public class PacketLength
    {
        public uint ReadPacketLength { get; set; }
        public uint WritePacketLength { get; set; }
    }

    #endregion

    /// <summary>
    /// XFlash DA 协议实现 (Stage 2)
    /// </summary>
    public class XFlashProtocol : IDisposable
    {
        private SerialPort? _port;
        private readonly object _lock = new();

        public event Action<string>? OnLog;
        public event Action<long, long>? OnProgress;

        public RamInfo? Sram { get; private set; }
        public RamInfo? Dram { get; private set; }
        public StorageInfo StorageInfo { get; } = new();
        public List<MtkPartitionInfo> Partitions { get; } = new();
        public ChipId? ChipInfo { get; private set; }
        public string DaVersion { get; private set; } = "";
        public byte[] RandomId { get; private set; } = Array.Empty<byte>();
        public bool IsConnected => _port?.IsOpen == true;

        public void AttachPort(SerialPort port)
        {
            _port = port;
        }

        #region 初始化

        /// <summary>
        /// 发送 EMI 配置初始化 DRAM
        /// </summary>
        public bool SendEmi(byte[] emiData)
        {
            Log($"Sending EMI configuration ({emiData.Length} bytes)...");

            if (!XSend(XFlashCmd.INIT_EXT_RAM))
                return false;

            uint status = GetStatus();
            if (status != 0)
            {
                Log($"INIT_EXT_RAM failed: 0x{status:X}");
                return false;
            }

            Thread.Sleep(10);

            // 发送 EMI 长度
            if (!XSend((uint)emiData.Length))
                return false;

            // 发送 EMI 数据
            if (!SendParam(emiData))
            {
                Log("Failed to send EMI data");
                return false;
            }

            Log("EMI configuration sent successfully, DRAM initialized");
            return true;
        }

        /// <summary>
        /// 发送 DA2 并启动
        /// </summary>
        public bool BootTo(uint address, byte[] data, bool display = true)
        {
            if (display)
                Log($"Booting to 0x{address:X8}, size=0x{data.Length:X}");

            if (!XSend(XFlashCmd.BOOT_TO))
                return false;

            if (GetStatus() != 0)
                return false;

            // 发送地址和长度 (64-bit)
            byte[] param = new byte[16];
            BitConverter.GetBytes((ulong)address).CopyTo(param, 0);
            BitConverter.GetBytes((ulong)data.Length).CopyTo(param, 8);

            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)param.Length).CopyTo(pkt, 8);
            WriteBytes(pkt);
            WriteBytes(param);

            // 发送数据
            if (!SendData(data))
                return false;

            if (address == 0x4FFF0000)
            {
                if (display)
                    Log("Extensions were accepted. Jumping to extensions...");
            }
            else
            {
                if (display)
                    Log("Upload data was accepted. Jumping to stage 2...");
            }

            Thread.Sleep(500);

            try
            {
                uint syncStatus = GetStatus();
                if (syncStatus == XFlashCmd.SYNC_SIGNAL || syncStatus == 0)
                {
                    if (display)
                        Log("Boot to succeeded");
                    return true;
                }

                Log($"Boot to failed: 0x{syncStatus:X}");
            }
            catch (Exception ex)
            {
                Log($"Boot to status error: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 重新初始化 (读取存储信息)
        /// </summary>
        public void Reinit(bool display = true)
        {
            // 获取 RAM 信息
            GetRamInfo();

            // 获取存储信息
            GetStorageInfo(display);

            // 获取芯片 ID
            ChipInfo = GetChipId();

            // 获取 DA 版本
            GetDaVersion();

            // 获取随机 ID
            GetRandomId();

            // 读取分区
            ReadPartitions();

            // 检查 USB 速度并重连
            CheckAndReconnectHighSpeed();
        }

        private void CheckAndReconnectHighSpeed()
        {
            byte[] speed = SendDevCtrl(XFlashCmd.GET_USB_SPEED);
            if (speed.Length > 0)
            {
                string speedStr = Encoding.ASCII.GetString(speed).TrimEnd('\0');
                Log($"USB Speed: {speedStr}");

                if (speedStr == "full-speed")
                {
                    Log("Device is in full-speed mode, consider reconnecting for higher speed");
                    // 可以调用 SET_USB_SPEED 然后重连
                }
            }
        }

        #endregion

        #region 设备信息

        private void GetRamInfo()
        {
            byte[] resp = SendDevCtrl(XFlashCmd.GET_RAM_INFO);
            if (resp.Length >= 24)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    Sram = new RamInfo();
                    Dram = new RamInfo();

                    using var ms = new MemoryStream(resp);
                    using var reader = new BinaryReader(ms);

                    if (resp.Length == 24)
                    {
                        Sram.Type = reader.ReadUInt32();
                        Sram.BaseAddress = reader.ReadUInt32();
                        Sram.Size = reader.ReadUInt32();
                        Dram.Type = reader.ReadUInt32();
                        Dram.BaseAddress = reader.ReadUInt32();
                        Dram.Size = reader.ReadUInt32();
                    }
                    else if (resp.Length >= 48)
                    {
                        Sram.Type = (uint)reader.ReadUInt64();
                        Sram.BaseAddress = reader.ReadUInt64();
                        Sram.Size = reader.ReadUInt64();
                        Dram.Type = (uint)reader.ReadUInt64();
                        Dram.BaseAddress = reader.ReadUInt64();
                        Dram.Size = reader.ReadUInt64();
                    }

                    Log($"SRAM: Base=0x{Sram.BaseAddress:X}, Size=0x{Sram.Size:X}");
                    Log($"DRAM: Base=0x{Dram.BaseAddress:X}, Size=0x{Dram.Size:X}");
                }
            }
        }

        private void GetStorageInfo(bool display)
        {
            // 尝试获取 eMMC 信息
            StorageInfo.Emmc = GetEmmcInfo(display);
            if (StorageInfo.Emmc != null && StorageInfo.Emmc.Type != 0)
            {
                StorageInfo.Type = "emmc";
                StorageInfo.TotalSize = StorageInfo.Emmc.UserSize;
                StorageInfo.Boot1Size = StorageInfo.Emmc.Boot1Size;
                StorageInfo.Boot2Size = StorageInfo.Emmc.Boot2Size;
                StorageInfo.RpmbSize = StorageInfo.Emmc.RpmbSize;
                StorageInfo.BlockSize = StorageInfo.Emmc.BlockSize;
                StorageInfo.Cid = StorageInfo.Emmc.Cid;
                return;
            }

            // 尝试获取 UFS 信息
            StorageInfo.Ufs = GetUfsInfo(display);
            if (StorageInfo.Ufs != null && StorageInfo.Ufs.Type != 0)
            {
                StorageInfo.Type = "ufs";
                StorageInfo.TotalSize = StorageInfo.Ufs.Lu0Size;
                StorageInfo.Boot1Size = StorageInfo.Ufs.Lu1Size;
                StorageInfo.Boot2Size = StorageInfo.Ufs.Lu2Size;
                StorageInfo.BlockSize = StorageInfo.Ufs.BlockSize;
                StorageInfo.Cid = StorageInfo.Ufs.Cid;
                return;
            }

            // 尝试获取 NAND 信息
            StorageInfo.Nand = GetNandInfo(display);
            if (StorageInfo.Nand != null && StorageInfo.Nand.Type != 0)
            {
                StorageInfo.Type = "nand";
                StorageInfo.TotalSize = StorageInfo.Nand.TotalSize;
                StorageInfo.BlockSize = StorageInfo.Nand.BlockSize;
                return;
            }

            // 尝试获取 NOR 信息
            StorageInfo.Nor = GetNorInfo(display);
            if (StorageInfo.Nor != null && StorageInfo.Nor.Type != 0)
            {
                StorageInfo.Type = "nor";
                StorageInfo.TotalSize = StorageInfo.Nor.AvailableSize;
                StorageInfo.BlockSize = StorageInfo.Nor.PageSize;
            }
        }

        private EmmcInfo? GetEmmcInfo(bool display)
        {
            byte[] resp = SendDevCtrl(XFlashCmd.GET_EMMC_INFO);
            if (resp.Length == 0)
                return null;

            uint status = GetStatus();
            if (status != 0)
            {
                Log($"Error on getting emmc info: 0x{status:X}");
                return null;
            }

            var emmc = new EmmcInfo();
            int pos = 0;

            emmc.Type = BitConverter.ToUInt32(resp, pos); pos += 4;
            emmc.BlockSize = BitConverter.ToUInt32(resp, pos); pos += 4;
            emmc.Boot1Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.Boot2Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.RpmbSize = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.Gp1Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.Gp2Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.Gp3Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.Gp4Size = BitConverter.ToUInt64(resp, pos); pos += 8;
            emmc.UserSize = BitConverter.ToUInt64(resp, pos); pos += 8;

            if (pos + 16 <= resp.Length)
            {
                Array.Copy(resp, pos, emmc.Cid, 0, 16);
                pos += 16;
            }

            if (pos + 8 <= resp.Length)
            {
                emmc.FwVersion = BitConverter.ToUInt64(resp, pos);
            }

            if (emmc.Type != 0 && display)
            {
                Log($"eMMC FW Ver: 0x{emmc.FwVersion:X}");
                Log($"eMMC CID: {BitConverter.ToString(emmc.Cid).Replace("-", "")}");
                Log($"eMMC Boot1: 0x{emmc.Boot1Size:X} ({emmc.Boot1Size / 1024 / 1024}MB)");
                Log($"eMMC Boot2: 0x{emmc.Boot2Size:X} ({emmc.Boot2Size / 1024 / 1024}MB)");
                Log($"eMMC RPMB: 0x{emmc.RpmbSize:X} ({emmc.RpmbSize / 1024 / 1024}MB)");
                Log($"eMMC User: 0x{emmc.UserSize:X} ({emmc.UserSize / 1024 / 1024 / 1024}GB)");
            }

            return emmc;
        }

        private UfsInfo? GetUfsInfo(bool display)
        {
            byte[] resp = SendDevCtrl(XFlashCmd.GET_UFS_INFO);
            if (resp.Length == 0)
                return null;

            uint status = GetStatus();
            if (status != 0)
                return null;

            var ufs = new UfsInfo();
            using var ms = new MemoryStream(resp);
            using var reader = new BinaryReader(ms);

            ufs.Type = reader.ReadUInt32();
            ufs.BlockSize = reader.ReadUInt32();
            ufs.Lu2Size = reader.ReadUInt64();
            ufs.Lu1Size = reader.ReadUInt64();
            ufs.Lu0Size = reader.ReadUInt64();

            if (resp.Length > 32)
            {
                ufs.Cid = reader.ReadBytes(16);
            }

            if (ufs.Type != 0 && display)
            {
                Log($"UFS LU0 (User): 0x{ufs.Lu0Size:X} ({ufs.Lu0Size / 1024 / 1024 / 1024}GB)");
                Log($"UFS LU1 (Boot1): 0x{ufs.Lu1Size:X}");
                Log($"UFS LU2 (Boot2): 0x{ufs.Lu2Size:X}");
            }

            return ufs;
        }

        private NandInfo? GetNandInfo(bool display)
        {
            byte[] resp = SendDevCtrl(XFlashCmd.GET_NAND_INFO);
            if (resp.Length == 0)
                return null;

            uint status = GetStatus();
            if (status != 0)
                return null;

            var nand = new NandInfo();
            int pos = 0;

            nand.Type = BitConverter.ToUInt32(resp, pos); pos += 4;
            nand.PageSize = BitConverter.ToUInt32(resp, pos); pos += 4;
            nand.BlockSize = BitConverter.ToUInt32(resp, pos); pos += 4;
            nand.SpareSize = BitConverter.ToUInt32(resp, pos); pos += 4;
            nand.TotalSize = BitConverter.ToUInt64(resp, pos); pos += 8;
            nand.AvailableSize = BitConverter.ToUInt64(resp, pos); pos += 8;

            if (pos < resp.Length)
            {
                nand.BmtExist = resp[pos] != 0;
                pos++;
            }

            if (pos + 12 <= resp.Length)
            {
                Array.Copy(resp, pos, nand.NandId, 0, 12);
            }

            if (nand.Type != 0 && display)
            {
                Log($"NAND Page Size: 0x{nand.PageSize:X}");
                Log($"NAND Block Size: 0x{nand.BlockSize:X}");
                Log($"NAND Total Size: 0x{nand.TotalSize:X}");
            }

            return nand;
        }

        private NorInfo? GetNorInfo(bool display)
        {
            byte[] resp = SendDevCtrl(XFlashCmd.GET_NOR_INFO);
            if (resp.Length == 0)
                return null;

            uint status = GetStatus();
            if (status != 0)
                return null;

            var nor = new NorInfo();
            nor.Type = BitConverter.ToUInt32(resp, 0);
            nor.PageSize = BitConverter.ToUInt32(resp, 4);
            nor.AvailableSize = BitConverter.ToUInt64(resp, 8);

            if (nor.Type != 0 && display)
            {
                Log($"NOR Page Size: 0x{nor.PageSize:X}");
                Log($"NOR Size: 0x{nor.AvailableSize:X}");
            }

            return nor;
        }

        private ChipId? GetChipId()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_CHIP_ID);
            if (data.Length < 10)
                return null;

            uint status = GetStatus();
            if (status != 0)
                return null;

            var chip = new ChipId
            {
                HwCode = BitConverter.ToUInt16(data, 0),
                HwSubCode = BitConverter.ToUInt16(data, 2),
                HwVersion = BitConverter.ToUInt16(data, 4),
                SwVersion = BitConverter.ToUInt16(data, 6),
                ChipEvolution = BitConverter.ToUInt16(data, 8)
            };

            Log($"Chip: HW=0x{chip.HwCode:X4}, Sub=0x{chip.HwSubCode:X4}, Ver=0x{chip.HwVersion:X4}");
            return chip;
        }

        private void GetDaVersion()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_DA_VERSION);
            if (data.Length > 0)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    DaVersion = Encoding.UTF8.GetString(data).TrimEnd('\0');
                    Log($"DA Version: {DaVersion}");
                }
            }
        }

        private void GetRandomId()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_RANDOM_ID);
            if (data.Length > 0)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    RandomId = data;
                    Log($"Random ID: {BitConverter.ToString(data).Replace("-", "").Substring(0, Math.Min(32, data.Length * 2))}...");
                }
            }
        }

        /// <summary>
        /// 获取连接代理类型
        /// </summary>
        public string GetConnectionAgent()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_CONNECTION_AGENT);
            if (data.Length > 0)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    return Encoding.ASCII.GetString(data).TrimEnd('\0');
                }
            }
            return "";
        }

        /// <summary>
        /// 获取 SLA 状态
        /// </summary>
        public bool GetSlaStatus()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_SLA_STATUS);
            if (data.Length >= 4)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    uint slaStatus = BitConverter.ToUInt32(data, 0);
                    return slaStatus != 0;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取数据包长度
        /// </summary>
        public PacketLength? GetPacketLength()
        {
            byte[] data = SendDevCtrl(XFlashCmd.GET_PACKET_LENGTH);
            if (data.Length >= 8)
            {
                uint status = GetStatus();
                if (status == 0)
                {
                    return new PacketLength
                    {
                        ReadPacketLength = BitConverter.ToUInt32(data, 0),
                        WritePacketLength = BitConverter.ToUInt32(data, 4)
                    };
                }
            }
            return null;
        }

        #endregion

        #region 分区操作

        private void ReadPartitions()
        {
            Partitions.Clear();

            // 读取 GPT (34 扇区 = GPT header + entries)
            byte[]? gptData = ReadFlash(0, 34 * 512, "user");
            if (gptData == null || gptData.Length < 512)
            {
                Log("Failed to read GPT");
                return;
            }

            // 解析 GPT
            ParseGpt(gptData);
        }

        private void ParseGpt(byte[] data)
        {
            int headerOffset = 0;

            // 检查 MBR 或直接 GPT
            if (data.Length > 512 && data[510] == 0x55 && data[511] == 0xAA)
            {
                // MBR 存在，GPT 在 LBA 1
                headerOffset = 512;
            }

            // 检查 GPT 签名 "EFI PART"
            if (data.Length < headerOffset + 92)
                return;

            string sig = Encoding.ASCII.GetString(data, headerOffset, 8);
            if (sig != "EFI PART")
            {
                Log($"Invalid GPT signature: {sig}");
                return;
            }

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            ms.Seek(headerOffset + 72, SeekOrigin.Begin);
            ulong partEntryLba = reader.ReadUInt64();
            uint numEntries = reader.ReadUInt32();
            uint entrySize = reader.ReadUInt32();

            Log($"GPT: {numEntries} entries, entry size: {entrySize}");

            // 分区条目从 LBA 2 开始 (或 headerOffset + 512)
            int entriesOffset = headerOffset + 512;
            if (entriesOffset >= data.Length)
            {
                Log("GPT entries not in buffer, need to read more data");
                return;
            }

            ms.Seek(entriesOffset, SeekOrigin.Begin);

            for (int i = 0; i < numEntries && ms.Position + entrySize <= data.Length; i++)
            {
                long entryStart = ms.Position;

                byte[] typeGuid = reader.ReadBytes(16);
                byte[] uniqueGuid = reader.ReadBytes(16);
                ulong firstLba = reader.ReadUInt64();
                ulong lastLba = reader.ReadUInt64();
                ulong attributes = reader.ReadUInt64();
                byte[] nameBytes = reader.ReadBytes(72);

                // 检查是否为空条目
                bool isEmpty = true;
                foreach (byte b in typeGuid)
                {
                    if (b != 0) { isEmpty = false; break; }
                }
                if (isEmpty) break;

                string name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

                Partitions.Add(new MtkPartitionInfo
                {
                    Name = name,
                    TypeGuid = new Guid(typeGuid),
                    UniqueGuid = new Guid(uniqueGuid),
                    StartSector = firstLba,
                    SectorCount = lastLba - firstLba + 1,
                    SectorSize = StorageInfo.BlockSize > 0 ? StorageInfo.BlockSize : 512,
                    Attributes = attributes
                });

                ms.Seek(entryStart + entrySize, SeekOrigin.Begin);
            }

            Log($"Found {Partitions.Count} partitions");
        }

        #endregion

        #region 读写操作

        /// <summary>
        /// 读取闪存数据
        /// </summary>
        public byte[]? ReadFlash(ulong address, ulong length, string? partType)
        {
            if (!XSend(XFlashCmd.READ))
                return null;

            if (GetStatus() != 0)
                return null;

            // 获取存储类型和分区类型
            var (storage, partTypeValue) = GetStorageAndPartType(partType);

            // 参数: storage(4) + parttype(4) + address(8) + length(8)
            byte[] param = new byte[24];
            BitConverter.GetBytes(storage).CopyTo(param, 0);
            BitConverter.GetBytes(partTypeValue).CopyTo(param, 4);
            BitConverter.GetBytes(address).CopyTo(param, 8);
            BitConverter.GetBytes(length).CopyTo(param, 16);

            if (!SendParam(param))
                return null;

            using var ms = new MemoryStream();
            ulong remaining = length;

            while (remaining > 0)
            {
                byte[]? chunk = XRead();
                if (chunk == null || chunk.Length == 0)
                    break;

                ms.Write(chunk, 0, chunk.Length);
                remaining -= (ulong)chunk.Length;

                Ack();
                OnProgress?.Invoke((long)(length - remaining), (long)length);
            }

            GetStatus();  // Final status
            return ms.ToArray();
        }

        /// <summary>
        /// 写入闪存数据
        /// </summary>
        public bool WriteFlash(ulong address, byte[] data, string? partType)
        {
            if (!XSend(XFlashCmd.WRITE))
                return false;

            if (GetStatus() != 0)
                return false;

            var (storage, partTypeValue) = GetStorageAndPartType(partType);

            // 参数
            byte[] param = new byte[24];
            BitConverter.GetBytes(storage).CopyTo(param, 0);
            BitConverter.GetBytes(partTypeValue).CopyTo(param, 4);
            BitConverter.GetBytes(address).CopyTo(param, 8);
            BitConverter.GetBytes((ulong)data.Length).CopyTo(param, 16);

            if (!SendParam(param))
                return false;

            // 分块写入
            int pos = 0;
            int chunkSize = 0x100000;  // 1MB chunks

            while (pos < data.Length)
            {
                int size = Math.Min(chunkSize, data.Length - pos);
                byte[] chunk = new byte[size];
                Array.Copy(data, pos, chunk, 0, size);

                if (!SendData(chunk))
                    return false;

                pos += size;
                OnProgress?.Invoke(pos, data.Length);

                uint status = GetStatus();
                if (status != 0 && status != StatusCode.STATUS_CONTINUE)
                {
                    Log($"Write error at 0x{address + (ulong)pos:X}: 0x{status:X}");
                    return false;
                }
            }

            return GetStatus() == StatusCode.STATUS_COMPLETE;
        }

        /// <summary>
        /// 格式化/擦除闪存
        /// </summary>
        public bool FormatFlash(ulong address, ulong length, string? partType = null)
        {
            if (!XSend(XFlashCmd.FORMAT))
                return false;

            if (GetStatus() != 0)
                return false;

            var (storage, partTypeValue) = GetStorageAndPartType(partType);

            // NandExtension 参数
            byte[] param = new byte[48];
            BitConverter.GetBytes(storage).CopyTo(param, 0);
            BitConverter.GetBytes(partTypeValue).CopyTo(param, 4);
            BitConverter.GetBytes(address).CopyTo(param, 8);
            BitConverter.GetBytes(length).CopyTo(param, 16);
            // 剩余为 NandExtension 字段

            if (!SendParam(param))
                return false;

            while (true)
            {
                uint status = GetStatus();
                if (status == StatusCode.STATUS_CONTINUE)
                {
                    uint delay = GetStatus();
                    Thread.Sleep((int)Math.Min(delay, 5000));
                    Ack();
                }
                else if (status == StatusCode.STATUS_COMPLETE)
                {
                    return true;
                }
                else
                {
                    Log($"Format error: 0x{status:X}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 设备关机
        /// </summary>
        public bool Shutdown(ShutdownMode mode = ShutdownMode.Normal)
        {
            if (!XSend(XFlashCmd.SHUTDOWN))
                return false;

            if (GetStatus() != 0)
                return false;

            byte[] param = new byte[12];
            BitConverter.GetBytes((uint)0).CopyTo(param, 0);  // async_mode
            BitConverter.GetBytes((uint)0).CopyTo(param, 4);  // dl_bit
            BitConverter.GetBytes((uint)mode).CopyTo(param, 8);

            return SendParam(param);
        }

        /// <summary>
        /// 设备重启
        /// </summary>
        public bool Reboot()
        {
            if (!XSend(XFlashCmd.REBOOT))
                return false;

            return GetStatus() == 0;
        }

        private (uint storage, uint partType) GetStorageAndPartType(string? partType)
        {
            uint storage = DaStorage.EMMC;
            uint partTypeValue = EmmcPartitionType.USER;

            if (StorageInfo.Type == "ufs")
            {
                storage = DaStorage.UFS;
                partTypeValue = UfsPartitionType.LU3;  // User
            }
            else if (StorageInfo.Type == "nand")
            {
                storage = DaStorage.NAND;
            }
            else if (StorageInfo.Type == "nor")
            {
                storage = DaStorage.NOR;
            }

            if (!string.IsNullOrEmpty(partType))
            {
                switch (partType.ToLower())
                {
                    case "boot1": partTypeValue = EmmcPartitionType.BOOT1; break;
                    case "boot2": partTypeValue = EmmcPartitionType.BOOT2; break;
                    case "rpmb": partTypeValue = EmmcPartitionType.RPMB; break;
                    case "gp1": partTypeValue = EmmcPartitionType.GP1; break;
                    case "gp2": partTypeValue = EmmcPartitionType.GP2; break;
                    case "gp3": partTypeValue = EmmcPartitionType.GP3; break;
                    case "gp4": partTypeValue = EmmcPartitionType.GP4; break;
                    case "user": partTypeValue = EmmcPartitionType.USER; break;
                }
            }

            return (storage, partTypeValue);
        }

        #endregion

        #region 设置命令

        /// <summary>
        /// 设置重置键
        /// </summary>
        public bool SetResetKey(uint resetKey = 0x68)
        {
            byte[] param = BitConverter.GetBytes(resetKey);
            return SendDevCtrl(XFlashCmd.SET_RESET_KEY, param).Length > 0 || GetStatus() == 0;
        }

        /// <summary>
        /// 设置校验和级别
        /// </summary>
        public bool SetChecksumLevel(uint level = 0)
        {
            byte[] param = BitConverter.GetBytes(level);
            SendDevCtrl(XFlashCmd.SET_CHECKSUM_LEVEL, param);
            return true;
        }

        /// <summary>
        /// 设置电池选项
        /// </summary>
        public bool SetBatteryOpt(uint option = 2)
        {
            byte[] param = BitConverter.GetBytes(option);
            SendDevCtrl(XFlashCmd.SET_BATTERY_OPT, param);
            return true;
        }

        /// <summary>
        /// 设置 META 启动模式
        /// </summary>
        public bool SetMetaBootMode(string mode)
        {
            byte bootMode = 0, comType = 0, comId = 0;

            switch (mode.ToLower())
            {
                case "uart":
                    bootMode = 1; comType = 1; comId = 0;
                    break;
                case "usb":
                    bootMode = 1; comType = 2; comId = 0;
                    break;
                default: // off
                    break;
            }

            byte[] param = new byte[] { bootMode, comType, comId };
            SendDevCtrl(XFlashCmd.SET_META_BOOT_MODE, param);
            return true;
        }

        /// <summary>
        /// 设置远程安全策略 (SLA 签名)
        /// </summary>
        public bool SetRemoteSecPolicy(byte[] signature)
        {
            SendDevCtrl(XFlashCmd.SET_REMOTE_SEC_POLICY, signature);
            return GetStatus() == 0;
        }

        #endregion

        #region 底层协议

        private bool XSend(uint cmd)
        {
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)4).CopyTo(pkt, 8);
            WriteBytes(pkt);
            WriteBytes(BitConverter.GetBytes(cmd));
            return true;
        }

        private bool XSend(uint value, bool is64Bit = false)
        {
            byte[] data = is64Bit ? BitConverter.GetBytes((ulong)value) : BitConverter.GetBytes(value);
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)data.Length).CopyTo(pkt, 8);
            WriteBytes(pkt);
            WriteBytes(data);
            return true;
        }

        private bool XSend(byte[] data)
        {
            if (data == null || data.Length == 0)
                return true;
            
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)data.Length).CopyTo(pkt, 8);
            WriteBytes(pkt);
            WriteBytes(data);
            return true;
        }

        private byte[]? XRead(int timeout = 5000)
        {
            try
            {
                byte[] hdr = ReadBytes(12, timeout);
                uint magic = BitConverter.ToUInt32(hdr, 0);
                uint dataType = BitConverter.ToUInt32(hdr, 4);
                uint length = BitConverter.ToUInt32(hdr, 8);

                if (magic != XFlashCmd.MAGIC)
                {
                    Log($"XRead: Wrong magic 0x{magic:X8}");
                    return null;
                }

                return ReadBytes((int)length, timeout);
            }
            catch (Exception ex)
            {
                Log($"XRead error: {ex.Message}");
                return null;
            }
        }

        private uint GetStatus(int timeout = 5000)
        {
            try
            {
                byte[] hdr = ReadBytes(12, timeout);
                uint magic = BitConverter.ToUInt32(hdr, 0);
                uint dataType = BitConverter.ToUInt32(hdr, 4);
                uint length = BitConverter.ToUInt32(hdr, 8);

                if (magic != XFlashCmd.MAGIC)
                {
                    Log($"Status: Wrong magic 0x{magic:X8}");
                    return 0xFFFFFFFF;
                }

                byte[] data = ReadBytes((int)length, timeout);

                if (length == 2)
                    return BitConverter.ToUInt16(data, 0);
                if (length == 4)
                {
                    uint status = BitConverter.ToUInt32(data, 0);
                    if (status == XFlashCmd.MAGIC)
                        return 0;
                    return status;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log($"GetStatus error: {ex.Message}");
                return 0xFFFFFFFF;
            }
        }

        private bool Ack(bool readStatus = true)
        {
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)4).CopyTo(pkt, 8);
            WriteBytes(pkt);
            WriteBytes(BitConverter.GetBytes((uint)0));

            if (readStatus)
                return GetStatus() == 0;
            return true;
        }

        private bool SendParam(byte[] param)
        {
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)param.Length).CopyTo(pkt, 8);
            WriteBytes(pkt);

            int pos = 0;
            while (pos < param.Length)
            {
                int size = Math.Min(0x200, param.Length - pos);
                byte[] chunk = new byte[size];
                Array.Copy(param, pos, chunk, 0, size);
                WriteBytes(chunk);
                pos += size;
            }

            return GetStatus() == 0;
        }

        private bool SendData(byte[] data)
        {
            byte[] pkt = new byte[12];
            BitConverter.GetBytes(XFlashCmd.MAGIC).CopyTo(pkt, 0);
            BitConverter.GetBytes(DataType.DT_PROTOCOL_FLOW).CopyTo(pkt, 4);
            BitConverter.GetBytes((uint)data.Length).CopyTo(pkt, 8);
            WriteBytes(pkt);

            int pos = 0;
            const int chunkSize = 4096; // 4KB 块大小，提升传输速度
            while (pos < data.Length)
            {
                int size = Math.Min(chunkSize, data.Length - pos);
                byte[] chunk = new byte[size];
                Array.Copy(data, pos, chunk, 0, size);
                WriteBytes(chunk);
                pos += size;
            }

            return GetStatus() == 0;
        }

        private byte[] SendDevCtrl(uint cmd, byte[]? param = null)
        {
            if (!XSend(XFlashCmd.DEVICE_CTRL))
                return Array.Empty<byte>();

            uint status = GetStatus();
            if (status != 0)
                return Array.Empty<byte>();

            if (!XSend(cmd))
                return Array.Empty<byte>();

            status = GetStatus();
            if (status != 0)
                return Array.Empty<byte>();

            if (param != null)
            {
                SendParam(param);
                return Array.Empty<byte>();
            }

            return XRead() ?? Array.Empty<byte>();
        }

        private void WriteBytes(byte[] data)
        {
            lock (_lock)
            {
                if (data.Length > 0)
                    _port?.Write(data, 0, data.Length);
            }
        }

        private byte[] ReadBytes(int count, int timeout = 1000)
        {
            lock (_lock)
            {
                if (_port == null) return Array.Empty<byte>();
                _port.ReadTimeout = timeout;
                byte[] buffer = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int r = _port.Read(buffer, read, count - read);
                    if (r == 0) throw new TimeoutException("Read timeout");
                    read += r;
                }
                return buffer;
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[XFlash] {message}");
            System.Diagnostics.Debug.WriteLine($"[XFlash] {message}");
        }

        public void Dispose()
        {
            Disconnect();
        }
        
        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _port = null;
        }

        /// <summary>
        /// 发送命令包装方法
        /// </summary>
        private bool SendXFlashCommand(uint cmd, byte[] data)
        {
            if (!XSend(XFlashCmd.MAGIC)) return false;
            if (!XSend(cmd)) return false;
            if (data.Length > 0)
            {
                if (!XSend((uint)data.Length)) return false;
                if (!XSend(data)) return false;
            }
            return true;
        }

        #endregion

        #region Flash 读写操作

        /// <summary>
        /// 读取闪存数据
        /// </summary>
        public async Task<byte[]?> ReadFlashAsync(ulong offset, uint length, CancellationToken ct = default)
        {
            try
            {
                // 使用 READ 命令
                byte[] param = new byte[12];
                BitConverter.GetBytes(offset).CopyTo(param, 0);
                BitConverter.GetBytes(length).CopyTo(param, 8);

                if (!SendXFlashCommand(XFlashCmd.READ, param))
                    return null;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"ReadFlash status error: 0x{status:X}");
                    return null;
                }

                // 读取数据
                byte[] data = new byte[length];
                uint readOffset = 0;
                const uint chunkSize = 0x40000; // 256KB per chunk

                while (readOffset < length && !ct.IsCancellationRequested)
                {
                    uint toRead = Math.Min(chunkSize, length - readOffset);

                    // 读取块
                    var chunk = XRead((int)toRead);
                    if (chunk == null || chunk.Length == 0)
                    {
                        Log($"Read chunk failed at offset 0x{readOffset:X}");
                        return null;
                    }

                    Array.Copy(chunk, 0, data, readOffset, chunk.Length);
                    readOffset += (uint)chunk.Length;

                    // ACK
                    status = GetStatus();
                    if (status != 0 && status != 0x96) // 0x96 = continue
                        break;
                }

                return data;
            }
            catch (Exception ex)
            {
                Log($"ReadFlash error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 写入闪存数据
        /// </summary>
        public async Task<bool> WriteFlashAsync(ulong offset, byte[] data, CancellationToken ct = default)
        {
            try
            {
                // 命令: WRITE_FLASH (0x0B)
                // 参数: offset (8 bytes) + length (4 bytes) + storage_type (1 byte)
                byte[] param = new byte[13];
                BitConverter.GetBytes(offset).CopyTo(param, 0);
                BitConverter.GetBytes((uint)data.Length).CopyTo(param, 8);
                param[12] = 0x08; // UFS_LU2 or EMMC_USER

                if (!SendXFlashCommand(XFlashCmd.WRITE, param))
                    return false;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"WriteFlash init status error: 0x{status:X}");
                    return false;
                }

                // 分块发送数据
                uint writeOffset = 0;
                const uint chunkSize = 0x40000; // 256KB per chunk

                while (writeOffset < data.Length && !ct.IsCancellationRequested)
                {
                    uint toWrite = Math.Min(chunkSize, (uint)data.Length - writeOffset);

                    byte[] chunk = new byte[toWrite];
                    Array.Copy(data, writeOffset, chunk, 0, toWrite);

                    // 发送块
                    if (!XSend(chunk))
                    {
                        Log($"Write chunk failed at offset 0x{writeOffset:X}");
                        return false;
                    }

                    writeOffset += toWrite;

                    // 等待 ACK
                    status = GetStatus();
                    if (status != 0 && status != 0x96) // 0x96 = continue
                    {
                        Log($"WriteFlash chunk status error: 0x{status:X}");
                        return false;
                    }
                }

                // 最终确认
                status = GetStatus();
                return status == 0;
            }
            catch (Exception ex)
            {
                Log($"WriteFlash error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 擦除闪存区域
        /// </summary>
        public async Task<bool> EraseFlashAsync(ulong offset, ulong length, CancellationToken ct = default)
        {
            try
            {
                // 命令: ERASE_FLASH (0x0C)
                // 参数: offset (8 bytes) + length (8 bytes) + storage_type (1 byte)
                byte[] param = new byte[17];
                BitConverter.GetBytes(offset).CopyTo(param, 0);
                BitConverter.GetBytes(length).CopyTo(param, 8);
                param[16] = 0x08; // UFS_LU2 or EMMC_USER

                if (!SendXFlashCommand(XFlashCmd.FORMAT, param))
                    return false;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"EraseFlash status error: 0x{status:X}");
                    return false;
                }

                // 等待擦除完成
                while (!ct.IsCancellationRequested)
                {
                    status = GetStatus();
                    if (status == 0)
                        return true;
                    if (status != 0x96) // 0x96 = in progress
                        return false;

                    await Task.Delay(100, ct);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"EraseFlash error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 格式化闪存
        /// </summary>
        public async Task<bool> FormatAsync(CancellationToken ct = default)
        {
            try
            {
                // 命令: FORMAT (0x0D)
                // 参数: format_type (1 byte) + storage_type (1 byte)
                byte[] param = new byte[] { 0x01, 0x08 }; // Format all, USER partition

                if (!SendXFlashCommand(XFlashCmd.FORMAT, param))
                    return false;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"Format init status error: 0x{status:X}");
                    return false;
                }

                // 等待格式化完成 (可能需要很长时间)
                int timeout = 600; // 10 minutes
                while (timeout > 0 && !ct.IsCancellationRequested)
                {
                    status = GetStatus();
                    if (status == 0)
                        return true;
                    if (status != 0x96) // 0x96 = in progress
                        return false;

                    await Task.Delay(1000, ct);
                    timeout--;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"Format error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取分区表
        /// </summary>
        public async Task<byte[]?> GetPartitionTableAsync(CancellationToken ct = default)
        {
            try
            {
                // 命令: GET_PARTITION_TABLE (0x0E)
                if (!SendXFlashCommand(XFlashCmd.GET_PARTITION_TBL_CATA, Array.Empty<byte>()))
                    return null;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"GetPartitionTable status error: 0x{status:X}");
                    return null;
                }

                // 读取分区表数据
                return XRead();
            }
            catch (Exception ex)
            {
                Log($"GetPartitionTable error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 关机/重启设备
        /// </summary>
        public async Task<bool> ShutdownAsync(ShutdownMode mode = ShutdownMode.Normal, CancellationToken ct = default)
        {
            try
            {
                // 命令: SHUTDOWN (0x13)
                byte[] param = new byte[] { (byte)mode };

                if (!SendXFlashCommand(XFlashCmd.SHUTDOWN, param))
                    return false;

                return GetStatus() == 0;
            }
            catch (Exception ex)
            {
                Log($"Shutdown error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 分区操作 (高级接口)

        /// <summary>
        /// 读取 GPT 分区表
        /// </summary>
        public List<MtkPartitionInfo>? ReadGpt()
        {
            try
            {
                Log("Reading GPT partition table...");
                
                // 先获取分区表元数据
                if (!XSend(XFlashCmd.GET_PARTITION_TBL_CATA))
                    return null;

                uint status = GetStatus();
                if (status != 0)
                {
                    Log($"GET_PARTITION_TBL_CATA failed: 0x{status:X}");
                    return null;
                }

                // 读取分区数量
                byte[]? countData = XRead();
                if (countData == null || countData.Length < 4)
                    return null;

                uint partCount = BitConverter.ToUInt32(countData, 0);
                Log($"Found {partCount} partitions");

                // 读取各分区信息
                var partitions = new List<MtkPartitionInfo>();
                
                for (int i = 0; i < partCount; i++)
                {
                    byte[]? partData = XRead();
                    if (partData == null || partData.Length < 72)
                        continue;

                    var partition = new MtkPartitionInfo();
                    
                    // 解析分区名称 (64 bytes)
                    partition.Name = System.Text.Encoding.UTF8.GetString(partData, 0, 64).TrimEnd('\0');
                    
                    // 解析起始扇区和扇区数
                    partition.StartSector = BitConverter.ToUInt64(partData, 64);
                    if (partData.Length >= 80)
                    {
                        partition.SectorCount = BitConverter.ToUInt64(partData, 72);
                    }
                    
                    partition.SectorSize = StorageInfo.BlockSize > 0 ? StorageInfo.BlockSize : 512;
                    
                    partitions.Add(partition);
                    Partitions.Add(partition);
                }

                Log($"GPT: Loaded {partitions.Count} partitions");
                return partitions;
            }
            catch (Exception ex)
            {
                Log($"ReadGpt error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 按分区名读取数据
        /// </summary>
        public byte[]? ReadPartitionData(string partitionName, long offset, int length)
        {
            try
            {
                // 查找分区
                var partition = Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                
                if (partition == null)
                {
                    Log($"Partition not found: {partitionName}");
                    return null;
                }

                // 计算绝对地址
                ulong absoluteAddr = partition.Offset + (ulong)offset;
                
                // 使用 ReadFlash 读取
                return ReadFlash(absoluteAddr, (ulong)length, null);
            }
            catch (Exception ex)
            {
                Log($"ReadPartitionData error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 按分区名写入数据
        /// </summary>
        public bool WritePartitionData(string partitionName, long offset, byte[] data)
        {
            try
            {
                // 查找分区
                var partition = Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                
                if (partition == null)
                {
                    Log($"Partition not found: {partitionName}");
                    return false;
                }

                // 计算绝对地址
                ulong absoluteAddr = partition.Offset + (ulong)offset;
                
                // 使用 WriteFlash 写入
                return WriteFlash(absoluteAddr, data, null);
            }
            catch (Exception ex)
            {
                Log($"WritePartitionData error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 擦除分区
        /// </summary>
        public bool ErasePartition(string partitionName)
        {
            try
            {
                // 查找分区
                var partition = Partitions.Find(p => 
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                
                if (partition == null)
                {
                    Log($"Partition not found: {partitionName}");
                    return false;
                }

                Log($"Erasing partition: {partitionName} ({partition.Size / (1024.0 * 1024):F2} MB)");

                // 使用 FormatFlash 擦除
                return FormatFlash(partition.Offset, partition.Size, null);
            }
            catch (Exception ex)
            {
                Log($"ErasePartition error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 初始化 DA (获取存储信息和分区表)
        /// </summary>
        public bool Initialize()
        {
            try
            {
                Log("Initializing DA...");
                
                // 1. 同步
                if (!Sync())
                {
                    Log("DA sync failed");
                    return false;
                }

                // 2. 获取存储信息
                Reinit(true);

                // 3. 读取分区表
                ReadGpt();

                Log($"DA initialized: Storage={StorageInfo.Type}, Partitions={Partitions.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Initialize error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 同步 DA
        /// </summary>
        private bool Sync()
        {
            try
            {
                // 发送同步信号
                if (!XSend(XFlashCmd.SYNC_SIGNAL))
                    return false;

                uint status = GetStatus();
                return status == 0 || status == XFlashCmd.SYNC_SIGNAL;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 发送自定义命令 (用于扩展功能)
        /// </summary>
        /// <param name="command">命令数据</param>
        /// <param name="responseLength">期望的响应长度</param>
        /// <returns>响应数据</returns>
        public byte[]? SendCustomCommand(byte[] command, int responseLength)
        {
            lock (_lock)
            {
                try
                {
                    if (_port == null || !_port.IsOpen)
                        return null;

                    // 清空缓冲区
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();

                    // 发送命令
                    _port.Write(command, 0, command.Length);

                    // 读取响应
                    if (responseLength > 0)
                    {
                        var response = new byte[responseLength];
                        int totalRead = 0;
                        int timeout = 5000;
                        int startTime = Environment.TickCount;

                        while (totalRead < responseLength)
                        {
                            if (Environment.TickCount - startTime > timeout)
                            {
                                Log($"Custom command timeout: read {totalRead}/{responseLength}");
                                return totalRead > 0 ? response[..totalRead] : null;
                            }

                            if (_port.BytesToRead > 0)
                            {
                                int read = _port.Read(response, totalRead, responseLength - totalRead);
                                totalRead += read;
                            }
                            else
                            {
                                Thread.Sleep(1);
                            }
                        }

                        return response;
                    }

                    return Array.Empty<byte>();
                }
                catch (Exception ex)
                {
                    Log($"SendCustomCommand error: {ex.Message}");
                    return null;
                }
            }
        }

        #endregion
    }
}
