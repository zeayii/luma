using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
/// <b>滚动文件日志提供程序</b>
/// </summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// 最低输出等级。
    /// </summary>
    private readonly LogLevel _minimumLevel;

    /// <summary>
    /// 文件写入汇聚器。
    /// </summary>
    private readonly RollingFileLogSink _sink;

    /// <summary>
    /// 初始化文件日志提供程序。
    /// </summary>
    /// <param name="logDirectory">日志目录。</param>
    /// <param name="minimumLevel">最低输出等级。</param>
    /// <param name="retentionDays">保留天数。</param>
    /// <param name="maxTotalMegabytes">总大小上限（MB）。</param>
    /// <param name="maxFileMegabytes">单文件大小上限（MB）。</param>
    public RollingFileLoggerProvider(
        DirectoryInfo logDirectory,
        LogLevel minimumLevel,
        int retentionDays,
        int maxTotalMegabytes,
        int maxFileMegabytes)
    {
        ArgumentNullException.ThrowIfNull(logDirectory);
        _minimumLevel = minimumLevel;
        _sink = new RollingFileLogSink(
            logDirectory,
            retentionDays,
            Math.Max(1L, maxTotalMegabytes) * 1024L * 1024L,
            Math.Max(1L, maxFileMegabytes) * 1024L * 1024L);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new RollingFileLogger(categoryName, _minimumLevel, _sink);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sink.Dispose();
    }
}

