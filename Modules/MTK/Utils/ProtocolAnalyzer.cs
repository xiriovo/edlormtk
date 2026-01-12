using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace tools.Modules.MTK.Utils
{
    /// <summary>
    /// MTK 协议分析器
    /// 用于抓包分析和对比官方工具
    /// </summary>
    public class ProtocolAnalyzer
    {
        private readonly List<PacketRecord> _packets = new();
        private readonly object _lock = new();
        private bool _enabled = false;
        private StreamWriter? _logWriter;

        public event Action<string>? OnLog;

        /// <summary>
        /// 数据包方向
        /// </summary>
        public enum Direction
        {
            Send,    // 发送到设备
            Receive  // 从设备接收
        }

        /// <summary>
        /// 数据包记录
        /// </summary>
        public class PacketRecord
        {
            public DateTime Timestamp { get; set; }
            public Direction Direction { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public string? Description { get; set; }
            public string? CommandName { get; set; }
            public int Index { get; set; }
        }

        /// <summary>
        /// 启用分析器
        /// </summary>
        public void Enable(string? logFile = null)
        {
            _enabled = true;
            if (logFile != null)
            {
                _logWriter = new StreamWriter(logFile, false, Encoding.UTF8);
                _logWriter.WriteLine($"# MTK Protocol Capture - {DateTime.Now}");
                _logWriter.WriteLine($"# Format: [Index] [Time] [Direction] [Length] [Data] [Description]");
                _logWriter.WriteLine();
            }
            Log("Protocol Analyzer enabled");
        }

        /// <summary>
        /// 禁用分析器
        /// </summary>
        public void Disable()
        {
            _enabled = false;
            _logWriter?.Flush();
            _logWriter?.Dispose();
            _logWriter = null;
            Log("Protocol Analyzer disabled");
        }

        /// <summary>
        /// 记录发送的数据
        /// </summary>
        public void RecordSend(byte[] data, string? description = null)
        {
            if (!_enabled) return;
            Record(Direction.Send, data, description);
        }

        /// <summary>
        /// 记录接收的数据
        /// </summary>
        public void RecordReceive(byte[] data, string? description = null)
        {
            if (!_enabled) return;
            Record(Direction.Receive, data, description);
        }

        /// <summary>
        /// 记录数据包
        /// </summary>
        private void Record(Direction dir, byte[] data, string? description)
        {
            lock (_lock)
            {
                var record = new PacketRecord
                {
                    Timestamp = DateTime.Now,
                    Direction = dir,
                    Data = (byte[])data.Clone(),
                    Description = description,
                    CommandName = ParseCommandName(data, dir),
                    Index = _packets.Count
                };

                _packets.Add(record);

                // 输出到日志
                string dirStr = dir == Direction.Send ? "TX" : "RX";
                string hex = BitConverter.ToString(data).Replace("-", " ");
                if (hex.Length > 100) hex = hex.Substring(0, 100) + "...";
                
                string line = $"[{record.Index:D4}] [{record.Timestamp:HH:mm:ss.fff}] [{dirStr}] [{data.Length,5}] {hex}";
                if (!string.IsNullOrEmpty(record.CommandName))
                    line += $" | {record.CommandName}";
                if (!string.IsNullOrEmpty(description))
                    line += $" | {description}";

                _logWriter?.WriteLine(line);
                Log(line);
            }
        }

        /// <summary>
        /// 解析命令名称
        /// </summary>
        private string? ParseCommandName(byte[] data, Direction dir)
        {
            if (data.Length == 0) return null;

            // Preloader 命令 (单字节)
            if (data.Length == 1 && dir == Direction.Send)
            {
                return data[0] switch
                {
                    0xFD => "GET_HW_CODE",
                    0xFC => "GET_HW_SW_VER",
                    0xFE => "GET_BL_VER",
                    0xFF => "GET_VERSION",
                    0xD8 => "GET_TARGET_CONFIG",
                    0xD0 => "READ16",
                    0xD1 => "READ32",
                    0xD2 => "WRITE16",
                    0xD4 => "WRITE32",
                    0xD7 => "SEND_DA",
                    0xD5 => "JUMP_DA",
                    0xDE => "JUMP_DA64",
                    0xE1 => "GET_ME_ID",
                    0xE7 => "GET_SOC_ID",
                    0xE0 => "SEND_CERT",
                    0xE2 => "SEND_AUTH",
                    0xE3 => "SLA",
                    0xDA => "BROM_REGISTER_ACCESS",
                    0xFB => "GET_PL_CAP",
                    0xD6 => "JUMP_BL",
                    0x70 => "SEND_PARTITION_DATA",
                    0x71 => "JUMP_TO_PARTITION",
                    0xB0 => "I2C_INIT",
                    0xB1 => "I2C_DEINIT",
                    0xC4 => "PWR_INIT",
                    0xC8 => "CMD_C8",
                    0xA2 => "CMD_READ16_A2",
                    _ => $"CMD_0x{data[0]:X2}"
                };
            }

            // XFlash 协议 (带 Magic)
            if (data.Length >= 12 && dir == Direction.Send)
            {
                uint magic = BitConverter.ToUInt32(data, 0);
                if (magic == 0xFEEEEEEF)
                {
                    uint dataType = BitConverter.ToUInt32(data, 4);
                    uint length = BitConverter.ToUInt32(data, 8);
                    
                    if (dataType == 1 && length >= 4)
                    {
                        uint cmd = BitConverter.ToUInt32(data, 12);
                        return GetXFlashCommandName(cmd);
                    }
                    return $"XFLASH_DT{dataType}";
                }
            }

            // 响应检测
            if (dir == Direction.Receive)
            {
                if (data.Length == 1)
                {
                    return data[0] switch
                    {
                        0x5A => "ACK",
                        0xA5 => "NACK",
                        0x69 => "CONF",
                        0x96 => "STOP",
                        _ => $"RSP_0x{data[0]:X2}"
                    };
                }

                if (data.Length >= 4)
                {
                    uint magic = BitConverter.ToUInt32(data, 0);
                    if (magic == 0xFEEEEEEF)
                        return "XFLASH_RSP";
                }
            }

            return null;
        }

        /// <summary>
        /// 获取 XFlash 命令名称
        /// </summary>
        private string GetXFlashCommandName(uint cmd)
        {
            return cmd switch
            {
                0x01 => "XFLASH_SYNC",
                0x02 => "XFLASH_INIT_EXT_RAM",
                0x03 => "XFLASH_GET_CONNECTION_AGENT",
                0x04 => "XFLASH_GET_CHIP_ID",
                0x05 => "XFLASH_GET_RAM_INFO",
                0x06 => "XFLASH_GET_EMMC_INFO",
                0x07 => "XFLASH_GET_NAND_INFO",
                0x08 => "XFLASH_GET_NOR_INFO",
                0x09 => "XFLASH_GET_UFS_INFO",
                0x0A => "XFLASH_GET_DA_VERSION",
                0x0B => "XFLASH_GET_RANDOM_ID",
                0x0C => "XFLASH_GET_EXPIRE_DATA",
                0x10 => "XFLASH_READ",
                0x11 => "XFLASH_WRITE",
                0x12 => "XFLASH_ERASE",
                0x13 => "XFLASH_FORMAT",
                0x17 => "XFLASH_SHUTDOWN",
                0x18 => "XFLASH_REBOOT",
                0x20 => "XFLASH_WRITE_PARTITIONS",
                0x21 => "XFLASH_WRITE_PARTITION_RAW",
                0x22 => "XFLASH_WRITE_FLASH_RAW",
                0x30 => "XFLASH_DEVICE_CTRL",
                0x40 => "XFLASH_BOOT_TO",
                0x70 => "XFLASH_GET_PACKET_LENGTH",
                _ => $"XFLASH_CMD_{cmd:X4}"
            };
        }

        /// <summary>
        /// 获取所有数据包
        /// </summary>
        public List<PacketRecord> GetPackets()
        {
            lock (_lock)
            {
                return new List<PacketRecord>(_packets);
            }
        }

        /// <summary>
        /// 清除所有记录
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _packets.Clear();
            }
        }

        /// <summary>
        /// 导出到文件
        /// </summary>
        public void ExportToFile(string filename)
        {
            lock (_lock)
            {
                using var writer = new StreamWriter(filename, false, Encoding.UTF8);
                writer.WriteLine($"# MTK Protocol Capture Export");
                writer.WriteLine($"# Exported: {DateTime.Now}");
                writer.WriteLine($"# Total Packets: {_packets.Count}");
                writer.WriteLine();

                foreach (var pkt in _packets)
                {
                    string dirStr = pkt.Direction == Direction.Send ? "TX" : "RX";
                    string hex = BitConverter.ToString(pkt.Data).Replace("-", " ");
                    
                    writer.WriteLine($"[{pkt.Index:D4}] [{pkt.Timestamp:HH:mm:ss.fff}] [{dirStr}] [{pkt.Data.Length,5}]");
                    if (!string.IsNullOrEmpty(pkt.CommandName))
                        writer.WriteLine($"  Command: {pkt.CommandName}");
                    if (!string.IsNullOrEmpty(pkt.Description))
                        writer.WriteLine($"  Description: {pkt.Description}");
                    writer.WriteLine($"  Data: {hex}");
                    writer.WriteLine();
                }
            }
        }

        /// <summary>
        /// 生成对比报告
        /// </summary>
        public string GenerateComparisonReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                    MTK Protocol Analysis Report");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"Generated: {DateTime.Now}");
            sb.AppendLine($"Total Packets: {_packets.Count}");
            sb.AppendLine();

            // 统计命令使用
            var cmdStats = new Dictionary<string, int>();
            foreach (var pkt in _packets)
            {
                if (!string.IsNullOrEmpty(pkt.CommandName))
                {
                    if (!cmdStats.ContainsKey(pkt.CommandName))
                        cmdStats[pkt.CommandName] = 0;
                    cmdStats[pkt.CommandName]++;
                }
            }

            sb.AppendLine("Command Statistics:");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            foreach (var kv in cmdStats)
            {
                sb.AppendLine($"  {kv.Key,-30}: {kv.Value}");
            }
            sb.AppendLine();

            // 数据流分析
            sb.AppendLine("Data Flow Summary:");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            long totalSent = 0, totalReceived = 0;
            foreach (var pkt in _packets)
            {
                if (pkt.Direction == Direction.Send)
                    totalSent += pkt.Data.Length;
                else
                    totalReceived += pkt.Data.Length;
            }
            sb.AppendLine($"  Total Sent:     {totalSent:N0} bytes");
            sb.AppendLine($"  Total Received: {totalReceived:N0} bytes");
            sb.AppendLine();

            return sb.ToString();
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[Analyzer] {message}");
        }
    }

    /// <summary>
    /// 数据包比较器
    /// 用于对比官方工具和我们的实现
    /// </summary>
    public class PacketComparator
    {
        /// <summary>
        /// 比较两组数据包
        /// </summary>
        public static ComparisonResult Compare(
            List<ProtocolAnalyzer.PacketRecord> official,
            List<ProtocolAnalyzer.PacketRecord> ours)
        {
            var result = new ComparisonResult();

            int maxLen = Math.Max(official.Count, ours.Count);
            for (int i = 0; i < maxLen; i++)
            {
                var diff = new PacketDifference { Index = i };

                if (i >= official.Count)
                {
                    diff.Type = DiffType.ExtraInOurs;
                    diff.OurPacket = ours[i];
                }
                else if (i >= ours.Count)
                {
                    diff.Type = DiffType.MissingInOurs;
                    diff.OfficialPacket = official[i];
                }
                else
                {
                    diff.OfficialPacket = official[i];
                    diff.OurPacket = ours[i];

                    if (!ComparePackets(official[i], ours[i]))
                    {
                        diff.Type = DiffType.Different;
                    }
                    else
                    {
                        diff.Type = DiffType.Same;
                        result.MatchCount++;
                        continue; // 不添加到差异列表
                    }
                }

                result.Differences.Add(diff);
            }

            result.TotalOfficial = official.Count;
            result.TotalOurs = ours.Count;
            return result;
        }

        private static bool ComparePackets(
            ProtocolAnalyzer.PacketRecord a,
            ProtocolAnalyzer.PacketRecord b)
        {
            if (a.Direction != b.Direction) return false;
            if (a.Data.Length != b.Data.Length) return false;
            
            for (int i = 0; i < a.Data.Length; i++)
            {
                if (a.Data[i] != b.Data[i]) return false;
            }
            return true;
        }
    }

    public enum DiffType
    {
        Same,
        Different,
        MissingInOurs,
        ExtraInOurs
    }

    public class PacketDifference
    {
        public int Index { get; set; }
        public DiffType Type { get; set; }
        public ProtocolAnalyzer.PacketRecord? OfficialPacket { get; set; }
        public ProtocolAnalyzer.PacketRecord? OurPacket { get; set; }
    }

    public class ComparisonResult
    {
        public int TotalOfficial { get; set; }
        public int TotalOurs { get; set; }
        public int MatchCount { get; set; }
        public List<PacketDifference> Differences { get; } = new();

        public double MatchRate => TotalOfficial > 0 ? (double)MatchCount / TotalOfficial * 100 : 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("              Protocol Comparison Report");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"Official Packets: {TotalOfficial}");
            sb.AppendLine($"Our Packets:      {TotalOurs}");
            sb.AppendLine($"Matches:          {MatchCount}");
            sb.AppendLine($"Match Rate:       {MatchRate:F1}%");
            sb.AppendLine();
            
            if (Differences.Count > 0)
            {
                sb.AppendLine("Differences:");
                sb.AppendLine("───────────────────────────────────────────────────────────────");
                foreach (var diff in Differences)
                {
                    sb.AppendLine($"[{diff.Index}] {diff.Type}");
                    if (diff.OfficialPacket != null)
                    {
                        string hex = BitConverter.ToString(diff.OfficialPacket.Data).Replace("-", " ");
                        if (hex.Length > 60) hex = hex.Substring(0, 60) + "...";
                        sb.AppendLine($"  Official: {hex}");
                    }
                    if (diff.OurPacket != null)
                    {
                        string hex = BitConverter.ToString(diff.OurPacket.Data).Replace("-", " ");
                        if (hex.Length > 60) hex = hex.Substring(0, 60) + "...";
                        sb.AppendLine($"  Ours:     {hex}");
                    }
                }
            }

            return sb.ToString();
        }
    }
}
