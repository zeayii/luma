using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>数据项包裹</b>
/// <para>
/// 表示进入持久化管道的数据项及其来源上下文。
/// </para>
/// </summary>
/// <param name="Item">数据项对象。</param>
/// <param name="Context">产生该数据项的节点上下文。</param>
public readonly record struct ItemEnvelope(IItem Item, LumaNodeContext Context)
{
    /// <summary>
    /// 节点路径。
    /// </summary>
    public string NodePath => Context.NodePath;
}

