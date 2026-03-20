using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Presentation.Core;

/// <summary>
///     <b>空日志管理器</b>
///     <para>
///         用于日志等级为 <c>None</c> 时关闭日志管线初始化与处理开销。
///     </para>
/// </summary>
public sealed class NullLogManager : ILogManager
{
    /// <inheritdoc />
    public void MarkPresentationStarted()
    {
    }

    /// <inheritdoc />
    public void DrainPendingEntries(int maxBatch = 4096)
    {
        _ = maxBatch;
    }

    /// <inheritdoc />
    public void Write(LogLevelKind level, string tag, string message, Exception? exception = null)
    {
        _ = level;
        _ = tag;
        _ = message;
        _ = exception;
    }

    /// <inheritdoc />
    public LogSnapshot CreateSnapshot()
    {
        return new LogSnapshot
        {
            Entries = []
        };
    }
}

