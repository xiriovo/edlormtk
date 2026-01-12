using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tools.Modules.Qualcomm.SuperDef
{
    /// <summary>
    /// Super分区定义 - 对应 super_def.{nv_id}.json
    /// </summary>
    public class SuperDefinition
    {
        [JsonPropertyName("super_meta")]
        public SuperMetaInfo? SuperMeta { get; set; }

        [JsonPropertyName("super_device")]
        public SuperDeviceInfo? SuperDevice { get; set; }

        [JsonPropertyName("nv_text")]
        public string? NvText { get; set; }

        [JsonPropertyName("nv_id")]
        public string? NvId { get; set; }

        [JsonPropertyName("block_devices")]
        public List<BlockDeviceInfo>? BlockDevices { get; set; }

        [JsonPropertyName("groups")]
        public List<PartitionGroupInfo>? Groups { get; set; }

        [JsonPropertyName("partitions")]
        public List<SuperPartitionInfo>? Partitions { get; set; }
    }

    /// <summary>
    /// Super元数据信息
    /// </summary>
    public class SuperMetaInfo
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        public long SizeBytes => long.TryParse(Size, out var s) ? s : 0;
    }

    /// <summary>
    /// Super设备信息
    /// </summary>
    public class SuperDeviceInfo
    {
        [JsonPropertyName("total_size")]
        public string? TotalSize { get; set; }

        [JsonPropertyName("used_size")]
        public string? UsedSize { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        public long TotalSizeBytes => long.TryParse(TotalSize, out var s) ? s : 0;
        public long UsedSizeBytes => long.TryParse(UsedSize, out var s) ? s : 0;
    }

    /// <summary>
    /// 块设备信息
    /// </summary>
    public class BlockDeviceInfo
    {
        [JsonPropertyName("block_size")]
        public string? BlockSize { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("alignment")]
        public string? Alignment { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        public int BlockSizeBytes => int.TryParse(BlockSize, out var s) ? s : 4096;
        public long SizeBytes => long.TryParse(Size, out var s) ? s : 0;
    }

    /// <summary>
    /// 分区组信息
    /// </summary>
    public class PartitionGroupInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("maximum_size")]
        public string? MaximumSize { get; set; }

        public long MaximumSizeBytes => long.TryParse(MaximumSize, out var s) ? s : 0;
    }

    /// <summary>
    /// Super内部分区信息
    /// </summary>
    public class SuperPartitionInfo
    {
        [JsonPropertyName("is_dynamic")]
        public bool IsDynamic { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("group_name")]
        public string? GroupName { get; set; }

        public long SizeBytes => long.TryParse(Size, out var s) ? s : 0;

        /// <summary>
        /// 是否有镜像文件
        /// </summary>
        public bool HasImage => !string.IsNullOrEmpty(Path);

        /// <summary>
        /// 是否为 A 槽位
        /// </summary>
        public bool IsSlotA => Name?.EndsWith("_a") ?? false;

        /// <summary>
        /// 是否为 B 槽位
        /// </summary>
        public bool IsSlotB => Name?.EndsWith("_b") ?? false;

        /// <summary>
        /// 获取不带槽位后缀的基础名称
        /// </summary>
        public string BaseName
        {
            get
            {
                if (string.IsNullOrEmpty(Name)) return "";
                if (Name.EndsWith("_a") || Name.EndsWith("_b"))
                    return Name[..^2];
                return Name;
            }
        }
    }

    /// <summary>
    /// Super定义解析器
    /// </summary>
    public class SuperDefParser
    {
        /// <summary>
        /// 解析 super_def.json 文件
        /// </summary>
        public SuperDefinition? Parse(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return null;

            try
            {
                var json = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };
                return JsonSerializer.Deserialize<SuperDefinition>(json, options);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从固件目录自动查找并解析 super_def
        /// </summary>
        public SuperDefinition? ParseFromFirmware(string firmwareDir, string? nvId = null)
        {
            var metaDir = Path.Combine(firmwareDir, "META");
            if (!Directory.Exists(metaDir))
                return null;

            // 查找 super_def.{nvId}.json 或 super_def.json
            string[] patterns = nvId != null
                ? new[] { $"super_def.{nvId}.json", "super_def.json" }
                : new[] { "super_def.*.json", "super_def.json" };

            foreach (var pattern in patterns)
            {
                var files = Directory.GetFiles(metaDir, pattern);
                if (files.Length > 0)
                {
                    var def = Parse(files[0]);
                    if (def != null) return def;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取需要刷写的分区列表 (仅A槽位且有镜像的分区)
        /// </summary>
        public List<SuperPartitionInfo> GetFlashablePartitions(SuperDefinition def, bool includeSlotB = false)
        {
            var result = new List<SuperPartitionInfo>();
            if (def?.Partitions == null) return result;

            foreach (var partition in def.Partitions)
            {
                if (!partition.HasImage) continue;
                if (partition.IsSlotB && !includeSlotB) continue;
                result.Add(partition);
            }

            return result;
        }

        /// <summary>
        /// 获取super_meta文件路径
        /// </summary>
        public string? GetSuperMetaPath(string firmwareDir, SuperDefinition def)
        {
            if (def?.SuperMeta?.Path == null) return null;

            var metaPath = Path.Combine(firmwareDir, def.SuperMeta.Path);
            return File.Exists(metaPath) ? metaPath : null;
        }
    }
}
