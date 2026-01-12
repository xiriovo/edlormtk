using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace tools.Modules.Unisoc.Firmware
{
    /// <summary>
    /// BMAConfig XML 解析器
    /// 用于从 Unisoc PAC 固件中提取 FDL 地址和分区配置
    /// </summary>
    public class BmaConfigParser
    {
        /// <summary>
        /// FDL 配置信息
        /// </summary>
        public class FdlConfig
        {
            public string Fdl1Name { get; set; } = "";
            public string Fdl1Address { get; set; } = "";
            public string Fdl2Name { get; set; } = "";
            public string Fdl2Address { get; set; } = "";
            public bool IsNand { get; set; }
            public string FlashType { get; set; } = "";
        }

        /// <summary>
        /// 分区配置信息
        /// </summary>
        public class PartitionConfig
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string FileName { get; set; } = "";
            public string Type { get; set; } = "";
            public long Size { get; set; }
            public long Offset { get; set; }
            public int BlockSize { get; set; }
            public bool IsOmittable { get; set; }
            public bool UseBackup { get; set; }
        }

        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// 解析结果
        /// </summary>
        public FdlConfig? Fdl { get; private set; }
        public List<PartitionConfig> Partitions { get; } = new();

        /// <summary>
        /// 从 XML 文件解析
        /// </summary>
        public bool ParseFromFile(string xmlPath)
        {
            if (!File.Exists(xmlPath))
            {
                OnLog?.Invoke($"XML 文件不存在: {xmlPath}");
                return false;
            }

            try
            {
                var content = File.ReadAllText(xmlPath, Encoding.UTF8);
                return ParseFromXml(content);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"读取 XML 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 XML 内容解析
        /// </summary>
        public bool ParseFromXml(string xmlContent)
        {
            try
            {
                Partitions.Clear();
                
                var doc = XDocument.Parse(xmlContent);
                var root = doc.Root;
                
                if (root == null)
                {
                    OnLog?.Invoke("无效的 XML 格式");
                    return false;
                }

                // 尝试解析 BMAConfig 格式
                if (root.Name.LocalName == "BMAConfig" || root.Name.LocalName == "Bin")
                {
                    return ParseBmaConfig(root);
                }
                
                // 尝试解析 scatter 格式
                if (root.Name.LocalName == "scatter" || root.Name.LocalName == "partitions")
                {
                    return ParseScatterFormat(root);
                }

                // 尝试解析通用 XML 格式
                return ParseGenericFormat(root);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"解析 XML 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析 BMAConfig 格式
        /// </summary>
        private bool ParseBmaConfig(XElement root)
        {
            OnLog?.Invoke("检测到 BMAConfig 格式");

            // 解析 FDL 配置
            var fdlNode = root.Element("FDL") ?? root.Element("fdl");
            if (fdlNode != null)
            {
                Fdl = new FdlConfig
                {
                    Fdl1Name = GetElementValue(fdlNode, "FDL1") ?? GetElementValue(fdlNode, "fdl1") ?? "",
                    Fdl1Address = GetAttributeValue(fdlNode, "FDL1", "Base") ?? 
                                  GetAttributeValue(fdlNode, "fdl1", "base") ?? "0x5000",
                    Fdl2Name = GetElementValue(fdlNode, "FDL2") ?? GetElementValue(fdlNode, "fdl2") ?? "",
                    Fdl2Address = GetAttributeValue(fdlNode, "FDL2", "Base") ?? 
                                  GetAttributeValue(fdlNode, "fdl2", "base") ?? "0x9efffe00"
                };

                OnLog?.Invoke($"FDL1: {Fdl.Fdl1Name} @ {Fdl.Fdl1Address}");
                OnLog?.Invoke($"FDL2: {Fdl.Fdl2Name} @ {Fdl.Fdl2Address}");
            }

            // 解析 NV 配置
            var nvNode = root.Element("NV") ?? root.Element("nv");
            
            // 解析分区配置
            var partNode = root.Element("Partitions") ?? root.Element("partitions") ?? 
                          root.Element("FileList") ?? root.Element("filelist");
            
            if (partNode != null)
            {
                foreach (var file in partNode.Elements())
                {
                    var partition = new PartitionConfig
                    {
                        Id = file.Attribute("ID")?.Value ?? file.Attribute("id")?.Value ?? "",
                        Name = file.Attribute("Name")?.Value ?? file.Attribute("name")?.Value ?? 
                               file.Name.LocalName,
                        FileName = file.Value ?? file.Attribute("File")?.Value ?? "",
                        Type = file.Attribute("Type")?.Value ?? file.Attribute("type")?.Value ?? "",
                        IsOmittable = file.Attribute("Omit")?.Value == "1" || 
                                     file.Attribute("omit")?.Value == "1",
                        UseBackup = file.Attribute("Backup")?.Value == "1" ||
                                   file.Attribute("backup")?.Value == "1"
                    };

                    // 解析大小
                    var sizeStr = file.Attribute("Size")?.Value ?? file.Attribute("size")?.Value;
                    if (!string.IsNullOrEmpty(sizeStr))
                    {
                        partition.Size = ParseSize(sizeStr);
                    }

                    // 解析偏移
                    var offsetStr = file.Attribute("Offset")?.Value ?? file.Attribute("offset")?.Value;
                    if (!string.IsNullOrEmpty(offsetStr))
                    {
                        partition.Offset = ParseSize(offsetStr);
                    }

                    if (!string.IsNullOrEmpty(partition.Name) || !string.IsNullOrEmpty(partition.FileName))
                    {
                        Partitions.Add(partition);
                    }
                }
            }

            OnLog?.Invoke($"解析完成: {Partitions.Count} 个分区");
            return true;
        }

        /// <summary>
        /// 解析 Scatter 格式
        /// </summary>
        private bool ParseScatterFormat(XElement root)
        {
            OnLog?.Invoke("检测到 Scatter 格式");

            foreach (var part in root.Elements())
            {
                var partition = new PartitionConfig
                {
                    Name = part.Element("partition_name")?.Value ?? part.Name.LocalName,
                    FileName = part.Element("file_name")?.Value ?? "",
                    Type = part.Element("type")?.Value ?? "",
                };

                var sizeStr = part.Element("partition_size")?.Value;
                if (!string.IsNullOrEmpty(sizeStr))
                {
                    partition.Size = ParseSize(sizeStr);
                }

                var offsetStr = part.Element("physical_start_addr")?.Value;
                if (!string.IsNullOrEmpty(offsetStr))
                {
                    partition.Offset = ParseSize(offsetStr);
                }

                if (!string.IsNullOrEmpty(partition.Name))
                {
                    Partitions.Add(partition);
                }
            }

            OnLog?.Invoke($"解析完成: {Partitions.Count} 个分区");
            return true;
        }

        /// <summary>
        /// 解析通用 XML 格式
        /// </summary>
        private bool ParseGenericFormat(XElement root)
        {
            OnLog?.Invoke($"尝试解析通用 XML 格式: {root.Name.LocalName}");

            // 递归查找 FDL 相关节点
            var fdl1Elements = root.DescendantsAndSelf()
                .Where(e => e.Name.LocalName.Contains("FDL1", StringComparison.OrdinalIgnoreCase) ||
                           e.Name.LocalName.Contains("fdl1", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fdl1Elements.Any())
            {
                Fdl = new FdlConfig();
                
                foreach (var elem in fdl1Elements)
                {
                    if (elem.Name.LocalName.Contains("Addr", StringComparison.OrdinalIgnoreCase) ||
                        elem.Name.LocalName.Contains("Base", StringComparison.OrdinalIgnoreCase))
                    {
                        Fdl.Fdl1Address = elem.Value;
                    }
                    else if (!string.IsNullOrEmpty(elem.Value))
                    {
                        Fdl.Fdl1Name = elem.Value;
                    }
                }

                OnLog?.Invoke($"FDL1 地址: {Fdl.Fdl1Address}");
            }

            // 查找分区相关节点
            var partitionElements = root.DescendantsAndSelf()
                .Where(e => e.Attribute("partition_name") != null ||
                           e.Attribute("name") != null ||
                           e.Element("partition_name") != null)
                .ToList();

            foreach (var elem in partitionElements)
            {
                var partition = new PartitionConfig
                {
                    Name = elem.Attribute("partition_name")?.Value ??
                           elem.Attribute("name")?.Value ??
                           elem.Element("partition_name")?.Value ?? "",
                    FileName = elem.Attribute("filename")?.Value ??
                              elem.Attribute("file")?.Value ??
                              elem.Element("file_name")?.Value ?? ""
                };

                if (!string.IsNullOrEmpty(partition.Name))
                {
                    Partitions.Add(partition);
                }
            }

            OnLog?.Invoke($"解析完成: {Partitions.Count} 个分区");
            return Fdl != null || Partitions.Count > 0;
        }

        /// <summary>
        /// 从 PAC 中查找并解析 XML 配置
        /// </summary>
        public bool ParseFromPac(PacExtractor pac)
        {
            if (pac == null || pac.Partitions == null)
            {
                OnLog?.Invoke("PAC 未加载");
                return false;
            }

            // 查找 XML 配置文件
            var xmlPartitions = pac.Partitions.Where(p =>
                p.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                (p.PartitionName.Contains("BMAConfig", StringComparison.OrdinalIgnoreCase) ||
                 p.PartitionName.Contains("scatter", StringComparison.OrdinalIgnoreCase) ||
                 p.FileName.Contains("BMAConfig", StringComparison.OrdinalIgnoreCase))
            ).ToList();

            foreach (var xmlPart in xmlPartitions)
            {
                OnLog?.Invoke($"发现配置文件: {xmlPart.FileName}");
            }

            return xmlPartitions.Any();
        }

        /// <summary>
        /// 自动检测 FDL 地址
        /// </summary>
        public static FdlConfig? AutoDetectFdlConfig(string pacFilePath)
        {
            // 常见的 FDL 地址配置
            var commonConfigs = new Dictionary<string, FdlConfig>
            {
                // SC7731/SC9832E
                { "SC7731", new FdlConfig { Fdl1Address = "0x5000", Fdl2Address = "0x9efffe00" } },
                { "SC9832E", new FdlConfig { Fdl1Address = "0x5000", Fdl2Address = "0x9efffe00" } },
                
                // SC9863A
                { "SC9863A", new FdlConfig { Fdl1Address = "0x65000800", Fdl2Address = "0x9efffe00" } },
                
                // T 系列
                { "T606", new FdlConfig { Fdl1Address = "0x65000800", Fdl2Address = "0x9efffe00" } },
                { "T618", new FdlConfig { Fdl1Address = "0x65000800", Fdl2Address = "0x9efffe00" } },
                { "T700", new FdlConfig { Fdl1Address = "0x9efffe00", Fdl2Address = "0x9f000000" } },
            };

            // 从 PAC 文件名推断芯片类型
            var fileName = Path.GetFileName(pacFilePath).ToUpperInvariant();
            foreach (var config in commonConfigs)
            {
                if (fileName.Contains(config.Key))
                {
                    return config.Value;
                }
            }

            // 默认配置
            return new FdlConfig
            {
                Fdl1Address = "0x5000",
                Fdl2Address = "0x9efffe00"
            };
        }

        #region 辅助方法

        private static string? GetElementValue(XElement parent, string name)
        {
            return parent.Element(name)?.Value;
        }

        private static string? GetAttributeValue(XElement parent, string elementName, string attrName)
        {
            return parent.Element(elementName)?.Attribute(attrName)?.Value;
        }

        private static long ParseSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return 0;

            sizeStr = sizeStr.Trim().ToUpperInvariant();

            // 处理十六进制
            if (sizeStr.StartsWith("0X"))
            {
                return Convert.ToInt64(sizeStr, 16);
            }

            // 处理带单位的大小
            if (sizeStr.EndsWith("K") || sizeStr.EndsWith("KB"))
            {
                var num = double.Parse(sizeStr.Replace("KB", "").Replace("K", ""));
                return (long)(num * 1024);
            }
            if (sizeStr.EndsWith("M") || sizeStr.EndsWith("MB"))
            {
                var num = double.Parse(sizeStr.Replace("MB", "").Replace("M", ""));
                return (long)(num * 1024 * 1024);
            }
            if (sizeStr.EndsWith("G") || sizeStr.EndsWith("GB"))
            {
                var num = double.Parse(sizeStr.Replace("GB", "").Replace("G", ""));
                return (long)(num * 1024 * 1024 * 1024);
            }

            // 纯数字
            if (long.TryParse(sizeStr, out var result))
            {
                return result;
            }

            return 0;
        }

        #endregion
    }
}
