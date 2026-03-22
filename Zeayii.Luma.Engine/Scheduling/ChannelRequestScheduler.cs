using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
///     <b>节点任务调度器</b>
///     <para>
///         提供 FIFO 请求调度能力。
///     </para>
/// </summary>
/// <param name="capacity">队列容量上限。</param>
/// <param name="consumerCount">消费者数量。</param>
internal sealed class NodeTaskScheduler(int capacity, int consumerCount) : IDisposable
{
    /// <summary>
    ///     可读信号量。
    /// </summary>
    private readonly SemaphoreSlim _availableItems = new(0);

    /// <summary>
    ///     可写槽位信号量（统一容量背压）。
    /// </summary>
    private readonly SemaphoreSlim _availableSlots = new(Math.Max(1, capacity), Math.Max(1, capacity));

    /// <summary>
    ///     消费者数量。
    /// </summary>
    private readonly int _consumerCount = Math.Max(1, consumerCount);

    /// <summary>
    ///     普通请求队列。
    /// </summary>
    private readonly LinkedList<LumaRequest> _normalQueue = [];

    /// <summary>
    ///     队列互斥锁。
    /// </summary>
    private readonly Lock _syncRoot = new();

    /// <summary>
    ///     完成标记。
    /// </summary>
    private int _completed;

    /// <summary>
    ///     当前队列长度。
    /// </summary>
    private long _count;

    /// <summary>
    ///     当前排队数量。
    /// </summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>
    ///     释放调度器资源。
    /// </summary>
    public void Dispose()
    {
        _availableItems.Dispose();
        _availableSlots.Dispose();
    }

    /// <summary>
    ///     请求入队。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async ValueTask EnqueueAsync(LumaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Volatile.Read(ref _completed) != 0)
        {
            throw new InvalidOperationException("Scheduler is completed.");
        }

        await _availableSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _completed) != 0)
            {
                throw new InvalidOperationException("Scheduler is completed.");
            }

            lock (_syncRoot)
            {
                _normalQueue.AddLast(request);

                Interlocked.Increment(ref _count);
            }

            _availableItems.Release();
        }
        catch
        {
            _availableSlots.Release();
            throw;
        }
    }

    /// <summary>
    ///     请求出队。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求对象；完成且无数据时返回 null。</returns>
    public async ValueTask<LumaRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) == 0)
            {
                return null;
            }

            await _availableItems.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                LinkedListNode<LumaRequest>? node = null;
                if (_normalQueue.First is not null)
                {
                    node = _normalQueue.First;
                    _normalQueue.RemoveFirst();
                }

                if (node is not null)
                {
                    Interlocked.Decrement(ref _count);
                    _availableSlots.Release();
                    return node.Value;
                }
            }

            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) == 0)
            {
                return null;
            }
        }
    }

    /// <summary>
    ///     标记调度完成。
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _availableItems.Release(_consumerCount);
    }
}
