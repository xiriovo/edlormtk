using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace tools.Modules.MTK.Utils
{
    /// <summary>
    /// 逆向工程辅助工具
    /// 用于分析官方工具的二进制和协议
    /// </summary>
    public static class ReverseEngineering
    {
        #region 二进制分析

        /// <summary>
        /// 查找二进制模式
        /// </summary>
        public static List<int> FindPattern(byte[] data, byte[] pattern, byte[]? mask = null)
        {
            var results = new List<int>();
            
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    byte maskByte = mask != null && j < mask.Length ? mask[j] : (byte)0xFF;
                    if ((data[i + j] & maskByte) != (pattern[j] & maskByte))
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    results.Add(i);
                }
            }

            return results;
        }

        /// <summary>
        /// 查找字符串
        /// </summary>
        public static List<(int Offset, string Value)> FindStrings(byte[] data, int minLength = 4)
        {
            var results = new List<(int, string)>();
            var current = new List<byte>();
            int startOffset = 0;

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 0x20 && b < 0x7F) // 可打印 ASCII
                {
                    if (current.Count == 0)
                        startOffset = i;
                    current.Add(b);
                }
                else
                {
                    if (current.Count >= minLength)
                    {
                        results.Add((startOffset, Encoding.ASCII.GetString(current.ToArray())));
                    }
                    current.Clear();
                }
            }

            if (current.Count >= minLength)
            {
                results.Add((startOffset, Encoding.ASCII.GetString(current.ToArray())));
            }

            return results;
        }

        /// <summary>
        /// 分析 DA 文件结构
        /// </summary>
        public static DaFileInfo AnalyzeDaFile(byte[] data)
        {
            var info = new DaFileInfo
            {
                Size = data.Length
            };

            // 检测 DA 版本
            // V5 DA 通常以特定模式开始
            // V6 DA 有不同的结构

            // 查找 "MTK_DA" 签名
            var mtkDaOffsets = FindPattern(data, Encoding.ASCII.GetBytes("MTK_DA"));
            if (mtkDaOffsets.Count > 0)
            {
                info.HasMtkDaSignature = true;
                info.SignatureOffset = mtkDaOffsets[0];
            }

            // 查找版本字符串
            var versionStrings = FindStrings(data, 6);
            foreach (var (offset, str) in versionStrings)
            {
                if (str.Contains("DA_") || str.Contains("V5") || str.Contains("V6"))
                {
                    info.VersionStrings.Add(str);
                }
            }

            // 分析 DA 头部
            if (data.Length >= 0x100)
            {
                // DA 头部通常包含:
                // - Magic
                // - 区域信息 (地址, 大小, 签名长度)
                uint magic = BitConverter.ToUInt32(data, 0);
                info.Magic = magic;

                // 尝试解析区域
                for (int i = 0; i < 8; i++)
                {
                    int offset = 0x10 + i * 0x30;
                    if (offset + 0x30 <= data.Length)
                    {
                        var region = new DaRegion
                        {
                            Index = i,
                            Address = BitConverter.ToUInt32(data, offset),
                            Length = BitConverter.ToUInt32(data, offset + 4),
                            SignatureLength = BitConverter.ToUInt32(data, offset + 8)
                        };

                        // 验证区域是否有效
                        if (region.Address > 0 && region.Address < 0xFFFFFFFF &&
                            region.Length > 0 && region.Length < 0x1000000)
                        {
                            info.Regions.Add(region);
                        }
                    }
                }
            }

            // 检测加密/签名
            // DA 通常在末尾有 256 字节的 RSA 签名
            if (data.Length > 0x100)
            {
                byte[] lastBytes = data.Skip(data.Length - 0x100).ToArray();
                bool allZero = lastBytes.All(b => b == 0);
                info.HasSignature = !allZero;
            }

            return info;
        }

        /// <summary>
        /// 分析 Preloader 文件
        /// </summary>
        public static PreloaderInfo AnalyzePreloader(byte[] data)
        {
            var info = new PreloaderInfo
            {
                Size = data.Length
            };

            // 查找 Preloader 头部 "MMM\x01"
            var headerOffsets = FindPattern(data, new byte[] { 0x4D, 0x4D, 0x4D, 0x01 });
            if (headerOffsets.Count > 0)
            {
                info.HeaderOffset = headerOffsets[0];
                
                // 解析头部信息
                int offset = headerOffsets[0];
                if (offset + 0x40 <= data.Length)
                {
                    info.Magic = BitConverter.ToUInt32(data, offset);
                    info.Version = BitConverter.ToUInt16(data, offset + 4);
                    // 更多字段...
                }
            }

            // 查找 EMI 配置
            var emiOffsets = FindPattern(data, Encoding.ASCII.GetBytes("EMI_"));
            if (emiOffsets.Count > 0)
            {
                info.HasEmiConfig = true;
                info.EmiOffset = emiOffsets[0];
            }

            // 查找 GFH 头部 (Generic File Header)
            var gfhOffsets = FindPattern(data, new byte[] { 0x4D, 0x4D, 0x4D, 0x00 });
            foreach (var gfhOffset in gfhOffsets)
            {
                if (gfhOffset + 0x38 <= data.Length)
                {
                    var gfh = new GfhHeader
                    {
                        Offset = gfhOffset,
                        Magic = BitConverter.ToUInt32(data, gfhOffset),
                        Size = BitConverter.ToUInt16(data, gfhOffset + 4),
                        Type = BitConverter.ToUInt16(data, gfhOffset + 6)
                    };
                    info.GfhHeaders.Add(gfh);
                }
            }

            return info;
        }

        #endregion

        #region 协议分析

        /// <summary>
        /// 解析数据包序列，识别协议流程
        /// </summary>
        public static ProtocolFlow AnalyzeProtocolFlow(List<ProtocolAnalyzer.PacketRecord> packets)
        {
            var flow = new ProtocolFlow();

            for (int i = 0; i < packets.Count; i++)
            {
                var pkt = packets[i];
                
                if (pkt.Direction == ProtocolAnalyzer.Direction.Send && pkt.Data.Length > 0)
                {
                    var step = new ProtocolStep
                    {
                        Index = i,
                        CommandByte = pkt.Data[0],
                        CommandName = pkt.CommandName ?? $"CMD_0x{pkt.Data[0]:X2}",
                        RequestData = pkt.Data
                    };

                    // 查找对应的响应
                    if (i + 1 < packets.Count && 
                        packets[i + 1].Direction == ProtocolAnalyzer.Direction.Receive)
                    {
                        step.ResponseData = packets[i + 1].Data;
                        step.HasResponse = true;
                    }

                    flow.Steps.Add(step);
                }
            }

            // 识别协议阶段
            IdentifyPhases(flow);

            return flow;
        }

        /// <summary>
        /// 识别协议阶段
        /// </summary>
        private static void IdentifyPhases(ProtocolFlow flow)
        {
            foreach (var step in flow.Steps)
            {
                step.Phase = step.CommandByte switch
                {
                    0xFD or 0xFC or 0xFE or 0xFF or 0xD8 => "Handshake",
                    0xD0 or 0xD1 or 0xD2 or 0xD4 => "Memory Access",
                    0xD7 or 0xD5 or 0xDE => "DA Transfer",
                    0xE0 or 0xE1 or 0xE2 or 0xE3 => "Authentication",
                    0xDA => "Exploit (BROM_REGISTER_ACCESS)",
                    _ when step.RequestData.Length >= 12 && 
                           BitConverter.ToUInt32(step.RequestData, 0) == 0xFEEEEEEF => "XFlash DA",
                    _ => "Unknown"
                };
            }
        }

        /// <summary>
        /// 生成协议文档
        /// </summary>
        public static string GenerateProtocolDoc(ProtocolFlow flow)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                    Protocol Flow Analysis");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();

            string currentPhase = "";
            foreach (var step in flow.Steps)
            {
                if (step.Phase != currentPhase)
                {
                    currentPhase = step.Phase;
                    sb.AppendLine($"\n─── {currentPhase} ───────────────────────────────────────────");
                }

                sb.AppendLine($"\n[{step.Index}] {step.CommandName}");
                sb.AppendLine($"    Request:  {FormatHex(step.RequestData, 40)}");
                if (step.HasResponse)
                {
                    sb.AppendLine($"    Response: {FormatHex(step.ResponseData!, 40)}");
                }
            }

            return sb.ToString();
        }

        private static string FormatHex(byte[] data, int maxLength)
        {
            string hex = BitConverter.ToString(data).Replace("-", " ");
            if (hex.Length > maxLength * 3)
                hex = hex.Substring(0, maxLength * 3) + "...";
            return hex;
        }

        #endregion

        #region 签名分析

        /// <summary>
        /// 分析 RSA 签名
        /// </summary>
        public static SignatureInfo AnalyzeSignature(byte[] data, int signatureOffset, int signatureLength = 256)
        {
            var info = new SignatureInfo
            {
                Offset = signatureOffset,
                Length = signatureLength
            };

            if (signatureOffset + signatureLength <= data.Length)
            {
                info.SignatureData = data.Skip(signatureOffset).Take(signatureLength).ToArray();
                
                // 检测签名类型
                // RSA-2048 签名通常是 256 字节
                // RSA-1024 签名通常是 128 字节
                info.Type = signatureLength switch
                {
                    256 => "RSA-2048",
                    128 => "RSA-1024",
                    _ => $"Unknown ({signatureLength} bytes)"
                };

                // 检测是否全零（未签名）
                info.IsEmpty = info.SignatureData.All(b => b == 0);
            }

            return info;
        }

        #endregion
    }

    #region 数据结构

    public class DaFileInfo
    {
        public int Size { get; set; }
        public uint Magic { get; set; }
        public bool HasMtkDaSignature { get; set; }
        public int SignatureOffset { get; set; }
        public List<string> VersionStrings { get; } = new();
        public List<DaRegion> Regions { get; } = new();
        public bool HasSignature { get; set; }
    }

    public class DaRegion
    {
        public int Index { get; set; }
        public uint Address { get; set; }
        public uint Length { get; set; }
        public uint SignatureLength { get; set; }
    }

    public class PreloaderInfo
    {
        public int Size { get; set; }
        public int HeaderOffset { get; set; }
        public uint Magic { get; set; }
        public ushort Version { get; set; }
        public bool HasEmiConfig { get; set; }
        public int EmiOffset { get; set; }
        public List<GfhHeader> GfhHeaders { get; } = new();
    }

    public class GfhHeader
    {
        public int Offset { get; set; }
        public uint Magic { get; set; }
        public ushort Size { get; set; }
        public ushort Type { get; set; }
    }

    public class ProtocolFlow
    {
        public List<ProtocolStep> Steps { get; } = new();
    }

    public class ProtocolStep
    {
        public int Index { get; set; }
        public byte CommandByte { get; set; }
        public string CommandName { get; set; } = "";
        public string Phase { get; set; } = "";
        public byte[] RequestData { get; set; } = Array.Empty<byte>();
        public byte[]? ResponseData { get; set; }
        public bool HasResponse { get; set; }
    }

    public class SignatureInfo
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Type { get; set; } = "";
        public byte[]? SignatureData { get; set; }
        public bool IsEmpty { get; set; }
    }

    #endregion
}
