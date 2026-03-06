using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
/// <b>文件日志提供程序创建结果</b>
/// </summary>
/// <param name="Provider">日志提供程序。</param>
/// <param name="WarningMessage">告警消息。</param>
internal readonly record struct FileLoggerProviderFactoryResult(
    ILoggerProvider Provider,
    string? WarningMessage);

