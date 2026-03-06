using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Presentation.Configuration;
using global::Infrastructure.Net.Http.Configuration;
using global::Infrastructure.Net.Http.Configuration.Policies;
using Microsoft.Extensions.Logging;

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
        return new LumaEngineOptions
        {
            DownloadWorkerCount = Math.Max(1, applicationOptions.DownloadWorkerCount),
            PersistWorkerCount = Math.Max(1, applicationOptions.PersistWorkerCount),
            RequestChannelCapacity = Math.Max(32, applicationOptions.RequestChannelCapacity),
            PersistChannelCapacity = Math.Max(32, applicationOptions.PersistChannelCapacity),
            PersistBatchSize = Math.Max(1, applicationOptions.PersistBatchSize),
            PersistFlushInterval = TimeSpan.FromMilliseconds(Math.Max(1, applicationOptions.PersistFlushIntervalMilliseconds)),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(Math.Max(50, applicationOptions.RefreshIntervalMilliseconds)),
            MaxResponseBodyBytes = Math.Max(8 * 1024, applicationOptions.MaxResponseBodyBytes)
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
            MaxLogEntries = Math.Max(100, applicationOptions.MaxLogEntries),
            RefreshInterval = TimeSpan.FromMilliseconds(Math.Max(50, applicationOptions.RefreshIntervalMilliseconds))
        };
    }

    /// <summary>
    /// 构建网络配置。
    /// </summary>
    public static NetOptions BuildNetOptions(ApplicationOptions applicationOptions)
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);

        var proxies = applicationOptions.Proxies
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(static value => value is not null)
            .Cast<Uri>()
            .Distinct()
            .ToArray();

        return new NetOptions
        {
            MinimumLogLevel = applicationOptions.NetLogLevel,
            Proxies = proxies,
            DefaultTimeout = TimeSpan.FromSeconds(Math.Max(1, applicationOptions.DefaultTimeoutSeconds)),
            DefaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DefaultCookies = Array.Empty<System.Net.Cookie>(),
            RetryPolicy = new RetryPolicy
            {
                Enabled = applicationOptions.RetryEnabled,
                MaxAttempts = Math.Max(0, applicationOptions.RetryMaxAttempts),
                DelayMode = applicationOptions.RetryDelayMode,
                BaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, applicationOptions.RetryBaseDelayMilliseconds)),
                MaxDelay = TimeSpan.FromMilliseconds(Math.Max(0, applicationOptions.RetryMaxDelayMilliseconds)),
                IdempotentOnly = applicationOptions.RetryIdempotentOnly,
                FailurePolicy = applicationOptions.RetryFailurePolicy
            },
            RedirectPolicy = new RedirectPolicy
            {
                Enabled = applicationOptions.RedirectEnabled,
                MaxRedirects = Math.Max(0, applicationOptions.RedirectMaxRedirects),
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
                MinInterval = TimeSpan.FromMilliseconds(Math.Max(0, applicationOptions.RequestPacingMinIntervalMilliseconds))
            },
            CircuitBreakerPolicy = new CircuitBreakerPolicy
            {
                Enabled = applicationOptions.CircuitBreakerEnabled,
                FailureThreshold = Math.Max(1, applicationOptions.CircuitBreakerFailureThreshold),
                BreakDuration = TimeSpan.FromMilliseconds(Math.Max(1, applicationOptions.CircuitBreakerBreakDurationMilliseconds))
            },
            RateLimitPolicy = new RateLimitPolicy
            {
                Enabled = applicationOptions.RateLimitEnabled,
                GlobalRequestsPerSecond = Math.Max(0, applicationOptions.GlobalRequestsPerSecond),
                PerEgressRequestsPerSecond = Math.Max(0, applicationOptions.PerEgressRequestsPerSecond)
            },
            HealthCheckPolicy = new HealthCheckPolicy
            {
                Enabled = applicationOptions.HealthCheckEnabled,
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, applicationOptions.HealthCheckIntervalMilliseconds)),
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1, applicationOptions.HealthCheckTimeoutMilliseconds)),
                FailureThreshold = Math.Max(1, applicationOptions.HealthCheckFailureThreshold)
            }
        };
    }
}

