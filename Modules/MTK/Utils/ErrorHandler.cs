using System.Collections.Generic;

namespace tools.Modules.MTK.Utils
{
    /// <summary>
    /// MTK 错误码处理器
    /// 来源: mtkclient/Library/error.py
    /// </summary>
    public static class ErrorHandler
    {
        /// <summary>
        /// 错误码字典
        /// </summary>
        public static readonly Dictionary<uint, string> ErrorCodes = new()
        {
            // OK
            { 0x0, "OK" },

            // ===================== COMMON (0x3E8 - 0x412) =====================
            { 0x3E8, "STOP" },
            { 0x3E9, "UNDEFINED_ERROR" },
            { 0x3EA, "INVALID_ARGUMENTS" },
            { 0x3EB, "INVALID_BBCHIP_TYPE" },
            { 0x3EC, "INVALID_EXT_CLOCK" },
            { 0x3ED, "INVALID_BMTSIZE" },
            { 0x3EE, "GET_DLL_VER_FAIL" },
            { 0x3EF, "INVALID_BUF" },
            { 0x3F0, "BUF_IS_NULL" },
            { 0x3F1, "BUF_LEN_IS_ZERO" },
            { 0x3F2, "BUF_SIZE_TOO_SMALL" },
            { 0x3F3, "NOT_ENOUGH_STORAGE_SPACE" },
            { 0x3F4, "NOT_ENOUGH_MEMORY" },
            { 0x3F5, "COM_PORT_OPEN_FAIL" },
            { 0x3F6, "COM_PORT_SET_TIMEOUT_FAIL" },
            { 0x3F7, "COM_PORT_SET_STATE_FAIL" },
            { 0x3F8, "COM_PORT_PURGE_FAIL" },
            { 0x3F9, "FILEPATH_NOT_SPECIFIED_YET" },
            { 0x3FA, "UNKNOWN_TARGET_BBCHIP" },
            { 0x3FB, "SKIP_BBCHIP_HW_VER_CHECK" },
            { 0x3FC, "UNSUPPORTED_VER_OF_BOOT_ROM" },
            { 0x3FD, "UNSUPPORTED_VER_OF_BOOTLOADER" },
            { 0x3FE, "UNSUPPORTED_VER_OF_DA" },
            { 0x3FF, "UNSUPPORTED_VER_OF_SEC_INFO" },
            { 0x400, "UNSUPPORTED_VER_OF_ROM_INFO" },
            { 0x401, "SEC_INFO_NOT_FOUND" },
            { 0x402, "ROM_INFO_NOT_FOUND" },
            { 0x403, "CUST_PARA_NOT_SUPPORTED" },
            { 0x404, "CUST_PARA_WRITE_LEN_INCONSISTENT" },
            { 0x405, "SEC_RO_NOT_SUPPORTED" },
            { 0x406, "SEC_RO_WRITE_LEN_INCONSISTENT" },
            { 0x407, "ADDR_N_LEN_NOT_32BITS_ALIGNMENT" },
            { 0x408, "UART_CHKSUM_ERROR" },
            { 0x409, "EMMC_FLASH_BOOT" },
            { 0x40A, "NOR_FLASH_BOOT" },
            { 0x40B, "NAND_FLASH_BOOT" },
            { 0x40C, "UNSUPPORTED_VER_OF_EMI_INFO" },
            { 0x40D, "PART_NO_VALID_TABLE" },
            { 0x40E, "PART_NO_SPACE_FOUND" },
            { 0x40F, "UNSUPPORTED_VER_OF_SEC_CFG" },
            { 0x410, "UNSUPPORTED_OPERATION" },
            { 0x411, "CHKSUM_ERROR" },
            { 0x412, "TIMEOUT" },

            // ===================== BROM (0x7D0 - 0x800) =====================
            { 0x7D0, "SET_META_REG_FAIL" },
            { 0x7D1, "SET_FLASHTOOL_REG_FAIL" },
            { 0x7D2, "SET_REMAP_REG_FAIL" },
            { 0x7D3, "SET_EMI_FAIL" },
            { 0x7D4, "DOWNLOAD_DA_FAIL" },
            { 0x7D5, "CMD_STARTCMD_FAIL" },
            { 0x7D6, "CMD_STARTCMD_TIMEOUT" },
            { 0x7D7, "CMD_JUMP_FAIL" },
            { 0x7D8, "CMD_WRITE16_MEM_FAIL" },
            { 0x7D9, "CMD_READ16_MEM_FAIL" },
            { 0x7DA, "CMD_WRITE16_REG_FAIL" },
            { 0x7DB, "CMD_READ16_REG_FAIL" },
            { 0x7DC, "CMD_CHKSUM16_MEM_FAIL" },
            { 0x7DD, "CMD_WRITE32_MEM_FAIL" },
            { 0x7DE, "CMD_READ32_MEM_FAIL" },
            { 0x7DF, "CMD_WRITE32_REG_FAIL" },
            { 0x7E0, "CMD_READ32_REG_FAIL" },
            { 0x7E1, "CMD_CHKSUM32_MEM_FAIL" },
            { 0x7E2, "JUMP_TO_META_MODE_FAIL" },
            { 0x7E3, "WR16_RD16_MEM_RESULT_DIFF" },
            { 0x7E4, "CHKSUM16_MEM_RESULT_DIFF" },
            { 0x7E5, "BBCHIP_HW_VER_INCORRECT" },
            { 0x7E6, "FAIL_TO_GET_BBCHIP_HW_VER" },
            { 0x7E7, "AUTOBAUD_FAIL" },
            { 0x7E8, "SPEEDUP_BAUDRATE_FAIL" },
            { 0x7E9, "LOCK_POWERKEY_FAIL" },
            { 0x7EB, "NOT_SUPPORT_MT6205B" },
            { 0x7EC, "EXCEED_MAX_DATA_BLOCKS" },
            { 0x7ED, "EXTERNAL_SRAM_DETECTION_FAIL" },
            { 0x7EE, "EXTERNAL_DRAM_DETECTION_FAIL" },
            { 0x7EF, "GET_FW_VER_FAIL" },
            { 0x7F0, "CONNECT_TO_BOOTLOADER_FAIL" },
            { 0x7F1, "CMD_SEND_DA_FAIL" },
            { 0x7F2, "CMD_SEND_DA_CHKSUM_DIFF" },
            { 0x7F3, "CMD_JUMP_DA_FAIL" },
            { 0x7F4, "CMD_JUMP_BL_FAIL" },
            { 0x7F5, "EFUSE_REG_NO_MATCH_WITH_TARGET" },
            { 0x7F6, "EFUSE_WRITE_TIMEOUT" },
            { 0x7F7, "EFUSE_DATA_PROCESS_ERROR" },
            { 0x7F8, "EFUSE_BLOW_ERROR" },
            { 0x7F9, "EFUSE_ALREADY_BROKEN" },
            { 0x7FA, "EFUSE_BLOW_PARTIAL" },
            { 0x7FB, "SEC_VER_FAIL" },
            { 0x7FC, "PL_SEC_VER_FAIL" },
            { 0x7FD, "SET_WATCHDOG_FAIL" },

            // ===================== DA (0xBB8+) =====================
            { 0xBB8, "DA_EMMC_FLASH_NOT_FOUND" },
            { 0xBB9, "DA_SDMMC_WRITE_FAILED" },
            { 0xBBA, "DA_SDMMC_READ_FAILED" },
            { 0xBBB, "DA_SDMMC_SECTOR_NOT_FOUND" },
            { 0xBBC, "DA_SDMMC_GPT_FORMAT_FAIL" },
            { 0xBBD, "DA_NAND_FLASH_NOT_FOUND" },
            { 0xBBE, "DA_NOR_FLASH_NOT_FOUND" },
            { 0xBBF, "DA_INSUFFICIENT_BUFFER" },
            { 0xBC0, "DA_NAND_FLASH_BUSY" },
            { 0xBC1, "DA_NAND_FLASH_ECC" },
            { 0xBC2, "DA_NAND_FLASH_ERR" },
            { 0xBC3, "DA_NAND_FLASH_PROGRAM_FAIL" },
            { 0xBC4, "DA_NAND_FLASH_ERASE_FAIL" },
            { 0xBC5, "DA_FLASH_TIMEOUT" },
            { 0xBC6, "DA_UNSUPPORTED_FLASH" },
            { 0xBC7, "DA_FAT_DISMOUNT_FAIL" },
            { 0xBC8, "DA_FAT_MOUNT_FAIL" },
            { 0xBC9, "DA_FAT_READ_FAIL" },
            { 0xBCA, "DA_FAT_WRITE_FAIL" },
            { 0xBCB, "DA_FAT_INCORRECT_HANDLE" },
            { 0xBCC, "DA_FAT_NOT_FOUND" },
            { 0xBCD, "DA_FAT_FILE_TOO_BIG" },
            { 0xBCE, "DA_FAT_DISK_FULL" },
            { 0xBCF, "DA_FAT_ROOT_DIR_FULL" },
            { 0xBD0, "DA_FAT_WRITE_PROTECTION" },
            { 0xBD1, "DA_GPT_OVERFLOW" },

            // ===================== Security (0x1D00+) =====================
            { 0x1D00, "DA_SEC_UNKNOWN" },
            { 0x1D01, "DA_SEC_UNLOCK_FAIL" },
            { 0x1D02, "DA_SEC_HASH_VERIFICATION_FAIL" },
            { 0x1D03, "DA_SEC_AUTH_FAIL" },
            { 0x1D04, "DA_SEC_SBC_ENABLED" },
            { 0x1D05, "DA_SEC_INVALID_MAGIC" },
            { 0x1D06, "DA_SEC_INVALID_VERSION" },
            { 0x1D07, "DA_SEC_IMG_LEN_ERROR" },
            { 0x1D08, "DA_SEC_IMAGE_HASH_ERROR" },
            { 0x1D09, "DA_SEC_REGION_ERROR" },
            { 0x1D0A, "DA_SEC_KEY_ERROR" },
            { 0x1D0B, "DA_SEC_SLA_CHALLENGE_FAIL" },
            { 0x1D0C, "DA_SEC_SLA_NO_AUTH_NEEDED" },
            { 0x1D0D, "DA_SEC_SLA_REQUIRED" },

            // ===================== XFlash (0x8xxx) =====================
            { 0x8001, "XFLASH_INIT_DRAM_FAIL" },
            { 0x8002, "XFLASH_BOOT_TO_FAIL" },
            { 0x8003, "XFLASH_READ_FAIL" },
            { 0x8004, "XFLASH_WRITE_FAIL" },
            { 0x8005, "XFLASH_ERASE_FAIL" },
            { 0x8006, "XFLASH_FORMAT_FAIL" },
            { 0x8007, "XFLASH_CHECKSUM_ERROR" },
            
            // ===================== CFlashTool (1000-2000) 来自 SP Flash Tool 源码 =====================
            { 1000, "CFT_SUCCESS" },
            { 1001, "CFT_DRAM_REPAIR_NO_NEED" },
            { 1002, "CFT_DRAM_REPAIR_FAILED" },
            { 1003, "CFT_RPMB_WRITTEN_BEFORE_DL" },
            { 1004, "CFT_CHECK_STORAGE_LIFE_CYCLE_EXHAUST" },
            { 1005, "CFT_CHECK_STORAGE_LIFE_CYCLE_WARN_CANCEL" },
            { 1006, "CFT_CHECK_STORAGE_LIFE_CYCLE_WARNING" },
            { 1007, "CFT_NOT_SUPPORTED_CONSOLE_MODE_CMD" },
            { 1008, "CFT_CMD_NOT_SUPPORT" },
            { 1009, "CFT_DEVICE_TRACKING_CANCEL" },
            { 1010, "CFT_DL_CHKSUM_FEEDBACK_ERR" },
            { 1011, "CFT_DL_CHKSUM_FEEDBACK_PARSER_ERR" },
            { 1012, "CFT_LOAD_CHKSUM_PARSER_ERR" },
            { 1013, "CFT_SCATTER_PARSER_ERR" },
            { 2000, "CFT_UNKNOWN_ERROR" },
            
            // ===================== SLA 认证 (0x7000+) =====================
            { 0x7000, "SLA_PASS" },
            { 0x7001, "SLA_NEED_CHALLENGE" },
            { 0x7002, "SLA_FAIL" },
            { 0x7003, "SLA_INVALID_KEY" },
            { 0x7004, "SLA_TIMEOUT" },
            { 0x7005, "SLA_NOT_SUPPORTED" },
            { 0x7006, "SLA_VERIFY_FAIL" },
            { 0x7017, "SLA_AUTH_PASS" }, // 认证通过
            
            // ===================== Target Config flags (来自 flash.dll) =====================
            { 0xC1, "TARGET_CONFIG_SECURE_BOOT" },
            { 0xC2, "TARGET_CONFIG_DAA" },
            { 0xC4, "TARGET_CONFIG_SLA" },
            
            // ===================== DA 详细错误码 (来自 FlashToolLib.v1.dll 逆向) =====================
            { 0xC000, "S_DA_INT_RAM_ERROR" },
            { 0xC001, "S_DA_EXT_RAM_ERROR" },
            { 0xC002, "S_DA_SETUP_DRAM_FAIL" },
            { 0xC003, "S_DA_SETUP_PLL_ERR" },
            { 0xC004, "S_DA_SETUP_EMI_PLL_ERR" },
            { 0xC005, "S_DA_DRAM_ABNORMAL_TYPE_SETTING" },
            { 0xC006, "S_DA_DRAMC_RANK0_CALIBRATION_FAILED" },
            { 0xC007, "S_DA_DRAMC_RANK1_CALIBRATION_FAILED" },
            { 0xC008, "S_DA_DRAM_NOT_SUPPORT" },
            { 0xC009, "S_DA_RAM_FLOARTING" },
            { 0xC00A, "S_DA_RAM_UNACCESSABLE" },
            { 0xC00B, "S_DA_RAM_ERROR" },
            { 0xC00C, "S_DA_DEVICE_NOT_FOUND" },
            { 0xC00D, "S_DA_NOR_UNSUPPORTED_DEV_ID" },
            { 0xC00E, "S_DA_NAND_UNSUPPORTED_DEV_ID" },
            { 0xC00F, "S_DA_NOR_FLASH_NOT_FOUND" },
            { 0xC010, "S_DA_NAND_FLASH_NOT_FOUND" },
            { 0xC011, "S_DA_SOC_CHECK_FAIL" },
            { 0xC012, "S_DA_NOR_PROGRAM_FAILED" },
            { 0xC013, "S_DA_NOR_ERASE_FAILED" },
            { 0xC014, "S_DA_NAND_PAGE_PROGRAM_FAILED" },
            { 0xC015, "S_DA_NAND_SPARE_PROGRAM_FAILED" },
            { 0xC016, "S_DA_NAND_HW_COPYBACK_FAILED" },
            { 0xC017, "S_DA_NAND_ERASE_FAILED" },
            { 0xC018, "S_DA_TIMEOUT" },
            { 0xC019, "S_DA_IN_PROGRESS" },
            { 0xC01A, "S_DA_UART_GET_DATA_TIMEOUT" },
            { 0xC01B, "S_DA_UART_DATA_CKSUM_ERROR" },
            { 0xC01C, "S_DA_UART_RX_BUF_FULL" },
            { 0xC01D, "S_DA_NAND_BAD_BLOCK" },
            { 0xC01E, "S_DA_NAND_ECC_1BIT_CORRECT" },
            { 0xC01F, "S_DA_NAND_ECC_2BITS_ERR" },
            { 0xC020, "S_DA_BLANK_FLASH" },
            { 0xC021, "S_DA_UNSUPPORTED_BBCHIP" },
            { 0xC022, "S_DA_FAT_NOT_EXIST" },
            { 0xC023, "S_DA_EXT_SRAM_NOT_FOUND" },
            { 0xC024, "S_DA_EXT_DRAM_NOT_FOUND" },
            
            // ===================== Security 详细错误码 (来自 FlashToolLib.v1.dll 逆向) =====================
            { 0xD000, "S_SECURITY_SLA_CHALLENGE_FAIL" },
            { 0xD001, "S_SECURITY_SLA_WRONG_AUTH_FILE" },
            { 0xD002, "S_SECURITY_SLA_INVALID_AUTH_FILE" },
            { 0xD003, "S_SECURITY_SLA_FAIL" },
            { 0xD004, "S_SECURITY_DAA_FAIL" },
            { 0xD005, "S_SECURITY_SBC_FAIL" },
            { 0xD006, "S_SECURITY_SEND_CERT_FAIL" },
            { 0xD007, "S_SECURITY_SEND_AUTH_FAIL" },
            { 0xD008, "S_SECURITY_GET_SEC_CONFIG_FAIL" },
            { 0xD009, "S_SECURITY_GET_ME_ID_FAIL" },
            { 0xD00A, "S_SECURITY_ROM_INFO_NOT_FOUND" },
            { 0xD00B, "S_SECURITY_SEC_KEY_ID_MISMATCH" },
            { 0xD00C, "S_SECURITY_SECURE_USB_DL_FAIL" },
            { 0xD00D, "S_SECURITY_SECURE_USB_DL_DISABLED" },
            { 0xD00E, "S_SECURITY_SECURE_USB_DL_ENABLED" },
            { 0xD00F, "S_SECURITY_BOOTLOADER_IMAGE_SIGNATURE_FAIL" },
            { 0xD010, "S_SECURITY_BOOTLOADER_IMAGE_NO_SIGNATURE" },
            { 0xD011, "S_SECURITY_DOWNLOAD_FILE_IS_CORRUPTED" },
            { 0xD012, "S_SECURITY_NOT_SUPPORT" },
            
            // ===================== BROM 连接状态 (来自 FlashToolLib.v1.dll) =====================
            { 1012, "STATUS_NOT_ENOUGH_MEMORY" },
            { 1013, "STATUS_COM_PORT_OPEN_FAIL" },
            { 2013, "STATUS_BROM_WRITE32_FAIL" },
            { 2014, "STATUS_BROM_READ32_FAIL" },
            
            // ===================== USB/连接错误 =====================
            { 0xE000, "STATUS_USB_SCAN_ERR" },
            { 0xE001, "STATUS_INVALID_HSESSION" },
            { 0xE002, "STATUS_INVALID_SESSION" },
            { 0xE003, "STATUS_INVALID_STAGE" },
            { 0xE004, "STATUS_PROTOCOL_ERR" },
            { 0xE005, "STATUS_PROTOCOL_BUFFER_OVERFLOW" },
            { 0xE006, "STATUS_INSUFFICIENT_BUFFER" },
            
            // ===================== 文件操作错误 =====================
            { 0xF000, "STATUS_FILE_NOT_FOUND" },
            { 0xF001, "STATUS_OPEN_FILE_ERR" },
            { 0xF002, "STATUS_WRITE_FILE_ERR" },
            { 0xF003, "STATUS_READ_FILE_ERR" },
            { 0xF004, "STATUS_CREATE_FILE_ERR" },
            { 0xF005, "STATUS_XML_FILE_OP_ERR" },
            
            // ===================== 分散加载/GPT/PMT 错误 =====================
            { 0xF100, "STATUS_SCATTER_FILE_INVALID" },
            { 0xF101, "STATUS_SCATTER_FILE_NOT_FOUND" },
            { 0xF102, "STATUS_INVALID_GPT" },
            { 0xF103, "STATUS_INVALID_PMT" },
            { 0xF104, "STATUS_LAYOUT_CHANGED" },
            { 0xF105, "STATUS_PARTITION_TBL_NOT_EXIST" },
            { 0xF106, "STATUS_PARTITON_NOT_FOUND" },
            
            // ===================== OTP 错误 =====================
            { 0xF200, "STATUS_OTP_LOCKED" },
            { 0xF201, "STATUS_OTP_UNLOCKED" },
            { 0xF202, "STATUS_OTP_LOCKED_TYPE_PERMANENT" },
            { 0xF203, "STATUS_OTP_LOCKED_TYPE_TEMPORARY" },
            { 0xF204, "STATUS_OTP_LOCKED_TYPE_DISABLE" },
        };

        /// <summary>
        /// 获取错误描述
        /// </summary>
        public static string GetErrorMessage(uint errorCode)
        {
            if (ErrorCodes.TryGetValue(errorCode, out var message))
            {
                return message;
            }
            return $"UNKNOWN_ERROR (0x{errorCode:X})";
        }

        /// <summary>
        /// 检查是否为错误状态
        /// </summary>
        public static bool IsError(uint status)
        {
            return status != 0 && status != 0x96; // 0x96 = continue
        }

        /// <summary>
        /// 检查是否需要 SLA 认证
        /// </summary>
        public static bool NeedsSlaAuth(uint status)
        {
            return status == 0x1D0D; // DA_SEC_SLA_REQUIRED
        }

        /// <summary>
        /// 检查是否认证通过
        /// </summary>
        public static bool IsAuthPassed(uint status)
        {
            return status == 0x1D0C || status == 0x7017; // NO_AUTH_NEEDED or SLA_PASS
        }
    }
}
