using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>抓取节点抽象基类</b>
/// <para>
/// 节点天然表达树状结构，可持有子节点集合，并以异步流方式向框架产出工作单元。
/// </para>
/// </summary>
public abstract class LumaNode
{
    /// <summary>
    /// 子节点列表。
    /// </summary>
    private readonly List<LumaNode> _children = [];

    /// <summary>
    /// 初始化节点。
    /// </summary>
    /// <param name="key">节点键。</param>
    protected LumaNode(string key)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentNullException(nameof(key)) : key;
    }

    /// <summary>
    /// 节点键。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 父节点。
    /// </summary>
    public LumaNode? Parent { get; private set; }

    /// <summary>
    /// 子节点集合。
    /// </summary>
    public IReadOnlyList<LumaNode> Children => _children;

    /// <summary>
    /// 连续命中已存在结果后建议停止的阈值。
    /// </summary>
    public virtual int ConsecutiveExistingStopThreshold => 0;

    /// <summary>
    /// 添加子节点。
    /// </summary>
    /// <param name="child">子节点。</param>
    protected void AddChild(LumaNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>
    /// 节点启动时产出初始工作单元。
    /// </summary>
    /// <param name="context">节点上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>节点输出异步流。</returns>
    public virtual async IAsyncEnumerable<NodeOutput> StartAsync(
        LumaNodeContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var child in _children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return NodeOutput.FromChild(child);
        }
    }

    /// <summary>
    /// 节点解析响应后产出后续工作单元。
    /// </summary>
    /// <param name="response">抓取响应。</param>
    /// <param name="context">节点上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>节点输出异步流。</returns>
    public virtual async IAsyncEnumerable<NodeOutput> ParseAsync(
        LumaResponse response,
        LumaNodeContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>
    /// 返回节点展示文本。
    /// </summary>
    /// <returns>展示文本。</returns>
    public override string ToString() => Key;
}


