using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>数据项持久化契约</b>
/// </summary>
public interface IItemSink
{
    /// <summary>
    /// 批量持久化数据项。
    /// </summary>
    /// <param name="items">待持久化数据项集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入顺序一一对应的持久化结果集合。</returns>
    ValueTask<IReadOnlyList<PersistResult>> StoreBatchAsync(IReadOnlyList<ItemEnvelope> items, CancellationToken cancellationToken);
}

