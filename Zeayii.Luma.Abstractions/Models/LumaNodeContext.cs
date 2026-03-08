using AngleSharp.Dom;
using System.Net;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点上下文</b>
/// <para>
/// 供节点读取运行身份、资源对象与取消令牌。
/// </para>
/// </summary>
public sealed class LumaNodeContext
{
    /// <summary>
    /// 节点资源集合（私有持有）。
    /// </summary>
    private readonly LumaNodeResources _resources;

    /// <summary>
    /// 初始化节点上下文。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="nodePath">节点路径。</param>
    /// <param name="depth">节点深度。</param>
    /// <param name="resources">节点资源集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public LumaNodeContext(Guid runId, string runName, string commandName, string nodePath, int depth, LumaNodeResources resources, CancellationToken cancellationToken)
    {
        RunId = runId;
        RunName = runName;
        CommandName = commandName;
        NodePath = string.IsNullOrWhiteSpace(nodePath) ? throw new ArgumentNullException(nameof(nodePath)) : nodePath;
        Depth = depth;
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// 运行标识。
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// 运行名称。
    /// </summary>
    public string RunName { get; }

    /// <summary>
    /// 命令名称。
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// 节点路径。
    /// </summary>
    public string NodePath { get; }

    /// <summary>
    /// 节点深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 解析 HTML 文本。
    /// </summary>
    /// <param name="html">HTML 文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文档对象。</returns>
    public ValueTask<IDocument> ParseHtmlAsync(string html, CancellationToken cancellationToken)
    {
        return _resources.HtmlParser.ParseAsync(html, cancellationToken);
    }

    /// <summary>
    /// 写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookie">Cookie 对象。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public ValueTask SetCookieAsync(Uri uri, Cookie cookie, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.SetCookieAsync(uri, cookie, routeKind, cancellationToken);

    /// <summary>
    /// 批量写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookies">Cookie 集合。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public ValueTask SetCookiesAsync(Uri uri, IEnumerable<Cookie> cookies, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.SetCookiesAsync(uri, cookies, routeKind, cancellationToken);

    /// <summary>
    /// 判断 Cookie 是否存在。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    public ValueTask<bool> ContainsCookieAsync(Uri uri, string name, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.ContainsCookieAsync(uri, name, routeKind, cancellationToken);

    /// <summary>
    /// 获取指定名称 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的 Cookie，未命中返回 null。</returns>
    public ValueTask<Cookie?> GetCookieAsync(Uri uri, string name, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.GetCookieAsync(uri, name, routeKind, cancellationToken);

    /// <summary>
    /// 获取地址下可见的 Cookie 快照。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Cookie 快照集合。</returns>
    public ValueTask<IReadOnlyList<Cookie>> GetCookiesAsync(Uri uri, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.GetCookiesAsync(uri, routeKind, cancellationToken);

    /// <summary>
    /// 移除指定 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public ValueTask RemoveCookieAsync(Uri uri, string name, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.RemoveCookieAsync(uri, name, routeKind, cancellationToken);

    /// <summary>
    /// 清空地址下 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="routeKind">路由类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public ValueTask ClearCookiesAsync(Uri uri, LumaRouteKind routeKind = LumaRouteKind.Direct, CancellationToken cancellationToken = default)
        => _resources.ClearCookiesAsync(uri, routeKind, cancellationToken);
}
