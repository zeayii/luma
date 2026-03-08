namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>爬虫契约</b>
/// <para>
/// 仅负责提供根节点作为运行入口。
/// </para>
/// </summary>
/// <typeparam name="TState">实现层定义的运行状态类型。</typeparam>
public interface ISpider<TState>
{
    /// <summary>
    /// 创建运行状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行状态。</returns>
    ValueTask<TState> CreateStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 创建根节点。
    /// </summary>
    /// <param name="state">运行状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>根节点。</returns>
    ValueTask<LumaNode<TState>> CreateRootAsync(TState state, CancellationToken cancellationToken);
}
