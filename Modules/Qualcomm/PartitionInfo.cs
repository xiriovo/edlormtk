using System;
using System.ComponentModel;

namespace tools.Modules.Qualcomm
{
    /// <summary>
    /// 分区信息
    /// </summary>
    public class PartitionInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// LUN (Logical Unit Number)
        /// </summary>
        public int Lun { get; set; }

        /// <summary>
        /// 分区名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 起始扇区
        /// </summary>
        public long StartSector { get; set; }

        /// <summary>
        /// 扇区数量
        /// </summary>
        public long NumSectors { get; set; }

        /// <summary>
        /// 扇区大小 (通常为 512 或 4096)
        /// </summary>
        public int SectorSize { get; set; } = 512;

        /// <summary>
        /// 分区大小 (字节)
        /// </summary>
        public long Size => NumSectors * SectorSize;

        /// <summary>
        /// 分区类型 GUID
        /// </summary>
        public string TypeGuid { get; set; } = "";

        /// <summary>
        /// 分区唯一 GUID
        /// </summary>
        public string UniqueGuid { get; set; } = "";

        /// <summary>
        /// 分区属性
        /// </summary>
        public ulong Attributes { get; set; }

        private bool _isSelected;
        /// <summary>
        /// 是否选中 (用于 UI)
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        }

        private string _customFilePath = "";
        /// <summary>
        /// 自定义刷写文件路径
        /// </summary>
        public string CustomFilePath
        {
            get => _customFilePath;
            set 
            { 
                if (_customFilePath != value) 
                { 
                    _customFilePath = value; 
                    OnPropertyChanged(nameof(CustomFilePath)); 
                    OnPropertyChanged(nameof(CustomFileName));
                    OnPropertyChanged(nameof(HasCustomFile));
                } 
            }
        }

        /// <summary>
        /// 自定义文件名（用于显示）
        /// </summary>
        public string CustomFileName => string.IsNullOrEmpty(_customFilePath) ? "" : System.IO.Path.GetFileName(_customFilePath);

        /// <summary>
        /// 是否有自定义文件
        /// </summary>
        public bool HasCustomFile => !string.IsNullOrEmpty(_customFilePath);

        private string _sourceFilePath = "";
        /// <summary>
        /// 源文件路径（从 XML 解析）
        /// </summary>
        public string SourceFilePath
        {
            get => _sourceFilePath;
            set
            {
                if (_sourceFilePath != value)
                {
                    _sourceFilePath = value;
                    OnPropertyChanged(nameof(SourceFilePath));
                    OnPropertyChanged(nameof(SourceFileName));
                    OnPropertyChanged(nameof(FileExists));
                    OnPropertyChanged(nameof(HasSourceFile));
                    OnPropertyChanged(nameof(DisplayFilePath));
                }
            }
        }

        /// <summary>
        /// 源文件名
        /// </summary>
        public string SourceFileName => string.IsNullOrEmpty(_sourceFilePath) ? "" : System.IO.Path.GetFileName(_sourceFilePath);

        /// <summary>
        /// 文件是否存在
        /// </summary>
        public bool FileExists => !string.IsNullOrEmpty(_sourceFilePath) && System.IO.File.Exists(_sourceFilePath);

        /// <summary>
        /// 是否有源文件定义
        /// </summary>
        public bool HasSourceFile => !string.IsNullOrEmpty(_sourceFilePath);

        /// <summary>
        /// 显示用的文件路径（优先自定义，其次源文件）
        /// </summary>
        public string DisplayFilePath => HasCustomFile ? _customFilePath : _sourceFilePath;

        /// <summary>
        /// 显示用的文件名（优先自定义，其次源文件）
        /// </summary>
        public string DisplayFileName
        {
            get
            {
                if (HasCustomFile) return CustomFileName;
                if (HasSourceFile) return SourceFileName;
                return "";
            }
        }

        /// <summary>
        /// 文件状态文本
        /// </summary>
        public string FileStatusText
        {
            get
            {
                if (HasCustomFile) return "✓ 自定义";
                if (!HasSourceFile) return "无文件";
                return FileExists ? "✓ 存在" : "✗ 缺失";
            }
        }

        /// <summary>
        /// 用于 UI 显示的起始块
        /// </summary>
        public string Block => StartSector.ToString();

        /// <summary>
        /// 起始扇区（十六进制）
        /// </summary>
        public string StartSectorHex => $"0x{StartSector:X}";

        /// <summary>
        /// 用于 UI 显示的位置
        /// </summary>
        public string Location => $"0x{StartSector:X} - 0x{EndSector:X}";

        /// <summary>
        /// 格式化的大小字符串
        /// </summary>
        public string FormattedSize
        {
            get
            {
                var size = Size;
                if (size >= 1024L * 1024 * 1024)
                    return $"{size / (1024.0 * 1024 * 1024):F2} GB";
                if (size >= 1024 * 1024)
                    return $"{size / (1024.0 * 1024):F2} MB";
                if (size >= 1024)
                    return $"{size / 1024.0:F2} KB";
                return $"{size} B";
            }
        }

        /// <summary>
        /// 结束扇区
        /// </summary>
        public long EndSector => StartSector + NumSectors - 1;

        // 兼容属性 (与旧代码兼容)
        public ulong StartLba
        {
            get => (ulong)StartSector;
            set => StartSector = (long)value;
        }

        public ulong Sectors
        {
            get => (ulong)NumSectors;
            set => NumSectors = (long)value;
        }

        public override string ToString()
        {
            return $"[LUN{Lun}] {Name}: {FormattedSize} ({StartSector} - {EndSector})";
        }
    }

    /// <summary>
    /// 刷写分区信息 (用于刷写操作)
    /// </summary>
    public class FlashPartitionInfo
    {
        public string Lun { get; set; } = "0";
        public string Name { get; set; } = "";
        public string StartSector { get; set; } = "0";
        public long NumSectors { get; set; }
        public string Filename { get; set; } = "";
        public long FileOffset { get; set; } = 0;
        public bool IsSparse { get; set; } = false;

        public FlashPartitionInfo() { }

        public FlashPartitionInfo(string lun, string name, string start, long sectors, string filename = "", long offset = 0)
        {
            Lun = lun;
            Name = name;
            StartSector = start;
            NumSectors = sectors;
            Filename = filename;
            FileOffset = offset;
        }
    }
}
