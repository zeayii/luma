using Microsoft.Extensions.Logging;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
///     <b>节点运行时宿主</b>
///     <para>
///         持有节点生命周期、取消源、上下文和状态。
///     </para>
/// </summary>
internal sealed class LumaNodeRuntime<TState> : IAsyncDisposable
{
    /// <summary>
    ///     子节点并发闸门。
    /// </summary>
    private readonly SemaphoreSlim _childConcurrencyGate;

    /// <summary>
    ///     子树完成通知源。
    /// </summary>
    private readonly TaskCompletionSource _subtreeCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     节点初始化中的数量。
    ///     <para>
    ///         用于覆盖 BuildRequests/首轮分发阶段，避免初始化期间被误判为“已排空”。
    ///     </para>
    /// </summary>
    private long _initializingCount;

    /// <summary>
    ///     子树中待完成的直接子节点数量。
    /// </summary>
    private long _pendingChildSubtreeCount;

    /// <summary>
    ///     子节点注册中的数量。
    /// </summary>
    private long _registeringChildCount;

    /// <summary>
    ///     子树完成标记。
    /// </summary>
    private int _subtreeCompleted;

    /// <summary>
    ///     初始化节点运行时。
    /// </summary>
    /// <param name="node">抽象层节点。</param>
    /// <param name="path">节点路径。</param>
    /// <param name="depth">节点深度。</param>
    /// <param name="runId">运行标识。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="state">节点状态。</param>
    /// <param name="htmlParser">HTML 解析器。</param>
    /// <param name="cookieAccessor">Cookie 访问器。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    /// <param name="parentToken">父级取消令牌。</param>
    public LumaNodeRuntime(
        LumaNode<TState> node,
        string path,
        int depth,
        Guid runId,
        string runName,
        string commandName,
        TState state,
        IHtmlParser htmlParser,
        ICookieAccessor cookieAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken parentToken)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentNullException(nameof(path)) : path;
        Depth = depth;
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        Context = new LumaContext<TState>(
            runId,
            runName,
            commandName,
            Path,
            Depth,
            node.ExecutionOptions.DefaultRouteKind,
            state,
            htmlParser ?? throw new ArgumentNullException(nameof(htmlParser)),
            cookieAccessor ?? throw new ArgumentNullException(nameof(cookieAccessor)),
            (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(node.GetType()),
            CancellationTokenSource.Token);
        State = new LumaNodeState();
        _childConcurrencyGate = new SemaphoreSlim(Node.ExecutionOptions.ResolveChildMaxConcurrency());
    }

    /// <summary>
    ///     抽象层节点。
    /// </summary>
    public LumaNode<TState> Node { get; }

    /// <summary>
    ///     节点路径。
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     节点深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    ///     节点取消源。
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>
    ///     节点上下文。
    /// </summary>
    public LumaContext<TState> Context { get; }

    /// <summary>
    ///     节点状态。
    /// </summary>
    public LumaNodeState State { get; }

    /// <summary>
    ///     子树完成任务。
    /// </summary>
    public Task SubtreeCompletionTask => _subtreeCompletionSource.Task;

    /// <summary>
    ///     子节点注册中的数量。
    /// </summary>
    public long RegisteringChildCount => Interlocked.Read(ref _registeringChildCount);

    /// <summary>
    ///     节点初始化中的数量。
    /// </summary>
    public long InitializingCount => Interlocked.Read(ref _initializingCount);

    /// <summary>
    ///     子树中待完成的直接子节点数量。
    /// </summary>
    public long PendingChildSubtreeCount => Interlocked.Read(ref _pendingChildSubtreeCount);

    /// <summary>
    ///     释放运行时资源。
    /// </summary>
    /// <returns>异步任务。</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _childConcurrencyGate.Dispose();
        CancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     进入子节点扩展并发闸门。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task WaitChildSlotAsync(CancellationToken cancellationToken)
    {
        return _childConcurrencyGate.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     释放子节点扩展并发闸门。
    /// </summary>
    public void ReleaseChildSlot()
    {
        _childConcurrencyGate.Release();
    }

    /// <summary>
    ///     增加子节点注册中的数量。
    /// </summary>
    public void IncrementRegisteringChild()
    {
        Interlocked.Increment(ref _registeringChildCount);
    }

    /// <summary>
    ///     减少子节点注册中的数量。
    /// </summary>
    public void DecrementRegisteringChild()
    {
        Interlocked.Decrement(ref _registeringChildCount);
    }

    /// <summary>
    ///     增加节点初始化中的数量。
    /// </summary>
    public void IncrementInitializing()
    {
        Interlocked.Increment(ref _initializingCount);
    }

    /// <summary>
    ///     减少节点初始化中的数量。
    /// </summary>
    public void DecrementInitializing()
    {
        Interlocked.Decrement(ref _initializingCount);
    }

    /// <summary>
    ///     增加待完成子节点子树数量。
    /// </summary>
    public void IncrementPendingChildSubtree()
    {
        Interlocked.Increment(ref _pendingChildSubtreeCount);
    }

    /// <summary>
    ///     减少待完成子节点子树数量。
    /// </summary>
    public void DecrementPendingChildSubtree()
    {
        Interlocked.Decrement(ref _pendingChildSubtreeCount);
    }

    /// <summary>
    ///     尝试取消当前节点。
    /// </summary>
    /// <param name="reason">停止原因。</param>
    public void Cancel(string reason)
    {
        State.SetStatus(NodeExecutionStatus.Stopping, reason);
        if (!CancellationTokenSource.IsCancellationRequested)
        {
            CancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    ///     尝试完成当前节点子树。
    /// </summary>
    /// <returns>本次调用是否完成子树。</returns>
    public bool TryCompleteSubtree()
    {
        if (Volatile.Read(ref _subtreeCompleted) != 0)
        {
            return false;
        }

        if (State.ActiveRequestCount > 0 ||
            State.QueuedRequestCount > 0 ||
            RegisteringChildCount > 0 ||
            InitializingCount > 0 ||
            PendingChildSubtreeCount > 0 ||
            State.Status == NodeExecutionStatus.Pending)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _subtreeCompleted, 1, 0) != 0)
        {
            return false;
        }

        if (State.Status is NodeExecutionStatus.Running or NodeExecutionStatus.Stopping)
        {
            State.SetStatus(CancellationTokenSource.IsCancellationRequested ? NodeExecutionStatus.Cancelled : NodeExecutionStatus.Completed, State.Reason);
        }

        _subtreeCompletionSource.TrySetResult();
        return true;
    }
}
