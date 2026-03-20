namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>抓取请求调度信封</b>
///     <para>
///         承载原生 <see cref="HttpRequestMessage" /> 与框架调度元数据。
///     </para>
/// </summary>
public sealed class LumaRequest
{
    /// <summary>
    ///     初始化抓取请求调度信封。
    /// </summary>
    /// <param name="httpRequestMessage">原生 HTTP 请求消息。</param>
    /// <param name="nodePath">所属节点路径。</param>
    public LumaRequest(HttpRequestMessage httpRequestMessage, string nodePath)
    {
        HttpRequestMessage = httpRequestMessage ?? throw new ArgumentNullException(nameof(httpRequestMessage));
        NodePath = string.IsNullOrWhiteSpace(nodePath) ? throw new ArgumentNullException(nameof(nodePath)) : nodePath;
        RouteKind = LumaRouteKind.Auto;
    }

    /// <summary>
    ///     原生 HTTP 请求消息。
    /// </summary>
    public HttpRequestMessage HttpRequestMessage { get; }

    /// <summary>
    ///     所属节点路径。
    /// </summary>
    public string NodePath { get; }

    /// <summary>
    ///     路由类型。
    /// </summary>
    public LumaRouteKind RouteKind { get; init; }

    /// <summary>
    ///     超时时间。
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    ///     返回调试文本。
    /// </summary>
    /// <returns>调试文本。</returns>
    public override string ToString()
    {
        return $"{HttpRequestMessage.Method} {HttpRequestMessage.RequestUri}";
    }
}