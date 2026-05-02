namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     节点完成状态。
/// </summary>
public enum NodeCompletionStatus
{
    /// <summary>
    ///     正常完成。
    /// </summary>
    Succeeded = 1,

    /// <summary>
    ///     失败完成。
    /// </summary>
    Failed = 2,

    /// <summary>
    ///     取消完成。
    /// </summary>
    Cancelled = 3
}