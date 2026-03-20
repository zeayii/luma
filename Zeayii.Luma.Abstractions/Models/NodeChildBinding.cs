using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>子节点映射定义</b>
///     <para>
///         描述父节点扩展出的子节点实例与状态映射函数。
///     </para>
/// </summary>
/// <typeparam name="TState">节点状态类型。</typeparam>
/// <param name="Node">子节点实例。</param>
/// <param name="StateMapper">父状态到子状态的映射函数。</param>
public readonly record struct NodeChildBinding<TState>(LumaNode<TState> Node, Func<TState, TState> StateMapper);