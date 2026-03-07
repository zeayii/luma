using System.Collections.Generic;
using System.Threading;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
/// <b>节点任务调度器</b>
/// <para>
/// 提供支持队首/队尾插入的请求调度能力，用于实现节点级广度/深度扩展偏好。
/// </para>
/// </summary>
internal sealed class NodeTaskScheduler
{
    /// <summary>
    /// 请求队列。
    /// </summary>
    private readonly LinkedList<LumaRequest> _queue = [];

    /// <summary>
    /// 队列互斥锁。
    /// </summary>
    private readonly Lock _syncRoot = new();

    /// <summary>
    /// 可读信号量。
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>
    /// 最大容量。
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// 当前队列长度。
    /// </summary>
    private long _count;

    /// <summary>
    /// 完成标记。
    /// </summary>
    private int _completed;

    /// <summary>
    /// 初始化调度器。
    /// </summary>
    /// <param name="capacity">队列容量上限。</param>
    public NodeTaskScheduler(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>
    /// 当前排队数量。
    /// </summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>
    /// 请求入队。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="prioritize">是否优先插入队首。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async ValueTask EnqueueAsync(LumaRequest request, bool prioritize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _completed) != 0)
            {
                throw new InvalidOperationException("Scheduler is completed.");
            }

            var enqueued = false;
            lock (_syncRoot)
            {
                if (_queue.Count < _capacity)
                {
                    if (prioritize)
                    {
                        _queue.AddFirst(request);
                    }
                    else
                    {
                        _queue.AddLast(request);
                    }

                    enqueued = true;
                    Interlocked.Increment(ref _count);
                }
            }

            if (enqueued)
            {
                _signal.Release();
                return;
            }

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 请求出队。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求对象；完成且无数据时返回 null。</returns>
    public async ValueTask<LumaRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Volatile.Read(ref _completed) != 0)
            {
                lock (_syncRoot)
                {
                    if (_queue.Count == 0)
                    {
                        return null;
                    }
                }
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                if (_queue.First is null)
                {
                    if (Volatile.Read(ref _completed) != 0)
                    {
                        return null;
                    }

                    continue;
                }

                var node = _queue.First;
                _queue.RemoveFirst();
                Interlocked.Decrement(ref _count);
                return node.Value;
            }
        }
    }

    /// <summary>
    /// 标记调度完成。
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _signal.Release();
    }
}
