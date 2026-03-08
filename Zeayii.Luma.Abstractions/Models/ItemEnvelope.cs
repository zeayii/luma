using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>数据项封装</b>
/// <para>
/// 用于在持久化通道中传递数据项及其节点上下文。
/// </para>
/// </summary>
/// <param name="Item">数据项。</param>
/// <param name="Context">产生该数据项的节点上下文。</param>
/// <param name="SourceRequest">产生该数据项的源请求。</param>
public readonly record struct ItemEnvelope<TState>(IItem Item, LumaContext<TState> Context, LumaRequest? SourceRequest)
{
    /// <summary>
    /// 节点路径。
    /// </summary>
    public string NodePath => Context.NodePath;
}
