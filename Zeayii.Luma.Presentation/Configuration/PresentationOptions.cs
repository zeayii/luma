using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.Presentation.Configuration;

/// <summary>
///     <b>呈现配置</b>
/// </summary>
public sealed class PresentationOptions
{
    /// <summary>
    ///     控制台日志最低输出级别。
    /// </summary>
    public required LogLevel MinimumLogLevel { get; init; }

    /// <summary>
    ///     日志缓冲区容量。
    /// </summary>
    public required int MaxLogEntries { get; init; }

    /// <summary>
    ///     刷新间隔。
    /// </summary>
    public required TimeSpan RefreshInterval { get; init; }
}