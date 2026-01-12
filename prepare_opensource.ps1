# ============================================================================
# MultiFlash TOOL - Open Source Release Script
# MultiFlash TOOL - 开源发布脚本
# MultiFlash TOOL - オープンソースリリーススクリプト
# ============================================================================
# Usage / 使用方法 / 使い方:
#   .\prepare_opensource.ps1 -OutputDir "C:\opensource\multiflash-tool"
# ============================================================================
# GitHub: https://github.com/xiriovo/edlormtk
# Contact: QQ 1708298587 | Email: 1708298587@qq.com
# ============================================================================

param(
    [string]$OutputDir = ".\opensource_release"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  🚀 MultiFlash TOOL - Open Source Release" -ForegroundColor Cyan
Write-Host "     多平台安卓刷机工具 | 开源发布脚本" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# 创建输出目录
if (Test-Path $OutputDir) {
    Write-Host "[!] 删除已存在的输出目录..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null
Write-Host "[+] 创建输出目录: $OutputDir" -ForegroundColor Green

# 需要复制的文件/目录
$includeItems = @(
    "Modules",
    "Dialogs",
    "Utils",
    "App.xaml",
    "App.xaml.cs",
    "MainWindow.xaml",
    "MainWindow.xaml.cs",
    "AssemblyInfo.cs",
    "tools.csproj",
    "tools.slnx",
    ".gitignore",
    "LICENSE",
    "README.md",
    "CONTRIBUTING.md"
)

# 需要排除的文件/目录 (相对于 Modules)
$excludeFromModules = @(
    "Qualcomm\Services\OcdtService.cs"  # OCDT 不开源
)

# 复制文件
Write-Host ""
Write-Host "[*] 复制开源文件..." -ForegroundColor Cyan

foreach ($item in $includeItems) {
    $sourcePath = Join-Path $PSScriptRoot $item
    $destPath = Join-Path $OutputDir $item
    
    if (Test-Path $sourcePath) {
        if ((Get-Item $sourcePath).PSIsContainer) {
            # 目录
            Copy-Item -Path $sourcePath -Destination $destPath -Recurse -Force
            Write-Host "  [+] 复制目录: $item" -ForegroundColor Green
        } else {
            # 文件
            $destDir = Split-Path $destPath -Parent
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            Copy-Item -Path $sourcePath -Destination $destPath -Force
            Write-Host "  [+] 复制文件: $item" -ForegroundColor Green
        }
    } else {
        Write-Host "  [!] 跳过 (不存在): $item" -ForegroundColor Yellow
    }
}

# 删除不开源的文件
Write-Host ""
Write-Host "[*] 移除非开源文件..." -ForegroundColor Cyan

foreach ($exclude in $excludeFromModules) {
    $excludePath = Join-Path $OutputDir "Modules" $exclude
    if (Test-Path $excludePath) {
        Remove-Item -Path $excludePath -Force
        Write-Host "  [-] 移除: Modules\$exclude" -ForegroundColor Red
    }
}

# 创建 OCDT 占位符
Write-Host ""
Write-Host "[*] 创建 OCDT 占位符文件..." -ForegroundColor Cyan

$ocdtStubContent = @'
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Services
{
    /// <summary>
    /// OCDT 生成服务 - OPPO/OnePlus/Realme 配置分区
    /// 
    /// ⚠️ 此文件为占位符版本，实际实现未开源
    /// 如需完整功能，请联系开发者或使用其他工具
    /// </summary>
    public class OcdtService : IDisposable
    {
        private readonly FirehoseClient? _firehose;
        private readonly Action<string>? _log;
        private readonly Action<int>? _progress;
        private bool _disposed;

        public OcdtService(FirehoseClient firehose, Action<string>? log = null, Action<int>? progress = null)
        {
            _firehose = firehose;
            _log = log;
            _progress = progress;
        }

        public OcdtService()
        {
            _firehose = null;
            _log = null;
            _progress = null;
        }

        public void Dispose()
        {
            if (!_disposed) _disposed = true;
        }

        public static byte[] GeyixueEncrypt(byte[] data) => data ?? Array.Empty<byte>();
        public static byte[] GeyixueDecrypt(byte[] data) => data ?? Array.Empty<byte>();
        public static byte[] GenerateBasic(int projectId) => Array.Empty<byte>();
        public static byte[] GenerateFromBackup(byte[] originalOcdt, int? newProjectId = null, bool perfectClone = true) => Array.Empty<byte>();
        public static byte[] Clone(byte[] originalOcdt) => originalOcdt ?? Array.Empty<byte>();
        public static byte[] GenerateMtk8MB(int projectId, byte[]? osigBackup = null) => Array.Empty<byte>();

        public async Task<byte[]?> BackupOcdtAsync(List<PartitionInfo>? partitions, CancellationToken ct)
        {
            _log?.Invoke("[OCDT] ⚠️ OCDT 功能未开源");
            await Task.Delay(1, ct);
            return null;
        }

        public async Task<OcdtRepairResult> RepairOcdtAsync(List<PartitionInfo>? partitions, int? projectId = null, CancellationToken ct = default)
        {
            _log?.Invoke("[OCDT] ⚠️ OCDT 功能未开源");
            await Task.Delay(1, ct);
            return new OcdtRepairResult { Success = false, ErrorMessage = "OCDT 功能未开源" };
        }
    }

    public class OsigParseResult
    {
        public bool HasOsig { get; set; }
        public byte[]? DeviceId { get; set; }
        public byte[]? Md5Ascii { get; set; }
        public byte[]? SigPadding { get; set; }
        public byte[]? HiddenRegion { get; set; }
        public bool HasValidSignature { get; set; }
    }

    public class OcdtAnalysisResult
    {
        public bool IsValid { get; set; }
        public string? Error { get; set; }
        public bool HasTdco { get; set; }
        public int ProjectId { get; set; }
        public bool HasOsig { get; set; }
        public OsigParseResult? Osig { get; set; }
        public bool HasSignature { get; set; }
        public bool CanEnterSystem { get; set; }
        public bool CanEnterBootloader { get; set; }
    }

    public class OcdtRepairResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public static class OppoProjectDatabase
    {
        public static bool IsKnownProjectId(string projectId) => false;
        public static string? GetMarketName(string projectId) => null;
    }
}
'@

$ocdtStubPath = Join-Path $OutputDir "Modules\Qualcomm\Services\OcdtService.cs"
$ocdtStubContent | Out-File -FilePath $ocdtStubPath -Encoding utf8
Write-Host "  [+] 创建占位符: Modules\Qualcomm\Services\OcdtService.cs" -ForegroundColor Green

# 移除 bin 文件
Write-Host ""
Write-Host "[*] 清理二进制文件..." -ForegroundColor Cyan

$binPatterns = @("*.bin", "*.exe", "*.dll", "*.pdb", "*.7z", "*.zip")
foreach ($pattern in $binPatterns) {
    $files = Get-ChildItem -Path $OutputDir -Filter $pattern -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        Remove-Item -Path $file.FullName -Force
        Write-Host "  [-] 移除: $($file.FullName.Replace($OutputDir, ''))" -ForegroundColor Red
    }
}

# 统计
Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
$fileCount = (Get-ChildItem -Path $OutputDir -Recurse -File).Count
$dirCount = (Get-ChildItem -Path $OutputDir -Recurse -Directory).Count
Write-Host "[✓] 开源准备完成!" -ForegroundColor Green
Write-Host "    目录: $OutputDir" -ForegroundColor White
Write-Host "    文件数: $fileCount" -ForegroundColor White
Write-Host "    目录数: $dirCount" -ForegroundColor White
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "  1. cd $OutputDir" -ForegroundColor White
Write-Host "  2. git init" -ForegroundColor White
Write-Host "  3. git add ." -ForegroundColor White
Write-Host "  4. git commit -m 'Initial commit'" -ForegroundColor White
Write-Host "  5. git remote add origin https://github.com/xiriovo/edlormtk.git" -ForegroundColor White
Write-Host "  6. git push -u origin main" -ForegroundColor White
Write-Host ""
Write-Host "联系方式:" -ForegroundColor Cyan
Write-Host "  GitHub: https://github.com/xiriovo/edlormtk" -ForegroundColor White
Write-Host "  QQ: 1708298587" -ForegroundColor White
Write-Host "  Email: 1708298587@qq.com" -ForegroundColor White
