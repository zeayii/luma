namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点执行选项</b>
/// <para>
/// 定义节点在子节点调度与请求路由上的执行策略。
/// </para>
/// </summary>
public sealed class NodeExecutionOptions
{
    /// <summary>
    /// 默认执行选项。
    /// </summary>
    public static NodeExecutionOptions Default { get; } = Breadth();

    /// <summary>
    /// 初始化节点执行选项。
    /// </summary>
    /// <param name="defaultRouteKind">节点默认路由类型。</param>
    /// <param name="childTraversalPolicy">子节点遍历策略。</param>
    /// <param name="breadthMaxConcurrency">广度策略下的子节点并发上限。</param>
    private NodeExecutionOptions(LumaRouteKind defaultRouteKind, ChildTraversalPolicy childTraversalPolicy, int breadthMaxConcurrency)
    {
        DefaultRouteKind = defaultRouteKind;
        ChildTraversalPolicy = childTraversalPolicy;
        BreadthMaxConcurrency = Math.Max(1, breadthMaxConcurrency);
    }

    /// <summary>
    /// 节点默认路由类型。
    /// </summary>
    public LumaRouteKind DefaultRouteKind { get; }

    /// <summary>
    /// 子节点遍历策略。
    /// </summary>
    public ChildTraversalPolicy ChildTraversalPolicy { get; }

    /// <summary>
    /// 广度策略下的子节点并发上限。
    /// <para>
    /// 深度策略下该值不会生效，实际并发固定为 1。
    /// </para>
    /// </summary>
    public int BreadthMaxConcurrency { get; }

    /// <summary>
    /// 创建深度优先执行选项。
    /// </summary>
    /// <param name="defaultRouteKind">节点默认路由类型。</param>
    /// <returns>执行选项。</returns>
    public static NodeExecutionOptions Depth(LumaRouteKind defaultRouteKind = LumaRouteKind.Auto)
    {
        return new NodeExecutionOptions(defaultRouteKind, ChildTraversalPolicy.Depth, 1);
    }

    /// <summary>
    /// 创建广度优先执行选项。
    /// </summary>
    /// <param name="defaultRouteKind">节点默认路由类型。</param>
    /// <param name="breadthMaxConcurrency">子节点并发上限。</param>
    /// <returns>执行选项。</returns>
    public static NodeExecutionOptions Breadth(LumaRouteKind defaultRouteKind = LumaRouteKind.Auto, int breadthMaxConcurrency = 1)
    {
        return new NodeExecutionOptions(defaultRouteKind, ChildTraversalPolicy.Breadth, breadthMaxConcurrency);
    }

    /// <summary>
    /// 解析当前节点可用的子节点并发上限。
    /// </summary>
    /// <returns>子节点并发上限。</returns>
    public int ResolveChildMaxConcurrency()
    {
        return ChildTraversalPolicy == ChildTraversalPolicy.Depth ? 1 : Math.Max(1, BreadthMaxConcurrency);
    }
}
