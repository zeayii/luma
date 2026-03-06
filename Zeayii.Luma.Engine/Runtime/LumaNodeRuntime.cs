using Zeayii.Luma.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
/// <b>节点运行时宿主</b>
/// <para>
/// 持有节点生命周期、取消源、上下文、状态和结果。
/// </para>
/// </summary>
internal sealed class LumaNodeRuntime : IAsyncDisposable
{
    /// <summary>
    /// 释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 初始化节点运行时。
    /// </summary>
    /// <param name="node">抽象层节点。</param>
    /// <param name="path">节点路径。</param>
    /// <param name="depth">节点深度。</param>
    /// <param name="runId">运行标识。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="parentToken">父级取消令牌。</param>
    public LumaNodeRuntime(
        LumaNode node,
        string path,
        int depth,
        Guid runId,
        string runName,
        string commandName,
        CancellationToken parentToken)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Depth = depth;
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        Context = new LumaNodeContext(runId, runName, commandName, Path, Depth, CancellationTokenSource.Token);
        State = new LumaNodeState();
        Result = new LumaNodeResult();
    }

    /// <summary>
    /// 抽象层节点。
    /// </summary>
    public LumaNode Node { get; }

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
    public LumaNodeContext Context { get; }

    /// <summary>
    /// 节点状态。
    /// </summary>
    public LumaNodeState State { get; }

    /// <summary>
    /// 节点结果。
    /// </summary>
    public LumaNodeResult Result { get; }

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

        CancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }
}

