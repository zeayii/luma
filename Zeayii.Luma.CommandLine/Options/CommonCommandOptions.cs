using System.CommandLine;
using Microsoft.Extensions.Logging;
using Zeayii.Infrastructure.Net.Http.Configuration.Policies;
using Zeayii.Infrastructure.Net.Http.Logging;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.CommandLine.Options;

/// <summary>
///     <b>通用命令参数句柄集合</b>
/// </summary>
internal sealed class CommonCommandOptions
{
    /// <summary>
    ///     运行名称参数。
    /// </summary>
    public required Option<string?> RunNameOption { get; init; }

    /// <summary>
    ///     日志目录参数。
    /// </summary>
    public required Option<string> LogDirectoryOption { get; init; }

    /// <summary>
    ///     控制台日志等级参数。
    /// </summary>
    public required Option<LogLevel> ConsoleLogLevelOption { get; init; }

    /// <summary>
    ///     文件日志等级参数。
    /// </summary>
    public required Option<LogLevel> FileLogLevelOption { get; init; }

    /// <summary>
    ///     日志保留天数参数。
    /// </summary>
    public required Option<int> LogRetentionDaysOption { get; init; }

    /// <summary>
    ///     日志总大小上限参数。
    /// </summary>
    public required Option<int> LogTotalSizeMegabytesOption { get; init; }

    /// <summary>
    ///     单日志文件大小上限参数。
    /// </summary>
    public required Option<int> LogFileSizeMegabytesOption { get; init; }

    /// <summary>
    ///     网络日志等级参数。
    /// </summary>
    public required Option<NetLogLevel> NetLogLevelOption { get; init; }

    /// <summary>
    ///     品牌标题参数。
    /// </summary>
    public required Option<string> HeaderBrandOption { get; init; }

    /// <summary>
    ///     日志最大行数参数。
    /// </summary>
    public required Option<int> MaxLogEntriesOption { get; init; }

    /// <summary>
    ///     刷新间隔参数。
    /// </summary>
    public required Option<int> RefreshIntervalMillisecondsOption { get; init; }

    /// <summary>
    ///     下载工作协程数量参数。
    /// </summary>
    public required Option<int> DownloadWorkerCountOption { get; init; }

    /// <summary>
    ///     持久化工作协程数量参数。
    /// </summary>
    public required Option<int> PersistWorkerCountOption { get; init; }

    /// <summary>
    ///     请求通道容量参数。
    /// </summary>
    public required Option<int> RequestChannelCapacityOption { get; init; }

    /// <summary>
    ///     持久化通道容量参数。
    /// </summary>
    public required Option<int> PersistChannelCapacityOption { get; init; }

    /// <summary>
    ///     持久化批量大小参数。
    /// </summary>
    public required Option<int> PersistBatchSizeOption { get; init; }

    /// <summary>
    ///     持久化聚合刷新间隔参数。
    /// </summary>
    public required Option<int> PersistFlushIntervalMillisecondsOption { get; init; }

    /// <summary>
    ///     单响应体最大字节数参数。
    /// </summary>
    public required Option<int> MaxResponseBodyBytesOption { get; init; }

    /// <summary>
    ///     默认超时参数。
    /// </summary>
    public required Option<int> DefaultTimeoutSecondsOption { get; init; }

    /// <summary>
    ///     代理列表参数。
    /// </summary>
    public required Option<string[]> ProxiesOption { get; init; }

    /// <summary>
    ///     默认路由参数。
    /// </summary>
    public required Option<LumaRouteKind> DefaultRouteKindOption { get; init; }

    /// <summary>
    ///     重试开关参数。
    /// </summary>
    public required Option<bool> RetryEnabledOption { get; init; }

    /// <summary>
    ///     最大重试次数参数。
    /// </summary>
    public required Option<int> RetryMaxAttemptsOption { get; init; }

    /// <summary>
    ///     重试退避模式参数。
    /// </summary>
    public required Option<RetryDelayMode> RetryDelayModeOption { get; init; }

    /// <summary>
    ///     重试基准延迟参数。
    /// </summary>
    public required Option<int> RetryBaseDelayMillisecondsOption { get; init; }

    /// <summary>
    ///     重试最大延迟参数。
    /// </summary>
    public required Option<int> RetryMaxDelayMillisecondsOption { get; init; }

    /// <summary>
    ///     幂等重试参数。
    /// </summary>
    public required Option<bool> RetryIdempotentOnlyOption { get; init; }

    /// <summary>
    ///     失败处理策略参数。
    /// </summary>
    public required Option<HttpFailurePolicy> RetryFailurePolicyOption { get; init; }

    /// <summary>
    ///     重定向开关参数。
    /// </summary>
    public required Option<bool> RedirectEnabledOption { get; init; }

    /// <summary>
    ///     最大重定向次数参数。
    /// </summary>
    public required Option<int> RedirectMaxRedirectsOption { get; init; }

    /// <summary>
    ///     HTTPS 降级参数。
    /// </summary>
    public required Option<bool> AllowHttpsToHttpOption { get; init; }

    /// <summary>
    ///     重定向方法改写策略参数。
    /// </summary>
    public required Option<RedirectMethodRewriteMode> RedirectMethodRewriteModeOption { get; init; }

    /// <summary>
    ///     代理选择模式参数。
    /// </summary>
    public required Option<ProxySelectionMode> ProxySelectionModeOption { get; init; }

    /// <summary>
    ///     无代理回退直连参数。
    /// </summary>
    public required Option<bool> FallbackToDirectWhenNoProxyOption { get; init; }

    /// <summary>
    ///     头部预设参数。
    /// </summary>
    public required Option<HeaderPresetMode> HeaderPresetModeOption { get; init; }

    /// <summary>
    ///     请求节流开关参数。
    /// </summary>
    public required Option<bool> RequestPacingEnabledOption { get; init; }

    /// <summary>
    ///     请求节流最小间隔参数。
    /// </summary>
    public required Option<int> RequestPacingMinIntervalMillisecondsOption { get; init; }

    /// <summary>
    ///     熔断开关参数。
    /// </summary>
    public required Option<bool> CircuitBreakerEnabledOption { get; init; }

    /// <summary>
    ///     熔断失败阈值参数。
    /// </summary>
    public required Option<int> CircuitBreakerFailureThresholdOption { get; init; }

    /// <summary>
    ///     熔断持续时间参数。
    /// </summary>
    public required Option<int> CircuitBreakerBreakDurationMillisecondsOption { get; init; }

    /// <summary>
    ///     限流开关参数。
    /// </summary>
    public required Option<bool> RateLimitEnabledOption { get; init; }

    /// <summary>
    ///     全局每秒请求数参数。
    /// </summary>
    public required Option<int> GlobalRequestsPerSecondOption { get; init; }

    /// <summary>
    ///     每出口每秒请求数参数。
    /// </summary>
    public required Option<int> PerEgressRequestsPerSecondOption { get; init; }

    /// <summary>
    ///     健康检查开关参数。
    /// </summary>
    public required Option<bool> HealthCheckEnabledOption { get; init; }

    /// <summary>
    ///     健康检查间隔参数。
    /// </summary>
    public required Option<int> HealthCheckIntervalMillisecondsOption { get; init; }

    /// <summary>
    ///     健康检查超时参数。
    /// </summary>
    public required Option<int> HealthCheckTimeoutMillisecondsOption { get; init; }

    /// <summary>
    ///     健康检查失败阈值参数。
    /// </summary>
    public required Option<int> HealthCheckFailureThresholdOption { get; init; }
}