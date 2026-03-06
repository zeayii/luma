using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>节点停止策略契约</b>
/// </summary>
public interface INodeStopPolicy
{
    /// <summary>
    /// 评估当前节点是否应停止。
    /// </summary>
    /// <param name="context">节点上下文。</param>
    /// <param name="persistResult">最近一次持久化结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若应停止则返回 true。</returns>
    ValueTask<bool> ShouldStopAsync(LumaNodeContext context, PersistResult persistResult, CancellationToken cancellationToken);
}

