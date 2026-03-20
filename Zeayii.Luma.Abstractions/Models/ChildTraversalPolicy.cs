namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>子节点遍历策略</b>
///     <para>
///         定义当前节点产生子节点时的调度顺序偏好。
///     </para>
/// </summary>
public enum ChildTraversalPolicy
{
    /// <summary>
    ///     广度优先。
    /// </summary>
    Breadth = 0,

    /// <summary>
    ///     深度优先。
    /// </summary>
    Depth = 1
}