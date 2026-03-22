using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Presentation.Core;

/// <summary>
///     <b>环形日志缓冲区</b>
///     <para>
///         该类型由单消费者写入，不需要额外加锁。
///     </para>
/// </summary>
internal sealed class LogRingBuffer
{
    /// <summary>
    ///     环形数组缓冲区。
    /// </summary>
    private readonly LogEntry[] _buffer;

    /// <summary>
    ///     当前有效元素数量。
    /// </summary>
    private int _count;

    /// <summary>
    ///     当前起始索引。
    /// </summary>
    private int _start;

    /// <summary>
    ///     初始化环形缓冲区。
    /// </summary>
    /// <param name="capacity">容量。</param>
    public LogRingBuffer(int capacity)
    {
        _buffer = capacity <= 0 ? throw new ArgumentOutOfRangeException(nameof(capacity)) : new LogEntry[capacity];
    }

    /// <summary>
    ///     写入日志条目。
    /// </summary>
    /// <param name="entry">日志条目。</param>
    public void Enqueue(in LogEntry entry)
    {
        var index = (_start + _count) % _buffer.Length;
        _buffer[index] = entry;

        if (_count == _buffer.Length)
        {
            _start = (_start + 1) % _buffer.Length;
            return;
        }

        _count++;
    }

    /// <summary>
    ///     获取当前快照。
    /// </summary>
    /// <returns>日志数组。</returns>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        if (_count == 0)
        {
            return [];
        }

        var result = new LogEntry[_count];
        for (var index = 0; index < _count; index++)
        {
            result[index] = _buffer[(_start + index) % _buffer.Length];
        }

        return result;
    }
}