namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点快照</b>
///     <para>
///         供呈现层展示的节点状态快照。
///     </para>
/// </summary>
public readonly record struct NodeSnapshot(string Path, string DisplayText, int Depth, NodeExecutionStatus Status, long StoredCount, long AlreadyExistsCount, long QueuedRequestCount, long ActiveRequestCount, string Reason);