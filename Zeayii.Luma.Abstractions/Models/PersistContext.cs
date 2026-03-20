namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>持久化回调上下文</b>
///     <para>
///         向节点暴露单条数据项持久化前后所需的上下文信息。
///     </para>
/// </summary>
public readonly record struct PersistContext<TState>(LumaContext<TState> NodeContext, LumaRequest? SourceRequest, int ItemIndexInBatch);