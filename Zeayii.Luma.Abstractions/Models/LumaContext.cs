using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
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
public delegate ValueTask CookieContainerExecutor(LumaRouteKind routeKind, Func<CookieContainer, CancellationToken, ValueTask> action, CancellationToken cancellationToken);

/// <summary>
/// <b>Luma 运行上下文</b>
/// <para>
/// 由框架构造并贯穿节点生命周期，承载运行元信息、共享状态、日志与资源能力。
/// </para>
/// </summary>
/// <typeparam name="TState">实现层定义的运行状态类型。</typeparam>
public sealed class LumaContext<TState>
{
    /// <summary>
    /// HTML 解析器。
    /// </summary>
    private readonly IHtmlParser _htmlParser;

    /// <summary>
    /// Cookie 容器执行委托。
    /// </summary>
    private readonly CookieContainerExecutor _cookieExecutor;

    /// <summary>
    /// 初始化运行上下文。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="nodePath">节点路径。</param>
    /// <param name="depth">节点深度。</param>
    /// <param name="defaultRouteKind">节点默认路由类型。</param>
    /// <param name="state">运行状态。</param>
    /// <param name="htmlParser">HTML 解析器。</param>
    /// <param name="cookieExecutor">Cookie 容器执行委托。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public LumaContext(
        Guid runId,
        string runName,
        string commandName,
        string nodePath,
        int depth,
        LumaRouteKind defaultRouteKind,
        TState state,
        IHtmlParser htmlParser,
        CookieContainerExecutor cookieExecutor,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        RunId = runId;
        RunName = runName;
        CommandName = commandName;
        NodePath = string.IsNullOrWhiteSpace(nodePath) ? throw new ArgumentNullException(nameof(nodePath)) : nodePath;
        Depth = depth;
        DefaultRouteKind = defaultRouteKind;
        State = state;
        _htmlParser = htmlParser ?? throw new ArgumentNullException(nameof(htmlParser));
        _cookieExecutor = cookieExecutor ?? throw new ArgumentNullException(nameof(cookieExecutor));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// 节点默认路由类型。
    /// </summary>
    public LumaRouteKind DefaultRouteKind { get; }

    /// <summary>
    /// 实现层运行状态。
    /// </summary>
    public TState State { get; }

    /// <summary>
    /// 日志器。
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 解析 HTML 文本。
    /// </summary>
    /// <param name="html">HTML 文本。</param>
    /// <returns>文档对象。</returns>
    public ValueTask<IDocument> ParseHtmlAsync(string html)
    {
        return _htmlParser.ParseAsync(html, CancellationToken);
    }

    /// <summary>
    /// 写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookie">Cookie 对象。</param>
    /// <returns>异步任务。</returns>
    public ValueTask SetCookieAsync(Uri uri, Cookie cookie)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cookie);
        return _cookieExecutor(
            DefaultRouteKind,
            (cookieContainer, _) =>
            {
                cookieContainer.Add(uri, cookie);
                return ValueTask.CompletedTask;
            },
            CancellationToken);
    }

    /// <summary>
    /// 批量写入 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="cookies">Cookie 集合。</param>
    /// <returns>异步任务。</returns>
    public ValueTask SetCookiesAsync(Uri uri, IEnumerable<Cookie> cookies)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cookies);
        return _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            foreach (var cookie in cookies)
            {
                cookieContainer.Add(uri, cookie);
            }

            return ValueTask.CompletedTask;
        }, CancellationToken);
    }

    /// <summary>
    /// 按 Cookie 自带域信息批量导入 Cookie。
    /// </summary>
    /// <param name="cookies">Cookie 集合。</param>
    /// <returns>异步任务。</returns>
    public ValueTask ImportCookiesAsync(IEnumerable<Cookie> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        return _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            foreach (var cookie in cookies)
            {
                cookieContainer.Add(CloneCookie(cookie));
            }

            return ValueTask.CompletedTask;
        }, CancellationToken);
    }

    /// <summary>
    /// 判断 Cookie 是否存在。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <returns>存在返回 true。</returns>
    public async ValueTask<bool> ContainsCookieAsync(Uri uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var exists = false;
        await _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            exists = cookieContainer.GetCookies(uri).Any(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal));
            return ValueTask.CompletedTask;
        }, CancellationToken).ConfigureAwait(false);
        return exists;
    }

    /// <summary>
    /// 获取指定名称 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <returns>命中的 Cookie，未命中返回 null。</returns>
    public async ValueTask<Cookie?> GetCookieAsync(Uri uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Cookie? result = null;
        await _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            var cookie = cookieContainer.GetCookies(uri).FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            result = cookie is null ? null : CloneCookie(cookie);
            return ValueTask.CompletedTask;
        }, CancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 获取地址下可见的 Cookie 快照。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <returns>Cookie 快照集合。</returns>
    public async ValueTask<IReadOnlyList<Cookie>> GetCookiesAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        IReadOnlyList<Cookie> result = Array.Empty<Cookie>();
        await _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            result = cookieContainer.GetCookies(uri).Select(CloneCookie).ToArray();
            return ValueTask.CompletedTask;
        }, CancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 移除指定 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="name">Cookie 名称。</param>
    /// <returns>异步任务。</returns>
    public ValueTask RemoveCookieAsync(Uri uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            cookieContainer.Add(uri, new Cookie(name, string.Empty) { Expires = DateTime.UtcNow.AddYears(-1) });
            return ValueTask.CompletedTask;
        }, CancellationToken);
    }

    /// <summary>
    /// 清空地址下 Cookie。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <returns>异步任务。</returns>
    public ValueTask ClearCookiesAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return _cookieExecutor(DefaultRouteKind, (cookieContainer, _) =>
        {
            foreach (var cookie in cookieContainer.GetCookies(uri).Cast<Cookie>())
            {
                cookieContainer.Add(uri, new Cookie(cookie.Name, string.Empty) { Expires = DateTime.UtcNow.AddYears(-1) });
            }

            return ValueTask.CompletedTask;
        }, CancellationToken);
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
