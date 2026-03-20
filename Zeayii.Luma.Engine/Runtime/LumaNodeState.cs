using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
///     <b>节点运行时状态</b>
/// </summary>
internal sealed class LumaNodeState
{
    /// <summary>
    ///     活跃请求数量。
    /// </summary>
    private long _activeRequestCount;

    /// <summary>
    ///     已存在数量。
    /// </summary>
    private long _alreadyExistsCount;

    /// <summary>
    ///     连续已存在数量。
    /// </summary>
    private long _consecutiveExistingCount;

    /// <summary>
    ///     失败数量。
    /// </summary>
    private long _failedCount;

    /// <summary>
    ///     排队请求数量。
    /// </summary>
    private long _queuedRequestCount;

    /// <summary>
    ///     停止原因。
    /// </summary>
    private string _reason = string.Empty;

    /// <summary>
    ///     当前执行状态。
    /// </summary>
    private int _status = (int)NodeExecutionStatus.Pending;

    /// <summary>
    ///     已持久化成功数量。
    /// </summary>
    private long _storedCount;

    /// <summary>
    ///     当前执行状态。
    /// </summary>
    public NodeExecutionStatus Status => (NodeExecutionStatus)Volatile.Read(ref _status);

    /// <summary>
    ///     停止原因。
    /// </summary>
    public string Reason => _reason;

    /// <summary>
    ///     已持久化成功数量。
    /// </summary>
    public long StoredCount => Interlocked.Read(ref _storedCount);

    /// <summary>
    ///     已存在数量。
    /// </summary>
    public long AlreadyExistsCount => Interlocked.Read(ref _alreadyExistsCount);

    /// <summary>
    ///     失败数量。
    /// </summary>
    public long FailedCount => Interlocked.Read(ref _failedCount);

    /// <summary>
    ///     排队请求数量。
    /// </summary>
    public long QueuedRequestCount => Interlocked.Read(ref _queuedRequestCount);

    /// <summary>
    ///     活跃请求数量。
    /// </summary>
    public long ActiveRequestCount => Interlocked.Read(ref _activeRequestCount);

    /// <summary>
    ///     连续已存在数量。
    /// </summary>
    public long ConsecutiveExistingCount => Interlocked.Read(ref _consecutiveExistingCount);

    /// <summary>
    ///     设置执行状态。
    /// </summary>
    /// <param name="status">目标状态。</param>
    /// <param name="reason">原因。</param>
    public void SetStatus(NodeExecutionStatus status, string reason = "")
    {
        Interlocked.Exchange(ref _status, (int)status);
        _reason = reason;
    }

    /// <summary>
    ///     增加排队请求数量。
    /// </summary>
    public void IncrementQueued()
    {
        Interlocked.Increment(ref _queuedRequestCount);
    }

    /// <summary>
    ///     减少排队请求数量。
    /// </summary>
    public void DecrementQueued()
    {
        Interlocked.Decrement(ref _queuedRequestCount);
    }

    /// <summary>
    ///     增加活跃请求数量。
    /// </summary>
    public void IncrementActive()
    {
        Interlocked.Increment(ref _activeRequestCount);
    }

    /// <summary>
    ///     减少活跃请求数量。
    /// </summary>
    public void DecrementActive()
    {
        Interlocked.Decrement(ref _activeRequestCount);
    }

    /// <summary>
    ///     记录持久化结果。
    /// </summary>
    /// <param name="result">持久化结果。</param>
    public void ApplyPersistResult(in PersistResult result)
    {
        switch (result.Decision)
        {
            case PersistDecision.Stored:
            {
                Interlocked.Increment(ref _storedCount);
                Interlocked.Exchange(ref _consecutiveExistingCount, 0);
                break;
            }
            case PersistDecision.AlreadyExists:
            {
                Interlocked.Increment(ref _alreadyExistsCount);
                Interlocked.Increment(ref _consecutiveExistingCount);
                break;
            }
            case PersistDecision.Failed:
            {
                Interlocked.Increment(ref _failedCount);
                Interlocked.Exchange(ref _consecutiveExistingCount, 0);
                break;
            }
            case PersistDecision.Skipped:
            default:
            {
                Interlocked.Exchange(ref _consecutiveExistingCount, 0);
                break;
            }
        }
    }
}