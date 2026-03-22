using System.Collections.Concurrent;

namespace Zeayii.Luma.Engine.FlowControl;

/// <summary>
///     <b>节点请求流控策略注册表</b>
///     <para>
///         提供策略工厂注册与解析能力，用于在运行时按策略键创建对应算法实例。
///     </para>
/// </summary>
public static class NodeRequestFlowControlStrategyRegistry
{
    /// <summary>
    ///     默认策略键。
    /// </summary>
    public const string DefaultStrategyKey = "stable-probe";

    /// <summary>
    ///     策略工厂映射表。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<INodeRequestFlowControlStrategy>> Factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     初始化注册表并注册默认策略。
    /// </summary>
    static NodeRequestFlowControlStrategyRegistry()
    {
        Register(DefaultStrategyKey, static () => new StableProbeNodeRequestFlowControlStrategy());
    }

    /// <summary>
    ///     注册策略工厂。
    /// </summary>
    /// <param name="strategyKey">策略键。</param>
    /// <param name="factory">策略工厂。</param>
    public static void Register(string strategyKey, Func<INodeRequestFlowControlStrategy> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyKey);
        ArgumentNullException.ThrowIfNull(factory);
        Factories[strategyKey.Trim()] = factory;
    }

    /// <summary>
    ///     按策略键解析策略实例；未命中时回退默认策略。
    /// </summary>
    /// <param name="strategyKey">策略键。</param>
    /// <returns>策略实例。</returns>
    public static INodeRequestFlowControlStrategy ResolveOrDefault(string? strategyKey)
    {
        if (!string.IsNullOrWhiteSpace(strategyKey) && Factories.TryGetValue(strategyKey.Trim(), out var factory)) return factory();

        if (Factories.TryGetValue(DefaultStrategyKey, out var defaultFactory)) return defaultFactory();

        return new StableProbeNodeRequestFlowControlStrategy();
    }
}