using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
///     <b>滚动文件日志器</b>
/// </summary>
/// <param name="categoryName">分类名。</param>
/// <param name="minimumLevel">最低输出等级。</param>
/// <param name="sink">写入汇聚器。</param>
internal sealed class RollingFileLogger(string categoryName, LogLevel minimumLevel, RollingFileLogSink sink) : ILogger
{
    /// <summary>
    ///     日志分类名。
    /// </summary>
    private readonly string _categoryName = string.IsNullOrWhiteSpace(categoryName) ? "App" : categoryName;


    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Shared;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= minimumLevel;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = formatter(state, exception);
        var line = $"{timestamp} [{logLevel}] [{_categoryName}] {message}";
        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        sink.WriteLine(line);
    }

    /// <summary>
    ///     <b>空作用域</b>
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>
        ///     单例实例。
        /// </summary>
        public static readonly NullScope Shared = new();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}