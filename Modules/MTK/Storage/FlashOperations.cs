using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.MTK.Models;
using tools.Modules.MTK.Protocol;

namespace tools.Modules.MTK.Storage
{
    /// <summary>
    /// MTK 闪存操作
    /// </summary>
    public class FlashOperations
    {
        private readonly XFlashProtocol _protocol;
        private readonly MtkConfig _config;

        public event Action<string>? OnLog;
        public event Action<int, int>? OnProgress;

        public FlashOperations(XFlashProtocol protocol, MtkConfig config)
        {
            _protocol = protocol;
            _config = config;
        }

        #region 读取操作

        /// <summary>
        /// 读取分区
        /// </summary>
        public async Task<bool> ReadPartitionAsync(
            MtkPartition partition,
            string outputPath,
            CancellationToken ct = default)
        {
            return await ReadFlashAsync(
                partition.StartOffset,
                partition.Size,
                outputPath,
                ct);
        }

        /// <summary>
        /// 读取闪存区域
        /// </summary>
        public async Task<bool> ReadFlashAsync(
            ulong startOffset,
            ulong length,
            string outputPath,
            CancellationToken ct = default)
        {
            try
            {
                Log($"Reading flash: offset=0x{startOffset:X}, length=0x{length:X}");

                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                const int chunkSize = 0x100000; // 1MB
                ulong offset = 0;

                while (offset < length && !ct.IsCancellationRequested)
                {
                    int toRead = (int)Math.Min(chunkSize, length - offset);

                    var data = await _protocol.ReadFlashAsync(
                        startOffset + offset,
                        (uint)toRead,
                        ct);

                    if (data == null)
                    {
                        Log($"Read failed at offset 0x{startOffset + offset:X}");
                        return false;
                    }

                    await fs.WriteAsync(data, 0, data.Length, ct);
                    offset += (ulong)data.Length;

                    ReportProgress((int)(offset * 100 / length), 100);
                }

                Log($"Read completed: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Read error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 写入操作

        /// <summary>
        /// 写入分区
        /// </summary>
        public async Task<bool> WritePartitionAsync(
            MtkPartition partition,
            string inputPath,
            CancellationToken ct = default)
        {
            if (!File.Exists(inputPath))
            {
                Log($"File not found: {inputPath}");
                return false;
            }

            var fileInfo = new FileInfo(inputPath);

            // 检查稀疏镜像
            bool isSparse = await IsSparseImageAsync(inputPath);
            if (isSparse)
            {
                return await WriteSparseAsync(partition, inputPath, ct);
            }

            // 检查大小
            if ((ulong)fileInfo.Length > partition.Size)
            {
                Log($"Warning: File size ({fileInfo.Length}) exceeds partition size ({partition.Size})");
            }

            return await WriteFlashAsync(
                partition.StartOffset,
                inputPath,
                ct);
        }

        /// <summary>
        /// 写入闪存区域
        /// </summary>
        public async Task<bool> WriteFlashAsync(
            ulong startOffset,
            string inputPath,
            CancellationToken ct = default)
        {
            try
            {
                var fileInfo = new FileInfo(inputPath);
                Log($"Writing flash: offset=0x{startOffset:X}, file={inputPath}, size={fileInfo.Length}");

                using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);

                const int chunkSize = 0x100000; // 1MB
                byte[] buffer = new byte[chunkSize];
                long offset = 0;
                long total = fs.Length;

                while (offset < total && !ct.IsCancellationRequested)
                {
                    int read = await fs.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;

                    byte[] data = read == buffer.Length ? buffer : buffer[..read];

                    bool success = await _protocol.WriteFlashAsync(
                        startOffset + (ulong)offset,
                        data,
                        ct);

                    if (!success)
                    {
                        Log($"Write failed at offset 0x{startOffset + (ulong)offset:X}");
                        return false;
                    }

                    offset += read;
                    ReportProgress((int)(offset * 100 / total), 100);
                }

                Log("Write completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Write error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 写入稀疏镜像
        /// </summary>
        public async Task<bool> WriteSparseAsync(
            MtkPartition partition,
            string inputPath,
            CancellationToken ct = default)
        {
            try
            {
                Log($"Writing sparse image to {partition.Name}");

                using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fs);

                // 解析稀疏头
                var header = SparseHeader.Read(reader);
                if (header == null)
                {
                    Log("Invalid sparse image header");
                    return false;
                }

                Log($"Sparse image: {header.TotalBlocks} blocks, {header.TotalChunks} chunks");

                ulong currentOffset = partition.StartOffset;
                int processedChunks = 0;

                for (int i = 0; i < header.TotalChunks && !ct.IsCancellationRequested; i++)
                {
                    var chunk = SparseChunk.Read(reader);
                    if (chunk == null)
                    {
                        Log($"Failed to read chunk {i}");
                        return false;
                    }

                    switch (chunk.ChunkType)
                    {
                        case SparseChunkType.Raw:
                            // 直接写入数据
                            byte[] data = reader.ReadBytes((int)chunk.TotalSize - 12);
                            if (!await _protocol.WriteFlashAsync(currentOffset, data, ct))
                            {
                                Log($"Failed to write raw chunk at 0x{currentOffset:X}");
                                return false;
                            }
                            currentOffset += (ulong)data.Length;
                            break;

                        case SparseChunkType.Fill:
                            // 填充数据
                            uint fillValue = reader.ReadUInt32();
                            ulong fillSize = (ulong)chunk.ChunkBlocks * header.BlockSize;
                            // 生成填充数据
                            byte[] fillData = new byte[Math.Min(fillSize, 0x100000)];
                            for (int j = 0; j < fillData.Length; j += 4)
                            {
                                BitConverter.GetBytes(fillValue).CopyTo(fillData, j);
                            }
                            // 分块写入
                            for (ulong written = 0; written < fillSize; written += (ulong)fillData.Length)
                            {
                                int toWrite = (int)Math.Min((ulong)fillData.Length, fillSize - written);
                                if (!await _protocol.WriteFlashAsync(currentOffset + written, fillData[..toWrite], ct))
                                {
                                    return false;
                                }
                            }
                            currentOffset += fillSize;
                            break;

                        case SparseChunkType.DontCare:
                            // 跳过区域
                            currentOffset += (ulong)chunk.ChunkBlocks * header.BlockSize;
                            break;

                        case SparseChunkType.Crc32:
                            // CRC32 校验 (跳过)
                            reader.ReadUInt32();
                            break;
                    }

                    processedChunks++;
                    ReportProgress(processedChunks, (int)header.TotalChunks);
                }

                Log("Sparse write completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Sparse write error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 擦除操作

        /// <summary>
        /// 擦除分区
        /// </summary>
        public async Task<bool> ErasePartitionAsync(
            MtkPartition partition,
            CancellationToken ct = default)
        {
            return await EraseFlashAsync(
                partition.StartOffset,
                partition.Size,
                ct);
        }

        /// <summary>
        /// 擦除闪存区域
        /// </summary>
        public async Task<bool> EraseFlashAsync(
            ulong startOffset,
            ulong length,
            CancellationToken ct = default)
        {
            try
            {
                Log($"Erasing flash: offset=0x{startOffset:X}, length=0x{length:X}");

                bool success = await _protocol.EraseFlashAsync(startOffset, length, ct);

                if (success)
                {
                    Log("Erase completed");
                }
                else
                {
                    Log("Erase failed");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log($"Erase error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 格式化所有用户分区
        /// </summary>
        public async Task<bool> FormatAsync(CancellationToken ct = default)
        {
            try
            {
                Log("Formatting flash...");

                bool success = await _protocol.FormatAsync(ct);

                if (success)
                {
                    Log("Format completed");
                }
                else
                {
                    Log("Format failed");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log($"Format error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查是否为稀疏镜像
        /// </summary>
        public static async Task<bool> IsSparseImageAsync(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                byte[] magic = new byte[4];
                await fs.ReadAsync(magic, 0, 4);
                return BitConverter.ToUInt32(magic, 0) == SparseHeader.SPARSE_MAGIC;
            }
            catch
            {
                return false;
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[Flash] {message}");
        }

        private void ReportProgress(int current, int total)
        {
            OnProgress?.Invoke(current, total);
        }

        #endregion
    }

    #region Sparse Image 结构

    /// <summary>
    /// 稀疏镜像头
    /// </summary>
    public class SparseHeader
    {
        public const uint SPARSE_MAGIC = 0xED26FF3A;

        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }
        public ushort FileHeaderSize { get; set; }
        public ushort ChunkHeaderSize { get; set; }
        public uint BlockSize { get; set; }
        public uint TotalBlocks { get; set; }
        public uint TotalChunks { get; set; }
        public uint ImageChecksum { get; set; }

        public static SparseHeader? Read(BinaryReader reader)
        {
            uint magic = reader.ReadUInt32();
            if (magic != SPARSE_MAGIC)
                return null;

            return new SparseHeader
            {
                MajorVersion = reader.ReadUInt16(),
                MinorVersion = reader.ReadUInt16(),
                FileHeaderSize = reader.ReadUInt16(),
                ChunkHeaderSize = reader.ReadUInt16(),
                BlockSize = reader.ReadUInt32(),
                TotalBlocks = reader.ReadUInt32(),
                TotalChunks = reader.ReadUInt32(),
                ImageChecksum = reader.ReadUInt32()
            };
        }
    }

    /// <summary>
    /// 稀疏块类型
    /// </summary>
    public enum SparseChunkType : ushort
    {
        Raw = 0xCAC1,
        Fill = 0xCAC2,
        DontCare = 0xCAC3,
        Crc32 = 0xCAC4
    }

    /// <summary>
    /// 稀疏块头
    /// </summary>
    public class SparseChunk
    {
        public SparseChunkType ChunkType { get; set; }
        public ushort Reserved { get; set; }
        public uint ChunkBlocks { get; set; }
        public uint TotalSize { get; set; }

        public static SparseChunk? Read(BinaryReader reader)
        {
            try
            {
                return new SparseChunk
                {
                    ChunkType = (SparseChunkType)reader.ReadUInt16(),
                    Reserved = reader.ReadUInt16(),
                    ChunkBlocks = reader.ReadUInt32(),
                    TotalSize = reader.ReadUInt32()
                };
            }
            catch
            {
                return null;
            }
        }
    }

    #endregion
}
