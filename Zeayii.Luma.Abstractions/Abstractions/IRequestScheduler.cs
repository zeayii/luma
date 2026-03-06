using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>请求调度器契约</b>
/// </summary>
public interface IRequestScheduler
{
    /// <summary>
    /// 请求入队。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask EnqueueAsync(LumaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 按调度顺序出队请求。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求；若已完成则返回 null。</returns>
    ValueTask<LumaRequest?> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 标记调度完成。
    /// </summary>
    void Complete();

    /// <summary>
    /// 当前排队请求数量。
    /// </summary>
    long Count { get; }
}

