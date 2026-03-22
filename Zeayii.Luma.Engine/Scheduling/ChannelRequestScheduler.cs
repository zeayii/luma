using Zeayii.Luma.Abstractions.Models;
using System.Threading.Channels;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
///     <b>节点任务调度器</b>
///     <para>
///         提供 FIFO 请求调度能力。
///     </para>
/// </summary>
/// <param name="capacity">队列容量上限。</param>
internal sealed class NodeTaskScheduler(int capacity) : IDisposable
{
    /// <summary>
    ///     调度通道。
    /// </summary>
    private readonly Channel<LumaRequest> _channel = Channel.CreateBounded<LumaRequest>(new BoundedChannelOptions(Math.Max(1, capacity))
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
    ///     释放调度器资源。
    /// </summary>
    public void Dispose()
    {
        // Channel 调度器无非托管资源，保留空实现以兼容调用方释放路径。
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
            await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
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
    ///     请求出队。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求对象；完成且无数据时返回 null。</returns>
    public async ValueTask<LumaRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_channel.Reader.TryRead(out var request))
            {
                Interlocked.Decrement(ref _count);
                return request;
            }
        }

        return null;
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

        _channel.Writer.TryComplete();
    }
}
