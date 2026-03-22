using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
///     <b>抓取节点抽象基类</b>
///     <para>
///         节点负责请求构建、响应处理、下载处理、子节点扩展与数据项输出。
///         引擎按节点生命周期调用节点能力，并在每个阶段结束后统一分发节点输出。
///     </para>
/// </summary>
/// <typeparam name="TState">节点状态类型。</typeparam>
public abstract class LumaNode<TState>
{
    /// <summary>
    ///     节点输出同步锁。
    /// </summary>
    private readonly Lock _outputSyncRoot = new();

    /// <summary>
    ///     待分发子节点缓冲区。
    /// </summary>
    private readonly List<NodeChildBinding<TState>> _pendingChildren = [];

    /// <summary>
    ///     待持久化数据项缓冲区。
    /// </summary>
    private readonly List<IItem> _pendingItems = [];

    /// <summary>
    ///     待分发请求缓冲区。
    /// </summary>
    private readonly List<LumaRequest> _pendingRequests = [];

    /// <summary>
    ///     停止原因。
    /// </summary>
    private string _stopReason = string.Empty;

    /// <summary>
    ///     停止标记。
    /// </summary>
    private bool _stopRequested;

    /// <summary>
    ///     初始化节点。
    /// </summary>
    /// <param name="key">节点键。</param>
    protected LumaNode(string key)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentNullException(nameof(key)) : key;
    }

    /// <summary>
    ///     节点键。
    /// </summary>
    public string Key { get; }

    /// <summary>
    ///     节点执行选项。
    /// </summary>
    public virtual NodeExecutionOptions ExecutionOptions => NodeExecutionOptions.Default;

    /// <summary>
    ///     连续命中已存在结果后建议停止的阈值。
    /// </summary>
    public virtual int ConsecutiveExistingStopThreshold => 0;

    /// <summary>
    ///     解析节点请求流控配置。
    ///     <para>
    ///         该配置用于引擎构建“按节点类型共享”的请求节流器。
    ///         默认返回 <see cref="NodeFlowControlOptions.Disabled" />，表示不启用额外节点级流控。
    ///     </para>
    /// </summary>
    /// <param name="context">节点上下文。</param>
    /// <returns>节点流控配置快照。</returns>
    public virtual NodeFlowControlOptions ResolveFlowControlOptions(LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        return NodeFlowControlOptions.Disabled;
    }

    /// <summary>
    ///     构建初始请求流。
    /// </summary>
    /// <param name="context">节点上下文。</param>
    /// <returns>请求异步流。</returns>
    public virtual async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>
    ///     处理普通响应。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="context">节点上下文。</param>
    /// <returns>异步任务。</returns>
    public abstract ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TState> context);

    /// <summary>
    ///     判断是否进入下载阶段。
    /// </summary>
    /// <param name="response">普通响应。</param>
    /// <param name="context">节点上下文。</param>
    /// <returns>需要下载返回 true。</returns>
    public virtual ValueTask<bool> ShouldDownloadAsync(HttpResponseMessage response, LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    /// <summary>
    ///     构建下载请求流。
    /// </summary>
    /// <param name="response">普通响应。</param>
    /// <param name="context">节点上下文。</param>
    /// <returns>下载请求异步流。</returns>
    public virtual async IAsyncEnumerable<LumaRequest> BuildDownloadRequestsAsync(HttpResponseMessage response, LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>
    ///     处理下载响应。
    /// </summary>
    /// <param name="response">下载响应。</param>
    /// <param name="request">下载请求。</param>
    /// <param name="context">节点上下文。</param>
    /// <returns>异步任务。</returns>
    public virtual ValueTask HandleDownloadResponseAsync(HttpResponseMessage response, LumaRequest request, LumaContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     判断数据项是否应进入持久化管道。
    /// </summary>
    /// <param name="item">数据项。</param>
    /// <param name="context">持久化上下文。</param>
    /// <returns>应持久化返回 true。</returns>
    public virtual ValueTask<bool> ShouldPersistAsync(IItem item, PersistContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    /// <summary>
    ///     持久化完成回调。
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
    ///     节点异常处理钩子。
    /// </summary>
    /// <param name="exception">异常对象。</param>
    /// <param name="context">异常上下文。</param>
    /// <returns>异常处理动作。</returns>
    public virtual ValueTask<NodeExceptionAction> OnExceptionAsync(Exception exception, NodeExceptionContext<TState> context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(NodeExceptionAction.StopNode);
    }

    /// <summary>
    ///     添加子节点（复用父状态）。
    /// </summary>
    /// <param name="child">子节点实例。</param>
    protected void AddChild(LumaNode<TState> child)
    {
        AddChild(child, static state => state);
    }

    /// <summary>
    ///     添加子节点（使用状态映射）。
    /// </summary>
    /// <param name="child">子节点实例。</param>
    /// <param name="stateMapper">父状态到子状态的映射函数。</param>
    protected void AddChild(LumaNode<TState> child, Func<TState, TState> stateMapper)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(stateMapper);

        lock (_outputSyncRoot)
        {
            _pendingChildren.Add(new NodeChildBinding<TState>(child, stateMapper));
        }
    }

    /// <summary>
    ///     添加待调度请求。
    /// </summary>
    /// <param name="request">请求对象。</param>
    protected void AddRequest(LumaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_outputSyncRoot)
        {
            _pendingRequests.Add(request);
        }
    }

    /// <summary>
    ///     添加待持久化数据项。
    /// </summary>
    /// <param name="item">数据项。</param>
    protected void AddItem(IItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_outputSyncRoot)
        {
            _pendingItems.Add(item);
        }
    }

    /// <summary>
    ///     请求停止当前节点。
    /// </summary>
    /// <param name="reason">停止原因。</param>
    protected void StopNode(string reason)
    {
        lock (_outputSyncRoot)
        {
            _stopRequested = true;
            _stopReason = reason;
        }
    }

    /// <summary>
    ///     提取并清空当前节点输出。
    /// </summary>
    /// <returns>待分发批次。</returns>
    public NodeDispatchBatch<TState> DrainDispatchBatch()
    {
        lock (_outputSyncRoot)
        {
            var requests = _pendingRequests.Count == 0 ? Array.Empty<LumaRequest>() : _pendingRequests.ToArray();
            var children = _pendingChildren.Count == 0 ? Array.Empty<NodeChildBinding<TState>>() : _pendingChildren.ToArray();
            var items = _pendingItems.Count == 0 ? Array.Empty<IItem>() : _pendingItems.ToArray();
            var stopNode = _stopRequested;
            var stopReason = _stopReason;

            _pendingRequests.Clear();
            _pendingChildren.Clear();
            _pendingItems.Clear();
            _stopRequested = false;
            _stopReason = string.Empty;

            return new NodeDispatchBatch<TState>
            {
                Requests = requests,
                Children = children,
                Items = items,
                StopNode = stopNode,
                StopReason = stopReason
            };
        }
    }

    /// <summary>
    ///     返回节点展示文本。
    /// </summary>
    /// <returns>展示文本。</returns>
    public override string ToString()
    {
        return Key;
    }
}