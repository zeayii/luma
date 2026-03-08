namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点执行选项</b>
/// <para>
/// 定义节点在子节点调度和请求会话上的运行策略。
/// </para>
/// </summary>
public sealed class NodeExecutionOptions
{
    /// <summary>
    /// 默认执行选项。
    /// </summary>
    public static NodeExecutionOptions Default { get; } = new()
    {
        DefaultRouteKind = LumaRouteKind.Auto,
        ChildTraversalPolicy = ChildTraversalPolicy.Breadth,
        ChildMaxConcurrency = 1
    };

    /// <summary>
    /// 节点默认路由类型。
    /// </summary>
    public required LumaRouteKind DefaultRouteKind { get; init; }

    /// <summary>
    /// 子节点遍历策略。
    /// </summary>
    public required ChildTraversalPolicy ChildTraversalPolicy { get; init; }

    /// <summary>
    /// 子节点并发上限。
    /// </summary>
    public required int ChildMaxConcurrency { get; init; }
}
