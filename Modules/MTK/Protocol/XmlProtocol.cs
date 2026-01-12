using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace tools.Modules.MTK.Protocol
{
    /// <summary>
    /// MTK XML DA 协议实现 (v6 DA)
    /// 基于 SP Flash Tool V6.2228 源代码逆向分析
    /// 参考文件:
    /// - SP_Flash_Tool_src_6.2228/XML/XMLConst.h
    /// - SP_Flash_Tool_src_6.2228/XML/CmdGenerator/*.cpp
    /// - SP_Flash_Tool_src_6.2228/Cmd/DASLACmdHandler.cpp
    /// </summary>
    public class XmlProtocol : IDisposable
    {
        private SerialPort? _port;
        private readonly object _lock = new();

        public event Action<string>? OnLog;
#pragma warning disable CS0067 // 事件预留给未来使用
        public event Action<int, int>? OnProgress;
#pragma warning restore CS0067

        public const uint MAGIC = 0xFEEEEEEF;
        
        #region 官方命令版本常量 (来自 XMLConst.h)
        
        private const string CMD_VERSION_1_0 = "1.0";
        private const string CMD_VERSION_1_1 = "1.1";
        private const string CMD_VERSION_1_2 = "1.2";
        private const string CMD_VERSION_2_0 = "2.0";
        
        #endregion
        
        #region 官方命令名称常量 (来自 XMLConst.h)
        
        // DA 命令
        public const string CMD_WRITE_FLASH = "CMD:WRITE-FLASH";
        public const string CMD_WRITE_PARTITION = "CMD:WRITE-PARTITION";
        public const string CMD_READ_FLASH = "CMD:READ-FLASH";
        public const string CMD_READ_PARTITION = "CMD:READ-PARTITION";
        public const string CMD_FLASH_ALL = "CMD:FLASH-ALL";
        public const string CMD_WRITE_PARTITIONS = "CMD:WRITE-PARTITIONS";
        public const string CMD_FLASH_UPDATE = "CMD:FLASH-UPDATE";
        public const string CMD_ERASE_FLASH = "CMD:ERASE-FLASH";
        public const string CMD_ERASE_PARTITION = "CMD:ERASE-PARTITION";
        public const string CMD_GET_HW_INFO = "CMD:GET-HW-INFO";
        public const string CMD_REBOOT = "CMD:REBOOT";
        public const string CMD_SET_RUNTIME_PARAMETER = "CMD:SET-RUNTIME-PARAMETER";
        public const string CMD_NOTIFY_INIT_DRAM = "CMD:NOTIFY-INIT-DRAM";
        public const string CMD_BOOT_TO = "CMD:BOOT-TO";
        public const string CMD_WRITE_EFUSE = "CMD:WRITE-EFUSE";
        public const string CMD_READ_EFUSE = "CMD:READ-EFUSE";
        public const string CMD_RAM_TEST = "CMD:RAM-TEST";
        public const string CMD_READ_REGISTER = "CMD:READ-REGISTER";
        public const string CMD_WRITE_REGISTER = "CMD:WRITE-REGISTER";
        public const string CMD_EMMC_CONTROL = "CMD:EMMC-CONTROL";
        public const string CMD_SET_HOST_INFO = "CMD:SET-HOST-INFO";
        public const string CMD_SECURITY_GET_DEV_FW_INFO = "CMD:SECURITY-GET-DEV-FW-INFO";
        public const string CMD_SECURITY_SET_FLASH_POLICY = "CMD:SECURITY-SET-FLASH-POLICY";
        public const string CMD_SECURITY_SET_ALLINONE_SIGNATURE = "CMD:SECURITY-SET-ALLINONE-SIGNATURE";
        public const string CMD_SET_RSC = "CMD:SET-RSC";
        public const string CMD_SET_BOOT_MODE = "CMD:SET-BOOT-MODE";
        public const string CMD_WRITE_PRIVATE_CERT = "CMD:WRITE-PRIVATE-CERT";
        public const string CMD_DEBUG_DRAM_REPAIR = "CMD:DEBUG:DRAM-REPAIR";
        public const string CMD_GET_DA_INFO = "CMD:GET-DA-INFO";
        public const string CMD_READ_PARTITION_TABLE = "CMD:READ-PARTITION-TABLE";
        public const string CMD_DEBUG_UFS = "CMD:DEBUG:UFS";
        public const string CMD_GET_DOWNLOADED_IMAGE_FEEDBACK = "CMD:GET-DOWNLOADED-IMAGE-FEEDBACK";
        public const string CMD_GET_SYS_PROPERTY = "CMD:GET-SYS-PROPERTY";
        
        // BROM 命令
        public const string CMD_RUN_PROGRAM = "CMD:RUN-PROGRAM";
        public const string CMD_RUN_LOADER = "CMD:RUN-LOADER";
        public const string CMD_ACTION = "CMD:ACTION";
        
        // DA 扩展命令 (来自 flash.dll 逆向)
        public const string CMD_DOWNLOAD_FILE = "CMD:DOWNLOAD-FILE";
        public const string CMD_UPLOAD_FILE = "CMD:UPLOAD-FILE";
        public const string CMD_PROGRESS_REPORT = "CMD:PROGRESS-REPORT";
        public const string CMD_FILE_SYS_OPERATION = "CMD:FILE-SYS-OPERATION";
        public const string CMD_START = "CMD:START";
        public const string CMD_END = "CMD:END";
        public const string CMD_HOST_SUPPORTED_COMMANDS = "CMD:HOST-SUPPORTED-COMMANDS";
        public const string CMD_NOTIFY_INIT_HW = "CMD:NOTIFY-INIT-HW";
        public const string CMD_CAN_HIGHER_USB_SPEED = "CMD:CAN-HIGHER-USB-SPEED";
        public const string CMD_SWITCH_HIGHER_USB_SPEED = "CMD:SWITCH-HIGHER-USB-SPEED";
        
        // Flash 模式 (来自 flash.dll)
        public const string FLASH_MODE_DA = "FLASH-MODE-DA";
        public const string FLASH_MODE_DA_SRAM = "FLASH-MODE-DA-SRAM";
        public const string FLASH_MODE_XFLASH = "FLASH-MODE-XFLASH";
        
        // USB 速度常量 (来自 FlashToolLib.v1.dll 逆向)
        public const int USB_FULL_SPEED = 0;
        public const int USB_HIGH_SPEED = 1;
        public const int USB_ULTRA_HIGH_SPEED = 2;
        
        // 擦除标志常量 (来自 FlashToolLib.v1.dll 逆向)
        public const int NUTL_ERASE = 0;
        public const int NUTL_FORCE_ERASE = 1;
        public const int NUTL_MARK_BAD_BLOCK = 2;
        
        // SLA 特性值 (来自 CFlashToolTypes.h)
        public const byte SLA_FEATURE_NONE = 0x0;
        public const byte SLA_FEATURE_HRID = 0x1;
        public const byte SLA_FEATURE_SOCID = 0x2;
        
        // SLA 常量 (来自 DASLACmdHandler.cpp)
        public const int SLA_RANDOM_LENGTH = 16;
        public const int SLA_HRID_LENGTH = 16;
        public const int SLA_SOCID_LENGTH = 32;
        
        #endregion

        #region 连接管理

        public void SetPort(SerialPort port)
        {
            _port = port;
        }

        #endregion

        #region XML 命令生成

        /// <summary>
        /// 创建 XML 命令
        /// </summary>
        private string CreateCommand(string cmd, string? content = null, string version = "1.0")
        {
            var sb = new StringBuilder();
            sb.Append($"<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append($"<da><version>{version}</version>");
            sb.Append($"<command>CMD:{cmd}</command>");
            if (!string.IsNullOrEmpty(content))
            {
                sb.Append(content);
            }
            sb.Append("</da>");
            return sb.ToString();
        }

        /// <summary>
        /// 创建带参数的 XML 命令
        /// </summary>
        private string CreateCommandWithArg(string cmd, string argContent, string version = "1.0")
        {
            return CreateCommand(cmd, $"<arg>{argContent}</arg>", version);
        }

        #endregion

        #region DA1 命令

        /// <summary>
        /// 通知初始化硬件
        /// </summary>
        public async Task<bool> NotifyInitHwAsync(CancellationToken ct = default)
        {
            string cmd = CreateCommand("NOTIFY-INIT-HW");
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 设置运行时参数
        /// </summary>
        public async Task<bool> SetRuntimeParameterAsync(
            string checksumLevel = "NONE",
            string batteryExist = "AUTO-DETECT",
            string daLogLevel = "INFO",
            string logChannel = "UART",
            string systemOs = "LINUX",
            bool initializeDram = true,
            CancellationToken ct = default)
        {
            string dram = initializeDram ? "YES" : "NO";
            string argContent = $@"
        <checksum_level>{checksumLevel}</checksum_level>
        <battery_exist>{batteryExist}</battery_exist>
        <da_log_level>{daLogLevel}</da_log_level>
        <log_channel>{logChannel}</log_channel>
        <system_os>{systemOs}</system_os>
    </arg>
    <adv>
        <initialize_dram>{dram}</initialize_dram>
    </adv>";

            string cmd = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<da>
    <version>1.1</version>
    <command>CMD:SET-RUNTIME-PARAMETER</command>
    <arg>{argContent}
</da>";
            return await SendXmlCommandAsync(cmd + "\0", ct);
        }

        /// <summary>
        /// 安全设置 Flash 策略
        /// </summary>
        public async Task<bool> SecuritySetFlashPolicyAsync(
            uint hostOffset = 0x8000000,
            uint length = 0x100000,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>MEM://0x{hostOffset:X}:0x{length:X}</source_file>";
            string cmd = CreateCommandWithArg("SECURITY-SET-FLASH-POLICY", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// Boot To (加载 DA2)
        /// </summary>
        public async Task<bool> BootToAsync(
            uint atAddr,
            uint jmpAddr,
            uint hostOffset,
            uint length,
            CancellationToken ct = default)
        {
            string argContent = $@"<at_address>0x{atAddr:X}</at_address>
<jmp_address>0x{jmpAddr:X}</jmp_address>
<source_file>MEM://0x{hostOffset:X}:0x{length:X}</source_file>";
            string cmd = CreateCommandWithArg("BOOT-TO", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        #endregion

        #region DA2 命令 (来自 SP Flash Tool XML/CmdGenerator)

        /// <summary>
        /// 读取分区表 (READ-PARTITION-TABLE)
        /// 参考: ReadPartitionTblCmdXML.cpp
        /// </summary>
        public async Task<string?> ReadPartitionTableAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("READ-PARTITION-TABLE", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 读取分区 (READ-PARTITION)
        /// 参考: ReadPartitionCmdXML.cpp
        /// </summary>
        public async Task<bool> ReadPartitionAsync(
            string partitionName,
            string outputPath,
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partitionName}</partition>\n<target_file>{outputPath}</target_file>";
            string cmd = CreateCommandWithArg("READ-PARTITION", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 读取 Flash (READ-FLASH)
        /// 参考: ReadFlashCmdXML.cpp
        /// </summary>
        public async Task<bool> ReadFlashAsync(
            string partition,
            ulong offset,
            ulong length,
            string outputPath,
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partition}</partition>\n" +
                               $"<offset>0x{offset:X}</offset>\n" +
                               $"<length>0x{length:X}</length>\n" +
                               $"<target_file>{outputPath}</target_file>";
            string cmd = CreateCommandWithArg("READ-FLASH", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 写入分区 (WRITE-PARTITION)
        /// 参考: WritePartitionCmdXML.cpp
        /// </summary>
        public async Task<bool> WritePartitionAsync(
            string partitionName,
            string inputPath,
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partitionName}</partition>\n<source_file>{inputPath}</source_file>";
            string cmd = CreateCommandWithArg("WRITE-PARTITION", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 写入 Flash (WRITE-FLASH)
        /// 参考: WriteFlashCmdXML.cpp
        /// </summary>
        public async Task<bool> WriteFlashAsync(
            string partition,
            ulong offset,
            string inputPath,
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partition}</partition>\n" +
                               $"<offset>0x{offset:X}</offset>\n" +
                               $"<source_file>{inputPath}</source_file>";
            string cmd = CreateCommandWithArg("WRITE-FLASH", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 写入多个分区 (WRITE-PARTITIONS)
        /// 参考: WritePartitionsCmdXML.cpp
        /// </summary>
        public async Task<bool> WritePartitionsAsync(
            Dictionary<string, string> partitionFiles,
            CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            foreach (var kvp in partitionFiles)
            {
                sb.Append($"<partition>\n<partition_name>{kvp.Key}</partition_name>\n");
                sb.Append($"<file_name>{kvp.Value}</file_name>\n</partition>\n");
            }
            string cmd = CreateCommandWithArg("WRITE-PARTITIONS", sb.ToString());
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 擦除分区 (ERASE-PARTITION)
        /// 参考: ErasePartitionCmdXML.cpp
        /// </summary>
        public async Task<bool> ErasePartitionAsync(
            string partitionName,
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partitionName}</partition>";
            string cmd = CreateCommandWithArg("ERASE-PARTITION", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 擦除 Flash (ERASE-FLASH)
        /// 参考: EraseFlashCmdXML.cpp
        /// </summary>
        public async Task<bool> EraseFlashAsync(
            string partition = "ALL",
            ulong offset = 0,
            string length = "MAX",
            CancellationToken ct = default)
        {
            string argContent = $"<partition>{partition}</partition>\n" +
                               $"<offset>0x{offset:X}</offset>\n" +
                               $"<length>{length}</length>";
            string cmd = CreateCommandWithArg("ERASE-FLASH", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 全量刷写 (FLASH-ALL)
        /// 参考: FlashAllCmdXML.cpp
        /// </summary>
        public async Task<bool> FlashAllAsync(
            string scatterPath,
            string pathSeparator = "/",
            CancellationToken ct = default)
        {
            string argContent = $"<path_separator>{pathSeparator}</path_separator>\n" +
                               $"<source_file>{scatterPath}</source_file>";
            string cmd = CreateCommandWithArg("FLASH-ALL", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 刷写更新 (FLASH-UPDATE)
        /// 参考: FlashUpdateCmdXML.cpp
        /// </summary>
        public async Task<bool> FlashUpdateAsync(
            string scatterPath,
            string pathSeparator = "/",
            CancellationToken ct = default)
        {
            string argContent = $"<path_separator>{pathSeparator}</path_separator>\n" +
                               $"<source_file>{scatterPath}</source_file>";
            string cmd = CreateCommandWithArg("FLASH-UPDATE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 获取硬件信息 (GET-HW-INFO)
        /// 参考: GetHWInfoCmdXML.cpp
        /// </summary>
        public async Task<string?> GetHWInfoAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("GET-HW-INFO", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 获取 BROM 硬件信息并解析
        /// </summary>
        public async Task<BromHwInfo?> GetBromHwInfoAsync(CancellationToken ct = default)
        {
            var response = await GetHWInfoAsync(ct);
            if (string.IsNullOrEmpty(response)) return null;
            return BromHwInfo.Parse(response);
        }

        /// <summary>
        /// 获取 DA 信息 (GET-DA-INFO)
        /// 参考: GetDAInfoCmdXML.cpp
        /// </summary>
        public async Task<string?> GetDAInfoAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("GET-DA-INFO", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 重启设备 (REBOOT)
        /// 参考: RebootCmdXML.cpp
        /// </summary>
        public async Task<bool> RebootAsync(
            RebootType type = RebootType.WarmReset,
            CancellationToken ct = default)
        {
            string typeStr = type switch
            {
                RebootType.WarmReset => "warm-reset",
                RebootType.ColdReset => "pmic-cold-reset",
                RebootType.Disconnect => "disconnect",
                _ => "warm-reset"
            };
            string argContent = $"<type>{typeStr}</type>";
            string cmd = CreateCommandWithArg("REBOOT", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 读取寄存器 (READ-REGISTER)
        /// 参考: DAReadRegCmdXML.cpp
        /// </summary>
        public async Task<uint[]?> ReadRegisterAsync(
            ulong baseAddress,
            int count = 1,
            int bitWidth = 32,
            CancellationToken ct = default)
        {
            string argContent = $"<bit_width>{bitWidth}</bit_width>\n" +
                               $"<base_address>0x{baseAddress:X}</base_address>\n" +
                               $"<count>{count}</count>\n" +
                               "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("READ-REGISTER", argContent);
            var response = await SendXmlCommandAndReadAsync(cmd, ct);
            // TODO: 解析响应
            return null;
        }

        /// <summary>
        /// 写入寄存器 (WRITE-REGISTER)
        /// 参考: DAWriteRegCmdXML.cpp
        /// </summary>
        public async Task<bool> WriteRegisterAsync(
            ulong baseAddress,
            uint value,
            int bitWidth = 32,
            CancellationToken ct = default)
        {
            string argContent = $"<bit_width>{bitWidth}</bit_width>\n" +
                               $"<base_address>0x{baseAddress:X}</base_address>\n" +
                               $"<source_file>MEM://0x{value:X}:{bitWidth / 8}</source_file>";
            string cmd = CreateCommandWithArg("WRITE-REGISTER", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 读取 eFuse (READ-EFUSE)
        /// 参考: ReadEfuseCmdXML.cpp
        /// </summary>
        public async Task<string?> ReadEfuseAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("READ-EFUSE", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 写入 eFuse (WRITE-EFUSE)
        /// 参考: WriteEfuseCmdXML.cpp
        /// </summary>
        public async Task<bool> WriteEfuseAsync(
            string efuseFile,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{efuseFile}</source_file>";
            string cmd = CreateCommandWithArg("WRITE-EFUSE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// RAM 测试 (RAM-TEST)
        /// 参考: RamTestCmdXML.cpp
        /// </summary>
        public async Task<bool> RamTestAsync(
            ulong startAddress,
            ulong length,
            CancellationToken ct = default)
        {
            string argContent = $"<start_address>0x{startAddress:X}</start_address>\n" +
                               $"<length>0x{length:X}</length>\n" +
                               "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("RAM-TEST", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// eMMC 控制 (EMMC-CONTROL)
        /// 参考: EMMCControlCmdXML.cpp
        /// </summary>
        public async Task<bool> EmmcControlAsync(
            string action,
            CancellationToken ct = default)
        {
            string argContent = $"<action>{action}</action>";
            string cmd = CreateCommandWithArg("EMMC-CONTROL", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 设置主机信息 (SET-HOST-INFO)
        /// 参考: SetHostInfoCmdXML.cpp
        /// </summary>
        public async Task<bool> SetHostInfoAsync(
            string hostOs = "WINDOWS",
            CancellationToken ct = default)
        {
            string argContent = $"<host_os>{hostOs}</host_os>";
            string cmd = CreateCommandWithArg("SET-HOST-INFO", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 设置 RSC (SET-RSC)
        /// 参考: SetRSCCmdXML.cpp
        /// </summary>
        public async Task<bool> SetRscAsync(
            string rscFile,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{rscFile}</source_file>";
            string cmd = CreateCommandWithArg("SET-RSC", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 设置启动模式 (SET-BOOT-MODE)
        /// 参考: SetBootModeCmdXML.cpp
        /// </summary>
        public async Task<bool> SetBootModeAsync(
            BootModeConnectType connectType,
            bool mobileLog = false,
            bool adb = false,
            CancellationToken ct = default)
        {
            string connectTypeStr = connectType switch
            {
                BootModeConnectType.USB => "USB",
                BootModeConnectType.UART => "UART",
                BootModeConnectType.WiFi => "WIFI",
                _ => "USB"
            };
            string argContent = $"<connect_type>{connectTypeStr}</connect_type>\n" +
                               $"<mobile_log>{(mobileLog ? "ON" : "OFF")}</mobile_log>\n" +
                               $"<adb>{(adb ? "ON" : "OFF")}</adb>";
            string cmd = CreateCommandWithArg("SET-BOOT-MODE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 写入私有证书 (WRITE-PRIVATE-CERT)
        /// 参考: WritePrivateCertCmdXML.cpp
        /// </summary>
        public async Task<bool> WritePrivateCertAsync(
            string certFile,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{certFile}</source_file>";
            string cmd = CreateCommandWithArg("WRITE-PRIVATE-CERT", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// DRAM 修复 (DEBUG:DRAM-REPAIR)
        /// 参考: DebugDRAMRepairCmdXML.cpp
        /// </summary>
        public async Task<DramRepairResult> DramRepairAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("DEBUG:DRAM-REPAIR", argContent);
            var response = await SendXmlCommandAndReadAsync(cmd, ct);
            // 解析响应中的修复状态
            if (response?.Contains("SUCCESS") == true)
                return DramRepairResult.Success;
            if (response?.Contains("NO_NEED") == true)
                return DramRepairResult.NoNeed;
            return DramRepairResult.Failed;
        }

        /// <summary>
        /// UFS 调试 (DEBUG:UFS)
        /// 参考: DebugUFSCmdXML.cpp
        /// </summary>
        public async Task<bool> DebugUfsAsync(
            UfsDebugAction action,
            string? firmwarePath = null,
            CancellationToken ct = default)
        {
            string actionStr = action switch
            {
                UfsDebugAction.UpdateFirmware => "UPDATE-FIRMWARE",
                UfsDebugAction.Setting => "SETTING",
                _ => "SETTING"
            };
            
            string argContent = $"<action>{actionStr}</action>";
            if (!string.IsNullOrEmpty(firmwarePath))
            {
                argContent += $"\n<source_file>{firmwarePath}</source_file>";
            }
            
            string cmd = CreateCommandWithArg("DEBUG:UFS", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 获取下载镜像反馈 (GET-DOWNLOADED-IMAGE-FEEDBACK)
        /// 参考: GetDLImageFeedbackCmdXML.cpp
        /// </summary>
        public async Task<string?> GetDownloadedImageFeedbackAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("GET-DOWNLOADED-IMAGE-FEEDBACK", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 获取系统属性 (GET-SYS-PROPERTY)
        /// 参考: GetSysPropertyCmdXML.cpp
        /// </summary>
        public async Task<string?> GetSysPropertyAsync(
            string key,
            CancellationToken ct = default)
        {
            string argContent = $"<key>{key}</key>\n<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("GET-SYS-PROPERTY", argContent);
            return await SendXmlCommandAndReadAsync(cmd, ct);
        }

        /// <summary>
        /// 检查 DA SLA 是否启用
        /// 参考: DASLACmdHandler.cpp - checkDASLAEnabled()
        /// </summary>
        public async Task<bool> CheckDaSlaEnabledAsync(CancellationToken ct = default)
        {
            var response = await GetSysPropertyAsync("DA.SLA", ct);
            if (string.IsNullOrEmpty(response)) return false;
            
            // 解析 XML 响应中的 DA.SLA 值
            return response.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
        
        #region DA SLA 命令 (来自 DASLACmdHandler.cpp)

        /// <summary>
        /// 获取设备固件信息 (用于 SLA 认证)
        /// 参考: SecGetDevFWInfoCmdXML.cpp
        /// </summary>
        public async Task<DevFwInfo?> SecurityGetDevFwInfoAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("SECURITY-GET-DEV-FW-INFO", argContent);
            var response = await SendXmlCommandAndReadAsync(cmd, ct);
            
            if (string.IsNullOrEmpty(response)) return null;
            
            return ParseDevFwInfoResponse(response);
        }

        /// <summary>
        /// 设置 Flash 策略 (SLA 响应)
        /// 参考: SecSetFlashPolicyCmdXML.cpp
        /// </summary>
        public async Task<bool> SecuritySetFlashPolicyAsync(
            byte[] challengeResponse,
            CancellationToken ct = default)
        {
            // 将响应数据写入内存并传递地址
            // 实际实现中需要通过 USB 或串口发送数据
            string argContent = $"<source_file>MEM://0:{challengeResponse.Length:X}</source_file>";
            string cmd = CreateCommandWithArg("SECURITY-SET-FLASH-POLICY", argContent);
            
            // 先发送 XML 命令
            bool result = await SendXmlCommandAsync(cmd, ct);
            if (!result) return false;
            
            // 然后发送实际的 challenge response 数据
            return XSend(challengeResponse);
        }

        /// <summary>
        /// 设置全量签名 (SECURITY-SET-ALLINONE-SIGNATURE)
        /// 参考: SecSetAllInOneSignatureCmdXML.cpp
        /// </summary>
        public async Task<bool> SecuritySetAllInOneSignatureAsync(
            string signatureFile,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{signatureFile}</source_file>";
            string cmd = CreateCommandWithArg("SECURITY-SET-ALLINONE-SIGNATURE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 完整的 DA SLA 验证流程
        /// 参考: DASLACmdHandler.cpp - DASLAVerify()
        /// </summary>
        public async Task<bool> DaSlaVerifyAsync(
            Func<byte[], byte[]>? slaChallenge,
            byte slaFeature = SLA_FEATURE_NONE,
            CancellationToken ct = default)
        {
            // 1. 检查 DA SLA 是否启用
            if (!await CheckDaSlaEnabledAsync(ct))
            {
                Log("DA SLA Disabled, skip it!");
                return true;
            }

            // 2. 获取设备固件信息
            var devFwInfo = await SecurityGetDevFwInfoAsync(ct);
            if (devFwInfo == null)
            {
                Log("Failed to get device firmware info");
                return false;
            }

            // 3. 构建 challenge 输入数据
            // 根据 SLA 特性确定偏移
            int challengeOffset = slaFeature switch
            {
                SLA_FEATURE_HRID => SLA_HRID_LENGTH,
                SLA_FEATURE_SOCID => SLA_SOCID_LENGTH,
                _ => 0
            };

            int challengeLength = SLA_RANDOM_LENGTH + challengeOffset;
            byte[] challengeIn = new byte[challengeLength];

            // 复制 HRID 或 SOCID
            if (slaFeature == SLA_FEATURE_HRID && devFwInfo.Hrid != null)
            {
                Array.Copy(devFwInfo.Hrid, 0, challengeIn, 0, Math.Min(devFwInfo.Hrid.Length, challengeOffset));
            }
            else if (slaFeature == SLA_FEATURE_SOCID && devFwInfo.SocId != null)
            {
                Array.Copy(devFwInfo.SocId, 0, challengeIn, 0, Math.Min(devFwInfo.SocId.Length, challengeOffset));
            }

            // 复制随机数
            if (devFwInfo.RandomData != null)
            {
                Array.Copy(devFwInfo.RandomData, 0, challengeIn, challengeOffset, 
                    Math.Min(devFwInfo.RandomData.Length, SLA_RANDOM_LENGTH));
            }

            // 4. 调用 SLA Challenge 函数
            byte[] challengeOut;
            if (slaChallenge != null)
            {
                challengeOut = slaChallenge(challengeIn);
            }
            else
            {
                // 默认返回 "SLA" (如官方代码所示)
                challengeOut = Encoding.ASCII.GetBytes("SLA\0");
            }

            // 5. 发送响应
            return await SecuritySetFlashPolicyAsync(challengeOut, ct);
        }

        /// <summary>
        /// 解析设备固件信息响应
        /// 参考: SecDevFWInfoParser.cpp
        /// </summary>
        private DevFwInfo? ParseDevFwInfoResponse(string response)
        {
            try
            {
                var info = new DevFwInfo();
                
                // 解析 rnd (随机数)
                var rndMatch = System.Text.RegularExpressions.Regex.Match(response, @"<rnd>([^<]+)</rnd>");
                if (rndMatch.Success)
                {
                    info.RandomData = HexStringToBytes(rndMatch.Groups[1].Value.Trim());
                }

                // 解析 hrid
                var hridMatch = System.Text.RegularExpressions.Regex.Match(response, @"<hrid>([^<]+)</hrid>");
                if (hridMatch.Success)
                {
                    info.Hrid = HexStringToBytes(hridMatch.Groups[1].Value.Trim());
                }

                // 解析 socid
                var socidMatch = System.Text.RegularExpressions.Regex.Match(response, @"<socid>([^<]+)</socid>");
                if (socidMatch.Success)
                {
                    info.SocId = HexStringToBytes(socidMatch.Groups[1].Value.Trim());
                }

                return info;
            }
            catch (Exception ex)
            {
                Log($"ParseDevFwInfoResponse error: {ex.Message}");
                return null;
            }
        }

        private static byte[] HexStringToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        #endregion
        
        #region 官方扩展命令 (来自 flash.dll 逆向)

        /// <summary>
        /// 主机支持的命令 (HOST-SUPPORTED-COMMANDS)
        /// 在连接 DA 后首先发送，告知设备主机支持哪些命令
        /// </summary>
        public async Task<bool> HostSupportedCommandsAsync(
            string hostCapability = "DOWNLOAD,UPLOAD,PROGRESS",
            CancellationToken ct = default)
        {
            string xml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        $"<da><version>1.0</version>" +
                        $"<command>CMD:HOST-SUPPORTED-COMMANDS</command>" +
                        $"<arg><host_capability>{hostCapability}</host_capability></arg></da>";
            return await SendXmlCommandAsync(xml, ct);
        }

        /// <summary>
        /// 检查是否支持高速 USB (CAN-HIGHER-USB-SPEED)
        /// </summary>
        public async Task<bool> CanHigherUsbSpeedAsync(CancellationToken ct = default)
        {
            string argContent = "<target_file>MEM://</target_file>";
            string cmd = CreateCommandWithArg("CAN-HIGHER-USB-SPEED", argContent);
            var response = await SendXmlCommandAndReadAsync(cmd, ct);
            return response?.Contains("YES", StringComparison.OrdinalIgnoreCase) == true ||
                   response?.Contains("true", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// 切换到高速 USB (SWITCH-HIGHER-USB-SPEED)
        /// </summary>
        public async Task<bool> SwitchHigherUsbSpeedAsync(CancellationToken ct = default)
        {
            string cmd = CreateCommand("SWITCH-HIGHER-USB-SPEED");
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 下载文件 (DOWNLOAD-FILE)
        /// </summary>
        public async Task<bool> DownloadFileAsync(
            string remotePath,
            string localPath,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{remotePath}</source_file>\n" +
                               $"<target_file>{localPath}</target_file>";
            string cmd = CreateCommandWithArg("DOWNLOAD-FILE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 上传文件 (UPLOAD-FILE)
        /// </summary>
        public async Task<bool> UploadFileAsync(
            string localPath,
            string remotePath,
            CancellationToken ct = default)
        {
            string argContent = $"<source_file>{localPath}</source_file>\n" +
                               $"<target_file>{remotePath}</target_file>";
            string cmd = CreateCommandWithArg("UPLOAD-FILE", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 文件系统操作 (FILE-SYS-OPERATION)
        /// </summary>
        public async Task<bool> FileSysOperationAsync(
            string operation,
            string path,
            CancellationToken ct = default)
        {
            string argContent = $"<operation>{operation}</operation>\n<file_path>{path}</file_path>";
            string cmd = CreateCommandWithArg("FILE-SYS-OPERATION", argContent);
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 开始命令 (START)
        /// 在批量操作前发送
        /// </summary>
        public async Task<bool> StartAsync(CancellationToken ct = default)
        {
            string cmd = CreateCommand("START");
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 结束命令 (END)
        /// 在批量操作后发送
        /// </summary>
        public async Task<bool> EndAsync(CancellationToken ct = default)
        {
            string cmd = CreateCommand("END");
            return await SendXmlCommandAsync(cmd, ct);
        }

        /// <summary>
        /// 进度报告 (PROGRESS-REPORT)
        /// </summary>
        public async Task<(int current, int total)?> ProgressReportAsync(CancellationToken ct = default)
        {
            string cmd = CreateCommand("PROGRESS-REPORT");
            string? response = await SendXmlCommandAndReadAsync(cmd, ct);
            if (response != null)
            {
                try
                {
                    var currentMatch = System.Text.RegularExpressions.Regex.Match(response, @"<current>(\d+)</current>");
                    var totalMatch = System.Text.RegularExpressions.Regex.Match(response, @"<total>(\d+)</total>");
                    if (currentMatch.Success && totalMatch.Success)
                    {
                        return (int.Parse(currentMatch.Groups[1].Value), int.Parse(totalMatch.Groups[1].Value));
                    }
                }
                catch { }
            }
            return null;
        }

        #endregion

        #region BROM 命令

        /// <summary>
        /// 运行程序 (RUN-PROGRAM)
        /// 参考: RunProgramCmdXML.cpp
        /// </summary>
        public async Task<bool> RunProgramAsync(
            ulong atAddress,
            ulong jmpAddress,
            uint sigOffset,
            uint sigLength,
            string programFile,
            CancellationToken ct = default)
        {
            string argContent = $"<at_address>0x{atAddress:X}</at_address>\n" +
                               $"<jmp_address>0x{jmpAddress:X}</jmp_address>\n" +
                               $"<signature_offset>0x{sigOffset:X}</signature_offset>\n" +
                               $"<signature_length>0x{sigLength:X}</signature_length>\n" +
                               $"<source_file>{programFile}</source_file>";
            
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<brom><version>1.0</version>");
            sb.Append("<command>CMD:RUN-PROGRAM</command>");
            sb.Append($"<arg>{argContent}</arg>");
            sb.Append("</brom>");
            
            return await SendXmlCommandAsync(sb.ToString(), ct);
        }

        /// <summary>
        /// BROM 读取寄存器
        /// 参考: BromReadRegCmdXML.cpp
        /// </summary>
        public async Task<uint[]?> BromReadRegisterAsync(
            ulong baseAddress,
            int count = 1,
            int bitWidth = 32,
            CancellationToken ct = default)
        {
            string argContent = $"<bit_width>{bitWidth}</bit_width>\n" +
                               $"<base_address>0x{baseAddress:X}</base_address>\n" +
                               $"<count>{count}</count>\n" +
                               "<target_file>MEM://</target_file>";
            
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<brom><version>1.0</version>");
            sb.Append("<command>CMD:READ-REGISTER</command>");
            sb.Append($"<arg>{argContent}</arg>");
            sb.Append("</brom>");
            
            var response = await SendXmlCommandAndReadAsync(sb.ToString(), ct);
            // TODO: 解析响应
            return null;
        }

        /// <summary>
        /// BROM 写入寄存器
        /// 参考: BromWriteRegCmdXML.cpp
        /// </summary>
        public async Task<bool> BromWriteRegisterAsync(
            ulong baseAddress,
            uint value,
            int bitWidth = 32,
            CancellationToken ct = default)
        {
            string argContent = $"<bit_width>{bitWidth}</bit_width>\n" +
                               $"<base_address>0x{baseAddress:X}</base_address>\n" +
                               $"<source_file>MEM://0x{value:X}:{bitWidth / 8}</source_file>";
            
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<brom><version>1.0</version>");
            sb.Append("<command>CMD:WRITE-REGISTER</command>");
            sb.Append($"<arg>{argContent}</arg>");
            sb.Append("</brom>");
            
            return await SendXmlCommandAsync(sb.ToString(), ct);
        }

        #endregion
        
        #region 辅助类型

        /// <summary>
        /// 重启类型 (来自 CFlashToolTypes.h)
        /// </summary>
        public enum RebootType
        {
            WarmReset = 0,  // IMMEDIATE
            ColdReset = 1,  // PMIC_COLD_RESET
            Disconnect = 2
        }

        /// <summary>
        /// 启动模式连接类型 (来自 CFlashToolTypes.h)
        /// </summary>
        public enum BootModeConnectType
        {
            USB = 0,
            UART = 1,
            WiFi = 2
        }

        /// <summary>
        /// DRAM 修复结果 (来自 CFlashToolTypes.h)
        /// </summary>
        public enum DramRepairResult
        {
            Success = 0,
            NoNeed = 1,
            Failed = 2
        }

        /// <summary>
        /// UFS 调试操作
        /// </summary>
        public enum UfsDebugAction
        {
            UpdateFirmware,
            Setting
        }

        /// <summary>
        /// 设备固件信息
        /// </summary>
        public class DevFwInfo
        {
            public byte[]? RandomData { get; set; }
            public byte[]? Hrid { get; set; }
            public byte[]? SocId { get; set; }
        }

        /// <summary>
        /// BROM 硬件信息 (来自 flash.dll 逆向)
        /// XML 格式:
        /// <brom_hw_info>
        ///   <version>1.0</version>
        ///   <chip_hw_code>0x%X</chip_hw_code>
        ///   <chip_hw_sub_code>0x%X</chip_hw_sub_code>
        ///   <chip_hw_version>0x%X</chip_hw_version>
        ///   <chip_sw_version>0x%X</chip_sw_version>
        ///   <me_id>%s</me_id>
        ///   <soc_id>%s</soc_id>
        ///   <security_boot_enabled>true/false</security_boot_enabled>
        ///   <security_da_auth_enabled>true/false</security_da_auth_enabled>
        ///   <security_sla_enabled>true/false</security_sla_enabled>
        /// </brom_hw_info>
        /// </summary>
        public class BromHwInfo
        {
            public string Version { get; set; } = "1.0";
            public uint ChipHwCode { get; set; }
            public uint ChipHwSubCode { get; set; }
            public uint ChipHwVersion { get; set; }
            public uint ChipSwVersion { get; set; }
            public string MeId { get; set; } = "";
            public string SocId { get; set; } = "";
            public bool SecurityBootEnabled { get; set; }
            public bool SecurityDaAuthEnabled { get; set; }
            public bool SecuritySlaEnabled { get; set; }

            /// <summary>
            /// 从 XML 响应解析
            /// </summary>
            public static BromHwInfo? Parse(string xml)
            {
                try
                {
                    var info = new BromHwInfo();
                    
                    // 解析各字段
                    info.ChipHwCode = ParseHexValue(xml, "chip_hw_code");
                    info.ChipHwSubCode = ParseHexValue(xml, "chip_hw_sub_code");
                    info.ChipHwVersion = ParseHexValue(xml, "chip_hw_version");
                    info.ChipSwVersion = ParseHexValue(xml, "chip_sw_version");
                    info.MeId = ParseStringValue(xml, "me_id");
                    info.SocId = ParseStringValue(xml, "soc_id");
                    info.SecurityBootEnabled = ParseBoolValue(xml, "security_boot_enabled");
                    info.SecurityDaAuthEnabled = ParseBoolValue(xml, "security_da_auth_enabled");
                    info.SecuritySlaEnabled = ParseBoolValue(xml, "security_sla_enabled");
                    
                    return info;
                }
                catch
                {
                    return null;
                }
            }

            private static uint ParseHexValue(string xml, string tag)
            {
                var match = System.Text.RegularExpressions.Regex.Match(xml, $"<{tag}>([^<]+)</{tag}>");
                if (match.Success)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        return Convert.ToUInt32(value, 16);
                    return uint.Parse(value);
                }
                return 0;
            }

            private static string ParseStringValue(string xml, string tag)
            {
                var match = System.Text.RegularExpressions.Regex.Match(xml, $"<{tag}>([^<]*)</{tag}>");
                return match.Success ? match.Groups[1].Value.Trim() : "";
            }

            private static bool ParseBoolValue(string xml, string tag)
            {
                var match = System.Text.RegularExpressions.Regex.Match(xml, $"<{tag}>([^<]+)</{tag}>");
                if (match.Success)
                {
                    string value = match.Groups[1].Value.Trim().ToLower();
                    return value == "true" || value == "1" || value == "yes";
                }
                return false;
            }
        }

        /// <summary>
        /// 分区信息
        /// </summary>
        public class PartitionInfo
        {
            public string Name { get; set; } = "";
            public ulong Start { get; set; }
            public ulong Size { get; set; }
            public string Type { get; set; } = "";
            public bool IsDownload { get; set; }
        }

        /// <summary>
        /// DA 信息
        /// </summary>
        public class DaInfo
        {
            public string Version { get; set; } = "";
            public string StorageType { get; set; } = "";
            public string Platform { get; set; } = "";
        }

        #endregion

        #region 低层通信

        /// <summary>
        /// 发送 XML 命令
        /// </summary>
        private async Task<bool> SendXmlCommandAsync(string xmlCmd, CancellationToken ct)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(xmlCmd);
                
                // 发送包头
                if (!XSend(data))
                    return false;

                // 读取响应
                var response = XRead();
                if (response == null)
                    return false;

                // 解析响应
                string responseStr = Encoding.UTF8.GetString(response);
                return ParseResponse(responseStr);
            }
            catch (Exception ex)
            {
                Log($"SendXmlCommand error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送 XML 命令并读取响应
        /// </summary>
        private async Task<string?> SendXmlCommandAndReadAsync(string xmlCmd, CancellationToken ct)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(xmlCmd);
                
                if (!XSend(data))
                    return null;

                var response = XRead();
                if (response == null)
                    return null;

                return Encoding.UTF8.GetString(response);
            }
            catch (Exception ex)
            {
                Log($"SendXmlCommandAndRead error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析 XML 响应
        /// </summary>
        private bool ParseResponse(string response)
        {
            try
            {
                // 查找状态
                int statusStart = response.IndexOf("<status>");
                if (statusStart == -1) return true; // 无状态字段认为成功

                int statusEnd = response.IndexOf("</status>", statusStart);
                if (statusEnd == -1) return false;

                string status = response.Substring(statusStart + 8, statusEnd - statusStart - 8);
                return status.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                       status.Equals("0", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 发送数据包
        /// </summary>
        private bool XSend(byte[] data)
        {
            try
            {
                // 包头: Magic (4) + DataType (4) + Length (4)
                byte[] header = new byte[12];
                BitConverter.GetBytes(MAGIC).CopyTo(header, 0);
                BitConverter.GetBytes((uint)1).CopyTo(header, 4); // DT_PROTOCOL_FLOW
                BitConverter.GetBytes((uint)data.Length).CopyTo(header, 8);

                lock (_lock)
                {
                    _port?.Write(header, 0, header.Length);
                    _port?.Write(data, 0, data.Length);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"XSend error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取数据包
        /// </summary>
        private byte[]? XRead()
        {
            try
            {
                lock (_lock)
                {
                    if (_port == null) return null;

                    // 读取包头
                    byte[] header = new byte[12];
                    int read = 0;
                    while (read < 12)
                    {
                        int r = _port.Read(header, read, 12 - read);
                        if (r == 0) return null;
                        read += r;
                    }

                    uint magic = BitConverter.ToUInt32(header, 0);
                    if (magic != MAGIC)
                    {
                        Log($"Invalid magic: 0x{magic:X}");
                        return null;
                    }

                    uint dataType = BitConverter.ToUInt32(header, 4);
                    uint length = BitConverter.ToUInt32(header, 8);

                    if (length == 0 || length > 0x1000000) // 16MB limit
                    {
                        Log($"Invalid length: {length}");
                        return null;
                    }

                    // 读取数据
                    byte[] data = new byte[length];
                    read = 0;
                    while (read < length)
                    {
                        int r = _port.Read(data, read, (int)length - read);
                        if (r == 0) return null;
                        read += r;
                    }

                    return data;
                }
            }
            catch (Exception ex)
            {
                Log($"XRead error: {ex.Message}");
                return null;
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[XML] {message}");
            System.Diagnostics.Debug.WriteLine($"[XML] {message}");
        }

        public void Dispose()
        {
            _port = null;
        }

        #endregion
    }
}
