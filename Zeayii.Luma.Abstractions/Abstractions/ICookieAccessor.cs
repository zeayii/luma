using System.Net;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>Cookie 访问器契约</b>
/// <para>
/// 由引擎提供实现，向节点暴露最小且明确的 Cookie 能力。
/// </para>
/// </summary>
public interface ICookieAccessor
{
    /// <summary>
    /// 写入单个 Cookie。
    /// </summary>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cookie">Cookie 对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    ValueTask SetCookieAsync(LumaRouteKind routeKind, Cookie cookie, CancellationToken cancellationToken);

    /// <summary>
    /// 获取地址下可见 Cookie 快照。
    /// </summary>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="domain">Cookie 域名。</param>
    /// <param name="path">Cookie 路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Cookie 快照。</returns>
    ValueTask<IReadOnlyList<Cookie>> GetCookiesAsync(LumaRouteKind routeKind, string domain, string path, CancellationToken cancellationToken);

    /// <summary>
    /// 清空地址下 Cookie。
    /// </summary>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="domain">Cookie 域名。</param>
    /// <param name="path">Cookie 路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    ValueTask ClearCookiesAsync(LumaRouteKind routeKind, string domain, string path, CancellationToken cancellationToken);
}
