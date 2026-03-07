namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点执行选项</b>
/// <para>
/// 定义节点在子节点调度和请求会话上的运行策略。
/// </para>
/// </summary>
public sealed record NodeExecutionOptions
{
    /// <summary>
    /// 默认执行选项。
    /// </summary>
    public static NodeExecutionOptions Default { get; } = new();

    /// <summary>
    /// 子节点遍历策略。
    /// </summary>
    public ChildTraversalPolicy ChildTraversalPolicy { get; init; } = ChildTraversalPolicy.BreadthFirst;

    /// <summary>
    /// 子节点并发上限。
    /// </summary>
    public int ChildMaxConcurrency { get; init; } = 1;

    /// <summary>
    /// 请求会话策略。
    /// </summary>
    public RequestSessionPolicy RequestSessionPolicy { get; init; } = RequestSessionPolicy.SiteSticky;
}
