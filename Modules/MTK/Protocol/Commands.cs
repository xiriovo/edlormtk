using System;

namespace tools.Modules.MTK.Protocol
{
    /// <summary>
    /// MTK Preloader 命令定义
    /// </summary>
    public static class PreloaderCmd
    {
        // ===================== 基础命令 =====================
        public const byte GET_HW_CODE = 0xFD;        // 获取硬件代码
        public const byte GET_HW_SW_VER = 0xFC;      // 获取硬件软件版本
        public const byte GET_BL_VER = 0xFE;         // 获取 Bootloader 版本
        public const byte GET_VERSION = 0xFF;        // 获取 BROM 版本
        public const byte GET_TARGET_CONFIG = 0xD8;  // 获取目标配置
        public const byte GET_PL_CAP = 0xFB;         // 获取 Preloader 能力

        // ===================== 内存操作 =====================
        public const byte READ16 = 0xD0;             // 读 16 位
        public const byte READ32 = 0xD1;             // 读 32 位
        public const byte WRITE16 = 0xD2;            // 写 16 位
        public const byte WRITE16_NO_ECHO = 0xD3;    // 写 16 位无回显
        public const byte WRITE32 = 0xD4;            // 写 32 位

        // ===================== DA 操作 =====================
        public const byte SEND_DA = 0xD7;            // 发送 DA
        public const byte JUMP_DA = 0xD5;            // 跳转到 DA
        public const byte JUMP_DA64 = 0xDE;          // 跳转到 DA (64位)
        public const byte JUMP_BL = 0xD6;            // 跳转到 Bootloader
        public const byte SEND_ENV_PREPARE = 0xD9;   // 发送环境准备

        // ===================== 认证命令 =====================
        public const byte SEND_CERT = 0xE0;          // 发送证书
        public const byte GET_ME_ID = 0xE1;          // 获取 ME ID
        public const byte SEND_AUTH = 0xE2;          // 发送认证
        public const byte SLA = 0xE3;                // SLA 认证
        public const byte GET_SOC_ID = 0xE7;         // 获取 SOC ID

        // ===================== 旧版 SLA =====================
        public const byte OLD_SLA_SEND_AUTH = 0xC1;  // 旧版 SLA 发送认证
        public const byte OLD_SLA_GET_RN = 0xC2;     // 旧版 SLA 获取随机数
        public const byte OLD_SLA_VERIFY_RN = 0xC3;  // 旧版 SLA 验证随机数

        // ===================== UART 命令 =====================
        public const byte UART1_LOG_EN = 0xDB;       // UART1 日志使能
        public const byte UART1_SET_BAUDRATE = 0xDC; // UART1 设置波特率
        public const byte BROM_DEBUGLOG = 0xDD;      // BROM 调试日志
        public const byte GET_BROM_LOG_NEW = 0xDF;   // 获取新版 BROM 日志

        // ===================== I2C 命令 =====================
        public const byte I2C_INIT = 0xB0;
        public const byte I2C_DEINIT = 0xB1;
        public const byte I2C_WRITE8 = 0xB2;
        public const byte I2C_READ8 = 0xB3;
        public const byte I2C_SET_SPEED = 0xB4;
        public const byte I2C_INIT_EX = 0xB6;
        public const byte I2C_DEINIT_EX = 0xB7;
        public const byte I2C_WRITE8_EX = 0xB8;
        public const byte I2C_READ8_EX = 0xB9;
        public const byte I2C_SET_SPEED_EX = 0xBA;
        public const byte GET_MAUI_FW_VER = 0xBF;

        // ===================== 电源命令 =====================
        public const byte PWR_INIT = 0xC4;
        public const byte PWR_DEINIT = 0xC5;
        public const byte PWR_READ16 = 0xC6;
        public const byte PWR_WRITE16 = 0xC7;
        public const byte CMD_C8 = 0xC8;             // Cache 控制

        // ===================== 其他命令 =====================
        public const byte CHECK_USB_CMD = 0x72;
        public const byte STAY_STILL = 0x80;
        public const byte SEND_PARTITION_DATA = 0x70;
        public const byte JUMP_TO_PARTITION = 0x71;
        public const byte ZEROIZATION = 0xF0;
        public const byte BROM_REGISTER_ACCESS = 0xDA;

        // ===================== 响应码 =====================
        public const byte ACK = 0x5A;
        public const byte NACK = 0xA5;
        public const byte CONF = 0x69;
        public const byte STOP = 0x96;
    }

    /// <summary>
    /// XFlash DA 命令定义
    /// </summary>
    public static class XFlashCmd
    {
        public const uint MAGIC = 0xFEEEEEEF;

        // ===================== 设备信息命令 =====================
        public const uint DEVICE_CTRL = 0x00040000;
        public const uint GET_CHIP_ID = 0x00010002;
        public const uint GET_RAM_INFO = 0x00010003;
        public const uint GET_EMMC_INFO = 0x00010004;
        public const uint GET_NAND_INFO = 0x00010005;
        public const uint GET_NOR_INFO = 0x00010006;
        public const uint GET_UFS_INFO = 0x00010007;
        public const uint GET_RPMB_STATUS = 0x00010008;
        public const uint GET_EXPIRE_DATE = 0x00010009;
        public const uint GET_RANDOM_ID = 0x0001000A;
        public const uint GET_DA_VERSION = 0x0001000B;
        public const uint GET_PACKET_LENGTH = 0x0001000C;
        public const uint GET_USB_SPEED = 0x0001000D;
        public const uint GET_CONNECTION_AGENT = 0x0001000E;
        public const uint GET_DEV_FW_INFO = 0x0001000F;
        public const uint GET_PARTITION_TBL_CATA = 0x00010010;
        public const uint GET_LIFE_CYCLE_STATUS = 0x00010012;
        public const uint GET_SLA_STATUS = 0x00010017;

        // ===================== 设置命令 =====================
        public const uint SET_RESET_KEY = 0x00020001;
        public const uint SET_META_BOOT_MODE = 0x00020002;
        public const uint SET_CHECKSUM_LEVEL = 0x00020003;
        public const uint SET_BATTERY_OPT = 0x00020004;
        public const uint SET_HOST_INFO = 0x00020005;
        public const uint SET_REMOTE_SEC_POLICY = 0x00020006;
        public const uint SET_USB_SPEED = 0x0002000B;
        public const uint SET_BOOT_MODE = 0x0002000C;

        // ===================== 读写命令 =====================
        public const uint READ = 0x00030000;
        public const uint WRITE = 0x00030100;
        public const uint FORMAT = 0x00030200;

        // ===================== 执行命令 =====================
        public const uint INIT_EXT_RAM = 0x00060001;
        public const uint BOOT_TO = 0x00050001;
        public const uint SHUTDOWN = 0x00050002;
        public const uint REBOOT = 0x00050003;

        // ===================== 同步信号 =====================
        public const uint SYNC_SIGNAL = 0x434E5953; // "SYNC"
    }

    /// <summary>
    /// Legacy DA 命令定义
    /// </summary>
    public static class LegacyCmd
    {
        // ===================== 基础命令 =====================
        public const byte SYNC = 0xC0;
        public const byte ACK = 0x5A;
        public const byte NACK = 0xA5;
        public const byte CONT_CHAR = 0x69;

        // ===================== 读写命令 =====================
        public const byte READ_CMD = 0xD6;
        public const byte WRITE_CMD = 0xD5;
        public const byte FORMAT_CMD = 0xD4;
        public const byte ERASE_CMD = 0xD3;

        // ===================== 分区命令 =====================
        public const byte SDMMC_READ_PMT_CMD = 0xA5;
        public const byte SDMMC_WRITE_PMT_CMD = 0xA6;

        // ===================== Flash 命令 =====================
        public const byte READ_REG32_CMD = 0x7A;
        public const byte WRITE_REG32_CMD = 0x7B;
        public const byte PWR_WRITE16_CMD = 0xC7;
        public const byte PWR_READ16_CMD = 0xC6;

        // ===================== 设备信息 =====================
        public const byte GET_FAT_INFO_CMD = 0xF0;
        public const byte DA_READ_CMD = 0xDA;
        public const byte DA_WRITE_CMD = 0xD5;
        public const byte DA_FORMAT_CMD = 0xD4;

        // ===================== EMI 命令 =====================
        public const byte EMI_CONTAINER_CMD = 0xE2;
        public const byte EMI_PREPARE_CMD = 0xE3;

        // ===================== 控制命令 =====================
        public const byte FINISH_CMD = 0xD9;
        public const byte SPEED_CMD = 0xD1;
        public const byte MEM_CMD = 0xD2;
        public const byte FORMAT_FAT_CMD = 0xD6;
    }

    /// <summary>
    /// 数据类型
    /// </summary>
    public static class DataType
    {
        public const uint DT_PROTOCOL_FLOW = 1;
        public const uint DT_MESSAGE = 2;
    }

    /// <summary>
    /// 状态码
    /// </summary>
    public static class StatusCode
    {
        public const ushort OK = 0x0000;
        public const ushort SLA_REQUIRED = 0x1D0D;
        public const ushort NO_AUTH_NEEDED = 0x1D0C;
        public const ushort SLA_PASS = 0x7017;
        public const uint STATUS_CONTINUE = 0x40040004;
        public const uint STATUS_COMPLETE = 0x40040005;
        public const uint STATUS_ERROR = 0xC0010004;
        
        // 错误码
        public const ushort ERR_DA_SEC_UNLOCK_FAIL = 0x1D01;
        public const ushort ERR_HASH_VERIFICATION_FAIL = 0x1D02;
        public const ushort ERR_DA_SEC_AUTH_FAIL = 0x1D03;
        public const ushort ERR_DA_SBC_ENABLED = 0x1D04;
        public const ushort ERR_DA_SEC_INVALID_MAGIC = 0x1D05;
        public const ushort ERR_DA_SEC_INVALID_VERSION = 0x1D06;
        public const ushort ERR_DA_SEC_IMG_LEN_ERROR = 0x1D07;
        public const ushort ERR_DA_SEC_IMAGE_HASH_ERROR = 0x1D08;

        /// <summary>
        /// 获取错误码描述
        /// </summary>
        public static string GetDescription(ushort code)
        {
            return code switch
            {
                OK => "成功",
                SLA_REQUIRED => "需要 SLA 认证",
                NO_AUTH_NEEDED => "无需认证",
                SLA_PASS => "SLA 认证已通过",
                ERR_DA_SEC_UNLOCK_FAIL => "安全解锁失败",
                ERR_HASH_VERIFICATION_FAIL => "哈希验证失败",
                ERR_DA_SEC_AUTH_FAIL => "DA 安全认证失败",
                ERR_DA_SBC_ENABLED => "安全启动已启用，需要签名的 DA",
                ERR_DA_SEC_INVALID_MAGIC => "DA 魔数无效",
                ERR_DA_SEC_INVALID_VERSION => "DA 版本无效",
                ERR_DA_SEC_IMG_LEN_ERROR => "DA 镜像长度错误",
                ERR_DA_SEC_IMAGE_HASH_ERROR => "DA 镜像哈希错误",
                _ => $"未知错误: 0x{code:X4}"
            };
        }

        /// <summary>
        /// 获取状态描述 (32位)
        /// </summary>
        public static string GetDescription(uint status)
        {
            return status switch
            {
                STATUS_CONTINUE => "继续",
                STATUS_COMPLETE => "完成",
                STATUS_ERROR => "错误",
                _ when status <= 0xFFFF => GetDescription((ushort)status),
                _ => $"未知状态: 0x{status:X8}"
            };
        }

        /// <summary>
        /// 判断是否为错误状态
        /// </summary>
        public static bool IsError(ushort code)
        {
            return code >= 0x1D01 && code <= 0x1D08;
        }

        /// <summary>
        /// 判断是否为成功状态
        /// </summary>
        public static bool IsSuccess(ushort code)
        {
            return code == OK || code == SLA_PASS || code == NO_AUTH_NEEDED;
        }
    }

    /// <summary>
    /// 存储类型
    /// </summary>
    public static class DaStorage
    {
        public const uint EMMC = 0x1;
        public const uint SDMMC = 0x2;
        public const uint UFS = 0x30;
        public const uint NAND = 0x10;
        public const uint NAND_SLC = 0x11;
        public const uint NAND_MLC = 0x12;
        public const uint NAND_TLC = 0x13;
        public const uint NAND_AMLC = 0x14;
        public const uint NAND_SPI = 0x15;
        public const uint NOR = 0x20;
        public const uint NOR_SERIAL = 0x21;
        public const uint NOR_PARALLEL = 0x22;
    }

    /// <summary>
    /// eMMC 分区类型
    /// </summary>
    public static class EmmcPartitionType
    {
        public const uint BOOT1 = 1;
        public const uint BOOT2 = 2;
        public const uint RPMB = 3;
        public const uint GP1 = 4;
        public const uint GP2 = 5;
        public const uint GP3 = 6;
        public const uint GP4 = 7;
        public const uint USER = 8;
    }

    /// <summary>
    /// UFS 分区类型
    /// </summary>
    public static class UfsPartitionType
    {
        public const uint LU0 = 0;
        public const uint LU1 = 1;
        public const uint LU2 = 2;
        public const uint LU3 = 3;
        public const uint LU4 = 4;
        public const uint LU5 = 5;
        public const uint LU6 = 6;
        public const uint LU7 = 7;
        public const uint LU8 = 8;
    }

    /// <summary>
    /// DA 模式
    /// </summary>
    public enum DAMode
    {
        Unknown = 0,
        Legacy = 3,
        XFlash = 5,
        Xml = 6
    }

    /// <summary>
    /// USB 下载模式标志
    /// </summary>
    public static class UsbDlFlags
    {
        public const uint BIT_EN = 0x00000001;           // 下载位使能
        public const uint BROM = 0x00000002;             // 0:brom 1:bootloader
        public const uint TIMEOUT_MASK = 0x0000FFFC;     // 超时掩码
        public const uint TIMEOUT_MAX = 0x3FFF;          // 最大超时
        public const uint MAGIC = 0x444C0000;            // BROM 检查的魔数
    }

    /// <summary>
    /// Preloader 能力位
    /// </summary>
    [Flags]
    public enum PlCap
    {
        None = 0,
        XFlashSupport = 1 << 0,
        MeidSupport = 1 << 1,
        SocIdSupport = 1 << 2,
    }

    /// <summary>
    /// 目标配置位
    /// </summary>
    [Flags]
    public enum TargetConfigBits
    {
        None = 0,
        SBC = 1 << 0,           // Secure Boot Check
        SLA = 1 << 1,           // SLA 认证
        DAA = 1 << 2,           // DAA 认证
        SWJTAG = 1 << 3,        // SW JTAG
        EPP = 1 << 4,           // EPP 参数
        CERT = 1 << 5,          // 需要证书
        MEMREAD = 1 << 6,       // 内存读取需认证
        MEMWRITE = 1 << 7,      // 内存写入需认证
        CMDC8 = 1 << 8,         // C8 命令阻止
    }

    /// <summary>
    /// 校验和算法
    /// </summary>
    public enum ChecksumAlgorithm
    {
        Plain = 0,
        CRC16 = 1,
        Checksum16 = 2,
    }

    /// <summary>
    /// 关机模式
    /// </summary>
    public enum ShutdownMode
    {
        Normal = 0,           // 正常关机
        HomeScreen = 1,       // 重启到主屏幕
        FastBoot = 2,         // 重启到 Fastboot
        BootToFastboot = 2,   // 别名
        BootToBrom = 3,       // 重启到 BROM 模式
        BootToRecovery = 4,   // 重启到恢复模式
        BootToMeta = 5,       // 重启到 META 模式
        Charger = 6,          // 充电模式
        Exception = 7,        // 异常模式
    }
}
