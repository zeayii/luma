using System.Net;
using Zeayii.Luma.Engine.FlowControl;

namespace Zeayii.Luma.Engine.Tests.FlowControl;

/// <summary>
///     <b>稳态探测流控策略测试</b>
///     <para>
///         验证退避、冷却恢复、命中上限以及策略注册表可插拔行为。
///     </para>
/// </summary>
public sealed class StableProbeNodeRequestFlowControlStrategyTests
{
    /// <summary>
    ///     验证触发风控后会快速退避，冷却与成功窗口满足后才会缓慢恢复。
    /// </summary>
    [Fact]
    public void ObserveResponseShouldBackoffFastAndRecoverSlowlyAfterCooldown()
    {
        var strategy = new StableProbeNodeRequestFlowControlStrategy();
        strategy.Update(new NodeRequestFlowControlStrategyOptions(
            "test.scope",
            10,
            true,
            [429],
            0,
            0));

        Assert.Equal(10, strategy.ResolveEffectiveMinIntervalMilliseconds());

        strategy.ObserveResponse(HttpStatusCode.TooManyRequests, 0);
        Assert.Equal(20, strategy.ResolveEffectiveMinIntervalMilliseconds());

        strategy.ObserveResponse(HttpStatusCode.OK, 1_000);
        Assert.Equal(20, strategy.ResolveEffectiveMinIntervalMilliseconds());

        for (var index = 0; index < 48; index++)
        {
            strategy.ObserveResponse(HttpStatusCode.OK, 30_000 + index);
        }

        var recoveredInterval = strategy.ResolveEffectiveMinIntervalMilliseconds();
        Assert.InRange(recoveredInterval, 10, 19);
    }

    /// <summary>
    ///     验证达到命中上限后不会继续放大退避间隔。
    /// </summary>
    [Fact]
    public void ObserveResponseShouldStopEscalationWhenMaxHitsReached()
    {
        var strategy = new StableProbeNodeRequestFlowControlStrategy();
        strategy.Update(new NodeRequestFlowControlStrategyOptions(
            "test.scope",
            10,
            true,
            [429],
            2,
            0));

        strategy.ObserveResponse(HttpStatusCode.TooManyRequests, 0);
        Assert.Equal(20, strategy.ResolveEffectiveMinIntervalMilliseconds());

        strategy.ObserveResponse(HttpStatusCode.TooManyRequests, 1);
        Assert.Equal(40, strategy.ResolveEffectiveMinIntervalMilliseconds());

        strategy.ObserveResponse(HttpStatusCode.TooManyRequests, 2);
        Assert.Equal(40, strategy.ResolveEffectiveMinIntervalMilliseconds());
    }

    /// <summary>
    ///     验证退避后探测窗口会扩大，避免短周期抖动。
    /// </summary>
    [Fact]
    public void ObserveResponseShouldUseLongerProbeWindowAfterBackoff()
    {
        var strategy = new StableProbeNodeRequestFlowControlStrategy();
        strategy.Update(new NodeRequestFlowControlStrategyOptions(
            "test.scope",
            10,
            true,
            [429],
            0,
            0));

        strategy.ObserveResponse(HttpStatusCode.TooManyRequests, 0);
        Assert.Equal(20, strategy.ResolveEffectiveMinIntervalMilliseconds());

        for (var index = 0; index < 24; index++)
        {
            strategy.ObserveResponse(HttpStatusCode.OK, 30_000 + index);
        }

        Assert.Equal(20, strategy.ResolveEffectiveMinIntervalMilliseconds());

        for (var index = 24; index < 48; index++)
        {
            strategy.ObserveResponse(HttpStatusCode.OK, 30_000 + index);
        }

        Assert.InRange(strategy.ResolveEffectiveMinIntervalMilliseconds(), 10, 19);
    }

    /// <summary>
    ///     验证注册表支持挂载自定义策略并可按键解析。
    /// </summary>
    [Fact]
    public void RegistryShouldResolveRegisteredCustomStrategy()
    {
        var strategyKey = $"custom-{Guid.NewGuid():N}";
        NodeRequestFlowControlStrategyRegistry.Register(strategyKey, static () => new FakeNodeRequestFlowControlStrategy());

        var strategy = NodeRequestFlowControlStrategyRegistry.ResolveOrDefault(strategyKey);
        Assert.IsType<FakeNodeRequestFlowControlStrategy>(strategy);
    }

    /// <summary>
    ///     用于验证注册表可插拔能力的测试策略。
    /// </summary>
    private sealed class FakeNodeRequestFlowControlStrategy : INodeRequestFlowControlStrategy
    {
        /// <inheritdoc />
        public void Update(NodeRequestFlowControlStrategyOptions options)
        {
            _ = options;
        }

        /// <inheritdoc />
        public int ResolveEffectiveMinIntervalMilliseconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public void ObserveResponse(HttpStatusCode statusCode, long nowUtcMilliseconds)
        {
            _ = statusCode;
            _ = nowUtcMilliseconds;
        }
    }
}