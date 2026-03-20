namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>窗口日志级别</b>
/// </summary>
public enum LogLevelKind
{
    /// <summary>
    ///     跟踪。
    /// </summary>
    Trace = 0,

    /// <summary>
    ///     调试。
    /// </summary>
    Debug = 1,

    /// <summary>
    ///     信息。
    /// </summary>
    Information = 2,

    /// <summary>
    ///     警告。
    /// </summary>
    Warning = 3,

    /// <summary>
    ///     错误。
    /// </summary>
    Error = 4,

    /// <summary>
    ///     严重错误。
    /// </summary>
    Critical = 5
}