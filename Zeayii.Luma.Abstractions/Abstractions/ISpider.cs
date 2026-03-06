namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>爬虫契约</b>
/// <para>
/// 一个 spider 通常对应一个网站或一个站点族的抓取实现。
/// </para>
/// </summary>
public interface ISpider
{
    /// <summary>
    /// 创建根节点集合。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>根节点异步流。</returns>
    IAsyncEnumerable<LumaNode> CreateRootsAsync(CancellationToken cancellationToken);
}


