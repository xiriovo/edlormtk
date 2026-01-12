using System;
using System.Collections.Generic;
using System.Text;

namespace tools.Utils
{
    /// <summary>
    /// 日志颜色定义
    /// </summary>
    public static class LogColors
    {
        // 状态颜色
        public const string Success = "#10B981";      // 绿色 - 成功/Ok
        public const string Error = "#EF4444";        // 红色 - 错误
        public const string Warning = "#F59E0B";      // 橙色 - 警告
        public const string Info = "#3B82F6";         // 蓝色 - 信息/进行中
        public const string Debug = "#888888";        // 灰色 - 调试/次要

        // 模块颜色
        public const string Header = "#06B6D4";       // 青色 - 标题/横幅
        public const string Section = "#8B5CF6";      // 紫色 - 区块标题
        public const string Label = "#A78BFA";        // 浅紫 - 标签
        public const string Value = "#E5E5E5";        // 白灰 - 值
        public const string Highlight = "#FBBF24";    // 黄色 - 高亮

        // 协议颜色
        public const string Brom = "#8B5CF6";         // 紫色 - BROM
        public const string XFlash = "#3B82F6";       // 蓝色 - XFlash
        public const string Sahara = "#06B6D4";       // 青色 - Sahara
        public const string Firehose = "#10B981";     // 绿色 - Firehose
        public const string Device = "#F59E0B";       // 橙色 - 设备

        // 安全相关
        public const string SecurityEnabled = "#EF4444";   // 红色 - 启用
        public const string SecurityDisabled = "#10B981";  // 绿色 - 禁用
        public const string SecurityWarning = "#F59E0B";   // 橙色 - 警告
    }

    /// <summary>
    /// 日志格式化项
    /// </summary>
    public class FormattedLogItem
    {
        public string Text { get; set; } = "";
        public string Color { get; set; } = LogColors.Debug;
    }

    /// <summary>
    /// 日志格式化器 - 提供结构化日志输出
    /// </summary>
    public class LogFormatter
    {
        private readonly Action<string, string> _logAction;

        public LogFormatter(Action<string, string> logAction)
        {
            _logAction = logAction;
        }

        #region 基础日志方法

        /// <summary>
        /// 输出普通日志
        /// </summary>
        public void Log(string message, string color = "#888888")
        {
            _logAction(message, color);
        }

        /// <summary>
        /// 输出成功日志
        /// </summary>
        public void Success(string message) => _logAction(message, LogColors.Success);

        /// <summary>
        /// 输出错误日志
        /// </summary>
        public void Error(string message) => _logAction(message, LogColors.Error);

        /// <summary>
        /// 输出警告日志
        /// </summary>
        public void Warning(string message) => _logAction(message, LogColors.Warning);

        /// <summary>
        /// 输出信息日志
        /// </summary>
        public void Info(string message) => _logAction(message, LogColors.Info);

        /// <summary>
        /// 输出调试日志
        /// </summary>
        public void Debug(string message) => _logAction(message, LogColors.Debug);

        #endregion

        #region 格式化日志方法

        /// <summary>
        /// 输出标题/横幅
        /// </summary>
        public void Header(string title, string? version = null, string? author = null)
        {
            _logAction($"═══════════════════════════════════════════════════════════", LogColors.Header);
            _logAction($"  {title}", LogColors.Header);
            if (!string.IsNullOrEmpty(version))
                _logAction($"  版本: {version}", LogColors.Debug);
            if (!string.IsNullOrEmpty(author))
                _logAction($"  作者: {author}", LogColors.Debug);
            _logAction($"═══════════════════════════════════════════════════════════", LogColors.Header);
        }

        /// <summary>
        /// 输出区块标题
        /// </summary>
        public void Section(string title)
        {
            _logAction($"─────────────────────────────────────────", LogColors.Section);
            _logAction($" {title}", LogColors.Section);
            _logAction($"─────────────────────────────────────────", LogColors.Section);
        }

        /// <summary>
        /// 输出子区块标题
        /// </summary>
        public void SubSection(string title)
        {
            _logAction($" ▸ {title}", LogColors.Highlight);
        }

        /// <summary>
        /// 输出操作状态 (带 :Ok/:Failed)
        /// </summary>
        public void Status(string operation, bool success, string? detail = null)
        {
            string status = success ? "Ok" : "Failed";
            string color = success ? LogColors.Success : LogColors.Error;
            string message = string.IsNullOrEmpty(detail) 
                ? $"{operation} :{status}"
                : $"{operation} :{status} ({detail})";
            _logAction(message, color);
        }

        /// <summary>
        /// 输出键值对 (标签: 值)
        /// </summary>
        public void KeyValue(string key, string value, string? keyColor = null, string? valueColor = null)
        {
            // 对齐处理
            string paddedKey = key.PadRight(20);
            _logAction($" • {paddedKey}: {value}", valueColor ?? LogColors.Value);
        }

        /// <summary>
        /// 输出键值对 (带图标)
        /// </summary>
        public void KeyValueIcon(string key, string value, string icon = "•")
        {
            string paddedKey = key.PadRight(18);
            _logAction($" {icon} {paddedKey}: {value}", LogColors.Value);
        }

        /// <summary>
        /// 输出布尔状态
        /// </summary>
        public void BoolStatus(string label, bool value, bool successWhenTrue = false)
        {
            string valueStr = value ? "True" : "False";
            string color = successWhenTrue 
                ? (value ? LogColors.Success : LogColors.Error)
                : (value ? LogColors.Error : LogColors.Success);
            string paddedLabel = label.PadRight(20);
            _logAction($" • {paddedLabel}: {valueStr}", color);
        }

        /// <summary>
        /// 输出安全状态
        /// </summary>
        public void SecurityStatus(string label, bool enabled)
        {
            string valueStr = enabled ? "True (已启用)" : "False (已禁用)";
            string color = enabled ? LogColors.SecurityEnabled : LogColors.SecurityDisabled;
            string paddedLabel = label.PadRight(35);
            _logAction($"{paddedLabel}:{valueStr}", color);
        }

        /// <summary>
        /// 输出十六进制值
        /// </summary>
        public void HexValue(string label, uint value, int width = 4)
        {
            string paddedLabel = label.PadRight(20);
            _logAction($" • {paddedLabel}: 0x{value.ToString($"X{width}")}", LogColors.Value);
        }

        /// <summary>
        /// 输出十六进制字节数组
        /// </summary>
        public void HexBytes(string label, byte[] data, int maxLength = 32)
        {
            string hex = BitConverter.ToString(data).Replace("-", "");
            if (hex.Length > maxLength * 2)
                hex = hex.Substring(0, maxLength * 2) + "...";
            string paddedLabel = label.PadRight(10);
            _logAction($"{paddedLabel}:0x{hex}", LogColors.Value);
        }

        /// <summary>
        /// 输出进度
        /// </summary>
        public void Progress(string operation, int current, int total)
        {
            int percent = total > 0 ? (current * 100 / total) : 0;
            _logAction($"{operation}: {percent}% ({current}/{total})", LogColors.Info);
        }

        /// <summary>
        /// 输出等待状态
        /// </summary>
        public void Waiting(string message, string? timeout = null)
        {
            string timeoutStr = string.IsNullOrEmpty(timeout) ? "" : $" (Timeout {timeout})";
            _logAction($"{message}{timeoutStr}", LogColors.Info);
        }

        #endregion

        #region 设备信息格式化

        /// <summary>
        /// 输出 MTK 设备信息
        /// </summary>
        public void MtkDeviceInfo(
            ushort hwCode,
            ushort hwSubCode,
            ushort hwVersion,
            ushort swVersion,
            byte[]? meId = null,
            byte[]? socId = null)
        {
            SubSection("读取设备信息");
            
            if (meId != null && meId.Length > 0)
                HexBytes("ME_ID", meId);
            
            if (socId != null && socId.Length > 0)
                HexBytes("SOCID", socId);
            
            _logAction($"Hardware Sub Code :0x{hwSubCode:X4}", LogColors.Value);
            _logAction($"Hardware Code :0x{hwCode:X4}", LogColors.Value);
            _logAction($"Hardware Version :0x{hwVersion:X4}", LogColors.Value);
            _logAction($"Software Version :0x{swVersion:X4}", LogColors.Value);
        }

        /// <summary>
        /// 输出 MTK 安全配置
        /// </summary>
        public void MtkSecurityConfig(
            bool sbcEnabled,
            bool slaEnabled,
            bool daaEnabled,
            bool swJtagEnabled,
            bool eppEnabled,
            bool rootCertRequired,
            bool memReadAuth,
            bool memWriteAuth,
            bool cmdC8Blocked)
        {
            SecurityStatus("Is Secure boot", sbcEnabled);
            SecurityStatus("Serial Link authorization Protect", slaEnabled);
            SecurityStatus("download agent authorization Protect", daaEnabled);
            SecurityStatus("SWJTAG enabled", swJtagEnabled);
            SecurityStatus("EPP_PARAM at 0x600 after EMMC_BOOT/SDMMC_BOOT", eppEnabled);
            SecurityStatus("Root cert required", rootCertRequired);
            SecurityStatus("Mem read auth", memReadAuth);
            SecurityStatus("Mem write auth", memWriteAuth);
            SecurityStatus("Cmd 0xC8 blocked", cmdC8Blocked);
        }

        /// <summary>
        /// 输出 Android 设备信息
        /// </summary>
        public void AndroidDeviceInfo(
            string brand,
            string model,
            string device,
            string product,
            string serialNumber,
            string androidVersion,
            string chipset,
            string sdkVersion,
            string buildId,
            string securityPatch)
        {
            SubSection("Android Informations");
            KeyValueIcon("Brand", brand);
            KeyValueIcon("Model", model);
            KeyValueIcon("Devices", device);
            KeyValueIcon("Product", product);
            KeyValueIcon("Device SN", serialNumber);
            KeyValueIcon("Android Version", androidVersion);
            KeyValueIcon("Chipset", chipset);
            KeyValueIcon("SDK Version", sdkVersion);
            KeyValueIcon("Build ID", buildId);
            KeyValueIcon("Security", securityPatch);
        }

        /// <summary>
        /// 输出高通设备信息
        /// </summary>
        public void QualcommDeviceInfo(
            string chipSerial,
            string hwId,
            string chip,
            string oemPkHash,
            string secBoot)
        {
            SubSection("Qualcomm Device Info");
            _logAction($"- Chip Serial Number : {chipSerial}", LogColors.Value);
            _logAction($"- CHIP : {chip}", LogColors.Value);
            _logAction($"- HW_ID : {hwId}", LogColors.Value);
            _logAction($"- OEM PKHASH : {oemPkHash}", LogColors.Value);
            _logAction($"- SecBoot : {secBoot}", LogColors.Value);
        }

        #endregion

        #region 操作流程日志

        /// <summary>
        /// 开始操作
        /// </summary>
        public void BeginOperation(string operation)
        {
            _logAction($"▶ {operation}...", LogColors.Info);
        }

        /// <summary>
        /// 完成操作
        /// </summary>
        public void EndOperation(string operation, bool success)
        {
            string icon = success ? "✓" : "✗";
            string status = success ? "完成" : "失败";
            string color = success ? LogColors.Success : LogColors.Error;
            _logAction($"{icon} {operation}: {status}", color);
        }

        /// <summary>
        /// 输出步骤
        /// </summary>
        public void Step(int stepNumber, string description, bool? success = null)
        {
            string statusStr = "";
            string color = LogColors.Info;
            
            if (success.HasValue)
            {
                statusStr = success.Value ? " : Ok" : " : Failed";
                color = success.Value ? LogColors.Success : LogColors.Error;
            }
            
            _logAction($"[{stepNumber}] {description}{statusStr}", color);
        }

        /// <summary>
        /// 输出分隔线
        /// </summary>
        public void Separator(char character = '─', int length = 50)
        {
            _logAction(new string(character, length), LogColors.Debug);
        }

        /// <summary>
        /// 输出空行
        /// </summary>
        public void EmptyLine()
        {
            _logAction("", LogColors.Debug);
        }

        #endregion
    }

    /// <summary>
    /// 日志格式化扩展方法
    /// </summary>
    public static class LogFormatterExtensions
    {
        /// <summary>
        /// 格式化大小
        /// </summary>
        public static string FormatSize(this long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// 格式化时间
        /// </summary>
        public static string FormatDuration(this TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
            return $"{duration.Seconds}.{duration.Milliseconds:D3}s";
        }
    }
}
