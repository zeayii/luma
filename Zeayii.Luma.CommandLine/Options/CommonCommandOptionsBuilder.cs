using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Zeayii.Infrastructure.Net.Http.Configuration.Policies;
using Zeayii.Infrastructure.Net.Http.Logging;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.CommandLine.Options;

/// <summary>
///     <b>通用命令参数构建器</b>
///     <para>
///         负责向每个站点子命令挂载统一的全局参数，并在解析后构造 <see cref="ApplicationOptions" />。
///     </para>
/// </summary>
internal static class CommonCommandOptionsBuilder
{
    /// <summary>
    ///     将通用参数添加到命令对象。
    /// </summary>
    /// <param name="command">命令对象。</param>
    /// <returns>参数句柄集合。</returns>
    public static CommonCommandOptions AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = new CommonCommandOptions
        {
            Common = new CommonCommandOptionSet
            {
                RunNameOption = CreateOption<string?>("--run-name", "本次运行名称。", null),
                LogDirectoryOption = CreateOption("--log-directory", "日志目录路径。", Path.Combine(Environment.CurrentDirectory, "logs")),
                ConsoleLogLevelOption = CreateOption("--console-log-level", "控制台日志等级。", LogLevel.Information),
                FileLogLevelOption = CreateOption("--file-log-level", "文件日志等级。", LogLevel.Information),
                LogRetentionDaysOption = CreateOption("--log-retention-days", "日志保留天数。", 30),
                LogTotalSizeMegabytesOption = CreateOption("--log-total-size-mb", "日志总大小上限（MB）。", 300),
                LogFileSizeMegabytesOption = CreateOption("--log-file-size-mb", "单日志文件大小上限（MB）。", 20),
                NetLogLevelOption = CreateOption("--net-log-level", "网络模块日志等级。", NetLogLevel.Info),
                MaxLogEntriesOption = CreateOption("--max-log-entries", "窗口日志最大行数。", 1000),
                RefreshIntervalMillisecondsOption = CreateOption("--refresh-interval-ms", "窗口刷新间隔（毫秒）。", 250),
                RequestWorkerCountOption = CreateOption("--request-workers", "请求工作协程数量。", 4),
                DownloadWorkerCountOption = CreateOption("--download-workers", "下载工作协程数量。", 4),
                PersistWorkerCountOption = CreateOption("--persist-workers", "持久化工作协程数量。", 2),
                RequestChannelCapacityOption = CreateOption("--request-channel-capacity", "请求通道容量。", 512),
                DownloadChannelCapacityOption = CreateOption("--download-channel-capacity", "下载通道容量。", 512),
                PersistChannelCapacityOption = CreateOption("--persist-channel-capacity", "持久化通道容量。", 512),
                PersistBatchSizeOption = CreateOption("--persist-batch-size", "持久化批量大小。", 100),
                PersistFlushIntervalMillisecondsOption = CreateOption("--persist-flush-interval-ms", "持久化聚合刷新间隔（毫秒）。", 500),
                MaxResponseBodyBytesOption = CreateOption("--max-response-body-bytes", "单响应体最大字节数。", 4 * 1024 * 1024),
                DefaultTimeoutSecondsOption = CreateOption("--default-timeout-seconds", "默认请求超时（秒）。", 30),
                ProxiesOption = CreateOption("--proxy", "代理地址列表，可重复传入。", Array.Empty<string>()),
                DefaultRouteKindOption = CreateOption("--default-route", "默认路由类型（Auto/Direct/Proxy）。", LumaRouteKind.Auto),
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
            }
        };

        options.Common.ProxiesOption.Validators.Add(result =>
        {
            var values = result.GetValue(options.Common.ProxiesOption) ?? Array.Empty<string>();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    result.AddError("--proxy does not allow empty or whitespace values.");
                    return;
                }

                if (Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    continue;
                }

                result.AddError($"Invalid proxy URI: {value}");
                return;
            }
        });

        ValidateMinimum(options.Common.LogRetentionDaysOption, 1, "--log-retention-days");
        ValidateMinimum(options.Common.LogTotalSizeMegabytesOption, 1, "--log-total-size-mb");
        ValidateMinimum(options.Common.LogFileSizeMegabytesOption, 1, "--log-file-size-mb");
        ValidateMinimum(options.Common.MaxLogEntriesOption, 100, "--max-log-entries");
        ValidateMinimum(options.Common.RefreshIntervalMillisecondsOption, 50, "--refresh-interval-ms");
        ValidateMinimum(options.Common.RequestWorkerCountOption, 1, "--request-workers");
        ValidateMinimum(options.Common.DownloadWorkerCountOption, 1, "--download-workers");
        ValidateMinimum(options.Common.PersistWorkerCountOption, 1, "--persist-workers");
        ValidateMinimum(options.Common.RequestChannelCapacityOption, 32, "--request-channel-capacity");
        ValidateMinimum(options.Common.DownloadChannelCapacityOption, 32, "--download-channel-capacity");
        ValidateMinimum(options.Common.PersistChannelCapacityOption, 32, "--persist-channel-capacity");
        ValidateMinimum(options.Common.PersistBatchSizeOption, 1, "--persist-batch-size");
        ValidateMinimum(options.Common.PersistFlushIntervalMillisecondsOption, 1, "--persist-flush-interval-ms");
        ValidateMinimum(options.Common.MaxResponseBodyBytesOption, 8 * 1024, "--max-response-body-bytes");
        ValidateMinimum(options.Common.DefaultTimeoutSecondsOption, 1, "--default-timeout-seconds");
        ValidateMinimum(options.Common.RetryMaxAttemptsOption, 0, "--retry-max-attempts");
        ValidateMinimum(options.Common.RetryBaseDelayMillisecondsOption, 0, "--retry-base-delay-ms");
        ValidateMinimum(options.Common.RetryMaxDelayMillisecondsOption, 0, "--retry-max-delay-ms");
        ValidateMinimum(options.Common.RedirectMaxRedirectsOption, 0, "--redirect-max-redirects");
        ValidateMinimum(options.Common.RequestPacingMinIntervalMillisecondsOption, 0, "--request-pacing-min-interval-ms");
        ValidateMinimum(options.Common.CircuitBreakerFailureThresholdOption, 1, "--circuit-breaker-failure-threshold");
        ValidateMinimum(options.Common.CircuitBreakerBreakDurationMillisecondsOption, 1, "--circuit-breaker-break-duration-ms");
        ValidateMinimum(options.Common.GlobalRequestsPerSecondOption, 0, "--global-rps");
        ValidateMinimum(options.Common.PerEgressRequestsPerSecondOption, 0, "--per-egress-rps");
        ValidateMinimum(options.Common.HealthCheckIntervalMillisecondsOption, 1, "--health-check-interval-ms");
        ValidateMinimum(options.Common.HealthCheckTimeoutMillisecondsOption, 1, "--health-check-timeout-ms");
        ValidateMinimum(options.Common.HealthCheckFailureThresholdOption, 1, "--health-check-failure-threshold");

        command.Validators.Add(result =>
        {
            var retryBaseDelayMilliseconds = result.GetValue(options.Common.RetryBaseDelayMillisecondsOption);
            var retryMaxDelayMilliseconds = result.GetValue(options.Common.RetryMaxDelayMillisecondsOption);
            if (retryMaxDelayMilliseconds < retryBaseDelayMilliseconds)
            {
                result.AddError("--retry-max-delay-ms must be greater than or equal to --retry-base-delay-ms.");
            }
        });

        ApplyGeneratedShortAliases(options.Common);

        Add(command, options.Common.RunNameOption);
        Add(command, options.Common.LogDirectoryOption);
        Add(command, options.Common.ConsoleLogLevelOption);
        Add(command, options.Common.FileLogLevelOption);
        Add(command, options.Common.LogRetentionDaysOption);
        Add(command, options.Common.LogTotalSizeMegabytesOption);
        Add(command, options.Common.LogFileSizeMegabytesOption);
        Add(command, options.Common.NetLogLevelOption);
        Add(command, options.Common.MaxLogEntriesOption);
        Add(command, options.Common.RefreshIntervalMillisecondsOption);
        Add(command, options.Common.RequestWorkerCountOption);
        Add(command, options.Common.DownloadWorkerCountOption);
        Add(command, options.Common.PersistWorkerCountOption);
        Add(command, options.Common.RequestChannelCapacityOption);
        Add(command, options.Common.DownloadChannelCapacityOption);
        Add(command, options.Common.PersistChannelCapacityOption);
        Add(command, options.Common.PersistBatchSizeOption);
        Add(command, options.Common.PersistFlushIntervalMillisecondsOption);
        Add(command, options.Common.MaxResponseBodyBytesOption);
        Add(command, options.Common.DefaultTimeoutSecondsOption);
        Add(command, options.Common.ProxiesOption);
        Add(command, options.Common.DefaultRouteKindOption);
        Add(command, options.Common.RetryEnabledOption);
        Add(command, options.Common.RetryMaxAttemptsOption);
        Add(command, options.Common.RetryDelayModeOption);
        Add(command, options.Common.RetryBaseDelayMillisecondsOption);
        Add(command, options.Common.RetryMaxDelayMillisecondsOption);
        Add(command, options.Common.RetryIdempotentOnlyOption);
        Add(command, options.Common.RetryFailurePolicyOption);
        Add(command, options.Common.RedirectEnabledOption);
        Add(command, options.Common.RedirectMaxRedirectsOption);
        Add(command, options.Common.AllowHttpsToHttpOption);
        Add(command, options.Common.RedirectMethodRewriteModeOption);
        Add(command, options.Common.ProxySelectionModeOption);
        Add(command, options.Common.FallbackToDirectWhenNoProxyOption);
        Add(command, options.Common.HeaderPresetModeOption);
        Add(command, options.Common.RequestPacingEnabledOption);
        Add(command, options.Common.RequestPacingMinIntervalMillisecondsOption);
        Add(command, options.Common.CircuitBreakerEnabledOption);
        Add(command, options.Common.CircuitBreakerFailureThresholdOption);
        Add(command, options.Common.CircuitBreakerBreakDurationMillisecondsOption);
        Add(command, options.Common.RateLimitEnabledOption);
        Add(command, options.Common.GlobalRequestsPerSecondOption);
        Add(command, options.Common.PerEgressRequestsPerSecondOption);
        Add(command, options.Common.HealthCheckEnabledOption);
        Add(command, options.Common.HealthCheckIntervalMillisecondsOption);
        Add(command, options.Common.HealthCheckTimeoutMillisecondsOption);
        Add(command, options.Common.HealthCheckFailureThresholdOption);
        return options;
    }

    /// <summary>
    ///     基于解析结果构造应用配置。
    /// </summary>
    /// <param name="parseResult">解析结果。</param>
    /// <param name="options">通用参数句柄集合。</param>
    /// <param name="commandName">命令名称。</param>
    /// <returns>应用运行时配置。</returns>
    public static ApplicationOptions BuildApplicationOptions(ParseResult parseResult, CommonCommandOptions options, string commandName)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(options);

        var runName = parseResult.GetValue(options.Common.RunNameOption);
        return new ApplicationOptions
        {
            CommandName = commandName,
            RunName = string.IsNullOrWhiteSpace(runName) ? $"{commandName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : runName,
            LogDirectory = parseResult.GetRequiredValue(options.Common.LogDirectoryOption),
            ConsoleLogLevel = parseResult.GetRequiredValue(options.Common.ConsoleLogLevelOption),
            FileLogLevel = parseResult.GetRequiredValue(options.Common.FileLogLevelOption),
            LogRetentionDays = parseResult.GetRequiredValue(options.Common.LogRetentionDaysOption),
            LogTotalSizeMegabytes = parseResult.GetRequiredValue(options.Common.LogTotalSizeMegabytesOption),
            LogFileSizeMegabytes = parseResult.GetRequiredValue(options.Common.LogFileSizeMegabytesOption),
            NetLogLevel = parseResult.GetRequiredValue(options.Common.NetLogLevelOption),
            MaxLogEntries = parseResult.GetRequiredValue(options.Common.MaxLogEntriesOption),
            RefreshIntervalMilliseconds = parseResult.GetRequiredValue(options.Common.RefreshIntervalMillisecondsOption),
            RequestWorkerCount = parseResult.GetRequiredValue(options.Common.RequestWorkerCountOption),
            DownloadWorkerCount = parseResult.GetRequiredValue(options.Common.DownloadWorkerCountOption),
            PersistWorkerCount = parseResult.GetRequiredValue(options.Common.PersistWorkerCountOption),
            RequestChannelCapacity = parseResult.GetRequiredValue(options.Common.RequestChannelCapacityOption),
            DownloadChannelCapacity = parseResult.GetRequiredValue(options.Common.DownloadChannelCapacityOption),
            PersistChannelCapacity = parseResult.GetRequiredValue(options.Common.PersistChannelCapacityOption),
            PersistBatchSize = parseResult.GetRequiredValue(options.Common.PersistBatchSizeOption),
            PersistFlushIntervalMilliseconds = parseResult.GetRequiredValue(options.Common.PersistFlushIntervalMillisecondsOption),
            MaxResponseBodyBytes = parseResult.GetRequiredValue(options.Common.MaxResponseBodyBytesOption),
            DefaultTimeoutSeconds = parseResult.GetRequiredValue(options.Common.DefaultTimeoutSecondsOption),
            Proxies = parseResult.GetRequiredValue(options.Common.ProxiesOption),
            DefaultRouteKind = parseResult.GetRequiredValue(options.Common.DefaultRouteKindOption),
            RetryEnabled = parseResult.GetRequiredValue(options.Common.RetryEnabledOption),
            RetryMaxAttempts = parseResult.GetRequiredValue(options.Common.RetryMaxAttemptsOption),
            RetryDelayMode = parseResult.GetRequiredValue(options.Common.RetryDelayModeOption),
            RetryBaseDelayMilliseconds = parseResult.GetRequiredValue(options.Common.RetryBaseDelayMillisecondsOption),
            RetryMaxDelayMilliseconds = parseResult.GetRequiredValue(options.Common.RetryMaxDelayMillisecondsOption),
            RetryIdempotentOnly = parseResult.GetRequiredValue(options.Common.RetryIdempotentOnlyOption),
            RetryFailurePolicy = parseResult.GetRequiredValue(options.Common.RetryFailurePolicyOption),
            RedirectEnabled = parseResult.GetRequiredValue(options.Common.RedirectEnabledOption),
            RedirectMaxRedirects = parseResult.GetRequiredValue(options.Common.RedirectMaxRedirectsOption),
            AllowHttpsToHttp = parseResult.GetRequiredValue(options.Common.AllowHttpsToHttpOption),
            RedirectMethodRewriteMode = parseResult.GetRequiredValue(options.Common.RedirectMethodRewriteModeOption),
            ProxySelectionMode = parseResult.GetRequiredValue(options.Common.ProxySelectionModeOption),
            FallbackToDirectWhenNoProxy = parseResult.GetRequiredValue(options.Common.FallbackToDirectWhenNoProxyOption),
            HeaderPresetMode = parseResult.GetRequiredValue(options.Common.HeaderPresetModeOption),
            RequestPacingEnabled = parseResult.GetRequiredValue(options.Common.RequestPacingEnabledOption),
            RequestPacingMinIntervalMilliseconds = parseResult.GetRequiredValue(options.Common.RequestPacingMinIntervalMillisecondsOption),
            CircuitBreakerEnabled = parseResult.GetRequiredValue(options.Common.CircuitBreakerEnabledOption),
            CircuitBreakerFailureThreshold = parseResult.GetRequiredValue(options.Common.CircuitBreakerFailureThresholdOption),
            CircuitBreakerBreakDurationMilliseconds = parseResult.GetRequiredValue(options.Common.CircuitBreakerBreakDurationMillisecondsOption),
            RateLimitEnabled = parseResult.GetRequiredValue(options.Common.RateLimitEnabledOption),
            GlobalRequestsPerSecond = parseResult.GetRequiredValue(options.Common.GlobalRequestsPerSecondOption),
            PerEgressRequestsPerSecond = parseResult.GetRequiredValue(options.Common.PerEgressRequestsPerSecondOption),
            HealthCheckEnabled = parseResult.GetRequiredValue(options.Common.HealthCheckEnabledOption),
            HealthCheckIntervalMilliseconds = parseResult.GetRequiredValue(options.Common.HealthCheckIntervalMillisecondsOption),
            HealthCheckTimeoutMilliseconds = parseResult.GetRequiredValue(options.Common.HealthCheckTimeoutMillisecondsOption),
            HealthCheckFailureThreshold = parseResult.GetRequiredValue(options.Common.HealthCheckFailureThresholdOption)
        };
    }

    /// <summary>
    ///     创建带默认值的选项对象。
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
    ///     将选项挂载到命令对象。
    /// </summary>
    private static void Add<TValue>(Command command, Option<TValue> option)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(option);
        command.Options.Add(option);
    }

    /// <summary>
    ///     为整数选项添加最小值校验规则。
    /// </summary>
    /// <param name="option">需要校验的选项。</param>
    /// <param name="minimum">允许的最小值。</param>
    /// <param name="alias">选项别名。</param>
    private static void ValidateMinimum(Option<int> option, int minimum, string alias)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(alias);
        option.Validators.Add(result =>
        {
            var value = result.GetValue(option);
            if (value < minimum)
            {
                result.AddError($"{alias} must be greater than or equal to {minimum}.");
            }
        });
    }

    /// <summary>
    ///     为通用参数自动生成短别名。
    ///     <para>
    ///         规则为按长参数分段首字母生成，若发生冲突则扩展首段字符直到唯一。
    ///     </para>
    /// </summary>
    /// <param name="options">参数集合。</param>
    private static void ApplyGeneratedShortAliases(CommonCommandOptionSet options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var optionSymbols = typeof(CommonCommandOptionSet)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => typeof(Option).IsAssignableFrom(property.PropertyType))
            .Select(property => property.GetValue(options) as Option)
            .Where(static option => option is not null)
            .Cast<Option>()
            .Select(option => (Option: option, LongAlias: ResolveLongAlias(option)))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.LongAlias))
            .OrderBy(static entry => entry.LongAlias, StringComparer.Ordinal)
            .ToArray();

        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (option, _) in optionSymbols)
        {
            foreach (var alias in option.Aliases)
            {
                if (alias.StartsWith("-", StringComparison.Ordinal))
                {
                    usedAliases.Add(alias);
                }
            }
        }

        foreach (var (option, longAlias) in optionSymbols)
        {
            var shortAlias = BuildUniqueShortAlias(longAlias!, usedAliases);
            if (string.IsNullOrWhiteSpace(shortAlias))
            {
                continue;
            }

            option.Aliases.Add(shortAlias);
            usedAliases.Add(shortAlias);
        }
    }

    /// <summary>
    ///     解析参数主长别名。
    /// </summary>
    /// <param name="option">参数对象。</param>
    /// <returns>主长别名。</returns>
    private static string? ResolveLongAlias(Option option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.Aliases.FirstOrDefault(static alias => alias.StartsWith("--", StringComparison.Ordinal));
    }

    /// <summary>
    ///     构造唯一短别名。
    /// </summary>
    /// <param name="longAlias">长别名。</param>
    /// <param name="usedAliases">已占用别名集合。</param>
    /// <returns>短别名。</returns>
    private static string BuildUniqueShortAlias(string longAlias, HashSet<string> usedAliases)
    {
        ArgumentNullException.ThrowIfNull(longAlias);
        ArgumentNullException.ThrowIfNull(usedAliases);

        var segments = longAlias.TrimStart('-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        for (var extensionLength = 0; extensionLength < segments[0].Length; extensionLength++)
        {
            var candidate = BuildShortAliasCandidate(segments, extensionLength);
            if (string.IsNullOrWhiteSpace(candidate) || usedAliases.Contains(candidate))
            {
                continue;
            }

            return candidate;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"{BuildShortAliasCandidate(segments, segments[0].Length - 1)}{suffix}";
            if (!usedAliases.Contains(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    /// <summary>
    ///     基于分段构造短别名候选值。
    /// </summary>
    /// <param name="segments">分段数组。</param>
    /// <param name="extensionLength">首段扩展长度。</param>
    /// <returns>短别名候选值。</returns>
    private static string BuildShortAliasCandidate(string[] segments, int extensionLength)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var firstSegment = segments[0];
        var firstSegmentTake = Math.Min(firstSegment.Length, extensionLength + 1);
        var head = firstSegment[..firstSegmentTake];

        if (segments.Length == 1)
        {
            return $"-{head}";
        }

        var tailInitials = string.Concat(segments.Skip(1).Select(static segment => segment[0]));
        return $"-{head[0]}{head[1..]}{tailInitials}";
    }
}

