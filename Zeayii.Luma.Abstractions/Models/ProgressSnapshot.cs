namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>运行进度快照</b>
/// </summary>
public sealed class ProgressSnapshot
{
    /// <summary>
    /// 运行名称。
    /// </summary>
    public string RunName { get; init; } = string.Empty;

    /// <summary>
    /// 命令名称。
    /// </summary>
    public string CommandName { get; init; } = string.Empty;

    /// <summary>
    /// 运行标识。
    /// </summary>
    public Guid RunId { get; init; }

    /// <summary>
    /// 当前状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 已持久化成功数量。
    /// </summary>
    public long StoredItemCount { get; init; }

    /// <summary>
    /// 当前活跃请求数量。
    /// </summary>
    public long ActiveRequestCount { get; init; }

    /// <summary>
    /// 当前排队请求数量。
    /// </summary>
    public long QueuedRequestCount { get; init; }

    /// <summary>
    /// 运行时长。
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// 节点快照集合。
    /// </summary>
    public IReadOnlyList<NodeSnapshot> Nodes { get; init; } = Array.Empty<NodeSnapshot>();
}

