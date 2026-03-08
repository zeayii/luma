using Zeayii.Luma.Abstractions.Abstractions;
using System.Net;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>Cookie 容器执行委托</b>
/// <para>
/// 由引擎实现，负责基于路由类型绑定真实会话 Cookie 容器并执行操作。
/// </para>
/// </summary>
/// <param name="routeKind">路由类型。</param>
/// <param name="action">Cookie 操作函数。</param>
/// <param name="cancellationToken">取消令牌。</param>
public delegate ValueTask CookieContainerExecutor(
    LumaRouteKind routeKind,
    Func<CookieContainer, CancellationToken, ValueTask> action,
    CancellationToken cancellationToken);

/// <summary>
/// <b>节点资源集合</b>
/// <para>
/// 由框架注入，供节点在解析阶段使用的通用能力对象。
/// </para>
/// </summary>
/// <param name="htmlParser">HTML 解析器。</param>
/// <param name="cookieExecutor">Cookie 容器执行委托。</param>
public sealed class LumaNodeResources(IHtmlParser htmlParser, CookieContainerExecutor cookieExecutor)
{
    /// <summary>
    /// HTML 解析器。
    /// </summary>
    public IHtmlParser HtmlParser { get; } = htmlParser ?? throw new ArgumentNullException(nameof(htmlParser));

    /// <summary>
    /// Cookie 容器执行委托。
    /// </summary>
    private CookieContainerExecutor CookieExecutor { get; } = cookieExecutor ?? throw new ArgumentNullException(nameof(cookieExecutor));

    /// <summary>
    /// 写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookie">Cookie 对象。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    internal ValueTask SetCookieAsync(Uri uri, Cookie cookie, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cookie);
        return CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                cookieContainer.Add(uri, cookie);
                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    /// <summary>
    /// 批量写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookies">Cookie 集合。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    internal ValueTask SetCookiesAsync(Uri uri, IEnumerable<Cookie> cookies, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cookies);
        return CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                foreach (var cookie in cookies)
                {
                    if (cookie is null)
                    {
                        continue;
                    }

                    cookieContainer.Add(uri, cookie);
                }

                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    /// <summary>
    /// 判断 Cookie 是否存在。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    internal async ValueTask<bool> ContainsCookieAsync(Uri uri, string name, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var exists = false;
        await CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                exists = cookieContainer
                    .GetCookies(uri)
                    .Cast<Cookie>()
                    .Any(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal));
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return exists;
    }

    /// <summary>
    /// 获取指定名称 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的 Cookie，未命中返回 null。</returns>
    internal async ValueTask<Cookie?> GetCookieAsync(Uri uri, string name, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Cookie? result = null;
        await CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                var cookie = cookieContainer
                    .GetCookies(uri)
                    .Cast<Cookie>()
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                result = cookie is null ? null : CloneCookie(cookie);
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 获取地址下可见的 Cookie 快照。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Cookie 快照集合。</returns>
    internal async ValueTask<IReadOnlyList<Cookie>> GetCookiesAsync(Uri uri, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        IReadOnlyList<Cookie> result = Array.Empty<Cookie>();
        await CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                result = cookieContainer
                    .GetCookies(uri)
                    .Cast<Cookie>()
                    .Select(CloneCookie)
                    .ToArray();
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 移除指定 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    internal ValueTask RemoveCookieAsync(Uri uri, string name, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                cookieContainer.Add(uri, new Cookie(name, string.Empty) { Expires = DateTime.UtcNow.AddYears(-1) });
                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    /// <summary>
    /// 清空地址下 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    internal ValueTask ClearCookiesAsync(Uri uri, LumaRouteKind routeKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return CookieExecutor(
            routeKind,
            (cookieContainer, _) =>
            {
                foreach (var cookie in cookieContainer.GetCookies(uri).Cast<Cookie>())
                {
                    cookieContainer.Add(uri, new Cookie(cookie.Name, string.Empty) { Expires = DateTime.UtcNow.AddYears(-1) });
                }

                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    /// <summary>
    /// 克隆 Cookie 对象。
    /// </summary>
    /// <param name="cookie">源 Cookie。</param>
    /// <returns>克隆结果。</returns>
    private static Cookie CloneCookie(Cookie cookie)
    {
        return new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
        {
            Expires = cookie.Expires,
            Secure = cookie.Secure,
            HttpOnly = cookie.HttpOnly,
            Comment = cookie.Comment,
            CommentUri = cookie.CommentUri,
            Discard = cookie.Discard,
            Expired = cookie.Expired,
            Port = cookie.Port,
            Version = cookie.Version
        };
    }
}
