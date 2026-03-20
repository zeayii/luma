namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
/// <b>通用任务调度器</b>
/// <para>
/// 支持优先队列（Depth）与普通队列（Breadth）的统一调度语义。
/// </para>
/// </summary>
/// <typeparam name="TItem">任务项类型。</typeparam>
/// <param name="capacity">队列容量上限。</param>
/// <param name="consumerCount">消费者数量。</param>
internal sealed class PriorityTaskScheduler<TItem>(int capacity, int consumerCount) : IDisposable
{
    /// <summary>
    /// 可读信号量。
    /// </summary>
    private readonly SemaphoreSlim _availableItems = new(0);

    /// <summary>
    /// 可写槽位信号量。
    /// </summary>
    private readonly SemaphoreSlim _availableSlots = new(Math.Max(1, capacity), Math.Max(1, capacity));

    /// <summary>
    /// 消费者数量。
    /// </summary>
    private readonly int _consumerCount = Math.Max(1, consumerCount);

    /// <summary>
    /// 普通队列。
    /// </summary>
    private readonly LinkedList<TItem> _normalQueue = [];

    /// <summary>
    /// 优先队列。
    /// </summary>
    private readonly LinkedList<TItem> _priorityQueue = [];

    /// <summary>
    /// 队列同步锁。
    /// </summary>
    private readonly Lock _syncRoot = new();

    /// <summary>
    /// 完成标记。
    /// </summary>
    private int _completed;

    /// <summary>
    /// 当前队列长度。
    /// </summary>
    private long _count;

    /// <summary>
    /// 当前排队数量。
    /// </summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>
    /// 入队。
    /// </summary>
    /// <param name="item">任务项。</param>
    /// <param name="prioritize">是否走优先队列。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async ValueTask EnqueueAsync(TItem item, bool prioritize, CancellationToken cancellationToken)
    {
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
                if (prioritize)
                {
                    _priorityQueue.AddLast(item);
                }
                else
                {
                    _normalQueue.AddLast(item);
                }

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
    /// 出队。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>出队结果（HasItem=false 表示完成且无数据）。</returns>
    public async ValueTask<(bool HasItem, TItem Item)> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) == 0)
            {
                return (false, default!);
            }

            await _availableItems.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                LinkedListNode<TItem>? node = null;
                if (_priorityQueue.Last is not null)
                {
                    node = _priorityQueue.Last;
                    _priorityQueue.RemoveLast();
                }
                else if (_normalQueue.First is not null)
                {
                    node = _normalQueue.First;
                    _normalQueue.RemoveFirst();
                }

                if (node is not null)
                {
                    Interlocked.Decrement(ref _count);
                    _availableSlots.Release();
                    return (true, node.Value);
                }
            }

            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) == 0)
            {
                return (false, default!);
            }
        }
    }

    /// <summary>
    /// 尝试非阻塞出队。
    /// </summary>
    /// <param name="item">输出任务项。</param>
    /// <returns>是否成功。</returns>
    public bool TryDequeue(out TItem item)
    {
        lock (_syncRoot)
        {
            LinkedListNode<TItem>? node = null;
            if (_priorityQueue.Last is not null)
            {
                node = _priorityQueue.Last;
                _priorityQueue.RemoveLast();
            }
            else if (_normalQueue.First is not null)
            {
                node = _normalQueue.First;
                _normalQueue.RemoveFirst();
            }

            if (node is null)
            {
                item = default!;
                return false;
            }

            Interlocked.Decrement(ref _count);
            _availableSlots.Release();
            item = node.Value;
            return true;
        }
    }

    /// <summary>
    /// 标记完成。
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _availableItems.Release(_consumerCount);
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _availableItems.Dispose();
        _availableSlots.Dispose();
    }
}
