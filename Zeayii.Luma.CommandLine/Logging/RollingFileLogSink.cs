using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
/// <b>滚动文件日志写入汇聚器</b>
/// <para>
/// 采用多生产者写入通道、单消费者落盘的模型。
/// </para>
/// </summary>
internal sealed class RollingFileLogSink : IDisposable
{
    /// <summary>
    /// 日志目录。
    /// </summary>
    private readonly DirectoryInfo _logDirectory;

    /// <summary>
    /// 日志保留天数。
    /// </summary>
    private readonly int _retentionDays;

    /// <summary>
    /// 日志总大小上限（字节）。
    /// </summary>
    private readonly long _maxTotalBytes;

    /// <summary>
    /// 单日志文件大小上限（字节）。
    /// </summary>
    private readonly long _maxFileBytes;

    /// <summary>
    /// 日志通道。
    /// </summary>
    private readonly Channel<string> _channel;

    /// <summary>
    /// 停止取消源。
    /// </summary>
    private readonly CancellationTokenSource _stopCancellationTokenSource = new();

    /// <summary>
    /// 后台消费任务。
    /// </summary>
    private readonly Task _consumerTask;

    /// <summary>
    /// 当前活跃日期。
    /// </summary>
    private DateOnly _activeDate;

    /// <summary>
    /// 当前文件序号。
    /// </summary>
    private int _activeIndex;

    /// <summary>
    /// 当前写入器。
    /// </summary>
    private StreamWriter _writer;

    /// <summary>
    /// 当前文件全路径。
    /// </summary>
    private string _activeFilePath;

    /// <summary>
    /// 已释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 初始化滚动文件日志写入器。
    /// </summary>
    /// <param name="logDirectory">日志目录。</param>
    /// <param name="retentionDays">日志保留天数。</param>
    /// <param name="maxTotalBytes">日志总大小上限（字节）。</param>
    /// <param name="maxFileBytes">单日志文件大小上限（字节）。</param>
    public RollingFileLogSink(DirectoryInfo logDirectory, int retentionDays, long maxTotalBytes, long maxFileBytes)
    {
        _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
        _retentionDays = Math.Max(1, retentionDays);
        _maxTotalBytes = Math.Max(1, maxTotalBytes);
        _maxFileBytes = Math.Max(1, maxFileBytes);

        Directory.CreateDirectory(_logDirectory.FullName);
        _activeDate = DateOnly.FromDateTime(DateTime.Now);
        _activeIndex = 0;
        _activeFilePath = BuildLogPath(_activeDate, _activeIndex);
        _writer = CreateWriter(_activeFilePath);
        CleanupPolicyFiles();

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16_384)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _consumerTask = Task.Factory.StartNew(() => ConsumeLoopAsync(_stopCancellationTokenSource.Token).GetAwaiter().GetResult(), _stopCancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// 写入单行日志。
    /// </summary>
    /// <param name="line">日志文本。</param>
    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _channel.Writer.TryWrite(line);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "释放阶段需要吞掉后台收尾异常，保证进程退出路径稳定。")]
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _stopCancellationTokenSource.Cancel();
        try
        {
            _consumerTask.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // ignore
        }

        _writer.Dispose();
        _stopCancellationTokenSource.Dispose();
    }

    /// <summary>
    /// 后台消费循环。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ConsumeLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var line))
                {
                    RotateIfNeeded(line);
                    await _writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (_channel.Reader.TryRead(out var line))
            {
                RotateIfNeeded(line);
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 当日期变化或文件大小超过阈值时切换日志文件。
    /// </summary>
    /// <param name="nextLine">即将写入的日志行。</param>
    private void RotateIfNeeded(string nextLine)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _activeDate)
        {
            SwitchWriter(today, 0);
            return;
        }

        var estimatedBytes = GetEstimatedBytes(nextLine);
        if (SafeLength(_activeFilePath) + estimatedBytes > _maxFileBytes)
        {
            SwitchWriter(_activeDate, _activeIndex + 1);
        }
    }

    /// <summary>
    /// 切换当前写入器。
    /// </summary>
    /// <param name="date">目标日期。</param>
    /// <param name="index">目标序号。</param>
    private void SwitchWriter(DateOnly date, int index)
    {
        _writer.Dispose();
        _activeDate = date;
        _activeIndex = index;
        _activeFilePath = BuildLogPath(_activeDate, _activeIndex);
        _writer = CreateWriter(_activeFilePath);
        CleanupPolicyFiles();
    }

    /// <summary>
    /// 构建日志文件路径。
    /// </summary>
    /// <param name="date">日志日期。</param>
    /// <param name="index">当日文件序号。</param>
    /// <returns>日志文件路径。</returns>
    private string BuildLogPath(DateOnly date, int index)
    {
        var suffix = index <= 0 ? string.Empty : $".{index}";
        return Path.Combine(_logDirectory.FullName, $"crawler-{date:yyyyMMdd}{suffix}.log");
    }

    /// <summary>
    /// 创建追加写入器。
    /// </summary>
    /// <param name="path">日志文件路径。</param>
    /// <returns>写入器实例。</returns>
    private static StreamWriter CreateWriter(string path)
    {
        return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = false };
    }

    /// <summary>
    /// 执行日志清理策略。
    /// </summary>
    private void CleanupPolicyFiles()
    {
        var files = _logDirectory.GetFiles("crawler-*.log", SearchOption.TopDirectoryOnly).OrderBy(static file => file.Name, StringComparer.Ordinal).ToList();
        if (files.Count <= 0)
        {
            return;
        }

        var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-_retentionDays));
        foreach (var file in files.ToList())
        {
            if (!TryParseDateFromFileName(file.Name, out var date))
            {
                continue;
            }

            if (date >= cutoffDate || PathEquals(file.FullName, _activeFilePath))
            {
                continue;
            }

            if (TryDelete(file))
            {
                files.Remove(file);
            }
        }

        var totalBytes = files.Sum(static file => SafeLength(file.FullName));
        if (totalBytes <= _maxTotalBytes)
        {
            return;
        }

        foreach (var file in files)
        {
            if (totalBytes <= _maxTotalBytes)
            {
                break;
            }

            if (PathEquals(file.FullName, _activeFilePath))
            {
                continue;
            }

            var bytes = SafeLength(file.FullName);
            if (TryDelete(file))
            {
                totalBytes -= bytes;
            }
        }
    }

    /// <summary>
    /// 解析日志文件名中的日期。
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="date">解析出的日期。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    private static bool TryParseDateFromFileName(string fileName, out DateOnly date)
    {
        date = DateOnly.MinValue;
        if (!fileName.StartsWith("crawler-", StringComparison.OrdinalIgnoreCase) || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = fileName["crawler-".Length..^".log".Length];
        var separatorIndex = raw.IndexOf('.');
        var datePart = separatorIndex >= 0 ? raw[..separatorIndex] : raw;
        return DateOnly.TryParseExact(datePart, "yyyyMMdd", out date);
    }

    /// <summary>
    /// 估算单行日志字节数。
    /// </summary>
    /// <param name="line">日志文本。</param>
    /// <returns>估算字节数。</returns>
    private static int GetEstimatedBytes(string line) => System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

    /// <summary>
    /// 安全获取文件长度。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件长度。</returns>
    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// 尝试删除文件。
    /// </summary>
    /// <param name="file">目标文件。</param>
    /// <returns>删除成功返回 <c>true</c>。</returns>
    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 路径无大小写比较。
    /// </summary>
    /// <param name="left">路径 A。</param>
    /// <param name="right">路径 B。</param>
    /// <returns>相同返回 <c>true</c>。</returns>
    private static bool PathEquals(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}

