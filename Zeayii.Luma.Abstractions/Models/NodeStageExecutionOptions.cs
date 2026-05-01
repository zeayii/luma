namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点阶段执行选项</b>
/// </summary>
public sealed class NodeStageExecutionOptions
{
    /// <summary>
    ///     阶段键。
    /// </summary>
    public required string StageKey { get; init; }

    /// <summary>
    ///     普通请求阶段容量上限。
    /// </summary>
    public required int RequestCapacity { get; init; }

    /// <summary>
    ///     下载请求阶段容量上限。
    /// </summary>
    public required int DownloadCapacity { get; init; }

    /// <summary>
    ///     普通请求阶段并发上限。
    /// </summary>
    public required int RequestConcurrency { get; init; }

    /// <summary>
    ///     下载请求阶段并发上限。
    /// </summary>
    public required int DownloadConcurrency { get; init; }

    /// <summary>
    ///     普通请求阶段背压模式。
    /// </summary>
    public required NodeBackpressureMode RequestBackpressureMode { get; init; }

    /// <summary>
    ///     下载请求阶段背压模式。
    /// </summary>
    public required NodeBackpressureMode DownloadBackpressureMode { get; init; }

    /// <summary>
    ///     阶段最小请求间隔（毫秒）。
    /// </summary>
    public required int MinRequestIntervalMilliseconds { get; init; }

    /// <summary>
    ///     风控重试起始延迟（毫秒）。
    /// </summary>
    public required int RiskControlInitialDelayMilliseconds { get; init; }

    /// <summary>
    ///     风控重试最大延迟（毫秒）。
    /// </summary>
    public required int RiskControlMaxDelayMilliseconds { get; init; }

    /// <summary>
    ///     风控重试最大次数。
    /// </summary>
    public required int RiskControlMaxRetries { get; init; }

    /// <summary>
    ///     触发风控重试的状态码。
    /// </summary>
    public required IReadOnlyList<int> RiskControlStatusCodes { get; init; }

    /// <summary>
    ///     默认阶段选项。
    /// </summary>
    public static NodeStageExecutionOptions Default { get; } = new()
    {
        StageKey = "default",
        RequestCapacity = 512,
        DownloadCapacity = 512,
        RequestConcurrency = 0,
        DownloadConcurrency = 0,
        RequestBackpressureMode = NodeBackpressureMode.Wait,
        DownloadBackpressureMode = NodeBackpressureMode.Wait,
        MinRequestIntervalMilliseconds = 0,
        RiskControlInitialDelayMilliseconds = 500,
        RiskControlMaxDelayMilliseconds = 10000,
        RiskControlMaxRetries = 5,
        RiskControlStatusCodes = [429, 503]
    };
}
