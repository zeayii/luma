namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点请求流控配置</b>
/// <para>
/// 描述节点在运行期对“同类型实例共享节流器”的配置快照。
/// </para>
/// </summary>
/// <param name="ScopeName">流控语义名称（用于日志与诊断）。</param>
/// <param name="MinIntervalMilliseconds">最小请求间隔毫秒数；小于等于 0 表示不启用。</param>
/// <param name="AdaptiveBackoffEnabled">是否启用自适应退避。</param>
/// <param name="AdaptiveBackoffStatusCodes">触发自适应退避的 HTTP 状态码集合；为空表示不触发。</param>
/// <param name="AdaptiveBackoffMaxHits">自适应退避命中次数上限；小于等于 0 表示不限制命中次数。</param>
/// <param name="AdaptiveMaxIntervalMilliseconds">自适应退避上限毫秒数；小于等于 0 表示按默认上限处理。</param>
/// <param name="FlowControlStrategyKey">流控策略键；为空时回退默认策略。</param>
public readonly record struct NodeFlowControlOptions(
    string ScopeName,
    int MinIntervalMilliseconds,
    bool AdaptiveBackoffEnabled,
    IReadOnlyList<int>? AdaptiveBackoffStatusCodes,
    int AdaptiveBackoffMaxHits,
    int AdaptiveMaxIntervalMilliseconds,
    string? FlowControlStrategyKey = "stable-probe")
{
    /// <summary>
    /// 不启用流控的默认配置。
    /// </summary>
    public static NodeFlowControlOptions Disabled { get; } = new("default", 0, false, null, 0, 0, "stable-probe");
}
