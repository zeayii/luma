using System.Net;

namespace Zeayii.Luma.Engine.FlowControl;

/// <summary>
///     <b>节点请求流控策略配置</b>
///     <para>
///         描述单个节点类型流控策略在运行期可更新的输入参数。
///     </para>
/// </summary>
/// <param name="ScopeName">流控语义域名称。</param>
/// <param name="MinIntervalMilliseconds">基础最小请求间隔（毫秒）。</param>
/// <param name="AdaptiveBackoffEnabled">是否启用自适应退避。</param>
/// <param name="AdaptiveBackoffStatusCodes">触发退避的状态码集合。</param>
/// <param name="AdaptiveBackoffMaxHits">退避命中次数上限；0 表示不限制。</param>
/// <param name="AdaptiveMaxIntervalMilliseconds">退避间隔上限（毫秒）；0 表示使用策略默认上限。</param>
/// <param name="AdaptiveInitialIntervalMilliseconds">退避起始间隔（毫秒）；0 表示使用策略默认起始方式。</param>
public readonly record struct NodeRequestFlowControlStrategyOptions(
    string ScopeName,
    int MinIntervalMilliseconds,
    bool AdaptiveBackoffEnabled,
    IReadOnlyList<int>? AdaptiveBackoffStatusCodes,
    int AdaptiveBackoffMaxHits,
    int AdaptiveMaxIntervalMilliseconds,
    int AdaptiveInitialIntervalMilliseconds = 0)
{
    /// <summary>
    ///     获取已规范化的最小请求间隔（毫秒）。
    /// </summary>
    /// <returns>非负最小请求间隔。</returns>
    public int ResolveMinIntervalMilliseconds()
    {
        return Math.Max(0, MinIntervalMilliseconds);
    }

    /// <summary>
    ///     获取已规范化的退避命中次数上限。
    /// </summary>
    /// <returns>非负命中次数上限。</returns>
    public int ResolveAdaptiveBackoffMaxHits()
    {
        return Math.Max(0, AdaptiveBackoffMaxHits);
    }

    /// <summary>
    ///     获取已规范化的退避起始间隔（毫秒）。
    /// </summary>
    /// <returns>非负起始间隔；0 表示使用策略默认起始方式。</returns>
    public int ResolveAdaptiveInitialIntervalMilliseconds()
    {
        return Math.Max(0, AdaptiveInitialIntervalMilliseconds);
    }

    /// <summary>
    ///     构造退避触发状态码集合。
    /// </summary>
    /// <returns>去重后的状态码集合。</returns>
    public HashSet<int> BuildAdaptiveBackoffStatusCodeSet()
    {
        if (AdaptiveBackoffStatusCodes is null || AdaptiveBackoffStatusCodes.Count == 0)
        {
            return [];
        }

        var set = new HashSet<int>();
        foreach (var statusCode in AdaptiveBackoffStatusCodes)
        {
            if (statusCode is >= (int)HttpStatusCode.Continue and <= 599)
            {
                set.Add(statusCode);
            }
        }

        return set;
    }
}
