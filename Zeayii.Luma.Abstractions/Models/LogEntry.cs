namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>日志条目</b>
/// </summary>
public readonly record struct LogEntry(long SequenceId, DateTimeOffset Timestamp, LogLevelKind Level, string Tag, string Message);