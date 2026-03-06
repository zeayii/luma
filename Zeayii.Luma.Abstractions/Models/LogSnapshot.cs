namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>日志快照</b>
/// <para>
/// 供呈现层按帧读取的只读日志窗口。
/// </para>
/// </summary>
public sealed class LogSnapshot
{
    /// <summary>
    /// 快照中的日志条目集合。
    /// </summary>
    public IReadOnlyList<LogEntry> Entries { get; init; } = Array.Empty<LogEntry>();
}

