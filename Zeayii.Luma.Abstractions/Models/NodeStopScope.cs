namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点停止作用域</b>
/// </summary>
public enum NodeStopScope
{
    /// <summary>
    ///     不触发停止。
    /// </summary>
    None = 0,

    /// <summary>
    ///     仅停止当前节点实例。
    /// </summary>
    Self = 1,

    /// <summary>
    ///     停止当前分支根节点及其整棵子树。
    /// </summary>
    Branch = 2
}