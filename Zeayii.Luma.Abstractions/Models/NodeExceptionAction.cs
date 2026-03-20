namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点异常处理动作</b>
///     <para>
///         指示引擎在节点异常发生后应采取的处理策略。
///     </para>
/// </summary>
public enum NodeExceptionAction
{
    /// <summary>
    ///     保持运行。
    /// </summary>
    KeepRunning = 0,

    /// <summary>
    ///     停止当前节点。
    /// </summary>
    StopNode = 1,

    /// <summary>
    ///     停止当前运行。
    /// </summary>
    StopRun = 2,

    /// <summary>
    ///     重新抛出异常。
    /// </summary>
    Rethrow = 3
}