namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>爬虫契约</b>
/// <para>
/// 仅负责提供根节点作为运行入口。
/// </para>
/// </summary>
public interface ISpider
{
    /// <summary>
    /// 创建根节点。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>根节点。</returns>
    ValueTask<LumaNode> CreateRootAsync(CancellationToken cancellationToken);
}
