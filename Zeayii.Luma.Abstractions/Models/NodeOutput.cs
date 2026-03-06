using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点输出抽象</b>
/// <para>
/// 节点可向框架产出请求、数据项或子节点。
/// </para>
/// </summary>
public abstract record NodeOutput
{
    /// <summary>
    /// 构造请求输出。
    /// </summary>
    /// <param name="request">抓取请求。</param>
    /// <returns>节点输出。</returns>
    public static NodeOutput FromRequest(LumaRequest request) => new RequestNodeOutput(request);

    /// <summary>
    /// 构造数据项输出。
    /// </summary>
    /// <param name="item">数据项。</param>
    /// <returns>节点输出。</returns>
    public static NodeOutput FromItem(IItem item) => new ItemNodeOutput(item);

    /// <summary>
    /// 构造子节点输出。
    /// </summary>
    /// <param name="node">子节点。</param>
    /// <returns>节点输出。</returns>
    public static NodeOutput FromChild(LumaNode node) => new ChildNodeOutput(node);
}

/// <summary>
/// <b>请求输出</b>
/// </summary>
/// <param name="Request">抓取请求。</param>
public sealed record RequestNodeOutput(LumaRequest Request) : NodeOutput;

/// <summary>
/// <b>数据项输出</b>
/// </summary>
/// <param name="Item">数据项。</param>
public sealed record ItemNodeOutput(IItem Item) : NodeOutput;

/// <summary>
/// <b>子节点输出</b>
/// </summary>
/// <param name="Node">子节点。</param>
public sealed record ChildNodeOutput(LumaNode Node) : NodeOutput;

