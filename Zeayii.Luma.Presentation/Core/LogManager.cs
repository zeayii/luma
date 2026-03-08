using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Globalization;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Presentation.Configuration;
using Spectre.Console;

namespace Zeayii.Luma.Presentation.Core;

/// <summary>
/// <b>日志管理器</b>
/// <para>
/// 采用多生产者入队、单消费者聚合的模型。
/// </para>
/// </summary>
public sealed class LogManager : ILogManager, IDisposable
{
    /// <summary>
    /// 待消费日志载荷。
    /// </summary>
    /// <param name="Timestamp">日志时间。</param>
    /// <param name="Level">日志等级。</param>
    /// <param name="Tag">日志标签。</param>
    /// <param name="Message">日志消息。</param>
    /// <param name="ExceptionText">异常文本。</param>
    private readonly record struct PendingLogEntry(DateTimeOffset Timestamp, LogLevelKind Level, string Tag, string Message, string ExceptionText);

    /// <summary>
    /// 呈现配置。
    /// </summary>
    private readonly PresentationOptions _options;

    /// <summary>
    /// 环形日志缓冲区。
    /// </summary>
    private readonly LogRingBuffer _ringBuffer;

    /// <summary>
    /// 待处理日志通道。
    /// </summary>
    private readonly Channel<PendingLogEntry> _pendingEntries;

    /// <summary>
    /// Dashboard 启动标记。
    /// </summary>
    private volatile bool _isPresentationStarted;

    /// <summary>
    /// 日志序列号。
    /// </summary>
    private long _nextSequenceId;

    /// <summary>
    /// 最近一次快照。
    /// </summary>
    private LogSnapshot? _lastSnapshot;

    /// <summary>
    /// 最近一次快照对应的尾序号。
    /// </summary>
    private long _lastSnapshotTailSequence;

    /// <summary>
    /// 已消费的最后序号。
    /// </summary>
    private long _lastConsumedSequence;

    /// <summary>
    /// 初始化日志管理器。
    /// </summary>
    /// <param name="options">呈现配置。</param>
    public LogManager(PresentationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ringBuffer = new LogRingBuffer(options.MaxLogEntries);
        _pendingEntries = Channel.CreateBounded<PendingLogEntry>(new BoundedChannelOptions(16_384)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <inheritdoc />
    public void MarkPresentationStarted()
    {
        _isPresentationStarted = true;
    }

    /// <inheritdoc />
    public void DrainPendingEntries(int maxBatch = 4096)
    {
        if (maxBatch <= 0)
        {
            return;
        }

        var processed = 0;
        while (processed < maxBatch && _pendingEntries.Reader.TryRead(out var entry))
        {
            var logEntry = new LogEntry(++_nextSequenceId, entry.Timestamp, entry.Level, entry.Tag, entry.Message);
            _ringBuffer.Enqueue(logEntry);
            _lastConsumedSequence = logEntry.SequenceId;
            processed++;
        }
    }

    /// <inheritdoc />
    public void Write(LogLevelKind level, string tag, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var entry = new PendingLogEntry(DateTimeOffset.UtcNow, level, tag, message, exception?.ToString() ?? string.Empty);

        if (!_isPresentationStarted)
        {
            WriteToConsole(new LogEntry(0, entry.Timestamp, entry.Level, entry.Tag, entry.Message));
            return;
        }

        _pendingEntries.Writer.TryWrite(entry);
    }

    /// <inheritdoc />
    public LogSnapshot CreateSnapshot()
    {
        DrainPendingEntries();
        if (_lastSnapshot is not null && _lastSnapshotTailSequence == _lastConsumedSequence)
        {
            return _lastSnapshot;
        }

        _lastSnapshotTailSequence = _lastConsumedSequence;
        _lastSnapshot = new LogSnapshot
        {
            Entries = _ringBuffer.Snapshot()
        };
        return _lastSnapshot;
    }

    /// <summary>
    /// 输出 Trace 日志。
    /// </summary>
    public void Trace(string message, Exception? exception = null, [CallerFilePath] string? caller = null)
        => Write(LogLevelKind.Trace, Path.GetFileNameWithoutExtension(caller) ?? "Trace", message, exception);

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _pendingEntries.Writer.TryComplete();
    }

    /// <summary>
    /// 判断指定日志等级是否应被输出。
    /// </summary>
    /// <param name="level">日志等级。</param>
    /// <returns>应输出返回 <c>true</c>。</returns>
    private bool IsEnabled(LogLevelKind level)
    {
        return Map(level) >= _options.MinimumLogLevel;
    }

    /// <summary>
    /// 将日志等级映射到标准日志级别。
    /// </summary>
    /// <param name="level">内部日志等级。</param>
    /// <returns>标准日志级别。</returns>
    private static Microsoft.Extensions.Logging.LogLevel Map(LogLevelKind level)
    {
        return level switch
        {
            LogLevelKind.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            LogLevelKind.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevelKind.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevelKind.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogLevelKind.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevelKind.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    /// <summary>
    /// Dashboard 启动前直接写控制台。
    /// </summary>
    /// <param name="entry">日志条目。</param>
    private static void WriteToConsole(in LogEntry entry)
    {
        var color = entry.Level switch
        {
            LogLevelKind.Trace => Color.Grey,
            LogLevelKind.Debug => Color.SteelBlue1,
            LogLevelKind.Information => Color.White,
            LogLevelKind.Warning => Color.Yellow,
            LogLevelKind.Error => Color.Red1,
            LogLevelKind.Critical => Color.Red,
            _ => Color.White
        };

        var timestamp = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [bold {color}][[{Markup.Escape(entry.Tag)}]][/] {Markup.Escape(entry.Message)}");
    }
}

