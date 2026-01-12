using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace tools.Modules.MTK.Utils
{
    /// <summary>
    /// USB 抓包数据解析器
    /// 支持 Wireshark USB 抓包和 USBPcap 格式
    /// </summary>
    public class UsbSniffer
    {
        /// <summary>
        /// USB 数据包
        /// </summary>
        public class UsbPacket
        {
            public DateTime Timestamp { get; set; }
            public int PacketNumber { get; set; }
            public string Direction { get; set; } = ""; // "host->device" or "device->host"
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public int Endpoint { get; set; }
            public string TransferType { get; set; } = ""; // "BULK", "CONTROL", "INTERRUPT"
            public int DeviceAddress { get; set; }

            public bool IsOutgoing => Direction.Contains("host");
        }

        /// <summary>
        /// 从 Wireshark 文本导出解析 USB 数据
        /// </summary>
        public static List<UsbPacket> ParseWiresharkExport(string filePath)
        {
            var packets = new List<UsbPacket>();
            var lines = File.ReadAllLines(filePath);
            
            UsbPacket? current = null;
            var dataBuilder = new List<byte>();

            foreach (var line in lines)
            {
                // 检测新数据包开始 (Wireshark 格式)
                // 格式: No.     Time           Source                Destination           Protocol Length Info
                if (Regex.IsMatch(line, @"^\d+\s+[\d.]+"))
                {
                    // 保存前一个数据包
                    if (current != null)
                    {
                        current.Data = dataBuilder.ToArray();
                        packets.Add(current);
                        dataBuilder.Clear();
                    }

                    current = new UsbPacket();
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        current.PacketNumber = int.Parse(parts[0]);
                        if (double.TryParse(parts[1], out double time))
                        {
                            current.Timestamp = DateTime.Now.AddSeconds(time);
                        }
                    }
                    
                    // 解析方向
                    if (line.Contains("host") && line.Contains("->"))
                    {
                        current.Direction = "host->device";
                    }
                    else
                    {
                        current.Direction = "device->host";
                    }

                    // 解析传输类型
                    if (line.Contains("URB_BULK"))
                        current.TransferType = "BULK";
                    else if (line.Contains("URB_CONTROL"))
                        current.TransferType = "CONTROL";
                    else if (line.Contains("URB_INTERRUPT"))
                        current.TransferType = "INTERRUPT";
                }
                // 解析十六进制数据行
                else if (Regex.IsMatch(line.Trim(), @"^[0-9a-fA-F]{4}\s"))
                {
                    // 格式: 0000   xx xx xx xx ...
                    var hexPart = line.Substring(6).Trim();
                    var hexBytes = hexPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var hex in hexBytes)
                    {
                        if (hex.Length == 2 && Regex.IsMatch(hex, @"^[0-9a-fA-F]{2}$"))
                        {
                            dataBuilder.Add(Convert.ToByte(hex, 16));
                        }
                    }
                }
            }

            // 添加最后一个数据包
            if (current != null)
            {
                current.Data = dataBuilder.ToArray();
                packets.Add(current);
            }

            return packets;
        }

        /// <summary>
        /// 从十六进制文本文件解析
        /// 格式: 每行一个数据包，TX/RX 前缀
        /// </summary>
        public static List<UsbPacket> ParseHexLog(string filePath)
        {
            var packets = new List<UsbPacket>();
            var lines = File.ReadAllLines(filePath);
            int index = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var packet = new UsbPacket { PacketNumber = index++ };

                string hexData;
                if (line.StartsWith("TX:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith(">>", StringComparison.Ordinal))
                {
                    packet.Direction = "host->device";
                    hexData = line.Substring(line.IndexOf(':') + 1).Trim();
                    if (line.StartsWith(">>")) hexData = line.Substring(2).Trim();
                }
                else if (line.StartsWith("RX:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("<<", StringComparison.Ordinal))
                {
                    packet.Direction = "device->host";
                    hexData = line.Substring(line.IndexOf(':') + 1).Trim();
                    if (line.StartsWith("<<")) hexData = line.Substring(2).Trim();
                }
                else
                {
                    hexData = line.Trim();
                    packet.Direction = "unknown";
                }

                // 解析十六进制
                hexData = hexData.Replace(" ", "").Replace("-", "");
                if (hexData.Length % 2 == 0)
                {
                    var data = new List<byte>();
                    for (int i = 0; i < hexData.Length; i += 2)
                    {
                        data.Add(Convert.ToByte(hexData.Substring(i, 2), 16));
                    }
                    packet.Data = data.ToArray();
                    packets.Add(packet);
                }
            }

            return packets;
        }

        /// <summary>
        /// 将 USB 数据包转换为协议分析器格式
        /// </summary>
        public static List<ProtocolAnalyzer.PacketRecord> ConvertToPacketRecords(List<UsbPacket> usbPackets)
        {
            var records = new List<ProtocolAnalyzer.PacketRecord>();
            int index = 0;

            foreach (var pkt in usbPackets)
            {
                // 跳过空数据包和 USB 控制包
                if (pkt.Data.Length == 0) continue;
                if (pkt.TransferType == "CONTROL" && !IsRelevantControlTransfer(pkt)) continue;

                var record = new ProtocolAnalyzer.PacketRecord
                {
                    Index = index++,
                    Timestamp = pkt.Timestamp,
                    Direction = pkt.IsOutgoing 
                        ? ProtocolAnalyzer.Direction.Send 
                        : ProtocolAnalyzer.Direction.Receive,
                    Data = pkt.Data
                };

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// 检查是否是相关的控制传输
        /// </summary>
        private static bool IsRelevantControlTransfer(UsbPacket pkt)
        {
            // MTK 使用的控制传输:
            // - Kamakiri: bmRequestType=0xA1, bRequest=0
            // - Kamakiri2: SET_LINE_CODING (0x21, 0x20)
            if (pkt.Data.Length >= 8)
            {
                byte bmRequestType = pkt.Data[0];
                byte bRequest = pkt.Data[1];

                // Kamakiri 相关
                if (bmRequestType == 0xA1 && bRequest == 0) return true;
                // SET_LINE_CODING
                if (bmRequestType == 0x21 && bRequest == 0x20) return true;
                // GET_LINE_CODING
                if (bmRequestType == 0xA1 && bRequest == 0x21) return true;
            }
            return false;
        }

        /// <summary>
        /// 过滤 MTK 设备的数据包
        /// </summary>
        public static List<UsbPacket> FilterMtkDevice(List<UsbPacket> packets, int vid = 0x0E8D)
        {
            // 实际实现需要检查设备地址
            // 这里简化为返回所有 BULK 传输
            var filtered = new List<UsbPacket>();
            foreach (var pkt in packets)
            {
                if (pkt.TransferType == "BULK" || IsRelevantControlTransfer(pkt))
                {
                    filtered.Add(pkt);
                }
            }
            return filtered;
        }
    }

    /// <summary>
    /// 官方工具日志解析器
    /// 用于解析 SP Flash Tool 等官方工具的日志
    /// </summary>
    public class OfficialToolLogParser
    {
        /// <summary>
        /// 解析 SP Flash Tool 日志
        /// </summary>
        public static List<LogEntry> ParseSpFlashToolLog(string filePath)
        {
            var entries = new List<LogEntry>();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var entry = new LogEntry { RawLine = line };

                // 时间戳格式: [2024-01-01 12:00:00.000]
                var timeMatch = Regex.Match(line, @"\[(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d+)\]");
                if (timeMatch.Success)
                {
                    entry.Timestamp = DateTime.Parse(timeMatch.Groups[1].Value);
                }

                // 检测命令
                if (line.Contains("Send") || line.Contains("Write"))
                {
                    entry.Type = LogEntryType.Send;
                }
                else if (line.Contains("Recv") || line.Contains("Read"))
                {
                    entry.Type = LogEntryType.Receive;
                }
                else if (line.Contains("Error") || line.Contains("Fail"))
                {
                    entry.Type = LogEntryType.Error;
                }
                else
                {
                    entry.Type = LogEntryType.Info;
                }

                // 提取十六进制数据
                var hexMatch = Regex.Match(line, @":\s*([0-9A-Fa-f\s]+)$");
                if (hexMatch.Success)
                {
                    string hex = hexMatch.Groups[1].Value.Replace(" ", "");
                    if (hex.Length % 2 == 0 && hex.Length > 0)
                    {
                        var data = new List<byte>();
                        for (int i = 0; i < hex.Length; i += 2)
                        {
                            data.Add(Convert.ToByte(hex.Substring(i, 2), 16));
                        }
                        entry.Data = data.ToArray();
                    }
                }

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// 生成对比摘要
        /// </summary>
        public static string GenerateSummary(List<LogEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Official Tool Log Summary");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"Total Entries: {entries.Count}");
            sb.AppendLine($"Send Operations: {entries.FindAll(e => e.Type == LogEntryType.Send).Count}");
            sb.AppendLine($"Receive Operations: {entries.FindAll(e => e.Type == LogEntryType.Receive).Count}");
            sb.AppendLine($"Errors: {entries.FindAll(e => e.Type == LogEntryType.Error).Count}");
            return sb.ToString();
        }
    }

    public enum LogEntryType
    {
        Info,
        Send,
        Receive,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogEntryType Type { get; set; }
        public string RawLine { get; set; } = "";
        public byte[]? Data { get; set; }
    }
}
