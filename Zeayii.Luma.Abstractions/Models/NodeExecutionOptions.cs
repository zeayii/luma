namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点执行选项</b>
///     <para>
///         定义节点在子节点结构化并发与请求路由上的执行策略。
///     </para>
/// </summary>
public sealed class NodeExecutionOptions
{
    /// <summary>
    ///     初始化节点执行选项。
    /// </summary>
    /// <param name="defaultRouteKind">节点默认路由类型。</param>
    /// <param name="childMaxConcurrency">子节点并发上限。</param>
    public NodeExecutionOptions(LumaRouteKind defaultRouteKind = LumaRouteKind.Auto, int childMaxConcurrency = 1)
    {
        DefaultRouteKind = defaultRouteKind;
        ChildMaxConcurrency = Math.Max(1, childMaxConcurrency);
    }

    /// <summary>
    ///     默认执行选项。
    /// </summary>
    public static NodeExecutionOptions Default { get; } = new();

    /// <summary>
    ///     节点默认路由类型。
    /// </summary>
    public LumaRouteKind DefaultRouteKind { get; }

    /// <summary>
    ///     子节点并发上限。
    /// </summary>
    public int ChildMaxConcurrency { get; }

    /// <summary>
    ///     解析当前节点可用的子节点并发上限。
    /// </summary>
    /// <returns>子节点并发上限。</returns>
    public int ResolveChildMaxConcurrency()
    {
        return Math.Max(1, ChildMaxConcurrency);
    }
}
