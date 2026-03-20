namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点待分发批次</b>
/// <para>
/// 由节点内部累积，供引擎在阶段结束后统一分发请求、子节点与数据项。
/// </para>
/// </summary>
/// <typeparam name="TState">节点状态类型。</typeparam>
public sealed class NodeDispatchBatch<TState>
{
    /// <summary>
    /// 待调度请求集合。
    /// </summary>
    public required IReadOnlyList<LumaRequest> Requests { get; init; }

    /// <summary>
    /// 待注册子节点集合。
    /// </summary>
    public required IReadOnlyList<NodeChildBinding<TState>> Children { get; init; }

    /// <summary>
    /// 待持久化数据项集合。
    /// </summary>
    public required IReadOnlyList<Abstractions.IItem> Items { get; init; }

    /// <summary>
    /// 是否要求停止当前节点。
    /// </summary>
    public required bool StopNode { get; init; }

    /// <summary>
    /// 节点停止原因。
    /// </summary>
    public required string StopReason { get; init; }
}
