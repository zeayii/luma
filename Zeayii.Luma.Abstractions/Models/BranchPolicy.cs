namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>子节点分支策略</b>
///     <para>
///         指定子节点挂载时的分支继承行为。
///     </para>
/// </summary>
public enum BranchPolicy
{
    /// <summary>
    ///     继承父节点所属分支。
    /// </summary>
    InheritParent = 0,

    /// <summary>
    ///     以当前子节点作为新的分支根。
    /// </summary>
    NewBranch = 1
}