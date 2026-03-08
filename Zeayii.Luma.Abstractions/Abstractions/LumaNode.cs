using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>抓取节点抽象基类</b>
/// <para>
/// 节点表达一个页面语义步骤，负责请求描述、响应解析、子节点扩展与数据产出。
/// </para>
/// </summary>
/// <typeparam name="TState">实现层定义的运行状态类型。</typeparam>
public abstract class LumaNode<TState>
{
    /// <summary>
    /// 子节点列表。
    /// </summary>
    private readonly List<LumaNode<TState>> _children = [];

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
    /// 子节点集合。
    /// </summary>
    public IReadOnlyList<LumaNode<TState>> Children => _children;

    /// <summary>
    /// 节点执行选项。
    /// </summary>
    public virtual NodeExecutionOptions ExecutionOptions => NodeExecutionOptions.Default;

    /// <summary>
    /// 连续命中已存在结果后建议停止的阈值。
    /// </summary>
    public virtual int ConsecutiveExistingStopThreshold => 0;

    /// <summary>
    /// 添加子节点。
    /// </summary>
    /// <param name="child">子节点。</param>
    protected void AddChild(LumaNode<TState> child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    /// <summary>
    /// 节点启动阶段。
    /// </summary>
    /// <param name="context">节点上下文。</param>
    /// <returns>节点处理结果。</returns>
    public virtual ValueTask<NodeResult<TState>> StartAsync(LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (_children.Count == 0)
        {
            return ValueTask.FromResult(NodeResult<TState>.Empty);
        }

        return ValueTask.FromResult(new NodeResult<TState>
        {
            Children = _children.ToArray()
        });
    }

    /// <summary>
    /// 处理响应。
    /// </summary>
    /// <param name="response">原生 HTTP 响应。</param>
    /// <param name="context">节点上下文。</param>
    /// <returns>节点处理结果。</returns>
    public abstract ValueTask<NodeResult<TState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TState> context);

    /// <summary>
    /// 判断数据项是否应进入持久化管道。
    /// </summary>
    /// <param name="item">数据项。</param>
    /// <param name="context">持久化上下文。</param>
    /// <returns>应持久化返回 <see langword="true"/>。</returns>
    public virtual ValueTask<bool> ShouldPersistAsync(IItem item, PersistContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// 持久化完成回调。
    /// </summary>
    /// <param name="item">数据项。</param>
    /// <param name="persistResult">持久化结果。</param>
    /// <param name="context">持久化上下文。</param>
    /// <returns>异步任务。</returns>
    public virtual ValueTask OnPersistedAsync(IItem item, PersistResult persistResult, PersistContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 返回节点展示文本。
    /// </summary>
    /// <returns>展示文本。</returns>
    public override string ToString() => Key;
}
