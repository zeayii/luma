using System.Net;

namespace Zeayii.Luma.Engine.FlowControl;

/// <summary>
///     <b>节点请求流控策略接口</b>
///     <para>
///         用于描述节点类型共享流控策略的最小行为集合，支持按策略替换算法实现。
///     </para>
/// </summary>
public interface INodeRequestFlowControlStrategy
{
    /// <summary>
    ///     更新策略输入配置。
    /// </summary>
    /// <param name="options">策略输入配置。</param>
    void Update(NodeRequestFlowControlStrategyOptions options);

    /// <summary>
    ///     解析当前有效最小请求间隔（毫秒）。
    /// </summary>
    /// <returns>非负有效最小请求间隔。</returns>
    int ResolveEffectiveMinIntervalMilliseconds();

    /// <summary>
    ///     观察一次请求响应，用于更新内部流控状态。
    /// </summary>
    /// <param name="statusCode">HTTP 响应状态码。</param>
    /// <param name="nowUtcMilliseconds">当前 UTC 时间戳（毫秒）。</param>
    void ObserveResponse(HttpStatusCode statusCode, long nowUtcMilliseconds);
}