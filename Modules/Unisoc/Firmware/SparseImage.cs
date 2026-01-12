// ============================================================================
// MultiFlash TOOL - Android Sparse Image Handler
// Android Sparse 镜像处理器
// Android Sparseイメージハンドラー
// 안드로이드 스파스 이미지 핸들러
// ============================================================================
// GitHub: https://github.com/xiriovo/edlormtk
// Contact: QQ 1708298587 | Email: 1708298587@qq.com
// License: MIT
// ============================================================================

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Unisoc.Firmware
{
    /// <summary>
    /// Android Sparse Image Processor / Sparse 镜像处理器 / Sparseイメージプロセッサ
    /// 
    /// [EN] Detect, decompress and create Android sparse format images
    ///      Magic: 0xED26FF3A, Block size: typically 4096
    /// 
    /// [中文] 检测、解压和创建 Android Sparse 格式镜像
    ///       魔数: 0xED26FF3A, 块大小: 通常 4096
    /// 
    /// [日本語] Android Sparse形式イメージの検出、解凍、作成
    ///         マジック: 0xED26FF3A, ブロックサイズ: 通常4096
    /// </summary>
    public class SparseImage : IDisposable
    {
        #region Sparse 格式定义

        // Sparse 文件魔数
        private const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;

        // Chunk 类型
        private const ushort CHUNK_TYPE_RAW = 0xCAC1;
        private const ushort CHUNK_TYPE_FILL = 0xCAC2;
        private const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        private const ushort CHUNK_TYPE_CRC32 = 0xCAC4;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SparseHeader
        {
            public uint magic;           // 0xED26FF3A
            public ushort major_version; // 1
            public ushort minor_version; // 0
            public ushort file_hdr_sz;   // 28 bytes
            public ushort chunk_hdr_sz;  // 12 bytes
            public uint blk_sz;          // 通常 4096
            public uint total_blks;      // 总块数
            public uint total_chunks;    // chunk 数量
            public uint image_checksum;  // CRC32
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ChunkHeader
        {
            public ushort chunk_type;    // Raw/Fill/Don't care/CRC32
            public ushort reserved1;
            public uint chunk_sz;        // 块数
            public uint total_sz;        // 包含头的总大小
        }

        #endregion

        private bool _disposed;

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// 进度事件
        /// </summary>
        public event Action<long, long>? OnProgress;

        /// <summary>
        /// 检查文件是否是 Sparse 格式
        /// </summary>
        public static bool IsSparseImage(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(filePath));
                if (reader.BaseStream.Length < 28) return false;
                
                uint magic = reader.ReadUInt32();
                return magic == SPARSE_HEADER_MAGIC;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查字节数组是否是 Sparse 格式
        /// </summary>
        public static bool IsSparseImage(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            uint magic = BitConverter.ToUInt32(data, 0);
            return magic == SPARSE_HEADER_MAGIC;
        }

        /// <summary>
        /// 获取 Sparse 镜像信息
        /// </summary>
        public static SparseInfo? GetSparseInfo(string filePath)
        {
            if (!IsSparseImage(filePath)) return null;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(filePath));
                var headerBytes = reader.ReadBytes(Marshal.SizeOf<SparseHeader>());
                
                GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
                try
                {
                    var header = Marshal.PtrToStructure<SparseHeader>(handle.AddrOfPinnedObject());
                    
                    return new SparseInfo
                    {
                        MajorVersion = header.major_version,
                        MinorVersion = header.minor_version,
                        BlockSize = header.blk_sz,
                        TotalBlocks = header.total_blks,
                        TotalChunks = header.total_chunks,
                        OutputSize = (long)header.blk_sz * header.total_blks,
                        InputSize = reader.BaseStream.Length
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

        /// <summary>
        /// 解压 Sparse 镜像
        /// </summary>
        public async Task<bool> DecompressAsync(string inputPath, string outputPath, CancellationToken ct = default)
        {
            if (!IsSparseImage(inputPath))
            {
                OnLog?.Invoke("不是 Sparse 格式文件");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new BinaryReader(File.OpenRead(inputPath));
                    using var writer = new BinaryWriter(File.Create(outputPath));

                    // 读取头
                    var headerBytes = reader.ReadBytes(Marshal.SizeOf<SparseHeader>());
                    GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
                    SparseHeader header;
                    try
                    {
                        header = Marshal.PtrToStructure<SparseHeader>(handle.AddrOfPinnedObject());
                    }
                    finally
                    {
                        handle.Free();
                    }

                    if (header.magic != SPARSE_HEADER_MAGIC)
                    {
                        OnLog?.Invoke("无效的 Sparse 魔数");
                        return false;
                    }

                    OnLog?.Invoke($"Sparse 版本: {header.major_version}.{header.minor_version}");
                    OnLog?.Invoke($"块大小: {header.blk_sz}, 总块数: {header.total_blks}");
                    OnLog?.Invoke($"Chunk 数: {header.total_chunks}");

                    long outputSize = (long)header.blk_sz * header.total_blks;
                    long writtenBytes = 0;

                    // 跳过额外的头字节
                    if (header.file_hdr_sz > 28)
                    {
                        reader.ReadBytes(header.file_hdr_sz - 28);
                    }

                    // 处理每个 chunk
                    for (int i = 0; i < header.total_chunks; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var chunkBytes = reader.ReadBytes(Marshal.SizeOf<ChunkHeader>());
                        handle = GCHandle.Alloc(chunkBytes, GCHandleType.Pinned);
                        ChunkHeader chunk;
                        try
                        {
                            chunk = Marshal.PtrToStructure<ChunkHeader>(handle.AddrOfPinnedObject());
                        }
                        finally
                        {
                            handle.Free();
                        }

                        // 跳过额外的 chunk 头字节
                        if (header.chunk_hdr_sz > 12)
                        {
                            reader.ReadBytes(header.chunk_hdr_sz - 12);
                        }

                        long chunkDataSize = chunk.total_sz - header.chunk_hdr_sz;
                        long outputBytes = (long)chunk.chunk_sz * header.blk_sz;

                        switch (chunk.chunk_type)
                        {
                            case CHUNK_TYPE_RAW:
                                // 原始数据，直接复制
                                var rawData = reader.ReadBytes((int)chunkDataSize);
                                writer.Write(rawData);
                                writtenBytes += rawData.Length;
                                break;

                            case CHUNK_TYPE_FILL:
                                // 填充数据，重复写入
                                var fillValue = reader.ReadUInt32();
                                var fillBytes = BitConverter.GetBytes(fillValue);
                                for (long j = 0; j < outputBytes; j += 4)
                                {
                                    writer.Write(fillBytes);
                                }
                                writtenBytes += outputBytes;
                                break;

                            case CHUNK_TYPE_DONT_CARE:
                                // 不关心区域，写入零
                                var zeroBytes = new byte[Math.Min(outputBytes, 65536)];
                                long remaining = outputBytes;
                                while (remaining > 0)
                                {
                                    int writeLen = (int)Math.Min(remaining, zeroBytes.Length);
                                    writer.Write(zeroBytes, 0, writeLen);
                                    remaining -= writeLen;
                                }
                                writtenBytes += outputBytes;
                                break;

                            case CHUNK_TYPE_CRC32:
                                // CRC32 校验
                                reader.ReadUInt32();
                                break;

                            default:
                                OnLog?.Invoke($"未知 chunk 类型: 0x{chunk.chunk_type:X4}");
                                break;
                        }

                        OnProgress?.Invoke(writtenBytes, outputSize);
                    }

                    OnLog?.Invoke($"解压完成: {FormatSize(writtenBytes)}");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    OnLog?.Invoke("解压被取消");
                    return false;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"解压失败: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        /// <summary>
        /// 解压 Sparse 镜像到内存
        /// </summary>
        public async Task<byte[]?> DecompressToMemoryAsync(string inputPath, CancellationToken ct = default)
        {
            var info = GetSparseInfo(inputPath);
            if (info == null) return null;

            // 检查输出大小是否合理 (限制 2GB)
            if (info.OutputSize > 2L * 1024 * 1024 * 1024)
            {
                OnLog?.Invoke($"输出大小过大: {FormatSize(info.OutputSize)}, 请使用文件输出");
                return null;
            }

            using var outputStream = new MemoryStream();
            var tempPath = Path.GetTempFileName();
            
            try
            {
                if (await DecompressAsync(inputPath, tempPath, ct))
                {
                    return await File.ReadAllBytesAsync(tempPath, ct);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            return null;
        }

        /// <summary>
        /// 创建 Sparse 镜像
        /// </summary>
        public async Task<bool> CreateSparseImageAsync(string inputPath, string outputPath, 
            uint blockSize = 4096, CancellationToken ct = default)
        {
            if (!File.Exists(inputPath))
            {
                OnLog?.Invoke("输入文件不存在");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var reader = new BinaryReader(File.OpenRead(inputPath));
                    using var writer = new BinaryWriter(File.Create(outputPath));

                    long inputSize = reader.BaseStream.Length;
                    uint totalBlocks = (uint)((inputSize + blockSize - 1) / blockSize);

                    // 写入头部 (占位，稍后更新)
                    var header = new SparseHeader
                    {
                        magic = SPARSE_HEADER_MAGIC,
                        major_version = 1,
                        minor_version = 0,
                        file_hdr_sz = 28,
                        chunk_hdr_sz = 12,
                        blk_sz = blockSize,
                        total_blks = totalBlocks,
                        total_chunks = 0,
                        image_checksum = 0
                    };

                    long headerPos = writer.BaseStream.Position;
                    writer.Write(StructToBytes(header));

                    uint chunkCount = 0;
                    byte[] blockBuffer = new byte[blockSize];
                    byte[] zeroBlock = new byte[blockSize];

                    while (reader.BaseStream.Position < inputSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 读取一个块
                        int bytesRead = reader.Read(blockBuffer, 0, (int)blockSize);
                        if (bytesRead < blockSize)
                        {
                            Array.Clear(blockBuffer, bytesRead, (int)blockSize - bytesRead);
                        }

                        // 检查是否是零块
                        bool isZeroBlock = blockBuffer.AsSpan().SequenceEqual(zeroBlock);

                        if (isZeroBlock)
                        {
                            // Don't Care chunk
                            var chunk = new ChunkHeader
                            {
                                chunk_type = CHUNK_TYPE_DONT_CARE,
                                reserved1 = 0,
                                chunk_sz = 1,
                                total_sz = 12
                            };
                            writer.Write(StructToBytes(chunk));
                        }
                        else
                        {
                            // Raw chunk
                            var chunk = new ChunkHeader
                            {
                                chunk_type = CHUNK_TYPE_RAW,
                                reserved1 = 0,
                                chunk_sz = 1,
                                total_sz = 12 + blockSize
                            };
                            writer.Write(StructToBytes(chunk));
                            writer.Write(blockBuffer);
                        }

                        chunkCount++;
                        OnProgress?.Invoke(reader.BaseStream.Position, inputSize);
                    }

                    // 更新头部
                    header.total_chunks = chunkCount;
                    writer.BaseStream.Seek(headerPos, SeekOrigin.Begin);
                    writer.Write(StructToBytes(header));

                    OnLog?.Invoke($"Sparse 创建完成: {chunkCount} chunks");
                    return true;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"创建 Sparse 失败: {ex.Message}");
                    return false;
                }
            }, ct);
        }

        private static byte[] StructToBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return bytes;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    /// <summary>
    /// Sparse 镜像信息
    /// </summary>
    public class SparseInfo
    {
        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }
        public uint BlockSize { get; set; }
        public uint TotalBlocks { get; set; }
        public uint TotalChunks { get; set; }
        public long OutputSize { get; set; }
        public long InputSize { get; set; }
        
        public string OutputSizeStr => FormatSize(OutputSize);
        public string InputSizeStr => FormatSize(InputSize);
        public double CompressionRatio => InputSize > 0 ? (double)OutputSize / InputSize : 0;

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
