using System.Diagnostics.CodeAnalysis;
using System.Security;
using Microsoft.Extensions.Logging;
using Zeayii.Luma.CommandLine.Options;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
///     <b>文件日志提供程序工厂</b>
/// </summary>
internal static class FileLoggerProviderFactory
{
    /// <summary>
    ///     创建文件日志提供程序。
    /// </summary>
    /// <param name="applicationOptions">应用配置。</param>
    /// <returns>创建结果。</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "日志提供程序实例作为返回值交由外层宿主管理生命周期。")]
    public static FileLoggerProviderFactoryResult Create(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);

        if (applicationOptions.FileLogLevel == LogLevel.None) return new FileLoggerProviderFactoryResult(new NullLoggerProvider(), null);

        var configuredDirectory = applicationOptions.LogDirectory;
        try
        {
            var provider = CreateProvider(applicationOptions, configuredDirectory);
            return new FileLoggerProviderFactoryResult(provider, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            var fallbackDirectory = Path.Combine(Environment.CurrentDirectory, "logs");
            if (!string.Equals(Path.GetFullPath(configuredDirectory), Path.GetFullPath(fallbackDirectory), StringComparison.OrdinalIgnoreCase))
                try
                {
                    var fallbackProvider = CreateProvider(applicationOptions, fallbackDirectory);
                    var warningMessage = $"FileLoggingFallbackActivated ConfiguredDirectory='{configuredDirectory}' FallbackDirectory='{fallbackDirectory}' Reason='{exception.Message}'";
                    return new FileLoggerProviderFactoryResult(fallbackProvider, warningMessage);
                }
                catch (Exception fallbackException) when (fallbackException is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
                {
                    var warningMessage =
                        $"FileLoggingDisabled ConfiguredDirectory='{configuredDirectory}' FallbackDirectory='{fallbackDirectory}' PrimaryReason='{exception.Message}' FallbackReason='{fallbackException.Message}'";
                    return new FileLoggerProviderFactoryResult(new NullLoggerProvider(), warningMessage);
                }

            var disabledWarningMessage = $"FileLoggingDisabled Directory='{configuredDirectory}' Reason='{exception.Message}'";
            return new FileLoggerProviderFactoryResult(new NullLoggerProvider(), disabledWarningMessage);
        }
    }

    /// <summary>
    ///     创建滚动文件日志提供程序。
    /// </summary>
    /// <param name="applicationOptions">应用配置。</param>
    /// <param name="directory">日志目录。</param>
    /// <returns>日志提供程序。</returns>
    private static RollingFileLoggerProvider CreateProvider(ApplicationOptions applicationOptions, string directory)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return new RollingFileLoggerProvider(
            new DirectoryInfo(directory),
            applicationOptions.FileLogLevel,
            applicationOptions.LogRetentionDays,
            applicationOptions.LogTotalSizeMegabytes,
            applicationOptions.LogFileSizeMegabytes
        );
    }
}