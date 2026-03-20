using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点异常上下文</b>
/// <para>
/// 向节点异常处理钩子提供阶段、请求、响应与数据项等现场信息。
/// </para>
/// </summary>
/// <typeparam name="TState">节点状态类型。</typeparam>
public readonly record struct NodeExceptionContext<TState>
{
    /// <summary>
    /// 初始化节点异常上下文。
    /// </summary>
    /// <param name="nodeContext">节点上下文。</param>
    /// <param name="phase">异常阶段。</param>
    /// <param name="sourceRequest">触发异常的请求。</param>
    /// <param name="response">触发异常的响应。</param>
    /// <param name="item">触发异常的数据项。</param>
    public NodeExceptionContext(LumaContext<TState> nodeContext, NodeExceptionPhase phase, LumaRequest? sourceRequest = null, HttpResponseMessage? response = null, IItem? item = null)
    {
        NodeContext = nodeContext;
        Phase = phase;
        SourceRequest = sourceRequest;
        Response = response;
        Item = item;
    }

    /// <summary>
    /// 节点上下文。
    /// </summary>
    public LumaContext<TState> NodeContext { get; }

    /// <summary>
    /// 异常阶段。
    /// </summary>
    public NodeExceptionPhase Phase { get; }

    /// <summary>
    /// 触发异常的请求。
    /// </summary>
    public LumaRequest? SourceRequest { get; }

    /// <summary>
    /// 触发异常的响应。
    /// </summary>
    public HttpResponseMessage? Response { get; }

    /// <summary>
    /// 触发异常的数据项。
    /// </summary>
    public IItem? Item { get; }
}
