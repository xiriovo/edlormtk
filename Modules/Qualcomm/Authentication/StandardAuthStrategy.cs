using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// 标准认证策略 (无需认证)
    /// </summary>
    public class StandardAuthStrategy : IAuthStrategy
    {
        public string Name => "Standard (No Auth)";

        public Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default)
        {
            // 标准设备无需额外验证，直接返回成功
            return Task.FromResult(true);
        }
    }
}
