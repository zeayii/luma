namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>持久化决策</b>
///     <para>
///         表示一次数据持久化的最终语义结果。
///     </para>
/// </summary>
public enum PersistDecision
{
    /// <summary>
    ///     已成功存储。
    /// </summary>
    Stored = 0,

    /// <summary>
    ///     已存在。
    /// </summary>
    AlreadyExists = 1,

    /// <summary>
    ///     已跳过。
    /// </summary>
    Skipped = 2,

    /// <summary>
    ///     失败。
    /// </summary>
    Failed = 3
}