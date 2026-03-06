using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.Presentation.Configuration;

/// <summary>
/// <b>呈现配置</b>
/// </summary>
public sealed class PresentationOptions
{
    /// <summary>
    /// 标题品牌文本。
    /// </summary>
    public string HeaderBrand { get; init; } = "Luma";

    /// <summary>
    /// 控制台日志最低输出级别。
    /// </summary>
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// 日志缓冲区容量。
    /// </summary>
    public int MaxLogEntries { get; init; } = 1000;

    /// <summary>
    /// 刷新间隔。
    /// </summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}

