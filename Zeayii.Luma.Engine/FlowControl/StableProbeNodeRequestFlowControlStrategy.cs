using System.Net;

namespace Zeayii.Luma.Engine.FlowControl;

/// <summary>
/// <b>稳态探测节点流控策略</b>
/// <para>
/// 采用“快速退让 + 冷却窗口 + 低频探测恢复”的闭环策略：
/// 触发风控时指数退避，成功阶段在满足冷却与成功窗口后缓慢恢复。
/// </para>
/// </summary>
public sealed class StableProbeNodeRequestFlowControlStrategy : INodeRequestFlowControlStrategy
{
    /// <summary>
    /// 最小探测窗口成功次数。
    /// </summary>
    private const int MinProbeWindowSuccessCount = 24;

    /// <summary>
    /// 最大探测窗口成功次数。
    /// </summary>
    private const int MaxProbeWindowSuccessCount = 768;

    /// <summary>
    /// 退避触发后最小冷却时长（毫秒）。
    /// </summary>
    private const int CooldownMilliseconds = 30_000;

    /// <summary>
    /// 基础最小请求间隔（毫秒）。
    /// </summary>
    private int _configuredMinIntervalMilliseconds;

    /// <summary>
    /// 当前自适应最小请求间隔（毫秒）。
    /// </summary>
    private int _adaptiveMinIntervalMilliseconds;

    /// <summary>
    /// 是否启用自适应退避。
    /// </summary>
    private bool _adaptiveBackoffEnabled;

    /// <summary>
    /// 触发自适应退避状态码集合。
    /// </summary>
    private HashSet<int> _adaptiveBackoffStatusCodes = [];

    /// <summary>
    /// 自适应退避命中次数上限。
    /// </summary>
    private int _adaptiveBackoffMaxHits;

    /// <summary>
    /// 当前命中次数。
    /// </summary>
    private int _adaptiveBackoffHitCount;

    /// <summary>
    /// 自适应退避上限（毫秒）。
    /// </summary>
    private int _adaptiveMaxIntervalMilliseconds;

    /// <summary>
    /// 探测窗口成功阈值。
    /// </summary>
    private int _probeWindowSuccessThreshold = MinProbeWindowSuccessCount;

    /// <summary>
    /// 当前探测窗口成功计数。
    /// </summary>
    private int _probeWindowSuccessCount;

    /// <summary>
    /// 冷却期截止 UTC 毫秒时间戳。
    /// </summary>
    private long _cooldownUntilUtcMilliseconds;

    /// <inheritdoc />
    public void Update(NodeRequestFlowControlStrategyOptions options)
    {
        _configuredMinIntervalMilliseconds = options.ResolveMinIntervalMilliseconds();
        _adaptiveBackoffEnabled = options.AdaptiveBackoffEnabled;
        _adaptiveBackoffStatusCodes = options.BuildAdaptiveBackoffStatusCodeSet();
        _adaptiveBackoffMaxHits = options.ResolveAdaptiveBackoffMaxHits();
        _adaptiveMaxIntervalMilliseconds = Math.Max(0, options.AdaptiveMaxIntervalMilliseconds);

        if (_adaptiveMinIntervalMilliseconds < _configuredMinIntervalMilliseconds)
        {
            _adaptiveMinIntervalMilliseconds = _configuredMinIntervalMilliseconds;
        }

        if (_adaptiveBackoffMaxHits > 0 && _adaptiveBackoffHitCount > _adaptiveBackoffMaxHits)
        {
            _adaptiveBackoffHitCount = _adaptiveBackoffMaxHits;
        }
    }

    /// <inheritdoc />
    public int ResolveEffectiveMinIntervalMilliseconds()
    {
        return Math.Max(_configuredMinIntervalMilliseconds, _adaptiveMinIntervalMilliseconds);
    }

    /// <inheritdoc />
    public void ObserveResponse(HttpStatusCode statusCode, long nowUtcMilliseconds)
    {
        if (!_adaptiveBackoffEnabled || _adaptiveBackoffStatusCodes.Count == 0)
        {
            return;
        }

        var statusCodeValue = (int)statusCode;
        if (_adaptiveBackoffStatusCodes.Contains(statusCodeValue))
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
    /// 处理风控触发事件。
    /// </summary>
    /// <param name="nowUtcMilliseconds">当前 UTC 时间戳（毫秒）。</param>
    private void ObserveBackoffTrigger(long nowUtcMilliseconds)
    {
        if (_adaptiveBackoffMaxHits > 0 && _adaptiveBackoffHitCount >= _adaptiveBackoffMaxHits)
        {
            _probeWindowSuccessCount = 0;
            _cooldownUntilUtcMilliseconds = nowUtcMilliseconds + CooldownMilliseconds;
            return;
        }

        var baseline = Math.Max(1, ResolveEffectiveMinIntervalMilliseconds());
        var next = ResolveSafeDouble(baseline);
        var adaptiveCap = ResolveAdaptiveMaxIntervalMilliseconds();
        _adaptiveMinIntervalMilliseconds = Math.Min(adaptiveCap, next);
        _adaptiveBackoffHitCount += 1;
        _probeWindowSuccessCount = 0;
        _cooldownUntilUtcMilliseconds = nowUtcMilliseconds + CooldownMilliseconds;
        _probeWindowSuccessThreshold = Math.Min(MaxProbeWindowSuccessCount, Math.Max(MinProbeWindowSuccessCount, _probeWindowSuccessThreshold * 2));
    }

    /// <summary>
    /// 处理成功响应事件。
    /// </summary>
    /// <param name="nowUtcMilliseconds">当前 UTC 时间戳（毫秒）。</param>
    private void ObserveSuccess(long nowUtcMilliseconds)
    {
        if (_adaptiveMinIntervalMilliseconds <= _configuredMinIntervalMilliseconds)
        {
            _adaptiveMinIntervalMilliseconds = _configuredMinIntervalMilliseconds;
            _probeWindowSuccessCount = 0;
            return;
        }

        if (nowUtcMilliseconds < _cooldownUntilUtcMilliseconds)
        {
            return;
        }

        _probeWindowSuccessCount += 1;
        if (_probeWindowSuccessCount < _probeWindowSuccessThreshold)
        {
            return;
        }

        _probeWindowSuccessCount = 0;
        var diff = _adaptiveMinIntervalMilliseconds - _configuredMinIntervalMilliseconds;
        var reduce = Math.Max(1, diff / 8);
        _adaptiveMinIntervalMilliseconds = Math.Max(_configuredMinIntervalMilliseconds, _adaptiveMinIntervalMilliseconds - reduce);
        if (_adaptiveBackoffHitCount > 0)
        {
            _adaptiveBackoffHitCount -= 1;
        }

        if (_adaptiveMinIntervalMilliseconds <= _configuredMinIntervalMilliseconds)
        {
            _probeWindowSuccessThreshold = MinProbeWindowSuccessCount;
        }
        else
        {
            _probeWindowSuccessThreshold = Math.Max(MinProbeWindowSuccessCount, _probeWindowSuccessThreshold / 2);
        }
    }

    /// <summary>
    /// 获取自适应退避上限（毫秒）。
    /// </summary>
    /// <returns>退避上限。</returns>
    private int ResolveAdaptiveMaxIntervalMilliseconds()
    {
        if (_adaptiveMaxIntervalMilliseconds > 0)
        {
            return _adaptiveMaxIntervalMilliseconds;
        }

        var configured = Math.Max(1, _configuredMinIntervalMilliseconds);
        return Math.Max(configured, 60_000);
    }

    /// <summary>
    /// 安全执行 2 倍扩容，避免整数溢出。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>翻倍后的安全值。</returns>
    private static int ResolveSafeDouble(int value)
    {
        return value > int.MaxValue / 2 ? int.MaxValue : value * 2;
    }
}

