using System;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.Qualcomm.Authentication
{
    /// <summary>
    /// 设备认证策略接口
    /// </summary>
    public interface IAuthStrategy
    {
        /// <summary>
        /// 策略名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 执行认证
        /// </summary>
        /// <param name="client">Firehose 客户端</param>
        /// <param name="programmerPath">Loader 路径</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>认证是否成功</returns>
        Task<bool> AuthenticateAsync(FirehoseClient client, string programmerPath, CancellationToken ct = default);
    }
}
