using System.Threading.Channels;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
///     <b>通用任务调度器</b>
///     <para>
///         支持 FIFO 调度语义。
///     </para>
/// </summary>
/// <typeparam name="TItem">任务项类型。</typeparam>
/// <param name="capacity">队列容量上限。</param>
internal sealed class PriorityTaskScheduler<TItem>(int capacity) : IDisposable
{
    /// <summary>
    ///     调度通道。
    /// </summary>
    private readonly Channel<TItem> _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(Math.Max(1, capacity))
    {
        SingleReader = false,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

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
    ///     释放资源。
    /// </summary>
    public void Dispose()
    {
        // Channel 调度器无非托管资源，保留空实现以兼容调用方释放路径。
    }

    /// <summary>
    ///     入队。
    /// </summary>
    /// <param name="item">任务项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async ValueTask EnqueueAsync(TItem item, CancellationToken cancellationToken)
    {
        var countIncremented = false;

        if (Volatile.Read(ref _completed) != 0)
        {
            throw new InvalidOperationException("Scheduler is completed.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _completed) != 0)
            {
                throw new InvalidOperationException("Scheduler is completed.");
            }

            Interlocked.Increment(ref _count);
            countIncremented = true;
            await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            if (countIncremented)
            {
                Interlocked.Decrement(ref _count);
            }

            throw new InvalidOperationException("Scheduler is completed.");
        }
        catch
        {
            if (countIncremented)
            {
                Interlocked.Decrement(ref _count);
            }

            throw;
        }
    }

    /// <summary>
    ///     出队。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>出队结果（HasItem=false 表示完成且无数据）。</returns>
    public async ValueTask<(bool HasItem, TItem Item)> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_channel.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref _count);
                return (true, item);
            }
        }

        return (false, default!);
    }

    /// <summary>
    ///     尝试非阻塞出队。
    /// </summary>
    /// <param name="item">输出任务项。</param>
    /// <returns>是否成功。</returns>
    public bool TryDequeue(out TItem item)
    {
        if (_channel.Reader.TryRead(out var readItem))
        {
            item = readItem;
            Interlocked.Decrement(ref _count);
            return true;
        }

        item = default!;
        return false;
    }

    /// <summary>
    ///     标记完成。
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
    }
}
