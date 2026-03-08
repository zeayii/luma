using System.Net;
using Infrastructure.Net.Http.Configuration;
using Infrastructure.Net.Http.Configuration.Policies;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Presentation.Configuration;

namespace Zeayii.Luma.CommandLine.Options;

/// <summary>
/// <b>运行时配置构建器</b>
/// </summary>
internal static class OptionsBuilder
{
    /// <summary>
    /// 构建引擎配置。
    /// </summary>
    public static LumaEngineOptions BuildEngineOptions(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);
        var resolvedDefaultRouteKind = ResolveDefaultRouteKind(applicationOptions);
        return new LumaEngineOptions
        {
            DefaultRouteKind = resolvedDefaultRouteKind,
            DownloadWorkerCount = applicationOptions.DownloadWorkerCount,
            PersistWorkerCount = applicationOptions.PersistWorkerCount,
            RequestChannelCapacity = applicationOptions.RequestChannelCapacity,
            PersistChannelCapacity = applicationOptions.PersistChannelCapacity,
            PersistBatchSize = applicationOptions.PersistBatchSize,
            PersistFlushInterval = TimeSpan.FromMilliseconds(applicationOptions.PersistFlushIntervalMilliseconds),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(applicationOptions.RefreshIntervalMilliseconds),
            MaxResponseBodyBytes = applicationOptions.MaxResponseBodyBytes
        };
    }

    /// <summary>
    /// 构建呈现配置。
    /// </summary>
    public static PresentationOptions BuildPresentationOptions(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);
        return new PresentationOptions
        {
            HeaderBrand = applicationOptions.HeaderBrand,
            MinimumLogLevel = applicationOptions.ConsoleLogLevel,
            MaxLogEntries = applicationOptions.MaxLogEntries,
            RefreshInterval = TimeSpan.FromMilliseconds(applicationOptions.RefreshIntervalMilliseconds)
        };
    }

    /// <summary>
    /// 构建网络配置。
    /// </summary>
    public static NetOptions BuildNetOptions(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);

        var proxies = applicationOptions.Proxies
            .Select(static value => new Uri(value, UriKind.Absolute))
            .Distinct()
            .ToArray();

        return new NetOptions
        {
            MinimumLogLevel = applicationOptions.NetLogLevel,
            Proxies = proxies,
            DefaultTimeout = TimeSpan.FromSeconds(applicationOptions.DefaultTimeoutSeconds),
            DefaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DefaultCookies = Array.Empty<Cookie>(),
            RetryPolicy = new RetryPolicy
            {
                Enabled = applicationOptions.RetryEnabled,
                MaxAttempts = applicationOptions.RetryMaxAttempts,
                DelayMode = applicationOptions.RetryDelayMode,
                BaseDelay = TimeSpan.FromMilliseconds(applicationOptions.RetryBaseDelayMilliseconds),
                MaxDelay = TimeSpan.FromMilliseconds(applicationOptions.RetryMaxDelayMilliseconds),
                IdempotentOnly = applicationOptions.RetryIdempotentOnly,
                FailurePolicy = applicationOptions.RetryFailurePolicy
            },
            RedirectPolicy = new RedirectPolicy
            {
                Enabled = applicationOptions.RedirectEnabled,
                MaxRedirects = applicationOptions.RedirectMaxRedirects,
                AllowHttpsToHttp = applicationOptions.AllowHttpsToHttp,
                MethodRewriteMode = applicationOptions.RedirectMethodRewriteMode
            },
            ProxySelectionPolicy = new ProxySelectionPolicy
            {
                Mode = applicationOptions.ProxySelectionMode,
                FallbackToDirectWhenNoProxy = applicationOptions.FallbackToDirectWhenNoProxy
            },
            HeaderPolicy = new HeaderPolicy
            {
                Preset = applicationOptions.HeaderPresetMode
            },
            RequestPacingPolicy = new RequestPacingPolicy
            {
                Enabled = applicationOptions.RequestPacingEnabled,
                MinInterval = TimeSpan.FromMilliseconds(applicationOptions.RequestPacingMinIntervalMilliseconds)
            },
            CircuitBreakerPolicy = new CircuitBreakerPolicy
            {
                Enabled = applicationOptions.CircuitBreakerEnabled,
                FailureThreshold = applicationOptions.CircuitBreakerFailureThreshold,
                BreakDuration = TimeSpan.FromMilliseconds(applicationOptions.CircuitBreakerBreakDurationMilliseconds)
            },
            RateLimitPolicy = new RateLimitPolicy
            {
                Enabled = applicationOptions.RateLimitEnabled,
                GlobalRequestsPerSecond = applicationOptions.GlobalRequestsPerSecond,
                PerEgressRequestsPerSecond = applicationOptions.PerEgressRequestsPerSecond
            },
            HealthCheckPolicy = new HealthCheckPolicy
            {
                Enabled = applicationOptions.HealthCheckEnabled,
                Interval = TimeSpan.FromMilliseconds(applicationOptions.HealthCheckIntervalMilliseconds),
                Timeout = TimeSpan.FromMilliseconds(applicationOptions.HealthCheckTimeoutMilliseconds),
                FailureThreshold = applicationOptions.HealthCheckFailureThreshold
            }
        };
    }

    /// <summary>
    /// 解析默认路由类型。
    /// </summary>
    /// <param name="applicationOptions">应用参数。</param>
    /// <returns>最终默认路由类型。</returns>
    private static LumaRouteKind ResolveDefaultRouteKind(ApplicationOptions applicationOptions)
    {
        if (applicationOptions.DefaultRouteKind != LumaRouteKind.Auto)
        {
            return applicationOptions.DefaultRouteKind;
        }

        var hasProxy = applicationOptions.Proxies.Count > 0;
        return hasProxy ? LumaRouteKind.Proxy : LumaRouteKind.Direct;
    }
}
