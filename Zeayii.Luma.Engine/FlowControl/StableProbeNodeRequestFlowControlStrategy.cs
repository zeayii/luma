using System.Collections.Immutable;
using System.Net;

namespace Zeayii.Luma.Engine.FlowControl;

/// <summary>
///     <b>稳态探测节点流控策略</b>
///     <para>
///         采用“快速退让 + 冷却窗口 + 低频探测恢复”的闭环策略：
///         触发风控时指数退避，成功阶段在满足冷却与成功窗口后缓慢恢复。
///     </para>
/// </summary>
public sealed class StableProbeNodeRequestFlowControlStrategy : INodeRequestFlowControlStrategy
{
    /// <summary>
    ///     最小探测窗口成功次数。
    /// </summary>
    private const int MinProbeWindowSuccessCount = 24;

    /// <summary>
    ///     最大探测窗口成功次数。
    /// </summary>
    private const int MaxProbeWindowSuccessCount = 768;

    /// <summary>
    ///     退避触发后最小冷却时长（毫秒）。
    /// </summary>
    private const int CooldownMilliseconds = 30_000;

    /// <summary>
    ///     是否启用自适应退避（1=启用，0=禁用）。
    /// </summary>
    private int _adaptiveBackoffEnabledFlag;

    /// <summary>
    ///     当前命中次数。
    /// </summary>
    private int _adaptiveBackoffHitCount;

    /// <summary>
    ///     自适应退避命中次数上限。
    /// </summary>
    private int _adaptiveBackoffMaxHits;

    /// <summary>
    ///     触发自适应退避状态码集合。
    /// </summary>
    private ImmutableHashSet<int> _adaptiveBackoffStatusCodes = ImmutableHashSet<int>.Empty;

    /// <summary>
    ///     自适应退避上限（毫秒）。
    /// </summary>
    private int _adaptiveMaxIntervalMilliseconds;

    /// <summary>
    ///     当前自适应最小请求间隔（毫秒）。
    /// </summary>
    private int _adaptiveMinIntervalMilliseconds;

    /// <summary>
    ///     基础最小请求间隔（毫秒）。
    /// </summary>
    private int _configuredMinIntervalMilliseconds;

    /// <summary>
    ///     自适应退避起始间隔（毫秒）；0 表示使用历史兼容模式。
    /// </summary>
    private int _adaptiveInitialIntervalMilliseconds;

    /// <summary>
    ///     冷却期截止 UTC 毫秒时间戳。
    /// </summary>
    private long _cooldownUntilUtcMilliseconds;

    /// <summary>
    ///     当前探测窗口成功计数。
    /// </summary>
    private int _probeWindowSuccessCount;

    /// <summary>
    ///     探测窗口成功阈值。
    /// </summary>
    private int _probeWindowSuccessThreshold = MinProbeWindowSuccessCount;

    /// <inheritdoc />
    public void Update(NodeRequestFlowControlStrategyOptions options)
    {
        var configuredMinIntervalMilliseconds = options.ResolveMinIntervalMilliseconds();
        var adaptiveBackoffEnabledFlag = options.AdaptiveBackoffEnabled ? 1 : 0;
        var adaptiveBackoffMaxHits = options.ResolveAdaptiveBackoffMaxHits();
        var adaptiveMaxIntervalMilliseconds = Math.Max(0, options.AdaptiveMaxIntervalMilliseconds);
        var adaptiveInitialIntervalMilliseconds = options.ResolveAdaptiveInitialIntervalMilliseconds();
        var adaptiveBackoffStatusCodes = options.BuildAdaptiveBackoffStatusCodeSet().ToImmutableHashSet();

        Interlocked.Exchange(ref _configuredMinIntervalMilliseconds, configuredMinIntervalMilliseconds);
        Interlocked.Exchange(ref _adaptiveBackoffEnabledFlag, adaptiveBackoffEnabledFlag);
        Interlocked.Exchange(ref _adaptiveBackoffMaxHits, adaptiveBackoffMaxHits);
        Interlocked.Exchange(ref _adaptiveMaxIntervalMilliseconds, adaptiveMaxIntervalMilliseconds);
        Interlocked.Exchange(ref _adaptiveInitialIntervalMilliseconds, adaptiveInitialIntervalMilliseconds);
        Volatile.Write(ref _adaptiveBackoffStatusCodes, adaptiveBackoffStatusCodes);

        EnsureAdaptiveFloor(configuredMinIntervalMilliseconds);
        ClampAdaptiveHitCount(adaptiveBackoffMaxHits);
    }

    /// <inheritdoc />
    public int ResolveEffectiveMinIntervalMilliseconds()
    {
        return Math.Max(Volatile.Read(ref _configuredMinIntervalMilliseconds), Volatile.Read(ref _adaptiveMinIntervalMilliseconds));
    }

    /// <inheritdoc />
    public void ObserveResponse(HttpStatusCode statusCode, long nowUtcMilliseconds)
    {
        if (Volatile.Read(ref _adaptiveBackoffEnabledFlag) == 0)
        {
            return;
        }

        var adaptiveBackoffStatusCodes = Volatile.Read(ref _adaptiveBackoffStatusCodes);
        if (adaptiveBackoffStatusCodes.IsEmpty)
        {
            return;
        }

        var statusCodeValue = (int)statusCode;
        if (adaptiveBackoffStatusCodes.Contains(statusCodeValue))
        {
            ObserveBackoffTrigger(nowUtcMilliseconds);
            return;
        }

        if (statusCodeValue is >= 200 and < 400)
        {
            ObserveSuccess(nowUtcMilliseconds);
        }
    }

    /// <summary>
    ///     处理风控触发事件。
    /// </summary>
    /// <param name="nowUtcMilliseconds">当前 UTC 时间戳（毫秒）。</param>
    private void ObserveBackoffTrigger(long nowUtcMilliseconds)
    {
        var adaptiveBackoffMaxHits = Volatile.Read(ref _adaptiveBackoffMaxHits);
        var adaptiveBackoffHitCount = Volatile.Read(ref _adaptiveBackoffHitCount);

        if (adaptiveBackoffMaxHits > 0 && adaptiveBackoffHitCount >= adaptiveBackoffMaxHits)
        {
            Interlocked.Exchange(ref _probeWindowSuccessCount, 0);
            Volatile.Write(ref _cooldownUntilUtcMilliseconds, nowUtcMilliseconds + CooldownMilliseconds);
            return;
        }

        var adaptiveCap = ResolveAdaptiveMaxIntervalMilliseconds();
        var configuredFloor = Math.Max(1, Volatile.Read(ref _configuredMinIntervalMilliseconds));
        var adaptiveInitialIntervalMilliseconds = Volatile.Read(ref _adaptiveInitialIntervalMilliseconds);

        int nextAdaptiveMinIntervalMilliseconds;
        if (adaptiveInitialIntervalMilliseconds > 0)
        {
            var initial = Math.Max(configuredFloor, adaptiveInitialIntervalMilliseconds);
            var multiplierPower = Math.Max(0, adaptiveBackoffHitCount);
            var next = ResolveSafePow2Multiply(initial, multiplierPower);
            nextAdaptiveMinIntervalMilliseconds = Math.Min(adaptiveCap, next);
        }
        else
        {
            // 兼容历史行为：在当前有效间隔基础上 x2。
            var baseline = Math.Max(1, ResolveEffectiveMinIntervalMilliseconds());
            var next = ResolveSafeDouble(baseline);
            nextAdaptiveMinIntervalMilliseconds = Math.Min(adaptiveCap, next);
        }

        Interlocked.Exchange(ref _adaptiveMinIntervalMilliseconds, nextAdaptiveMinIntervalMilliseconds);
        Interlocked.Increment(ref _adaptiveBackoffHitCount);
        Interlocked.Exchange(ref _probeWindowSuccessCount, 0);
        Volatile.Write(ref _cooldownUntilUtcMilliseconds, nowUtcMilliseconds + CooldownMilliseconds);

        while (true)
        {
            var currentThreshold = Volatile.Read(ref _probeWindowSuccessThreshold);
            var nextThreshold = Math.Min(MaxProbeWindowSuccessCount, Math.Max(MinProbeWindowSuccessCount, currentThreshold * 2));
            if (Interlocked.CompareExchange(ref _probeWindowSuccessThreshold, nextThreshold, currentThreshold) == currentThreshold)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     处理成功响应事件。
    /// </summary>
    /// <param name="nowUtcMilliseconds">当前 UTC 时间戳（毫秒）。</param>
    private void ObserveSuccess(long nowUtcMilliseconds)
    {
        var configuredMinIntervalMilliseconds = Volatile.Read(ref _configuredMinIntervalMilliseconds);
        var adaptiveMinIntervalMilliseconds = Volatile.Read(ref _adaptiveMinIntervalMilliseconds);
        if (adaptiveMinIntervalMilliseconds <= configuredMinIntervalMilliseconds)
        {
            Interlocked.Exchange(ref _adaptiveMinIntervalMilliseconds, configuredMinIntervalMilliseconds);
            Interlocked.Exchange(ref _probeWindowSuccessCount, 0);
            return;
        }

        if (nowUtcMilliseconds < Volatile.Read(ref _cooldownUntilUtcMilliseconds))
        {
            return;
        }

        var observedSuccessCount = Interlocked.Increment(ref _probeWindowSuccessCount);
        var probeWindowSuccessThreshold = Volatile.Read(ref _probeWindowSuccessThreshold);
        if (observedSuccessCount < probeWindowSuccessThreshold)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _probeWindowSuccessCount, 0, observedSuccessCount) != observedSuccessCount)
        {
            return;
        }

        adaptiveMinIntervalMilliseconds = Volatile.Read(ref _adaptiveMinIntervalMilliseconds);
        configuredMinIntervalMilliseconds = Volatile.Read(ref _configuredMinIntervalMilliseconds);
        var diff = adaptiveMinIntervalMilliseconds - configuredMinIntervalMilliseconds;
        var reduce = Math.Max(1, diff / 8);
        var nextAdaptiveMinIntervalMilliseconds = Math.Max(configuredMinIntervalMilliseconds, adaptiveMinIntervalMilliseconds - reduce);
        Interlocked.Exchange(ref _adaptiveMinIntervalMilliseconds, nextAdaptiveMinIntervalMilliseconds);

        while (true)
        {
            var currentAdaptiveBackoffHitCount = Volatile.Read(ref _adaptiveBackoffHitCount);
            if (currentAdaptiveBackoffHitCount <= 0)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _adaptiveBackoffHitCount, currentAdaptiveBackoffHitCount - 1, currentAdaptiveBackoffHitCount) == currentAdaptiveBackoffHitCount)
            {
                break;
            }
        }

        if (nextAdaptiveMinIntervalMilliseconds <= configuredMinIntervalMilliseconds)
        {
            Interlocked.Exchange(ref _probeWindowSuccessThreshold, MinProbeWindowSuccessCount);
        }
        else
        {
            while (true)
            {
                var currentThreshold = Volatile.Read(ref _probeWindowSuccessThreshold);
                var nextThreshold = Math.Max(MinProbeWindowSuccessCount, currentThreshold / 2);
                if (Interlocked.CompareExchange(ref _probeWindowSuccessThreshold, nextThreshold, currentThreshold) == currentThreshold)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     确保自适应最小间隔不低于基础最小间隔。
    /// </summary>
    /// <param name="configuredMinIntervalMilliseconds">基础最小请求间隔。</param>
    private void EnsureAdaptiveFloor(int configuredMinIntervalMilliseconds)
    {
        while (true)
        {
            var currentAdaptiveMinIntervalMilliseconds = Volatile.Read(ref _adaptiveMinIntervalMilliseconds);
            if (currentAdaptiveMinIntervalMilliseconds >= configuredMinIntervalMilliseconds)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _adaptiveMinIntervalMilliseconds, configuredMinIntervalMilliseconds, currentAdaptiveMinIntervalMilliseconds) == currentAdaptiveMinIntervalMilliseconds)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     按上限裁剪命中次数。
    /// </summary>
    /// <param name="adaptiveBackoffMaxHits">命中次数上限。</param>
    private void ClampAdaptiveHitCount(int adaptiveBackoffMaxHits)
    {
        if (adaptiveBackoffMaxHits <= 0)
        {
            return;
        }

        while (true)
        {
            var currentAdaptiveBackoffHitCount = Volatile.Read(ref _adaptiveBackoffHitCount);
            if (currentAdaptiveBackoffHitCount <= adaptiveBackoffMaxHits)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _adaptiveBackoffHitCount, adaptiveBackoffMaxHits, currentAdaptiveBackoffHitCount) == currentAdaptiveBackoffHitCount)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     获取自适应退避上限（毫秒）。
    /// </summary>
    /// <returns>退避上限。</returns>
    private int ResolveAdaptiveMaxIntervalMilliseconds()
    {
        var adaptiveMaxIntervalMilliseconds = Volatile.Read(ref _adaptiveMaxIntervalMilliseconds);
        if (adaptiveMaxIntervalMilliseconds > 0)
        {
            return adaptiveMaxIntervalMilliseconds;
        }

        var configuredMinIntervalMilliseconds = Math.Max(1, Volatile.Read(ref _configuredMinIntervalMilliseconds));
        return Math.Max(configuredMinIntervalMilliseconds, 60_000);
    }

    /// <summary>
    ///     安全执行 2 倍扩容，避免整数溢出。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>翻倍后的安全值。</returns>
    private static int ResolveSafeDouble(int value)
    {
        return value > int.MaxValue / 2 ? int.MaxValue : value * 2;
    }

    /// <summary>
    ///     安全执行 value * 2^power，避免整数溢出。
    /// </summary>
    /// <param name="value">底数。</param>
    /// <param name="power">指数幂（>=0）。</param>
    /// <returns>放大后的安全值。</returns>
    private static int ResolveSafePow2Multiply(int value, int power)
    {
        var result = Math.Max(1, value);
        for (var index = 0; index < power; index++)
        {
            result = ResolveSafeDouble(result);
        }

        return result;
    }
}
