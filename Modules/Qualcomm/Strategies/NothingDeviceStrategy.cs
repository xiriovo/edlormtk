using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Qualcomm.Authentication;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// Nothing Phone 设备策略 - 继承自 StandardDeviceStrategy
    /// 支持设备: Nothing Phone 1, 2, 2a
    /// </summary>
    public class NothingDeviceStrategy : StandardDeviceStrategy
    {
        public override string Name => "Nothing Phone";

        public override async Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default)
        {
            log("[Nothing] 开始 Nothing Phone 设备认证...");

            // 检测是否是 Nothing Phone 设备
            var supportedFunctions = client.SupportedFunctions;

            if (!NothingAuthStrategy.DetectNothingDevice(supportedFunctions))
            {
                log("[Nothing] 未检测到 Nothing Phone 设备特征，尝试标准认证...");
                return true; // 继续使用标准模式
            }

            // 使用 NothingAuthStrategy 进行认证
            var nothingAuth = new NothingAuthStrategy(log);
            bool result = await nothingAuth.AuthenticateAsync(client, programmerPath, ct);

            if (!result)
            {
                log("[Nothing] Nothing Phone 认证失败！");
            }

            return result;
        }
    }
}
