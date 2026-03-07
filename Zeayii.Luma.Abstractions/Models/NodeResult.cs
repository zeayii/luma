using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点处理结果</b>
/// <para>
/// 统一承载节点在某次生命周期阶段产出的请求、子节点和待持久化数据项。
/// </para>
/// </summary>
public sealed record NodeResult
{
    /// <summary>
    /// 空结果实例。
    /// </summary>
    public static NodeResult Empty { get; } = new();

    /// <summary>
    /// 下一批请求。
    /// </summary>
    public IReadOnlyList<LumaRequest> Requests { get; init; } = Array.Empty<LumaRequest>();

    /// <summary>
    /// 下一批子节点。
    /// </summary>
    public IReadOnlyList<LumaNode> Children { get; init; } = Array.Empty<LumaNode>();

    /// <summary>
    /// 当前批次待持久化数据项。
    /// </summary>
    public IReadOnlyList<IItem> Items { get; init; } = Array.Empty<IItem>();

    /// <summary>
    /// 是否建议停止当前节点。
    /// </summary>
    public bool StopNode { get; init; }

    /// <summary>
    /// 建议停止原因。
    /// </summary>
    public string StopReason { get; init; } = string.Empty;
}
