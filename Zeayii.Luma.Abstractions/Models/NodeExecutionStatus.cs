namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点执行状态</b>
/// </summary>
public enum NodeExecutionStatus
{
    /// <summary>
    ///     待启动。
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     运行中。
    /// </summary>
    Running = 1,

    /// <summary>
    ///     停止中。
    /// </summary>
    Stopping = 2,

    /// <summary>
    ///     已完成。
    /// </summary>
    Completed = 3,

    /// <summary>
    ///     已取消。
    /// </summary>
    Cancelled = 4,

    /// <summary>
    ///     已失败。
    /// </summary>
    Failed = 5
}