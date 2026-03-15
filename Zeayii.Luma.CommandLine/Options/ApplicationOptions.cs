using Zeayii.Infrastructure.Net.Http.Configuration.Policies;
using Zeayii.Infrastructure.Net.Http.Logging;
using Microsoft.Extensions.Logging;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.CommandLine.Options;

/// <summary>
/// <b>应用运行时配置</b>
/// <para>
/// 由命令行统一解析得到的全局输入参数。
/// </para>
/// </summary>
internal sealed class ApplicationOptions
{
    /// <summary>
    /// 当前命令名称。
    /// </summary>
    public required string CommandName { get; init; }

    /// <summary>
    /// 当前运行名称。
    /// </summary>
    public required string RunName { get; init; }

    /// <summary>
    /// 日志输出目录。
    /// </summary>
    public required string LogDirectory { get; init; }

    /// <summary>
    /// 控制台日志等级。
    /// </summary>
    public required LogLevel ConsoleLogLevel { get; init; }

    /// <summary>
    /// 文件日志等级。
    /// </summary>
    public required LogLevel FileLogLevel { get; init; }

    /// <summary>
    /// 日志保留天数。
    /// </summary>
    public required int LogRetentionDays { get; init; }

    /// <summary>
    /// 日志总大小上限（MB）。
    /// </summary>
    public required int LogTotalSizeMegabytes { get; init; }

    /// <summary>
    /// 单日志文件大小上限（MB）。
    /// </summary>
    public required int LogFileSizeMegabytes { get; init; }

    /// <summary>
    /// 网络日志等级。
    /// </summary>
    public required NetLogLevel NetLogLevel { get; init; }

    /// <summary>
    /// 标题品牌文本。
    /// </summary>
    public required string HeaderBrand { get; init; }

    /// <summary>
    /// 日志最大行数。
    /// </summary>
    public required int MaxLogEntries { get; init; }

    /// <summary>
    /// 呈现刷新间隔（毫秒）。
    /// </summary>
    public required int RefreshIntervalMilliseconds { get; init; }

    /// <summary>
    /// 下载工作协程数量。
    /// </summary>
    public required int DownloadWorkerCount { get; init; }

    /// <summary>
    /// 持久化工作协程数量。
    /// </summary>
    public required int PersistWorkerCount { get; init; }

    /// <summary>
    /// 请求通道容量。
    /// </summary>
    public required int RequestChannelCapacity { get; init; }

    /// <summary>
    /// 持久化通道容量。
    /// </summary>
    public required int PersistChannelCapacity { get; init; }

    /// <summary>
    /// 持久化批量大小。
    /// </summary>
    public required int PersistBatchSize { get; init; }

    /// <summary>
    /// 持久化聚合刷新间隔（毫秒）。
    /// </summary>
    public required int PersistFlushIntervalMilliseconds { get; init; }

    /// <summary>
    /// 单响应体最大字节数。
    /// </summary>
    public required int MaxResponseBodyBytes { get; init; }

    /// <summary>
    /// 默认超时时长（秒）。
    /// </summary>
    public required int DefaultTimeoutSeconds { get; init; }

    /// <summary>
    /// 代理地址原始值列表。
    /// </summary>
    public required IReadOnlyList<string> Proxies { get; init; }

    /// <summary>
    /// 默认路由类型。
    /// </summary>
    public required LumaRouteKind DefaultRouteKind { get; init; }

    /// <summary>
    /// 是否启用重试。
    /// </summary>
    public required bool RetryEnabled { get; init; }

    /// <summary>
    /// 最大重试次数。
    /// </summary>
    public required int RetryMaxAttempts { get; init; }

    /// <summary>
    /// 重试退避模式。
    /// </summary>
    public required RetryDelayMode RetryDelayMode { get; init; }

    /// <summary>
    /// 重试基准延迟（毫秒）。
    /// </summary>
    public required int RetryBaseDelayMilliseconds { get; init; }

    /// <summary>
    /// 重试最大延迟（毫秒）。
    /// </summary>
    public required int RetryMaxDelayMilliseconds { get; init; }

    /// <summary>
    /// 是否仅对幂等请求重试。
    /// </summary>
    public required bool RetryIdempotentOnly { get; init; }

    /// <summary>
    /// 最终失败处理策略。
    /// </summary>
    public required HttpFailurePolicy RetryFailurePolicy { get; init; }

    /// <summary>
    /// 是否启用重定向。
    /// </summary>
    public required bool RedirectEnabled { get; init; }

    /// <summary>
    /// 最大重定向次数。
    /// </summary>
    public required int RedirectMaxRedirects { get; init; }

    /// <summary>
    /// 是否允许 HTTPS 降级到 HTTP。
    /// </summary>
    public required bool AllowHttpsToHttp { get; init; }

    /// <summary>
    /// 重定向方法改写策略。
    /// </summary>
    public required RedirectMethodRewriteMode RedirectMethodRewriteMode { get; init; }

    /// <summary>
    /// 代理选择模式。
    /// </summary>
    public required ProxySelectionMode ProxySelectionMode { get; init; }

    /// <summary>
    /// 无代理时是否允许直连回退。
    /// </summary>
    public required bool FallbackToDirectWhenNoProxy { get; init; }

    /// <summary>
    /// 请求头预设模式。
    /// </summary>
    public required HeaderPresetMode HeaderPresetMode { get; init; }

    /// <summary>
    /// 是否启用请求节流。
    /// </summary>
    public required bool RequestPacingEnabled { get; init; }

    /// <summary>
    /// 最小请求间隔（毫秒）。
    /// </summary>
    public required int RequestPacingMinIntervalMilliseconds { get; init; }

    /// <summary>
    /// 是否启用熔断器。
    /// </summary>
    public required bool CircuitBreakerEnabled { get; init; }

    /// <summary>
    /// 熔断失败阈值。
    /// </summary>
    public required int CircuitBreakerFailureThreshold { get; init; }

    /// <summary>
    /// 熔断持续时间（毫秒）。
    /// </summary>
    public required int CircuitBreakerBreakDurationMilliseconds { get; init; }

    /// <summary>
    /// 是否启用限流。
    /// </summary>
    public required bool RateLimitEnabled { get; init; }

    /// <summary>
    /// 全局每秒请求数。
    /// </summary>
    public required int GlobalRequestsPerSecond { get; init; }

    /// <summary>
    /// 每出口每秒请求数。
    /// </summary>
    public required int PerEgressRequestsPerSecond { get; init; }

    /// <summary>
    /// 是否启用健康检查。
    /// </summary>
    public required bool HealthCheckEnabled { get; init; }

    /// <summary>
    /// 健康检查间隔（毫秒）。
    /// </summary>
    public required int HealthCheckIntervalMilliseconds { get; init; }

    /// <summary>
    /// 健康检查超时（毫秒）。
    /// </summary>
    public required int HealthCheckTimeoutMilliseconds { get; init; }

    /// <summary>
    /// 健康检查失败阈值。
    /// </summary>
    public required int HealthCheckFailureThreshold { get; init; }
}

