using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zeayii.Luma.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Runtime;
using Zeayii.Luma.Engine.Scheduling;

namespace Zeayii.Luma.Engine.Engine;

/// <summary>
/// <b>爬虫引擎</b>
/// <para>
/// 负责驱动节点生命周期、请求调度、下载解析、持久化与运行观测。
/// </para>
/// </summary>
public sealed class LumaEngine
{
    /// <summary>
    /// 请求调度器。
    /// </summary>
    private readonly NodeTaskScheduler _scheduler;

    /// <summary>
    /// 下载器。
    /// </summary>
    private readonly IDownloader _downloader;

    /// <summary>
    /// 持久化入口。
    /// </summary>
    private readonly IItemSink _itemSink;

    /// <summary>
    /// 节点停止策略。
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
    /// HTML 解析器。
    /// </summary>
    private readonly IHtmlParser _htmlParser;

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
    /// <param name="downloader">下载器。</param>
    /// <param name="itemSink">数据项持久化入口。</param>
    /// <param name="nodeStopPolicy">节点停止策略。</param>
    /// <param name="logManager">日志管理器。</param>
    /// <param name="progressManager">进度管理器。</param>
    /// <param name="htmlParser">HTML 解析器。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="options">运行配置。</param>
    public LumaEngine(
        IDownloader downloader,
        IItemSink itemSink,
        INodeStopPolicy nodeStopPolicy,
        ILogManager logManager,
        IProgressManager progressManager,
        IHtmlParser htmlParser,
        ILogger<LumaEngine> logger,
        LumaEngineOptions options)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _itemSink = itemSink ?? throw new ArgumentNullException(nameof(itemSink));
        _nodeStopPolicy = nodeStopPolicy ?? throw new ArgumentNullException(nameof(nodeStopPolicy));
        _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        _progressManager = progressManager ?? throw new ArgumentNullException(nameof(progressManager));
        _htmlParser = htmlParser ?? throw new ArgumentNullException(nameof(htmlParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _scheduler = new NodeTaskScheduler(_options.RequestChannelCapacity);
    }

    /// <summary>
    /// 运行爬虫。
    /// </summary>
    /// <param name="spider">爬虫实例。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
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

        var resources = new LumaNodeResources(_htmlParser);
        var rootNode = await spider.CreateRootAsync(runRuntime.Token).ConfigureAwait(false);
        await RegisterNodeAsync(rootNode, parentPath: null, runRuntime, resources, persistChannel.Writer, prioritizeRequests: false).ConfigureAwait(false);

        var persistWorkers = Enumerable.Range(0, Math.Max(1, _options.PersistWorkerCount))
            .Select(_ => PersistWorkerAsync(persistChannel.Reader, runRuntime))
            .ToArray();
        var downloadWorkers = Enumerable.Range(0, Math.Max(1, _options.DownloadWorkerCount))
            .Select(_ => DownloadWorkerAsync(persistChannel.Writer, runRuntime, resources))
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
    /// 注册节点并执行启动阶段。
    /// </summary>
    /// <param name="node">节点对象。</param>
    /// <param name="parentPath">父节点路径。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="resources">节点资源集合。</param>
    /// <param name="persistWriter">持久化写入器。</param>
    /// <param name="prioritizeRequests">是否优先入队请求。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterNodeAsync(
        LumaNode node,
        string? parentPath,
        LumaRunRuntime runRuntime,
        LumaNodeResources resources,
        ChannelWriter<ItemEnvelope> persistWriter,
        bool prioritizeRequests)
    {
        var path = string.IsNullOrWhiteSpace(parentPath) ? node.Key : $"{parentPath}/{node.Key}";
        var depth = string.IsNullOrWhiteSpace(parentPath) ? 0 : parentPath.Count(static ch => ch == '/') + 1;
        var runtime = new LumaNodeRuntime(node, path, depth, runRuntime.RunId, runRuntime.RunName, runRuntime.CommandName, resources, runRuntime.Token);

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
            var result = await runtime.Node.StartAsync(runtime.Context, runtime.Context.CancellationToken).ConfigureAwait(false);
            await ProcessNodeResultAsync(result, runtime, runRuntime, persistWriter, sourceRequest: null, prioritizeRequests).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
        {
            runtime.State.SetStatus(NodeExecutionStatus.Cancelled, "Node initialization cancelled.");
        }
        catch (Exception exception)
        {
            runtime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
            _logManager.Write(LogLevelKind.Error, "Engine", $"Node start failed: {runtime.Path}", exception);
            _logger.LogError(exception, "Node start failed: {NodePath}", runtime.Path);
        }
        finally
        {
            Interlocked.Decrement(ref _initializingNodeCount);
            SignalStateChanged();
        }
    }

    /// <summary>
    /// 处理节点结果。
    /// </summary>
    /// <param name="result">节点结果。</param>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="persistWriter">持久化写入器。</param>
    /// <param name="sourceRequest">源请求。</param>
    /// <param name="prioritizeRequests">是否优先入队请求。</param>
    /// <returns>异步任务。</returns>
    private async Task ProcessNodeResultAsync(
        NodeResult result,
        LumaNodeRuntime runtime,
        LumaRunRuntime runRuntime,
        ChannelWriter<ItemEnvelope> persistWriter,
        LumaRequest? sourceRequest,
        bool prioritizeRequests)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.StopNode)
        {
            runtime.Cancel(result.StopReason);
        }

        foreach (var request in result.Requests)
        {
            var normalizedRequest = new LumaRequest(request.Url, runtime.Path)
            {
                Method = request.Method,
                Headers = request.Headers,
                Metadata = request.Metadata,
                Body = request.Body,
                Priority = request.Priority,
                Depth = request.Depth,
                DontFilter = request.DontFilter,
                RouteKind = request.RouteKind,
                SessionPolicy = request.SessionPolicy,
                Timeout = request.Timeout,
                CreatedAtUtc = request.CreatedAtUtc
            };

            runtime.State.IncrementQueued();
            Interlocked.Increment(ref _queuedRequestCount);
            SignalStateChanged();
            await _scheduler.EnqueueAsync(normalizedRequest, prioritizeRequests, runRuntime.Token).ConfigureAwait(false);
        }

        if (result.Items.Count > 0)
        {
            for (var index = 0; index < result.Items.Count; index++)
            {
                await persistWriter.WriteAsync(new ItemEnvelope(result.Items[index], runtime.Context, sourceRequest), runRuntime.Token).ConfigureAwait(false);
            }
        }

        if (result.Children.Count > 0)
        {
            await RegisterChildrenAsync(result.Children, runtime, runRuntime, persistWriter).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 按节点策略注册子节点。
    /// </summary>
    /// <param name="children">子节点集合。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="persistWriter">持久化写入器。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildrenAsync(
        IReadOnlyList<LumaNode> children,
        LumaNodeRuntime parentRuntime,
        LumaRunRuntime runRuntime,
        ChannelWriter<ItemEnvelope> persistWriter)
    {
        var traversalPolicy = parentRuntime.Node.ExecutionOptions.ChildTraversalPolicy;
        var prioritizeRequests = traversalPolicy == ChildTraversalPolicy.DepthFirst;

        var orderedChildren = traversalPolicy == ChildTraversalPolicy.DepthFirst
            ? children.Reverse().ToArray()
            : children.ToArray();

        var tasks = new List<Task>(orderedChildren.Length);
        foreach (var child in orderedChildren)
        {
            await parentRuntime.WaitChildSlotAsync(runRuntime.Token).ConfigureAwait(false);
            var task = RegisterChildInternalAsync(child, parentRuntime, runRuntime, persistWriter, prioritizeRequests);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册单个子节点并释放并发闸门。
    /// </summary>
    /// <param name="child">子节点。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="persistWriter">持久化写入器。</param>
    /// <param name="prioritizeRequests">是否优先入队请求。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildInternalAsync(
        LumaNode child,
        LumaNodeRuntime parentRuntime,
        LumaRunRuntime runRuntime,
        ChannelWriter<ItemEnvelope> persistWriter,
        bool prioritizeRequests)
    {
        try
        {
            await RegisterNodeAsync(child, parentRuntime.Path, runRuntime, parentRuntime.Context.Resources, persistWriter, prioritizeRequests).ConfigureAwait(false);
        }
        finally
        {
            parentRuntime.ReleaseChildSlot();
        }
    }

    /// <summary>
    /// 下载工作循环。
    /// </summary>
    /// <param name="persistWriter">持久化写入器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="resources">节点资源集合。</param>
    /// <returns>异步任务。</returns>
    private async Task DownloadWorkerAsync(
        ChannelWriter<ItemEnvelope> persistWriter,
        LumaRunRuntime runRuntime,
        LumaNodeResources resources)
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

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var runtime))
                {
                    continue;
                }

                if (runtime.CancellationTokenSource.IsCancellationRequested)
                {
                    continue;
                }

                runtime.State.DecrementQueued();
                Interlocked.Decrement(ref _queuedRequestCount);
                runtime.State.IncrementActive();
                Interlocked.Increment(ref _activeRequestCount);
                SignalStateChanged();

                try
                {
                    var response = await _downloader.DownloadAsync(request, runtime.Context.CancellationToken).ConfigureAwait(false);
                    var result = await runtime.Node.HandleResponseAsync(response, runtime.Context, runtime.Context.CancellationToken).ConfigureAwait(false);
                    await ProcessNodeResultAsync(result, runtime, runRuntime, persistWriter, request, prioritizeRequests: false).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    runtime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
                    _logManager.Write(LogLevelKind.Error, "Engine", $"Node response handling failed: {runtime.Path}", exception);
                    _logger.LogError(exception, "Node response handling failed: {NodePath}", runtime.Path);
                }
                finally
                {
                    runtime.State.DecrementActive();
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
    /// <param name="reader">持久化读取器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
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
    /// <param name="reader">持久化读取器。</param>
    /// <param name="buffer">批量缓冲区。</param>
    /// <returns>是否读取成功。</returns>
    private static async Task<bool> TryReadInitialPersistBatchItemAsync(ChannelReader<ItemEnvelope> reader, List<ItemEnvelope> buffer)
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
    /// 聚合批量持久化数据。
    /// </summary>
    /// <param name="reader">持久化读取器。</param>
    /// <param name="buffer">批量缓冲区。</param>
    /// <returns>异步任务。</returns>
    private async Task FillPersistBatchAsync(ChannelReader<ItemEnvelope> reader, List<ItemEnvelope> buffer)
    {
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
    /// <returns>异步任务。</returns>
    private async Task FlushPersistBatchAsync(List<ItemEnvelope> buffer, LumaRunRuntime runRuntime, CancellationToken cancellationToken)
    {
        if (buffer.Count <= 0)
        {
            return;
        }

        var persistedIndexes = new List<int>(buffer.Count);
        var filteredEnvelopes = new List<ItemEnvelope>(buffer.Count);
        var resolvedResults = new PersistResult[buffer.Count];

        for (var index = 0; index < buffer.Count; index++)
        {
            var envelope = buffer[index];
            if (!_nodeRuntimes.TryGetValue(envelope.NodePath, out var runtime))
            {
                resolvedResults[index] = PersistResult.Skipped("Node runtime not found.");
                continue;
            }

            var persistContext = new PersistContext(runtime.Context, envelope.SourceRequest, index);
            var shouldPersist = await runtime.Node.ShouldPersistAsync(envelope.Item, persistContext, cancellationToken).ConfigureAwait(false);
            if (!shouldPersist)
            {
                resolvedResults[index] = PersistResult.Skipped("Filtered by node policy.");
                continue;
            }

            persistedIndexes.Add(index);
            filteredEnvelopes.Add(envelope);
        }

        IReadOnlyList<PersistResult> batchPersistResults;
        if (filteredEnvelopes.Count == 0)
        {
            batchPersistResults = Array.Empty<PersistResult>();
        }
        else
        {
            try
            {
                batchPersistResults = await _itemSink.StoreBatchAsync(filteredEnvelopes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logManager.Write(LogLevelKind.Error, "Persist", "Persist batch failed.", exception);
                _logger.LogError(exception, "Persist batch failed.");
                batchPersistResults = CreateFailedPersistResults(filteredEnvelopes.Count, exception.Message);
            }
        }

        if (batchPersistResults.Count != filteredEnvelopes.Count)
        {
            throw new InvalidOperationException("Persist batch result count must match filtered input count.");
        }

        for (var resultIndex = 0; resultIndex < persistedIndexes.Count; resultIndex++)
        {
            resolvedResults[persistedIndexes[resultIndex]] = batchPersistResults[resultIndex];
        }

        for (var index = 0; index < buffer.Count; index++)
        {
            var envelope = buffer[index];
            if (!_nodeRuntimes.TryGetValue(envelope.NodePath, out var runtime))
            {
                continue;
            }

            var persistResult = resolvedResults[index];
            runtime.State.ApplyPersistResult(persistResult);
            if (persistResult.Decision == PersistDecision.Stored)
            {
                Interlocked.Increment(ref _storedItemCount);
            }

            var callbackContext = new PersistContext(runtime.Context, envelope.SourceRequest, index);
            await runtime.Node.OnPersistedAsync(envelope.Item, persistResult, callbackContext, cancellationToken).ConfigureAwait(false);

            var stopToken = runtime.CancellationTokenSource.IsCancellationRequested ? CancellationToken.None : runtime.Context.CancellationToken;
            var shouldStop = await _nodeStopPolicy.ShouldStopAsync(runtime.Context, persistResult, stopToken).ConfigureAwait(false);
            if (shouldStop ||
                (persistResult.Decision == PersistDecision.AlreadyExists &&
                 runtime.Node.ConsecutiveExistingStopThreshold > 0 &&
                 runtime.State.ConsecutiveExistingCount >= runtime.Node.ConsecutiveExistingStopThreshold))
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
    /// 创建最终收尾批次取消源。
    /// </summary>
    /// <param name="runCancellationToken">运行级取消令牌。</param>
    /// <returns>取消源。</returns>
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
    /// 等待运行完成。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
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
    /// 判断当前运行是否已完成。
    /// </summary>
    /// <returns>完成返回 true。</returns>
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

        return _nodeRuntimes.Values.All(static runtime => runtime.State.ActiveRequestCount <= 0 && runtime.State.QueuedRequestCount <= 0);
    }

    /// <summary>
    /// 发送状态变更信号。
    /// </summary>
    private void SignalStateChanged()
    {
        _stateSignalChannel.Writer.TryWrite(true);
    }

    /// <summary>
    /// 周期发布进度快照。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
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
