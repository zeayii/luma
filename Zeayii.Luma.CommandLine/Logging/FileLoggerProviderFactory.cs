using Zeayii.Luma.CommandLine.Options;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
/// <b>文件日志提供程序工厂</b>
/// </summary>
internal static class FileLoggerProviderFactory
{
    /// <summary>
    /// 创建文件日志提供程序。
    /// </summary>
    /// <param name="applicationOptions">应用配置。</param>
    /// <returns>创建结果。</returns>
    public static FileLoggerProviderFactoryResult Create(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);

        try
        {
            var provider = new RollingFileLoggerProvider(
                new DirectoryInfo(applicationOptions.LogDirectory),
                applicationOptions.FileLogLevel,
                Math.Max(1, applicationOptions.LogRetentionDays),
                Math.Max(1, applicationOptions.LogTotalSizeMegabytes),
                Math.Max(1, applicationOptions.LogFileSizeMegabytes));
            return new FileLoggerProviderFactoryResult(provider, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            var warningMessage = $"File logging disabled: unable to access log directory '{applicationOptions.LogDirectory}'. {exception.Message}";
            return new FileLoggerProviderFactoryResult(new NullLoggerProvider(), warningMessage);
        }
    }
}

