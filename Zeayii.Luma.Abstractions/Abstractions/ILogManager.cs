using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
///     <b>日志管理器</b>
///     <para>
///         负责维护窗口日志快照，并提供统一写入入口。
///     </para>
/// </summary>
public interface ILogManager
{
    /// <summary>
    ///     标记呈现层已启动。
    /// </summary>
    void MarkPresentationStarted();

    /// <summary>
    ///     拉取待处理日志并写入内存缓冲。
    /// </summary>
    /// <param name="maxBatch">单次最大处理批量。</param>
    void DrainPendingEntries(int maxBatch = 4096);

    /// <summary>
    ///     写入日志。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="tag">日志标签。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">异常对象。</param>
    void Write(LogLevelKind level, string tag, string message, Exception? exception = null);

    /// <summary>
    ///     创建日志快照。
    /// </summary>
    /// <returns>日志快照。</returns>
    LogSnapshot CreateSnapshot();
}