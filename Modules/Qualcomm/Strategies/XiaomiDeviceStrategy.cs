using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Qualcomm.Authentication;

namespace tools.Modules.Qualcomm.Strategies
{
    /// <summary>
    /// 小米设备策略 - 使用 EDL 签名认证
    /// 支持设备: Redmi A1, Poco F1, Redmi 5/6/7 Pro, Redmi 7A/8/8A 系列, Y2, S2
    /// </summary>
    public class XiaomiDeviceStrategy : StandardDeviceStrategy
    {
        public override string Name => "Xiaomi EDL Auth";

        public override async Task<bool> AuthenticateAsync(
            FirehoseClient client,
            string programmerPath,
            Action<string> log,
            Func<string, string>? inputCallback = null,
            string? digestPath = null,
            string? signaturePath = null,
            CancellationToken ct = default)
        {
            log("[Xiaomi] 开始小米设备认证...");

            // 使用 XiaomiAuthStrategy 进行认证
            var xiaomiAuth = new XiaomiAuthStrategy(log, inputCallback);

            bool result = await xiaomiAuth.AuthenticateAsync(client, programmerPath, ct);

            if (!result)
            {
                log("[Xiaomi] EDL 签名认证失败");
                // 某些设备可能不需要完整认证，继续尝试
                return true;
            }

            log("[Xiaomi] ✓ 小米认证成功!");
            return true;
        }
    }
}
