using Zeayii.Luma.Abstractions.Models;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;

namespace Zeayii.Luma.Engine.Scheduling;

/// <summary>
///     <b>节点任务调度器</b>
///     <para>
///         提供 FIFO 请求调度能力。
///     </para>
/// </summary>
/// <param name="capacity">默认队列容量上限。</param>
internal sealed class NodeTaskScheduler(int capacity) : IDisposable
{
    /// <summary>
    ///     阶段队列索引。
    /// </summary>
    private readonly ConcurrentDictionary<string, StageQueue> _stageQueues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     已就绪阶段键队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _readyStageKeys = new();

    /// <summary>
    ///     已就绪阶段键去重集合。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _readyStageKeySet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     就绪信号量。
    /// </summary>
    private readonly SemaphoreSlim _readySignal = new(0, int.MaxValue);

    /// <summary>
    ///     默认容量。
    /// </summary>
    private readonly int _defaultCapacity = Math.Max(1, capacity);

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
        _readySignal.Dispose();
    }

    /// <summary>
     ///     请求入队。
     /// </summary>
     /// <param name="request">请求对象。</param>
    /// <param name="stageKey">阶段键。</param>
    /// <param name="capacity">阶段容量上限。</param>
    /// <param name="backpressureMode">背压模式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async ValueTask EnqueueAsync(LumaRequest request, string stageKey, int capacity, NodeBackpressureMode backpressureMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Volatile.Read(ref _completed) != 0)
        {
            throw new InvalidOperationException("Scheduler is completed.");
        }

        var normalizedStageKey = NormalizeStageKey(stageKey);
        var normalizedCapacity = capacity > 0 ? capacity : _defaultCapacity;
        var queue = _stageQueues.GetOrAdd(
            normalizedStageKey,
            static (key, state) => StageQueue.Create(key, state.Capacity, state.BackpressureMode, state.OnItemDropped),
            (Capacity: normalizedCapacity, BackpressureMode: backpressureMode, OnItemDropped: (Action<string>)OnStageItemDropped));

        queue.UpdateConfiguration(normalizedCapacity, backpressureMode);

        if (queue.BackpressureMode == NodeBackpressureMode.Wait)
        {
            await WriteWithWaitModeAsync(queue, request, cancellationToken).ConfigureAwait(false);
            return;
        }

        WriteWithDropMode(queue, request, cancellationToken);
    }

    /// <summary>
    ///     以等待模式入队。
    /// </summary>
    /// <param name="queue">阶段队列。</param>
    /// <param name="request">请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask WriteWithWaitModeAsync(StageQueue queue, LumaRequest request, CancellationToken cancellationToken)
    {
        var countIncremented = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _completed) != 0)
            {
                throw new InvalidOperationException("Scheduler is completed.");
            }

            Interlocked.Increment(ref queue.Count);
            Interlocked.Increment(ref _count);
            countIncremented = true;
            await queue.Channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            MarkStageReady(queue.StageKey);
        }
        catch (ChannelClosedException)
        {
            if (countIncremented)
            {
                Interlocked.Decrement(ref queue.Count);
                Interlocked.Decrement(ref _count);
            }

            throw new InvalidOperationException("Scheduler is completed.");
        }
        catch
        {
            if (countIncremented)
            {
                Interlocked.Decrement(ref queue.Count);
                Interlocked.Decrement(ref _count);
            }

            throw;
        }
    }

    /// <summary>
    ///     以丢弃模式入队。
    /// </summary>
    /// <param name="queue">阶段队列。</param>
    /// <param name="request">请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private void WriteWithDropMode(StageQueue queue, LumaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _completed) != 0)
        {
            throw new InvalidOperationException("Scheduler is completed.");
        }

        if (!queue.Channel.Writer.TryWrite(request))
        {
            throw new InvalidOperationException("Scheduler is completed.");
        }

        Interlocked.Increment(ref queue.Count);
        Interlocked.Increment(ref _count);
        MarkStageReady(queue.StageKey);
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
            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) <= 0)
            {
                return null;
            }

            await _readySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _completed) != 0 && Interlocked.Read(ref _count) <= 0)
            {
                return null;
            }

            if (!TryDequeueInternal(out var request))
            {
                continue;
            }

            return request;
        }
    }

    /// <summary>
    ///     尝试非阻塞出队。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <returns>成功返回 true。</returns>
    public bool TryDequeue(out LumaRequest request)
    {
        return TryDequeueInternal(out request);
    }

    /// <summary>
    ///     非阻塞尝试出队实现。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <returns>成功返回 true。</returns>
    private bool TryDequeueInternal(out LumaRequest request)
    {
        while (_readyStageKeys.TryDequeue(out var stageKey))
        {
            _readyStageKeySet.TryRemove(stageKey, out _);
            if (!_stageQueues.TryGetValue(stageKey, out var queue))
            {
                continue;
            }

            if (!queue.Channel.Reader.TryRead(out var dequeuedRequest))
            {
                continue;
            }

            Interlocked.Decrement(ref queue.Count);
            Interlocked.Decrement(ref _count);

            if (Interlocked.Read(ref queue.Count) > 0)
            {
                MarkStageReady(stageKey);
            }

            request = dequeuedRequest;
            return true;
        }

        request = default!;
        return false;
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

        foreach (var queue in _stageQueues.Values)
        {
            queue.Channel.Writer.TryComplete();
        }

        _readySignal.Release(1024);
    }

    /// <summary>
    ///     标记阶段已就绪。
    /// </summary>
    /// <param name="stageKey">阶段键。</param>
    private void MarkStageReady(string stageKey)
    {
        if (_readyStageKeySet.TryAdd(stageKey, 0))
        {
            _readyStageKeys.Enqueue(stageKey);
        }

        _readySignal.Release();
    }

    /// <summary>
    ///     阶段项被丢弃时更新计数。
    /// </summary>
    /// <param name="stageKey">阶段键。</param>
    private void OnStageItemDropped(string stageKey)
    {
        if (!_stageQueues.TryGetValue(stageKey, out var queue))
        {
            return;
        }

        Interlocked.Add(ref queue.Count, -1);
        Interlocked.Add(ref _count, -1);
    }

    /// <summary>
    ///     规范化阶段键。
    /// </summary>
    /// <param name="stageKey">阶段键。</param>
    /// <returns>规范化后的阶段键。</returns>
    private static string NormalizeStageKey(string stageKey)
    {
        return string.IsNullOrWhiteSpace(stageKey) ? "default" : stageKey.Trim();
    }

    /// <summary>
    ///     阶段队列。
    /// </summary>
    private sealed class StageQueue
    {
        /// <summary>
        ///     初始化阶段队列。
        /// </summary>
        /// <param name="stageKey">阶段键。</param>
        /// <param name="channel">阶段通道。</param>
        /// <param name="capacity">容量。</param>
        /// <param name="backpressureMode">背压模式。</param>
        private StageQueue(string stageKey, Channel<LumaRequest> channel, int capacity, NodeBackpressureMode backpressureMode)
        {
            StageKey = stageKey;
            Channel = channel;
            Capacity = capacity;
            BackpressureMode = backpressureMode;
        }

        /// <summary>
        ///     阶段键。
        /// </summary>
        public string StageKey { get; }

        /// <summary>
        ///     通道。
        /// </summary>
        public Channel<LumaRequest> Channel { get; }

        /// <summary>
        ///     容量。
        /// </summary>
        public int Capacity { get; private set; }

        /// <summary>
        ///     背压模式。
        /// </summary>
        public NodeBackpressureMode BackpressureMode { get; private set; }

        /// <summary>
        ///     阶段计数。
        /// </summary>
        public long Count;

        /// <summary>
        ///     校验阶段配置一致性。
        /// </summary>
        /// <param name="capacity">容量。</param>
        /// <param name="backpressureMode">背压模式。</param>
        public void UpdateConfiguration(int capacity, NodeBackpressureMode backpressureMode)
        {
            var normalizedCapacity = Math.Max(1, capacity);
            if (normalizedCapacity != Capacity || backpressureMode != BackpressureMode)
            {
                throw new InvalidOperationException(
                    $"Stage queue configuration conflict detected. Stage={StageKey}, ExistingCapacity={Capacity}, ExistingBackpressure={BackpressureMode}, IncomingCapacity={normalizedCapacity}, IncomingBackpressure={backpressureMode}");
            }
        }

        /// <summary>
        ///     创建阶段队列。
        /// </summary>
        /// <param name="stageKey">阶段键。</param>
        /// <param name="capacity">容量。</param>
        /// <param name="backpressureMode">背压模式。</param>
        /// <param name="onDropped">丢弃回调。</param>
        /// <returns>阶段队列。</returns>
        public static StageQueue Create(string stageKey, int capacity, NodeBackpressureMode backpressureMode, Action<string> onDropped)
        {
            var normalizedCapacity = Math.Max(1, capacity);
            var fullMode = ResolveFullMode(backpressureMode);
            var options = new BoundedChannelOptions(normalizedCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = fullMode
            };
            var channel = System.Threading.Channels.Channel.CreateBounded<LumaRequest>(options, _ => onDropped(stageKey));
            return new StageQueue(stageKey, channel, normalizedCapacity, backpressureMode);
        }

        /// <summary>
        ///     解析通道满载策略。
        /// </summary>
        /// <param name="backpressureMode">背压模式。</param>
        /// <returns>通道满载策略。</returns>
        private static BoundedChannelFullMode ResolveFullMode(NodeBackpressureMode backpressureMode)
        {
            return backpressureMode switch
            {
                NodeBackpressureMode.Wait => BoundedChannelFullMode.Wait,
                NodeBackpressureMode.DropNewest => BoundedChannelFullMode.DropWrite,
                NodeBackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
                _ => BoundedChannelFullMode.Wait
            };
        }
    }
}
