namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>持久化结果</b>
/// <para>
/// 表示一次 item 持久化后的标准化结果。
/// </para>
/// </summary>
public readonly record struct PersistResult(PersistDecision Decision, string Message, bool SuggestStopNode = false)
{
    /// <summary>
    /// 构造存储成功结果。
    /// </summary>
    /// <param name="message">结果消息。</param>
    /// <returns>持久化结果。</returns>
    public static PersistResult Stored(string message = "Stored") => new(PersistDecision.Stored, message);

    /// <summary>
    /// 构造已存在结果。
    /// </summary>
    /// <param name="message">结果消息。</param>
    /// <param name="suggestStopNode">是否建议停止当前节点。</param>
    /// <returns>持久化结果。</returns>
    public static PersistResult AlreadyExists(string message = "AlreadyExists", bool suggestStopNode = false) => new(PersistDecision.AlreadyExists, message, suggestStopNode);

    /// <summary>
    /// 构造跳过结果。
    /// </summary>
    /// <param name="message">结果消息。</param>
    /// <returns>持久化结果。</returns>
    public static PersistResult Skipped(string message = "Skipped") => new(PersistDecision.Skipped, message);

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="message">结果消息。</param>
    /// <returns>持久化结果。</returns>
    public static PersistResult Failed(string message = "Failed") => new(PersistDecision.Failed, message);
}

