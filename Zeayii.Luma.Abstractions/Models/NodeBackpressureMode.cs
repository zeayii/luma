namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>背压模式</b>
/// </summary>
public enum NodeBackpressureMode
{
    /// <summary>
    ///     等待可用容量。
    /// </summary>
    Wait = 0,

    /// <summary>
    ///     丢弃最新任务。
    /// </summary>
    DropNewest = 1,

    /// <summary>
    ///     丢弃最旧任务。
    /// </summary>
    DropOldest = 2
}
