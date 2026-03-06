using System.Collections.Concurrent;
using System.Threading.Channels;
using Zeayii.Luma.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Runtime;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.Engine.Engine;

/// <summary>
/// <b>爬虫引擎</b>
/// <para>
/// 负责驱动节点树、请求调度、下载解析、持久化和增量停止。
/// </para>
/// </summary>
public sealed class LumaEngine
{
    /// <summary>
    /// 调度器。
    /// </summary>
    private readonly IRequestScheduler _scheduler;

    /// <summary>
    /// 下载器。
    /// </summary>
    private readonly IDownloader _downloader;

    /// <summary>
    /// 持久化入口。
    /// </summary>
    private readonly IItemSink _itemSink;

    /// <summary>
    /// 停止策略。
    /// </summary>
    private readonly INodeStopPolicy _nodeStopPolicy;

    /// <summary>
    /// 日志管理器。
    /// </summary>
    private readonly ILogManager _logManager;

    /// <summary>
    /// 进度管理器。
    /// </summary>
    private readonly IProgressManager _progressManager;

    /// <summary>
    /// 日志器。
    /// </summary>
    private readonly ILogger<LumaEngine> _logger;

    /// <summary>
    /// 配置。
    /// </summary>
    private readonly LumaEngineOptions _options;

    /// <summary>
    /// 节点运行时索引。
    /// </summary>
    private readonly ConcurrentDictionary<string, LumaNodeRuntime> _nodeRuntimes = new(StringComparer.Ordinal);

    /// <summary>
    /// 已排队请求总数。
    /// </summary>
    private long _queuedRequestCount;

    /// <summary>
    /// 已活跃请求总数。
    /// </summary>
    private long _activeRequestCount;

    /// <summary>
    /// 已成功持久化数量。
    /// </summary>
    private long _storedItemCount;

    /// <summary>
    /// 节点初始化中的任务数量。
    /// </summary>
    private long _initializingNodeCount;

    /// <summary>
    /// 状态变化通知通道。
    /// </summary>
    private readonly Channel<bool> _stateSignalChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    /// <summary>
    /// 初始化引擎。
    /// </summary>
    /// <param name="scheduler">请求调度器。</param>
    /// <param name="downloader">下载器。</param>
    /// <param name="itemSink">数据项持久化入口。</param>
    /// <param name="nodeStopPolicy">节点停止策略。</param>
    /// <param name="logManager">日志管理器。</param>
    /// <param name="progressManager">进度管理器。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="options">运行配置。</param>
    public LumaEngine(
        IRequestScheduler scheduler,
        IDownloader downloader,
        IItemSink itemSink,
        INodeStopPolicy nodeStopPolicy,
        ILogManager logManager,
        IProgressManager progressManager,
        ILogger<LumaEngine> logger,
        LumaEngineOptions options)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _itemSink = itemSink ?? throw new ArgumentNullException(nameof(itemSink));
        _nodeStopPolicy = nodeStopPolicy ?? throw new ArgumentNullException(nameof(nodeStopPolicy));
        _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        _progressManager = progressManager ?? throw new ArgumentNullException(nameof(progressManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 运行爬虫。
    /// </summary>
    /// <param name="spider">爬虫实例。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(ISpider spider, string commandName, string runName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spider);

        _logger.LogInformation("Luma run started. Command={CommandName}, Run={RunName}", commandName, runName);
        await using var runRuntime = new LumaRunRuntime(commandName, runName, cancellationToken);
        var persistChannel = Channel.CreateBounded<ItemEnvelope>(new BoundedChannelOptions(_options.PersistChannelCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        await foreach (var root in spider.CreateRootsAsync(runRuntime.Token).ConfigureAwait(false))
        {
            await RegisterNodeAsync(root, parentPath: null, runRuntime).ConfigureAwait(false);
        }

        var persistWorkers = Enumerable.Range(0, _options.PersistWorkerCount)
            .Select(_ => PersistWorkerAsync(persistChannel.Reader, runRuntime))
            .ToArray();
        var downloadWorkers = Enumerable.Range(0, _options.DownloadWorkerCount)
            .Select(_ => DownloadWorkerAsync(persistChannel.Writer, runRuntime))
            .ToArray();
        var snapshotTask = PublishSnapshotsLoopAsync(runRuntime);

        try
        {
            await WaitForCompletionAsync(runRuntime).ConfigureAwait(false);
        }
        finally
        {
            _scheduler.Complete();
            await Task.WhenAll(downloadWorkers).ConfigureAwait(false);
            persistChannel.Writer.TryComplete();
            await Task.WhenAll(persistWorkers).ConfigureAwait(false);
            if (!runRuntime.CancellationTokenSource.IsCancellationRequested)
            {
                await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);
            }

            await snapshotTask.ConfigureAwait(false);
            foreach (var runtime in _nodeRuntimes.Values)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Luma run finished. Command={CommandName}, Run={RunName}, Stored={StoredItemCount}, Active={ActiveRequestCount}, Queued={QueuedRequestCount}",
                commandName,
                runName,
                Interlocked.Read(ref _storedItemCount),
                Interlocked.Read(ref _activeRequestCount),
                Interlocked.Read(ref _queuedRequestCount));
        }
    }

    /// <summary>
    /// 注册节点并启动其初始输出。
    /// </summary>
    /// <param name="node">节点对象。</param>
    /// <param name="parentPath">父节点路径。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task RegisterNodeAsync(LumaNode node, string? parentPath, LumaRunRuntime runRuntime)
    {
        var path = string.IsNullOrWhiteSpace(parentPath) ? node.Key : $"{parentPath}/{node.Key}";
        var depth = string.IsNullOrWhiteSpace(parentPath) ? 0 : parentPath.Count(static ch => ch == '/') + 1;
        var runtime = new LumaNodeRuntime(node, path, depth, runRuntime.RunId, runRuntime.RunName, runRuntime.CommandName, runRuntime.Token);
        if (!_nodeRuntimes.TryAdd(path, runtime))
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            return;
        }

        Interlocked.Increment(ref _initializingNodeCount);
        SignalStateChanged();

        try
        {
        runtime.State.SetStatus(NodeExecutionStatus.Running);

            await foreach (var output in node.StartAsync(runtime.Context, runtime.Context.CancellationToken).ConfigureAwait(false))
            {
                await ProcessNodeOutputAsync(output, runtime, runRuntime).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _initializingNodeCount);
            SignalStateChanged();
        }
    }

    /// <summary>
    /// 处理节点输出。
    /// </summary>
    /// <param name="output">节点输出。</param>
    /// <param name="nodeRuntime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task ProcessNodeOutputAsync(NodeOutput output, LumaNodeRuntime nodeRuntime, LumaRunRuntime runRuntime)
    {
        switch (output)
        {
            case RequestNodeOutput requestOutput:
            {
                var sourceRequest = requestOutput.Request;
                var request = new LumaRequest(sourceRequest.Url, nodeRuntime.Path)
                {
                    Method = sourceRequest.Method,
                    Headers = sourceRequest.Headers,
                    Metadata = sourceRequest.Metadata,
                    Body = sourceRequest.Body,
                    Priority = sourceRequest.Priority,
                    Depth = sourceRequest.Depth,
                    DontFilter = sourceRequest.DontFilter,
                    RouteKind = sourceRequest.RouteKind,
                    Timeout = sourceRequest.Timeout,
                    CreatedAtUtc = sourceRequest.CreatedAtUtc
                };
                nodeRuntime.State.IncrementQueued();
                Interlocked.Increment(ref _queuedRequestCount);
                SignalStateChanged();
                await _scheduler.EnqueueAsync(request, runRuntime.Token).ConfigureAwait(false);
                break;
            }
            case ItemNodeOutput itemOutput:
            {
                throw new InvalidOperationException($"Item output must be routed through persist pipeline by the caller: {itemOutput.Item}");
            }
            case ChildNodeOutput childNodeOutput:
            {
                await RegisterNodeAsync(childNodeOutput.Node, nodeRuntime.Path, runRuntime).ConfigureAwait(false);
                break;
            }
        }
    }

    /// <summary>
    /// 下载工作循环。
    /// </summary>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task DownloadWorkerAsync(ChannelWriter<ItemEnvelope> persistWriter, LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                var request = await _scheduler.DequeueAsync(runRuntime.Token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var nodeRuntime))
                {
                    continue;
                }

                if (nodeRuntime.CancellationTokenSource.IsCancellationRequested)
                {
                    continue;
                }

                nodeRuntime.State.DecrementQueued();
                Interlocked.Decrement(ref _queuedRequestCount);
                nodeRuntime.State.IncrementActive();
                Interlocked.Increment(ref _activeRequestCount);
                SignalStateChanged();

                try
                {
                    var response = await _downloader.DownloadAsync(request, nodeRuntime.Context.CancellationToken).ConfigureAwait(false);
                    await foreach (var output in nodeRuntime.Node.ParseAsync(response, nodeRuntime.Context, nodeRuntime.Context.CancellationToken).ConfigureAwait(false))
                    {
                        switch (output)
                        {
                            case ItemNodeOutput itemOutput:
                                await persistWriter.WriteAsync(new ItemEnvelope(itemOutput.Item, nodeRuntime.Context), runRuntime.Token).ConfigureAwait(false);
                                break;
                            default:
                                await ProcessNodeOutputAsync(output, nodeRuntime, runRuntime).ConfigureAwait(false);
                                break;
                        }
                    }
                }
                catch (OperationCanceledException) when (nodeRuntime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    nodeRuntime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
                    _logManager.Write(LogLevelKind.Error, "Engine", $"Node parse failed: {nodeRuntime.Path}", exception);
                    _logger.LogError(exception, "Node parse failed: {NodePath}", nodeRuntime.Path);
                }
                finally
                {
                    nodeRuntime.State.DecrementActive();
                    Interlocked.Decrement(ref _activeRequestCount);
                    SignalStateChanged();
                }
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 持久化工作循环。
    /// </summary>
    /// <param name="reader">持久化通道读取器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task PersistWorkerAsync(ChannelReader<ItemEnvelope> reader, LumaRunRuntime runRuntime)
    {
        var buffer = new List<ItemEnvelope>(_options.PersistBatchSize);

        try
        {
            while (await TryReadInitialPersistBatchItemAsync(reader, buffer).ConfigureAwait(false))
            {
                await FillPersistBatchAsync(reader, buffer).ConfigureAwait(false);
                await FlushPersistBatchAsync(buffer, runRuntime, runRuntime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested || runRuntime.CancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (buffer.Count > 0)
            {
                using var finalFlushCancellationTokenSource = CreateFinalFlushCancellationTokenSource(runRuntime.Token);
                try
                {
                    await FlushPersistBatchAsync(buffer, runRuntime, finalFlushCancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (finalFlushCancellationTokenSource.IsCancellationRequested)
                {
                    _logManager.Write(LogLevelKind.Warning, "Persist", "Final persist batch flush timed out and was aborted.");
                    _logger.LogWarning("Final persist batch flush timed out and was aborted.");
                }
            }
        }
    }

    /// <summary>
    /// 读取单批持久化数据的首个元素。
    /// </summary>
    /// <param name="reader">持久化通道读取器。</param>
    /// <param name="buffer">批量缓冲区。</param>
    /// <returns>是否读取到首个元素。</returns>
    private static async Task<bool> TryReadInitialPersistBatchItemAsync(
        ChannelReader<ItemEnvelope> reader,
        List<ItemEnvelope> buffer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(buffer);

        buffer.Clear();
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            if (reader.TryRead(out var envelope))
            {
                buffer.Add(envelope);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 在批量大小或刷新时间达到之前继续聚合持久化数据。
    /// </summary>
    /// <param name="reader">持久化通道读取器。</param>
    /// <param name="buffer">批量缓冲区。</param>
    private async Task FillPersistBatchAsync(ChannelReader<ItemEnvelope> reader, List<ItemEnvelope> buffer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Count <= 0 || buffer.Count >= _options.PersistBatchSize)
        {
            return;
        }

        var deadlineUtc = DateTimeOffset.UtcNow + _options.PersistFlushInterval;
        while (buffer.Count < _options.PersistBatchSize)
        {
            while (buffer.Count < _options.PersistBatchSize && reader.TryRead(out var envelope))
            {
                buffer.Add(envelope);
            }

            if (buffer.Count >= _options.PersistBatchSize)
            {
                return;
            }

            var remaining = deadlineUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                var canRead = await reader.WaitToReadAsync().AsTask().WaitAsync(remaining).ConfigureAwait(false);
                if (!canRead)
                {
                    return;
                }
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 刷新当前持久化批次。
    /// </summary>
    /// <param name="buffer">批量缓冲区。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task FlushPersistBatchAsync(
        List<ItemEnvelope> buffer,
        LumaRunRuntime runRuntime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(runRuntime);

        if (buffer.Count <= 0)
        {
            return;
        }

        IReadOnlyList<PersistResult> persistResults;
        try
        {
            persistResults = await _itemSink.StoreBatchAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logManager.Write(LogLevelKind.Error, "Persist", "Persist batch failed.", exception);
            _logger.LogError(exception, "Persist batch failed.");
            persistResults = CreateFailedPersistResults(buffer.Count, exception.Message);
        }

        if (persistResults.Count != buffer.Count)
        {
            throw new InvalidOperationException("Persist batch result count must match input item count.");
        }

        for (var index = 0; index < buffer.Count; index++)
        {
            var envelope = buffer[index];
            if (!_nodeRuntimes.TryGetValue(envelope.NodePath, out var runtime))
            {
                continue;
            }

            var persistResult = persistResults[index];
            runtime.State.ApplyPersistResult(persistResult);
            if (persistResult.Decision == PersistDecision.Stored)
            {
                Interlocked.Increment(ref _storedItemCount);
            }

            var stopToken = runtime.CancellationTokenSource.IsCancellationRequested
                ? CancellationToken.None
                : runtime.Context.CancellationToken;
            var shouldStop = await _nodeStopPolicy.ShouldStopAsync(runtime.Context, persistResult, stopToken).ConfigureAwait(false);
            if (shouldStop || (persistResult.Decision == PersistDecision.AlreadyExists && runtime.Node.ConsecutiveExistingStopThreshold > 0 && runtime.State.ConsecutiveExistingCount >= runtime.Node.ConsecutiveExistingStopThreshold))
            {
                runtime.Cancel(persistResult.Message);
                SignalStateChanged();
            }
        }

        buffer.Clear();
    }

    /// <summary>
    /// 构造整批失败结果集合。
    /// </summary>
    /// <param name="count">结果数量。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>失败结果集合。</returns>
    private static IReadOnlyList<PersistResult> CreateFailedPersistResults(int count, string message)
    {
        var results = new PersistResult[count];
        for (var index = 0; index < count; index++)
        {
            results[index] = PersistResult.Failed(message);
        }

        return results;
    }

    /// <summary>
    /// 创建最终收尾批次的取消源。
    /// </summary>
    /// <param name="runCancellationToken">运行级取消令牌。</param>
    /// <returns>用于收尾刷新阶段的取消源。</returns>
    private CancellationTokenSource CreateFinalFlushCancellationTokenSource(CancellationToken runCancellationToken)
    {
        var finalFlushTimeout = _options.PersistFlushInterval > TimeSpan.FromSeconds(5)
            ? _options.PersistFlushInterval
            : TimeSpan.FromSeconds(5);
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(runCancellationToken);
        cancellationTokenSource.CancelAfter(finalFlushTimeout);
        return cancellationTokenSource;
    }

    /// <summary>
    /// 等待所有节点完成。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task WaitForCompletionAsync(LumaRunRuntime runRuntime)
    {
        while (!runRuntime.Token.IsCancellationRequested)
        {
            if (IsRunCompleted())
            {
                foreach (var runtime in _nodeRuntimes.Values)
                {
                    if (runtime.State.Status is NodeExecutionStatus.Running or NodeExecutionStatus.Stopping)
                    {
                        runtime.State.SetStatus(
                            runtime.CancellationTokenSource.IsCancellationRequested ? NodeExecutionStatus.Cancelled : NodeExecutionStatus.Completed,
                            runtime.State.Reason);
                    }

                    runtime.Result.Complete();
                }

                return;
            }

            await _stateSignalChannel.Reader.ReadAsync(runRuntime.Token).ConfigureAwait(false);
            while (_stateSignalChannel.Reader.TryRead(out _))
            {
            }
        }
    }

    /// <summary>
    /// 判断当前运行是否达到完成状态。
    /// </summary>
    /// <returns>完成返回 <c>true</c>。</returns>
    private bool IsRunCompleted()
    {
        if (Interlocked.Read(ref _initializingNodeCount) > 0)
        {
            return false;
        }

        if (_scheduler.Count > 0)
        {
            return false;
        }

        if (Interlocked.Read(ref _queuedRequestCount) > 0 || Interlocked.Read(ref _activeRequestCount) > 0)
        {
            return false;
        }

        return _nodeRuntimes.Values.All(static runtime =>
            runtime.State.ActiveRequestCount <= 0 &&
            runtime.State.QueuedRequestCount <= 0);
    }

    /// <summary>
    /// 通知运行状态发生变化。
    /// </summary>
    private void SignalStateChanged() => _stateSignalChannel.Writer.TryWrite(true);

    /// <summary>
    /// 周期性发布进度快照。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    private async Task PublishSnapshotsLoopAsync(LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                PublishSnapshot(runRuntime);
                await Task.Delay(_options.PresentationRefreshInterval, runRuntime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
            PublishSnapshot(runRuntime);
        }
    }

    /// <summary>
    /// 发布单次快照。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    private void PublishSnapshot(LumaRunRuntime runRuntime)
    {
        var nodes = _nodeRuntimes.Values
            .OrderBy(static runtime => runtime.Path, StringComparer.Ordinal)
            .Select(static runtime => new NodeSnapshot(
                runtime.Path,
                runtime.Node.ToString(),
                runtime.Depth,
                runtime.State.Status,
                runtime.State.StoredCount,
                runtime.State.AlreadyExistsCount,
                runtime.State.QueuedRequestCount,
                runtime.State.ActiveRequestCount,
                runtime.State.Reason))
            .ToArray();

        _progressManager.Publish(new ProgressSnapshot
        {
            RunId = runRuntime.RunId,
            RunName = runRuntime.RunName,
            CommandName = runRuntime.CommandName,
            Status = runRuntime.Token.IsCancellationRequested ? "Stopping" : "Running",
            StoredItemCount = Interlocked.Read(ref _storedItemCount),
            ActiveRequestCount = Interlocked.Read(ref _activeRequestCount),
            QueuedRequestCount = Interlocked.Read(ref _queuedRequestCount),
            Elapsed = DateTimeOffset.UtcNow - runRuntime.StartedAtUtc,
            Nodes = nodes
        });
    }
}
