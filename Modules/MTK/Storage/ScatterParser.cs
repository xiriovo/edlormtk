using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace tools.Modules.MTK.Storage
{
    /// <summary>
    /// MTK Scatter 分区信息
    /// </summary>
    public class ScatterPartition
    {
        public string Name { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public ulong StartAddress { get; set; }
        public ulong Size { get; set; }
        public string Type { get; set; } = "";
        public string Region { get; set; } = "EMMC_USER";
        public string StorageType { get; set; } = "HW_STORAGE_EMMC";
        public bool IsDownload { get; set; } = true;
        public bool IsUpgrade { get; set; } = true;
        public bool IsEmpty { get; set; }
        public bool IsBootable { get; set; }
        public int Index { get; set; }
        
        // V6 XML 格式特有字段
        public string OperationType { get; set; } = "";  // BINREGION, PROTECTED, NORMAL 等
        public bool IsProtected { get; set; }

        // UI 绑定属性
        public bool IsSelected { get; set; }
        public bool FileExists => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        public bool HasSourceFile => !string.IsNullOrEmpty(FileName);
        public bool HasCustomFile { get; set; }
        public string CustomFilePath { get; set; } = "";

        public string DisplayFilePath => HasCustomFile ? CustomFilePath : FilePath;
        public string DisplayFileName => HasCustomFile 
            ? Path.GetFileName(CustomFilePath) 
            : (FileExists ? Path.GetFileName(FilePath) : FileName);

        public string FormattedSize
        {
            get
            {
                if (Size >= 1024UL * 1024 * 1024)
                    return $"{Size / (1024.0 * 1024 * 1024):F2} GB";
                if (Size >= 1024 * 1024)
                    return $"{Size / (1024.0 * 1024):F2} MB";
                if (Size >= 1024)
                    return $"{Size / 1024.0:F2} KB";
                return $"{Size} B";
            }
        }

        public string StartAddressHex => $"0x{StartAddress:X}";

        public string FileStatusText
        {
            get
            {
                if (IsProtected) return "🔒 受保护";
                if (HasCustomFile) return "自定义";
                if (FileExists) return "✓ 就绪";
                if (HasSourceFile) return "⚠ 缺失";
                if (IsEmpty) return "空";
                return "---";
            }
        }

        public string StatusColor
        {
            get
            {
                if (IsProtected) return "#F59E0B";  // 橙色 - 受保护
                if (HasCustomFile) return "#00D4FF";
                if (FileExists) return "#10B981";
                if (HasSourceFile) return "#EF4444";
                return "#888888";
            }
        }
        
        // V6 operation type 描述
        public string OperationTypeDesc
        {
            get
            {
                return OperationType switch
                {
                    "BINREGION" => "二进制区域",
                    "PROTECTED" => "受保护",
                    "INVISIBLE" => "不可见",
                    "UPDATE" => "更新",
                    "NORMAL" => "普通",
                    _ => OperationType
                };
            }
        }
    }

    /// <summary>
    /// MTK Scatter 文件解析器
    /// 支持 TXT (传统格式) 和 XML (V6格式)
    /// </summary>
    public class ScatterParser
    {
        public string Version { get; private set; } = "";
        public string Platform { get; private set; } = "";
        public string Project { get; private set; } = "";
        public string StorageType { get; private set; } = "";
        public List<ScatterPartition> Partitions { get; } = new();
        public string BasePath { get; private set; } = "";
        
        // V6 格式特有属性
        public bool IsV6Format { get; private set; }
        public bool SkipPtOperation { get; private set; }
        public HashSet<string> ProtectedPartitions { get; } = new();

        /// <summary>
        /// 解析 Scatter 文件 (自动检测格式)
        /// </summary>
        public bool Parse(string scatterPath)
        {
            if (!File.Exists(scatterPath))
                return false;

            BasePath = Path.GetDirectoryName(scatterPath) ?? "";
            Partitions.Clear();

            try
            {
                string content = File.ReadAllText(scatterPath);

                // 检测文件格式
                if (content.TrimStart().StartsWith("<?xml") || content.TrimStart().StartsWith("<"))
                {
                    return ParseXml(scatterPath);
                }
                else
                {
                    return ParseTxt(scatterPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScatterParser error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析 TXT 格式 Scatter 文件
        /// 支持多种 MTK scatter 格式 (V1.x, V2.x)
        /// </summary>
        private bool ParseTxt(string scatterPath)
        {
            string[] lines = File.ReadAllLines(scatterPath);
            ScatterPartition? currentPartition = null;
            int index = 0;
            bool inGeneralSection = false;
            bool inPartitionSection = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string line = rawLine.Trim();

                // 跳过空行
                if (string.IsNullOrEmpty(line))
                    continue;

                // 跳过纯注释行 (以 # 开头且后面不是分隔符)
                if (line.StartsWith("#") && !line.StartsWith("############"))
                    continue;

                // 检测分区/节分隔符: ############...
                if (line.StartsWith("############"))
                {
                    // 保存上一个分区
                    if (currentPartition != null && !string.IsNullOrEmpty(currentPartition.Name))
                    {
                        Partitions.Add(currentPartition);
                    }
                    currentPartition = null;
                    inGeneralSection = false;
                    inPartitionSection = false;
                    continue;
                }

                // 检测新格式的节开始: - general: 或 - partition_index:
                if (line.StartsWith("-"))
                {
                    string sectionLine = line.TrimStart('-').Trim();
                    
                    if (sectionLine.StartsWith("general:"))
                    {
                        inGeneralSection = true;
                        inPartitionSection = false;
                        currentPartition = null;
                        continue;
                    }
                    else if (sectionLine.StartsWith("partition_index:"))
                    {
                        // 保存上一个分区
                        if (currentPartition != null && !string.IsNullOrEmpty(currentPartition.Name))
                        {
                            Partitions.Add(currentPartition);
                        }
                        inGeneralSection = false;
                        inPartitionSection = true;
                        currentPartition = new ScatterPartition { Index = index++ };
                        continue;
                    }
                }

                // 解析键值对
                var kvMatch = Regex.Match(line, @"^[-\s]*(\w+)\s*:\s*(.*)$");
                if (kvMatch.Success)
                {
                    string key = kvMatch.Groups[1].Value.ToLower().Trim();
                    string value = kvMatch.Groups[2].Value.Trim();

                    // General 节属性
                    if (inGeneralSection || currentPartition == null)
                    {
                        switch (key)
                        {
                            case "scatter_file_version":
                                Version = value;
                                break;
                            case "platform":
                                Platform = value;
                                break;
                            case "project":
                                Project = value;
                                break;
                            case "storage":
                                StorageType = value;
                                break;
                        }
                        
                        // 如果还没有分区但是遇到了partition_name，创建新分区
                        if (key == "partition_name" && currentPartition == null)
                        {
                            currentPartition = new ScatterPartition { Index = index++ };
                            inPartitionSection = true;
                            inGeneralSection = false;
                            currentPartition.Name = value;
                        }
                        continue;
                    }

                    // 分区属性
                    if (currentPartition != null)
                    {
                        switch (key)
                        {
                            case "partition_name":
                                currentPartition.Name = value;
                                break;
                            case "file_name":
                                currentPartition.FileName = value;
                                if (!string.IsNullOrEmpty(value) && 
                                    !value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                                {
                                    currentPartition.FilePath = Path.Combine(BasePath, value);
                                }
                                break;
                            case "physical_start_addr":
                            case "linear_start_addr":
                                currentPartition.StartAddress = ParseHexOrDecimal(value);
                                break;
                            case "partition_size":
                                currentPartition.Size = ParseHexOrDecimal(value);
                                break;
                            case "type":
                                currentPartition.Type = value;
                                break;
                            case "region":
                                currentPartition.Region = value;
                                break;
                            case "storage":
                                currentPartition.StorageType = value;
                                if (string.IsNullOrEmpty(StorageType))
                                    StorageType = value;
                                break;
                            case "is_download":
                                currentPartition.IsDownload = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "is_upgradable":
                                currentPartition.IsUpgrade = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "empty_boot_needed":
                                currentPartition.IsEmpty = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "is_reserved":
                                currentPartition.IsBootable = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "operation_type":
                                currentPartition.OperationType = value;
                                currentPartition.IsProtected = value == "BINREGION" || value == "PROTECTED";
                                break;
                        }
                    }
                }
                // 旧格式: partition_name: xxx (没有 - 前缀)
                else if (!inGeneralSection && currentPartition == null)
                {
                    // 检查是否是分区开始 (旧格式)
                    var oldMatch = Regex.Match(line, @"^partition_name\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (oldMatch.Success)
                    {
                        currentPartition = new ScatterPartition { Index = index++ };
                        currentPartition.Name = oldMatch.Groups[1].Value.Trim();
                        inPartitionSection = true;
                    }
                }
            }

            // 添加最后一个分区
            if (currentPartition != null && !string.IsNullOrEmpty(currentPartition.Name))
            {
                Partitions.Add(currentPartition);
            }

            // 设置默认选中状态
            foreach (var p in Partitions)
            {
                p.IsSelected = p.IsDownload;
            }

            return Partitions.Count > 0;
        }

        /// <summary>
        /// 解析 XML 格式 Scatter 文件 (V6 格式)
        /// 参考 SP Flash Tool V6 ScatterXMLParser 实现
        /// </summary>
        private bool ParseXml(string scatterPath)
        {
            try
            {
                var doc = XDocument.Load(scatterPath);
                var root = doc.Root;

                if (root == null) return false;

                IsV6Format = true;
                
                // 解析 general 节点
                var generalNode = root.Element("general");
                if (generalNode != null)
                {
                    Platform = generalNode.Element("platform")?.Value?.Trim() ?? "";
                    Project = generalNode.Element("project")?.Value?.Trim() ?? "";
                    SkipPtOperation = ParseBool(generalNode.Element("skip_pt_operate")?.Value);
                }

                // V6 格式: 解析 storage_type 节点
                var storageTypeNode = root.Element("storage_type");
                if (storageTypeNode != null)
                {
                    StorageType = storageTypeNode.Attribute("name")?.Value ?? "";
                    ParseV6StorageTypeNode(storageTypeNode);
                }
                else
                {
                    // 兼容旧格式
                    StorageType = root.Element("general")?.Element("storage")?.Value ?? "";
                    ParseLegacyXmlPartitions(root);
                }

                return Partitions.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XML parse error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析 V6 storage_type 节点下的分区
        /// </summary>
        private void ParseV6StorageTypeNode(XElement storageTypeNode)
        {
            int index = 0;
            var partNames = new HashSet<string>();

            foreach (var partIndexNode in storageTypeNode.Elements("partition_index"))
            {
                // 必需字段
                var partName = partIndexNode.Element("partition_name")?.Value?.Trim();
                var isDownloadStr = partIndexNode.Element("is_download")?.Value?.Trim();
                var startAddrStr = partIndexNode.Element("physical_start_addr")?.Value?.Trim();
                var fileNameStr = partIndexNode.Element("file_name")?.Value?.Trim();
                var opType = partIndexNode.Element("operation_type")?.Value?.Trim() ?? "";

                if (string.IsNullOrEmpty(partName)) continue;
                
                // 检查重复分区名
                if (partNames.Contains(partName)) continue;
                partNames.Add(partName);

                bool isDownload = ParseBool(isDownloadStr);

                var partition = new ScatterPartition
                {
                    Index = index++,
                    Name = partName,
                    FileName = fileNameStr ?? "",
                    StartAddress = ParseHexOrDecimal(startAddrStr ?? "0"),
                    OperationType = opType,
                    IsDownload = isDownload,
                    IsProtected = opType == "BINREGION" || opType == "PROTECTED",
                    StorageType = StorageType
                };

                // 设置文件路径
                if (!string.IsNullOrEmpty(partition.FileName) && partition.FileName.ToUpper() != "NONE")
                {
                    partition.FilePath = Path.Combine(BasePath, partition.FileName);
                }

                // 根据 is_download 决定是否默认选中
                partition.IsSelected = isDownload;

                // 记录受保护分区
                if (partition.IsProtected)
                {
                    ProtectedPartitions.Add(partName);
                }

                Partitions.Add(partition);
            }
        }

        /// <summary>
        /// 解析旧版 XML 格式分区 (兼容)
        /// </summary>
        private void ParseLegacyXmlPartitions(XElement root)
        {
            int index = 0;
            var partitionElements = root.Elements("partition_index")
                .Concat(root.Descendants("pt"));

            foreach (var pt in partitionElements)
            {
                var partition = new ScatterPartition
                {
                    Index = index++,
                    Name = pt.Element("partition_name")?.Value ?? pt.Element("name")?.Value ?? "",
                    FileName = pt.Element("file_name")?.Value ?? pt.Element("filename")?.Value ?? "",
                    Type = pt.Element("type")?.Value ?? "",
                    Region = pt.Element("region")?.Value ?? "EMMC_USER",
                    StorageType = pt.Element("storage")?.Value ?? StorageType,
                    IsDownload = ParseBool(pt.Element("is_download")?.Value),
                    IsUpgrade = ParseBool(pt.Element("is_upgradable")?.Value),
                    IsEmpty = ParseBool(pt.Element("empty_boot_needed")?.Value),
                    IsBootable = ParseBool(pt.Element("is_reserved")?.Value),
                };

                string? startAddr = pt.Element("physical_start_addr")?.Value 
                    ?? pt.Element("linear_start_addr")?.Value
                    ?? pt.Element("start")?.Value;
                partition.StartAddress = ParseHexOrDecimal(startAddr ?? "0");

                string? size = pt.Element("partition_size")?.Value 
                    ?? pt.Element("size")?.Value;
                partition.Size = ParseHexOrDecimal(size ?? "0");

                if (!string.IsNullOrEmpty(partition.FileName) && partition.FileName != "NONE")
                {
                    partition.FilePath = Path.Combine(BasePath, partition.FileName);
                }

                if (!string.IsNullOrEmpty(partition.Name))
                {
                    Partitions.Add(partition);
                }
            }
        }

        /// <summary>
        /// 解析十六进制或十进制数字
        /// </summary>
        private static ulong ParseHexOrDecimal(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            value = value.Trim();

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToUInt64(value.Substring(2), 16);
            }

            if (ulong.TryParse(value, out ulong result))
            {
                return result;
            }

            return 0;
        }

        /// <summary>
        /// 解析布尔值
        /// </summary>
        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return value.ToLower() == "true" || value == "1";
        }

        /// <summary>
        /// 获取可下载分区
        /// </summary>
        public List<ScatterPartition> GetDownloadablePartitions()
        {
            return Partitions.Where(p => p.IsDownload && !p.IsEmpty).ToList();
        }

        /// <summary>
        /// 获取有文件的分区
        /// </summary>
        public List<ScatterPartition> GetPartitionsWithFiles()
        {
            return Partitions.Where(p => p.FileExists || p.HasCustomFile).ToList();
        }

        /// <summary>
        /// 查找 scatter 文件
        /// </summary>
        public static string? FindScatterFile(string directory)
        {
            if (!Directory.Exists(directory))
                return null;

            // 查找顺序: *scatter*.txt, *.xml
            var patterns = new[] { "*scatter*.txt", "*scatter*.xml", "*.xml" };

            foreach (var pattern in patterns)
            {
                var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    // 优先选择文件名包含 scatter 的
                    var scatterFile = files.FirstOrDefault(f => 
                        Path.GetFileName(f).ToLower().Contains("scatter"));
                    return scatterFile ?? files[0];
                }
            }

            return null;
        }

        /// <summary>
        /// 验证所有分区文件
        /// </summary>
        public (int total, int exists, int missing) ValidateFiles()
        {
            int total = Partitions.Count(p => p.HasSourceFile);
            int exists = Partitions.Count(p => p.FileExists || p.HasCustomFile);
            int missing = total - exists;
            return (total, exists, missing);
        }
    }
}
