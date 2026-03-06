using System.Threading.Channels;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
/// <b>基于 Channel 的请求调度器</b>
/// </summary>
internal sealed class ChannelRequestScheduler : IRequestScheduler
{
    /// <summary>
    /// 请求通道。
    /// </summary>
    private readonly Channel<LumaRequest> _channel;

    /// <summary>
    /// 当前请求数量。
    /// </summary>
    private long _count;

    /// <summary>
    /// 初始化调度器。
    /// </summary>
    /// <param name="capacity">通道容量。</param>
    public ChannelRequestScheduler(int capacity)
    {
        _channel = Channel.CreateBounded<LumaRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <inheritdoc />
    public long Count => Interlocked.Read(ref _count);

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(LumaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _count);
    }

    /// <inheritdoc />
    public async ValueTask<LumaRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var request))
            {
                Interlocked.Decrement(ref _count);
                return request;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}

