namespace Zeayii.Luma.Engine.Configuration;

/// <summary>
/// <b>爬虫引擎配置</b>
/// </summary>
public sealed class LumaEngineOptions
{
    /// <summary>
    /// 下载工作线程数量。
    /// </summary>
    public int DownloadWorkerCount { get; init; } = 4;

    /// <summary>
    /// 持久化工作线程数量。
    /// </summary>
    public int PersistWorkerCount { get; init; } = 2;

    /// <summary>
    /// 请求通道容量。
    /// </summary>
    public int RequestChannelCapacity { get; init; } = 512;

    /// <summary>
    /// 持久化通道容量。
    /// </summary>
    public int PersistChannelCapacity { get; init; } = 512;

    /// <summary>
    /// 持久化批量大小。
    /// </summary>
    public int PersistBatchSize { get; init; } = 100;

    /// <summary>
    /// 持久化聚合刷新间隔。
    /// </summary>
    public TimeSpan PersistFlushInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 呈现刷新间隔。
    /// </summary>
    public TimeSpan PresentationRefreshInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 单响应体最大字节数。
    /// </summary>
    public int MaxResponseBodyBytes { get; init; } = 4 * 1024 * 1024;
}

