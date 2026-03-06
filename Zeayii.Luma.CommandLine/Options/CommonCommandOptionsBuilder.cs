using System.CommandLine;
using global::Infrastructure.Net.Http.Configuration.Policies;
using global::Infrastructure.Net.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Options;

/// <summary>
/// <b>通用命令参数构建器</b>
/// <para>
/// 负责向每个站点子命令挂载统一的全局参数，并在解析后构造 <see cref="ApplicationOptions"/>。
/// </para>
/// </summary>
internal static class CommonCommandOptionsBuilder
{
    /// <summary>
    /// 将通用参数添加到命令对象。
    /// </summary>
    /// <param name="command">命令对象。</param>
    /// <returns>参数句柄集合。</returns>
    public static CommonCommandOptions AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = new CommonCommandOptions
        {
            RunNameOption = CreateOption<string?>("--run-name", "本次运行名称。", null),
            LogDirectoryOption = CreateOption("--log-directory", "日志目录路径。", Path.Combine(Environment.CurrentDirectory, "logs")),
            ConsoleLogLevelOption = CreateOption("--console-log-level", "控制台日志等级。", LogLevel.Information),
            FileLogLevelOption = CreateOption("--file-log-level", "文件日志等级。", LogLevel.Information),
            LogRetentionDaysOption = CreateOption("--log-retention-days", "日志保留天数。", 30),
            LogTotalSizeMegabytesOption = CreateOption("--log-total-size-mb", "日志总大小上限（MB）。", 300),
            LogFileSizeMegabytesOption = CreateOption("--log-file-size-mb", "单日志文件大小上限（MB）。", 20),
            NetLogLevelOption = CreateOption("--net-log-level", "网络模块日志等级。", NetLogLevel.Info),
            HeaderBrandOption = CreateOption("--header-brand", "窗口标题品牌文本。", "Luma"),
            MaxLogEntriesOption = CreateOption("--max-log-entries", "窗口日志最大行数。", 1000),
            RefreshIntervalMillisecondsOption = CreateOption("--refresh-interval-ms", "窗口刷新间隔（毫秒）。", 150),
            DownloadWorkerCountOption = CreateOption("--download-workers", "下载工作协程数量。", 4),
            PersistWorkerCountOption = CreateOption("--persist-workers", "持久化工作协程数量。", 2),
            RequestChannelCapacityOption = CreateOption("--request-channel-capacity", "请求通道容量。", 512),
            PersistChannelCapacityOption = CreateOption("--persist-channel-capacity", "持久化通道容量。", 512),
            PersistBatchSizeOption = CreateOption("--persist-batch-size", "持久化批量大小。", 100),
            PersistFlushIntervalMillisecondsOption = CreateOption("--persist-flush-interval-ms", "持久化聚合刷新间隔（毫秒）。", 500),
            MaxResponseBodyBytesOption = CreateOption("--max-response-body-bytes", "单响应体最大字节数。", 4 * 1024 * 1024),
            DefaultTimeoutSecondsOption = CreateOption("--default-timeout-seconds", "默认请求超时（秒）。", 30),
            ProxiesOption = CreateOption("--proxy", "代理地址列表，可重复传入。", Array.Empty<string>()),
            RetryEnabledOption = CreateOption("--retry-enabled", "是否启用重试。", true),
            RetryMaxAttemptsOption = CreateOption("--retry-max-attempts", "最大重试次数。", 2),
            RetryDelayModeOption = CreateOption("--retry-delay-mode", "重试退避模式。", RetryDelayMode.ExponentialWithJitter),
            RetryBaseDelayMillisecondsOption = CreateOption("--retry-base-delay-ms", "重试基准延迟（毫秒）。", 150),
            RetryMaxDelayMillisecondsOption = CreateOption("--retry-max-delay-ms", "重试最大延迟（毫秒）。", 2000),
            RetryIdempotentOnlyOption = CreateOption("--retry-idempotent-only", "是否仅对幂等请求重试。", true),
            RetryFailurePolicyOption = CreateOption("--retry-failure-policy", "最终失败处理策略。", HttpFailurePolicy.ReturnResponse),
            RedirectEnabledOption = CreateOption("--redirect-enabled", "是否启用重定向。", true),
            RedirectMaxRedirectsOption = CreateOption("--redirect-max-redirects", "最大重定向次数。", 5),
            AllowHttpsToHttpOption = CreateOption("--allow-https-to-http", "是否允许 HTTPS 降级到 HTTP。", false),
            RedirectMethodRewriteModeOption = CreateOption("--redirect-method-rewrite-mode", "重定向方法改写策略。", RedirectMethodRewriteMode.BrowserLike),
            ProxySelectionModeOption = CreateOption("--proxy-selection-mode", "代理选择模式。", ProxySelectionMode.WeightedLeastLoad),
            FallbackToDirectWhenNoProxyOption = CreateOption("--fallback-to-direct", "无代理时是否允许直连回退。", true),
            HeaderPresetModeOption = CreateOption("--header-preset", "请求头预设模式。", HeaderPresetMode.ChromeDesktop),
            RequestPacingEnabledOption = CreateOption("--request-pacing-enabled", "是否启用请求节流。", false),
            RequestPacingMinIntervalMillisecondsOption = CreateOption("--request-pacing-min-interval-ms", "最小请求间隔（毫秒）。", 0),
            CircuitBreakerEnabledOption = CreateOption("--circuit-breaker-enabled", "是否启用熔断器。", false),
            CircuitBreakerFailureThresholdOption = CreateOption("--circuit-breaker-failure-threshold", "熔断失败阈值。", 5),
            CircuitBreakerBreakDurationMillisecondsOption = CreateOption("--circuit-breaker-break-duration-ms", "熔断持续时间（毫秒）。", 5000),
            RateLimitEnabledOption = CreateOption("--rate-limit-enabled", "是否启用限流。", false),
            GlobalRequestsPerSecondOption = CreateOption("--global-rps", "全局每秒请求数。", 0),
            PerEgressRequestsPerSecondOption = CreateOption("--per-egress-rps", "每出口每秒请求数。", 0),
            HealthCheckEnabledOption = CreateOption("--health-check-enabled", "是否启用健康检查。", false),
            HealthCheckIntervalMillisecondsOption = CreateOption("--health-check-interval-ms", "健康检查间隔（毫秒）。", 30000),
            HealthCheckTimeoutMillisecondsOption = CreateOption("--health-check-timeout-ms", "健康检查超时（毫秒）。", 3000),
            HealthCheckFailureThresholdOption = CreateOption("--health-check-failure-threshold", "健康检查失败阈值。", 2)
        };

        Add(command, options.RunNameOption);
        Add(command, options.LogDirectoryOption);
        Add(command, options.ConsoleLogLevelOption);
        Add(command, options.FileLogLevelOption);
        Add(command, options.LogRetentionDaysOption);
        Add(command, options.LogTotalSizeMegabytesOption);
        Add(command, options.LogFileSizeMegabytesOption);
        Add(command, options.NetLogLevelOption);
        Add(command, options.HeaderBrandOption);
        Add(command, options.MaxLogEntriesOption);
        Add(command, options.RefreshIntervalMillisecondsOption);
        Add(command, options.DownloadWorkerCountOption);
        Add(command, options.PersistWorkerCountOption);
        Add(command, options.RequestChannelCapacityOption);
        Add(command, options.PersistChannelCapacityOption);
        Add(command, options.PersistBatchSizeOption);
        Add(command, options.PersistFlushIntervalMillisecondsOption);
        Add(command, options.MaxResponseBodyBytesOption);
        Add(command, options.DefaultTimeoutSecondsOption);
        Add(command, options.ProxiesOption);
        Add(command, options.RetryEnabledOption);
        Add(command, options.RetryMaxAttemptsOption);
        Add(command, options.RetryDelayModeOption);
        Add(command, options.RetryBaseDelayMillisecondsOption);
        Add(command, options.RetryMaxDelayMillisecondsOption);
        Add(command, options.RetryIdempotentOnlyOption);
        Add(command, options.RetryFailurePolicyOption);
        Add(command, options.RedirectEnabledOption);
        Add(command, options.RedirectMaxRedirectsOption);
        Add(command, options.AllowHttpsToHttpOption);
        Add(command, options.RedirectMethodRewriteModeOption);
        Add(command, options.ProxySelectionModeOption);
        Add(command, options.FallbackToDirectWhenNoProxyOption);
        Add(command, options.HeaderPresetModeOption);
        Add(command, options.RequestPacingEnabledOption);
        Add(command, options.RequestPacingMinIntervalMillisecondsOption);
        Add(command, options.CircuitBreakerEnabledOption);
        Add(command, options.CircuitBreakerFailureThresholdOption);
        Add(command, options.CircuitBreakerBreakDurationMillisecondsOption);
        Add(command, options.RateLimitEnabledOption);
        Add(command, options.GlobalRequestsPerSecondOption);
        Add(command, options.PerEgressRequestsPerSecondOption);
        Add(command, options.HealthCheckEnabledOption);
        Add(command, options.HealthCheckIntervalMillisecondsOption);
        Add(command, options.HealthCheckTimeoutMillisecondsOption);
        Add(command, options.HealthCheckFailureThresholdOption);
        return options;
    }

    /// <summary>
    /// 基于解析结果构造应用配置。
    /// </summary>
    /// <param name="parseResult">解析结果。</param>
    /// <param name="options">通用参数句柄集合。</param>
    /// <param name="commandName">命令名称。</param>
    /// <returns>应用运行时配置。</returns>
    public static ApplicationOptions BuildApplicationOptions(ParseResult parseResult, CommonCommandOptions options, string commandName)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(options);

        var runName = parseResult.GetValue(options.RunNameOption);
        return new ApplicationOptions
        {
            CommandName = commandName,
            RunName = string.IsNullOrWhiteSpace(runName) ? $"{commandName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : runName,
            LogDirectory = parseResult.GetValue(options.LogDirectoryOption) ?? Path.Combine(Environment.CurrentDirectory, "logs"),
            ConsoleLogLevel = parseResult.GetValue(options.ConsoleLogLevelOption),
            FileLogLevel = parseResult.GetValue(options.FileLogLevelOption),
            LogRetentionDays = parseResult.GetValue(options.LogRetentionDaysOption),
            LogTotalSizeMegabytes = parseResult.GetValue(options.LogTotalSizeMegabytesOption),
            LogFileSizeMegabytes = parseResult.GetValue(options.LogFileSizeMegabytesOption),
            NetLogLevel = parseResult.GetValue(options.NetLogLevelOption),
            HeaderBrand = parseResult.GetValue(options.HeaderBrandOption) ?? "Luma",
            MaxLogEntries = parseResult.GetValue(options.MaxLogEntriesOption),
            RefreshIntervalMilliseconds = parseResult.GetValue(options.RefreshIntervalMillisecondsOption),
            DownloadWorkerCount = parseResult.GetValue(options.DownloadWorkerCountOption),
            PersistWorkerCount = parseResult.GetValue(options.PersistWorkerCountOption),
            RequestChannelCapacity = parseResult.GetValue(options.RequestChannelCapacityOption),
            PersistChannelCapacity = parseResult.GetValue(options.PersistChannelCapacityOption),
            PersistBatchSize = parseResult.GetValue(options.PersistBatchSizeOption),
            PersistFlushIntervalMilliseconds = parseResult.GetValue(options.PersistFlushIntervalMillisecondsOption),
            MaxResponseBodyBytes = parseResult.GetValue(options.MaxResponseBodyBytesOption),
            DefaultTimeoutSeconds = parseResult.GetValue(options.DefaultTimeoutSecondsOption),
            Proxies = parseResult.GetValue(options.ProxiesOption) ?? Array.Empty<string>(),
            RetryEnabled = parseResult.GetValue(options.RetryEnabledOption),
            RetryMaxAttempts = parseResult.GetValue(options.RetryMaxAttemptsOption),
            RetryDelayMode = parseResult.GetValue(options.RetryDelayModeOption),
            RetryBaseDelayMilliseconds = parseResult.GetValue(options.RetryBaseDelayMillisecondsOption),
            RetryMaxDelayMilliseconds = parseResult.GetValue(options.RetryMaxDelayMillisecondsOption),
            RetryIdempotentOnly = parseResult.GetValue(options.RetryIdempotentOnlyOption),
            RetryFailurePolicy = parseResult.GetValue(options.RetryFailurePolicyOption),
            RedirectEnabled = parseResult.GetValue(options.RedirectEnabledOption),
            RedirectMaxRedirects = parseResult.GetValue(options.RedirectMaxRedirectsOption),
            AllowHttpsToHttp = parseResult.GetValue(options.AllowHttpsToHttpOption),
            RedirectMethodRewriteMode = parseResult.GetValue(options.RedirectMethodRewriteModeOption),
            ProxySelectionMode = parseResult.GetValue(options.ProxySelectionModeOption),
            FallbackToDirectWhenNoProxy = parseResult.GetValue(options.FallbackToDirectWhenNoProxyOption),
            HeaderPresetMode = parseResult.GetValue(options.HeaderPresetModeOption),
            RequestPacingEnabled = parseResult.GetValue(options.RequestPacingEnabledOption),
            RequestPacingMinIntervalMilliseconds = parseResult.GetValue(options.RequestPacingMinIntervalMillisecondsOption),
            CircuitBreakerEnabled = parseResult.GetValue(options.CircuitBreakerEnabledOption),
            CircuitBreakerFailureThreshold = parseResult.GetValue(options.CircuitBreakerFailureThresholdOption),
            CircuitBreakerBreakDurationMilliseconds = parseResult.GetValue(options.CircuitBreakerBreakDurationMillisecondsOption),
            RateLimitEnabled = parseResult.GetValue(options.RateLimitEnabledOption),
            GlobalRequestsPerSecond = parseResult.GetValue(options.GlobalRequestsPerSecondOption),
            PerEgressRequestsPerSecond = parseResult.GetValue(options.PerEgressRequestsPerSecondOption),
            HealthCheckEnabled = parseResult.GetValue(options.HealthCheckEnabledOption),
            HealthCheckIntervalMilliseconds = parseResult.GetValue(options.HealthCheckIntervalMillisecondsOption),
            HealthCheckTimeoutMilliseconds = parseResult.GetValue(options.HealthCheckTimeoutMillisecondsOption),
            HealthCheckFailureThreshold = parseResult.GetValue(options.HealthCheckFailureThresholdOption)
        };
    }

    /// <summary>
    /// 创建带默认值的选项对象。
    /// </summary>
    private static Option<TValue> CreateOption<TValue>(string alias, string description, TValue defaultValue)
    {
        return new Option<TValue>(alias)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// 将选项挂载到命令对象。
    /// </summary>
    private static void Add<TValue>(Command command, Option<TValue> option)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(option);
        command.Options.Add(option);
    }
}

