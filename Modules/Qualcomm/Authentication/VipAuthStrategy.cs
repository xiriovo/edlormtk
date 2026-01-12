using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// VIP 认证策略 (OPPO/Realme/OnePlus)
    /// 使用 Digest + Signature 文件进行认证
    /// </summary>
    public class VipAuthStrategy : IAuthStrategy
    {
        private readonly Action<string>? _log;
        private readonly string? _digestPath;
        private readonly string? _signaturePath;

        public string Name => "VIP (Digest/Signature)";

        public VipAuthStrategy(Action<string>? log = null, string? digestPath = null, string? signaturePath = null)
        {
            _log = log;
            _digestPath = digestPath;
            _signaturePath = signaturePath;
        }

        public async Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default)
        {
            _log?.Invoke("[VIP] 准备执行 VIP 签名验证...");

            string? digestPath = _digestPath;
            string? signaturePath = _signaturePath;

            // 自动查找认证文件
            if (string.IsNullOrEmpty(digestPath) || string.IsNullOrEmpty(signaturePath))
            {
                string? dir = Path.GetDirectoryName(programmerPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    if (string.IsNullOrEmpty(digestPath))
                    {
                        digestPath = FindAuthFile(dir, "digest");
                    }
                    if (string.IsNullOrEmpty(signaturePath))
                    {
                        signaturePath = FindAuthFile(dir, "signature");
                    }
                }
            }

            // 检查文件
            if (string.IsNullOrEmpty(digestPath) || !File.Exists(digestPath))
            {
                _log?.Invoke("[VIP] ⚠️ 未找到 Digest 文件");
                return true; // 允许继续尝试
            }

            if (string.IsNullOrEmpty(signaturePath) || !File.Exists(signaturePath))
            {
                _log?.Invoke("[VIP] ⚠️ 未找到 Signature 文件");
                return true; // 允许继续尝试
            }

            _log?.Invoke($"[VIP] Digest: {Path.GetFileName(digestPath)}");
            _log?.Invoke($"[VIP] Signature: {Path.GetFileName(signaturePath)}");

            // 执行 VIP 认证
            return await Task.Run(() => client.PerformVipAuth(digestPath, signaturePath), ct);
        }

        private string? FindAuthFile(string dir, string baseName)
        {
            string[] extensions = { ".bin", ".mbn", ".elf" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(dir, baseName + ext);
                if (File.Exists(path)) return path;
            }

            // 尝试大写
            foreach (var ext in extensions)
            {
                string path = Path.Combine(dir, baseName.ToUpper() + ext);
                if (File.Exists(path)) return path;
            }

            return null;
        }
    }
}
