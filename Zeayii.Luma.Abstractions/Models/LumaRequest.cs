namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>抓取请求模型</b>
/// <para>
/// 表示框架可调度的一条请求任务。
/// </para>
/// </summary>
public sealed class LumaRequest
{
    /// <summary>
    /// 空请求头集合。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化抓取请求。
    /// </summary>
    /// <param name="url">目标地址。</param>
    /// <param name="nodePath">所属节点路径。</param>
    public LumaRequest(Uri url, string nodePath)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        NodePath = string.IsNullOrWhiteSpace(nodePath) ? throw new ArgumentNullException(nameof(nodePath)) : nodePath;
        Method = HttpMethod.Get;
        Headers = EmptyHeaders;
    }

    /// <summary>
    /// 目标地址。
    /// </summary>
    public Uri Url { get; init; }

    /// <summary>
    /// 所属节点路径。
    /// </summary>
    public string NodePath { get; init; }

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    public HttpMethod Method { get; init; }

    /// <summary>
    /// 请求头集合。
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// 请求体。
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// 路由类型。
    /// </summary>
    public LumaRouteKind RouteKind { get; init; }

    /// <summary>
    /// 超时时间。
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// 返回调试文本。
    /// </summary>
    /// <returns>调试文本。</returns>
    public override string ToString() => $"{Method} {Url}";
}
