using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
/// <b>节点运行时宿主</b>
/// <para>
/// 持有节点生命周期、取消源、上下文和状态。
/// </para>
/// </summary>
internal sealed class LumaNodeRuntime<TState> : IAsyncDisposable
{
    /// <summary>
    /// 释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 子节点并发闸门。
    /// </summary>
    private readonly SemaphoreSlim _childConcurrencyGate;

    /// <summary>
    /// 节点请求执行闸门。
    /// <para>
    /// 仅在 Depth 节点启用，用于保证该节点请求/下载处理阶段串行执行。
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim? _requestExecutionGate;

    /// <summary>
    /// 初始化节点运行时。
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
        if (Node.ExecutionOptions.ChildTraversalPolicy == ChildTraversalPolicy.Depth)
        {
            _requestExecutionGate = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// 抽象层节点。
    /// </summary>
    public LumaNode<TState> Node { get; }

    /// <summary>
    /// 节点路径。
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 节点深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 节点取消源。
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>
    /// 节点上下文。
    /// </summary>
    public LumaContext<TState> Context { get; }

    /// <summary>
    /// 节点状态。
    /// </summary>
    public LumaNodeState State { get; }

    /// <summary>
    /// 进入子节点扩展并发闸门。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task WaitChildSlotAsync(CancellationToken cancellationToken)
    {
        return _childConcurrencyGate.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 释放子节点扩展并发闸门。
    /// </summary>
    public void ReleaseChildSlot()
    {
        _childConcurrencyGate.Release();
    }

    /// <summary>
    /// 进入节点请求执行闸门。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public Task WaitRequestExecutionSlotAsync(CancellationToken cancellationToken)
    {
        if (_requestExecutionGate is null)
        {
            return Task.CompletedTask;
        }

        return _requestExecutionGate.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 释放节点请求执行闸门。
    /// </summary>
    public void ReleaseRequestExecutionSlot()
    {
        _requestExecutionGate?.Release();
    }

    /// <summary>
    /// 尝试取消当前节点。
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
    /// 释放运行时资源。
    /// </summary>
    /// <returns>异步任务。</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _childConcurrencyGate.Dispose();
        _requestExecutionGate?.Dispose();
        CancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }
}
