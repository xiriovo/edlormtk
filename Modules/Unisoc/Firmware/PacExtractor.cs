// ============================================================================
// MultiFlash TOOL - Unisoc PAC Firmware Extractor
// 展讯 PAC 固件提取器 | Unisoc PACファームウェア抽出 | Unisoc PAC 펌웨어 추출기
// ============================================================================
// [EN] Parse and extract Unisoc/Spreadtrum PAC firmware packages
//      Supports BP_R1.0.0 and BP_R2.0.1 versions
// [中文] 解析和提取展讯 PAC 固件包
//       支持 BP_R1.0.0 和 BP_R2.0.1 版本
// [日本語] Unisoc/Spreadtrum PACファームウェアパッケージの解析と抽出
//         BP_R1.0.0 と BP_R2.0.1 バージョンをサポート
// [한국어] Unisoc/Spreadtrum PAC 펌웨어 패키지 파싱 및 추출
//         BP_R1.0.0 및 BP_R2.0.1 버전 지원
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Unisoc.Models;

namespace tools.Modules.Unisoc.Firmware
{
    /// <summary>
    /// PAC Firmware Extractor / PAC 固件提取器 / PACファームウェア抽出 / PAC 펌웨어 추출기
    /// [EN] Supports BP_R1.0.0 and BP_R2.0.1 versions
    /// [中文] 支持 BP_R1.0.0 和 BP_R2.0.1 版本
    /// </summary>
    public class PacExtractor : IDisposable
    {
        #region PAC 结构定义

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PacHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 44)]
            public byte[] szVersion;
            public uint dwHiSize;
            public uint dwLoSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] productName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] firmwareName;
            public uint partitionCount;
            public uint partitionsListStart;
            public uint dwMode;
            public uint dwFlashType;
            public uint dwNandStrategy;
            public uint dwIsNvBackup;
            public uint dwNandPageType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 996)]
            public byte[] szPrdAlias;
            public uint dwOmaDmProductFlag;
            public uint dwIsOmaDM;
            public uint dwIsPreload;
            public uint dwReserved;
            public uint dwMagic;
            public uint wCRC1;
            public uint wCRC2;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 60)]
            public string reservedData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FileHeaderV1
        {
            public uint length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] partitionName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] fileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 508)]
            public byte[] szFileName;
            public uint hiDataOffset;
            public uint hiPartitionSize;
            public uint dwReserved1;
            public uint dwReserved2;
            public uint loDataOffset;
            public uint loPartitionSize;
            public ushort nFileFlag;
            public ushort nCheckFlag;
            public uint dwReserved3;
            public uint dwCanOmitFlag;
            public uint dwAddrNum;
            public uint dwAddr;
            public uint dwReserved4;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 996)]
            public string reservedData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FileHeaderV2
        {
            public uint length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] partitionName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] fileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 508)]
            public byte[] szFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] szPartitionInfo;
            public uint dwReserved2;
            public ushort nFileFlag;
            public ushort nCheckFlag;
            public uint dwReserved3;
            public uint dwCanOmitFlag;
            public uint dwAddrNum;
            public uint dwAddr;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 996)]
            public string reservedData;
        }

        #endregion

        private string? _pacFilePath;
        private string? _version;
        private bool _disposed;

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// 进度事件
        /// </summary>
        public event Action<int, int>? OnProgress;

        /// <summary>
        /// 固件信息
        /// </summary>
        public PacFirmwareInfo? FirmwareInfo { get; private set; }

        /// <summary>
        /// 分区列表
        /// </summary>
        public List<PacPartitionEntry> Partitions { get; } = new();

        /// <summary>
        /// 解析 PAC 文件
        /// </summary>
        public async Task<bool> ParseAsync(string pacFilePath, CancellationToken ct = default)
        {
            _pacFilePath = pacFilePath;
            Partitions.Clear();

            if (!File.Exists(pacFilePath))
            {
                OnLog?.Invoke($"PAC 文件不存在: {pacFilePath}");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new BinaryReader(File.Open(pacFilePath, FileMode.Open, FileAccess.Read, FileShare.Read));

                    // 解析 PAC 头
                    var header = ParseHeader(reader);
                    if (header == null)
                    {
                        OnLog?.Invoke("PAC 头解析失败");
                        return false;
                    }

                    FirmwareInfo = header;
                    _version = header.Version;

                    OnLog?.Invoke($"固件名称: {header.FirmwareName}");
                    OnLog?.Invoke($"产品名称: {header.ProductName}");
                    OnLog?.Invoke($"PAC 版本: {header.Version}");
                    OnLog?.Invoke($"固件大小: {FormatSize(header.Size)}");
                    OnLog?.Invoke($"分区数量: {header.PartitionCount}");

                    // 验证文件大小
                    var fileInfo = new FileInfo(pacFilePath);
                    if ((ulong)fileInfo.Length != (ulong)header.Size)
                    {
                        OnLog?.Invoke($"警告: 文件大小不匹配 (期望: {header.Size}, 实际: {fileInfo.Length})");
                    }

                    // 跳转到分区列表
                    reader.BaseStream.Seek(header.PartitionsListStart, SeekOrigin.Begin);

                    // 解析分区列表
                    for (int i = 0; i < header.PartitionCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var partition = ParseFileHeader(reader);
                        if (partition != null)
                        {
                            Partitions.Add(partition);
                        }

                        OnProgress?.Invoke(i + 1, header.PartitionCount);
                    }

                    OnLog?.Invoke($"解析完成: 找到 {Partitions.Count} 个分区");
                    return true;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"PAC 解析异常: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        /// <summary>
        /// 提取分区到目录
        /// </summary>
        public async Task<bool> ExtractAsync(string outputDir, IEnumerable<string>? partitionNames = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_pacFilePath) || !File.Exists(_pacFilePath))
            {
                OnLog?.Invoke("请先解析 PAC 文件");
                return false;
            }

            Directory.CreateDirectory(outputDir);

            var toExtract = partitionNames != null
                ? Partitions.Where(p => partitionNames.Contains(p.PartitionName, StringComparer.OrdinalIgnoreCase)).ToList()
                : Partitions.Where(p => p.DataSize > 0).ToList();

            if (toExtract.Count == 0)
            {
                OnLog?.Invoke("没有可提取的分区");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new BinaryReader(File.Open(_pacFilePath, FileMode.Open, FileAccess.Read, FileShare.Read));

                    int current = 0;
                    foreach (var partition in toExtract)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (string.IsNullOrEmpty(partition.FileName) || partition.DataSize == 0)
                        {
                            current++;
                            continue;
                        }

                        OnLog?.Invoke($"提取: {partition.FileName}");

                        var outputPath = Path.Combine(outputDir, partition.FileName);
                        ExtractFile(reader, partition, outputPath);

                        current++;
                        OnProgress?.Invoke(current, toExtract.Count);
                    }

                    OnLog?.Invoke($"提取完成: {current} 个文件");
                    return true;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"提取异常: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        /// <summary>
        /// 获取 FDL 文件信息
        /// </summary>
        public (PacPartitionEntry? Fdl1, PacPartitionEntry? Fdl2) GetFdlInfo()
        {
            var fdl1 = Partitions.FirstOrDefault(p =>
                p.PartitionName.Equals("FDL", StringComparison.OrdinalIgnoreCase) ||
                p.PartitionName.Equals("FDL1", StringComparison.OrdinalIgnoreCase));

            var fdl2 = Partitions.FirstOrDefault(p =>
                p.PartitionName.Equals("FDL2", StringComparison.OrdinalIgnoreCase));

            return (fdl1, fdl2);
        }

        #region 私有方法

        private PacFirmwareInfo? ParseHeader(BinaryReader reader)
        {
            try
            {
                int headerSize = Marshal.SizeOf<PacHeader>();
                byte[] headerBytes = reader.ReadBytes(headerSize);

                GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
                try
                {
                    var header = Marshal.PtrToStructure<PacHeader>(handle.AddrOfPinnedObject());

                    string version = GetString(header.szVersion);
                    if (version != "BP_R1.0.0" && version != "BP_R2.0.1")
                    {
                        OnLog?.Invoke($"不支持的 PAC 版本: {version}");
                        return null;
                    }

                    ulong size = ((ulong)header.dwHiSize << 32) | header.dwLoSize;

                    return new PacFirmwareInfo
                    {
                        Version = version,
                        ProductName = GetString(header.productName),
                        FirmwareName = GetString(header.firmwareName),
                        Size = (long)size,
                        PartitionCount = (int)header.partitionCount,
                        PartitionsListStart = (int)header.partitionsListStart,
                        FlashType = (int)header.dwFlashType
                    };
                }
                finally
                {
                    handle.Free();
                }
            }
            catch
            {
                return null;
            }
        }

        private PacPartitionEntry? ParseFileHeader(BinaryReader reader)
        {
            try
            {
                if (_version == "BP_R1.0.0")
                {
                    return ParseFileHeaderV1(reader);
                }
                else if (_version == "BP_R2.0.1")
                {
                    return ParseFileHeaderV2(reader);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private PacPartitionEntry? ParseFileHeaderV1(BinaryReader reader)
        {
            int headerSize = Marshal.SizeOf<FileHeaderV1>();
            byte[] headerBytes = reader.ReadBytes(headerSize);

            GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
            try
            {
                var header = Marshal.PtrToStructure<FileHeaderV1>(handle.AddrOfPinnedObject());

                ulong dataOffset = ((ulong)header.hiDataOffset << 32) | header.loDataOffset;
                ulong dataSize = ((ulong)header.hiPartitionSize << 32) | header.loPartitionSize;

                return new PacPartitionEntry
                {
                    PartitionName = GetString(header.partitionName),
                    FileName = GetString(header.fileName),
                    DataOffset = (long)dataOffset,
                    DataSize = (long)dataSize,
                    CanOmit = header.dwCanOmitFlag != 0,
                    Address = header.dwAddr
                };
            }
            finally
            {
                handle.Free();
            }
        }

        private PacPartitionEntry? ParseFileHeaderV2(BinaryReader reader)
        {
            int headerSize = Marshal.SizeOf<FileHeaderV2>();
            byte[] headerBytes = reader.ReadBytes(headerSize);

            GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
            try
            {
                var header = Marshal.PtrToStructure<FileHeaderV2>(handle.AddrOfPinnedObject());

                // 解析 szPartitionInfo 获取真实的偏移和大小
                // szPartitionInfo 结构: [4字节hiSize][4字节loSize][4字节?][4字节?][4字节hiOffset][4字节loOffset]
                byte[] info = header.szPartitionInfo;
                
                uint hiSize = BitConverter.ToUInt32(ReverseBytes(info.Take(4).ToArray()), 0);
                uint loSize = BitConverter.ToUInt32(ReverseBytes(info.Skip(4).Take(4).ToArray()), 0);
                uint hiOffset = BitConverter.ToUInt32(ReverseBytes(info.Skip(16).Take(4).ToArray()), 0);
                uint loOffset = BitConverter.ToUInt32(ReverseBytes(info.Skip(20).Take(4).ToArray()), 0);

                ulong dataOffset = hiOffset > 2 ? hiOffset : loOffset;
                ulong dataSize = hiSize > 2 ? hiSize : loSize;

                return new PacPartitionEntry
                {
                    PartitionName = GetString(header.partitionName),
                    FileName = GetString(header.fileName),
                    DataOffset = (long)dataOffset,
                    DataSize = (long)dataSize,
                    CanOmit = header.dwCanOmitFlag != 0,
                    Address = header.dwAddr
                };
            }
            finally
            {
                handle.Free();
            }
        }

        private void ExtractFile(BinaryReader reader, PacPartitionEntry partition, string outputPath)
        {
            reader.BaseStream.Seek(partition.DataOffset, SeekOrigin.Begin);

            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

            const int bufferSize = 1024 * 1024; // 1MB
            byte[] buffer = new byte[bufferSize];
            long remaining = partition.DataSize;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(bufferSize, remaining);
                int read = reader.Read(buffer, 0, toRead);
                if (read == 0) break;

                outputStream.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static string GetString(byte[] bytes)
        {
            if (bytes == null) return "";
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        private static byte[] ReverseBytes(byte[] bytes)
        {
            byte[] result = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                result[i] = bytes[bytes.Length - 1 - i];
            }
            return result;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Partitions.Clear();
        }
    }

    /// <summary>
    /// PAC 分区条目
    /// </summary>
    public class PacPartitionEntry
    {
        public string PartitionName { get; set; } = "";
        public string FileName { get; set; } = "";
        public long DataOffset { get; set; }
        public long DataSize { get; set; }
        public bool CanOmit { get; set; }
        public uint Address { get; set; }

        public string SizeStr => FormatSize(DataSize);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
