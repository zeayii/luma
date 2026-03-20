using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zeayii.Infrastructure.Net.Abstractions.Http;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Runtime;
using Zeayii.Luma.Engine.Scheduling;

namespace Zeayii.Luma.Engine.Engine;

/// <summary>
///     <b>爬虫引擎</b>
///     <para>
///         负责驱动节点生命周期、请求调度、下载处理、持久化与运行观测。
///     </para>
/// </summary>
/// <typeparam name="TState">节点状态类型。</typeparam>
public sealed class LumaEngine<TState>
{
    /// <summary>
    ///     HTML 解析器。
    /// </summary>
    private readonly IHtmlParser _htmlParser;

    /// <summary>
    ///     持久化入口。
    /// </summary>
    private readonly IItemSink<TState> _itemSink;

    /// <summary>
    ///     日志器。
    /// </summary>
    private readonly ILogger<LumaEngine<TState>> _logger;

    /// <summary>
    ///     日志工厂。
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    ///     日志管理器。
    /// </summary>
    private readonly ILogManager _logManager;

    /// <summary>
    ///     网络客户端入口。
    /// </summary>
    private readonly INetClient _netClient;

    /// <summary>
    ///     节点运行时索引。
    /// </summary>
    private readonly ConcurrentDictionary<string, LumaNodeRuntime<TState>> _nodeRuntimes = new(StringComparer.Ordinal);

    /// <summary>
    ///     引擎选项。
    /// </summary>
    private readonly LumaEngineOptions _options;

    /// <summary>
    ///     进度管理器。
    /// </summary>
    private readonly IProgressManager _progressManager;

    /// <summary>
    ///     状态变化通知通道。
    /// </summary>
    private readonly Channel<bool> _stateSignalChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    /// <summary>
    ///     当前活跃网络任务数量。
    /// </summary>
    private long _activeNetworkCount;

    /// <summary>
    ///     节点初始化中的任务数量。
    /// </summary>
    private long _initializingNodeCount;

    /// <summary>
    ///     引擎运行中标记。
    /// </summary>
    private int _isRunning;

    /// <summary>
    ///     已成功持久化数量。
    /// </summary>
    private long _storedItemCount;

    /// <summary>
    ///     初始化引擎。
    /// </summary>
    /// <param name="itemSink">持久化入口。</param>
    /// <param name="logManager">日志管理器。</param>
    /// <param name="progressManager">进度管理器。</param>
    /// <param name="htmlParser">HTML 解析器。</param>
    /// <param name="netClient">网络客户端入口。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="options">引擎选项。</param>
    public LumaEngine(
        IItemSink<TState> itemSink,
        ILogManager logManager,
        IProgressManager progressManager,
        IHtmlParser htmlParser,
        INetClient netClient,
        ILoggerFactory loggerFactory,
        ILogger<LumaEngine<TState>> logger,
        LumaEngineOptions options)
    {
        _itemSink = itemSink ?? throw new ArgumentNullException(nameof(itemSink));
        _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        _progressManager = progressManager ?? throw new ArgumentNullException(nameof(progressManager));
        _htmlParser = htmlParser ?? throw new ArgumentNullException(nameof(htmlParser));
        _netClient = netClient ?? throw new ArgumentNullException(nameof(netClient));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     运行爬虫。
    /// </summary>
    /// <param name="spider">蜘蛛实例。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task RunAsync(ISpider<TState> spider, string commandName, string runName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spider);

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) throw new InvalidOperationException("LumaEngine is already running.");

        try
        {
            ResetRuntimeState();
            LumaEngineLogMessages.RunStartedLog(_logger, commandName, runName, null);

            var runRuntime = new LumaRunRuntime(commandName, runName, cancellationToken);
            await using (runRuntime.ConfigureAwait(false))
            {
                var rootState = await spider.CreateStateAsync(runRuntime.Token).ConfigureAwait(false);
                var rootNode = await spider.CreateRootAsync(rootState, runRuntime.Token).ConfigureAwait(false);
                var effectiveRequestWorkerCount = ResolveEffectiveRequestWorkerCount(rootNode);
                var requestScheduler = new NodeTaskScheduler(_options.RequestChannelCapacity, effectiveRequestWorkerCount);
                var downloadScheduler = new NodeTaskScheduler(_options.DownloadChannelCapacity, _options.DownloadWorkerCount);
                var cookieAccessor = new NetCookieAccessor(_netClient, ResolveRouteKind);

                try
                {
                    var persistChannel = Channel.CreateBounded<ItemEnvelope<TState>>(new BoundedChannelOptions(_options.PersistChannelCapacity)
                    {
                        SingleReader = false,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                    var persistWorkers = Enumerable.Range(0, _options.PersistWorkerCount).Select(_ => PersistWorkerAsync(persistChannel.Reader, runRuntime)).ToArray();
                    var requestWorkers = Enumerable.Range(0, effectiveRequestWorkerCount).Select(_ => RequestWorkerAsync(requestScheduler, downloadScheduler, persistChannel.Writer, runRuntime)).ToArray();
                    var downloadWorkers = Enumerable.Range(0, _options.DownloadWorkerCount).Select(_ => DownloadWorkerAsync(downloadScheduler, requestScheduler, persistChannel.Writer, runRuntime)).ToArray();
                    var snapshotTask = PublishSnapshotsLoopAsync(runRuntime, requestScheduler, downloadScheduler);
                    await RegisterNodeAsync(rootNode, rootState, null, runRuntime, cookieAccessor, requestScheduler, persistChannel.Writer, false).ConfigureAwait(false);

                    try
                    {
                        await WaitForCompletionAsync(runRuntime, requestScheduler, downloadScheduler).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
                    {
                        if (!string.Equals(runRuntime.Status, "Stopped", StringComparison.Ordinal)) runRuntime.SetStatus("Cancelled");
                    }
                    catch (Exception)
                    {
                        runRuntime.SetStatus("Failed");
                        throw;
                    }
                    finally
                    {
                        requestScheduler.Complete();
                        downloadScheduler.Complete();
                        await Task.WhenAll(requestWorkers).ConfigureAwait(false);
                        await Task.WhenAll(downloadWorkers).ConfigureAwait(false);
                        persistChannel.Writer.TryComplete();
                        await Task.WhenAll(persistWorkers).ConfigureAwait(false);

                        if (string.Equals(runRuntime.Status, "Running", StringComparison.Ordinal)) runRuntime.SetStatus("Completed");

                        if (!runRuntime.CancellationTokenSource.IsCancellationRequested) await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);

                        await snapshotTask.ConfigureAwait(false);
                        foreach (var runtime in _nodeRuntimes.Values) await runtime.DisposeAsync().ConfigureAwait(false);

                        _nodeRuntimes.Clear();
                        LumaEngineLogMessages.RunFinishedLog(_logger, commandName, runName, Interlocked.Read(ref _storedItemCount), Interlocked.Read(ref _activeNetworkCount), requestScheduler.Count + downloadScheduler.Count, null);
                    }
                }
                finally
                {
                    requestScheduler.Dispose();
                    downloadScheduler.Dispose();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    /// <summary>
    ///     重置运行态字段。
    /// </summary>
    private void ResetRuntimeState()
    {
        _nodeRuntimes.Clear();
        Interlocked.Exchange(ref _activeNetworkCount, 0);
        Interlocked.Exchange(ref _storedItemCount, 0);
        Interlocked.Exchange(ref _initializingNodeCount, 0);
        while (_stateSignalChannel.Reader.TryRead(out _))
        {
        }
    }

    /// <summary>
    ///     注册节点并执行初始化阶段。
    /// </summary>
    /// <param name="node">节点实例。</param>
    /// <param name="nodeState">节点状态。</param>
    /// <param name="parentPath">父节点路径。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="cookieAccessor">Cookie 访问器。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="prioritizeRequests">是否优先入队普通请求。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "引擎需隔离节点异常，保证主流程可持续。")]
    private async Task RegisterNodeAsync(
        LumaNode<TState> node,
        TState nodeState,
        string? parentPath,
        LumaRunRuntime runRuntime,
        ICookieAccessor cookieAccessor,
        NodeTaskScheduler requestScheduler,
        ChannelWriter<ItemEnvelope<TState>> persistWriter,
        bool prioritizeRequests)
    {
        var path = string.IsNullOrWhiteSpace(parentPath) ? node.Key : $"{parentPath}/{node.Key}";
        var depth = string.IsNullOrWhiteSpace(parentPath) ? 0 : parentPath.Count(static ch => ch == '/') + 1;
        var runtime = new LumaNodeRuntime<TState>(node, path, depth, runRuntime.RunId, runRuntime.RunName, runRuntime.CommandName, nodeState, _htmlParser, cookieAccessor, _loggerFactory, runRuntime.Token);

        if (!_nodeRuntimes.TryAdd(path, runtime))
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            _logManager.Write(LogLevelKind.Warning, "Engine", $"Duplicate node path skipped: {path}");
            LumaEngineLogMessages.DuplicateNodePathLog(_logger, path, null);
            return;
        }

        Interlocked.Increment(ref _initializingNodeCount);
        SignalStateChanged();

        try
        {
            runtime.State.SetStatus(NodeExecutionStatus.Running);
            await BuildNodeRequestsAsync(runtime, requestScheduler, runRuntime.Token, prioritizeRequests).ConfigureAwait(false);
            await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistWriter, null, prioritizeRequests).ConfigureAwait(false);
        }
        catch (LumaStopException stopException)
        {
            await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node build-request phase stopped by business rule.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
        {
            runtime.State.SetStatus(NodeExecutionStatus.Cancelled, "Node initialization cancelled.");
        }
        catch (Exception exception)
        {
            if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.BuildRequests, null, null, null).ConfigureAwait(false))
            {
                runtime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
                _logManager.Write(LogLevelKind.Error, "Engine", $"Node start failed: {runtime.Path}", exception);
                LumaEngineLogMessages.NodeStartFailedLog(_logger, runtime.Path, exception);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _initializingNodeCount);
            SignalStateChanged();
        }
    }

    /// <summary>
    ///     构建节点初始请求并入队。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="prioritizeRequests">是否优先入队。</param>
    /// <returns>异步任务。</returns>
    private async Task BuildNodeRequestsAsync(LumaNodeRuntime<TState> runtime, NodeTaskScheduler requestScheduler, CancellationToken cancellationToken, bool prioritizeRequests)
    {
        await foreach (var request in runtime.Node.BuildRequestsAsync(runtime.Context).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var normalizedRequest = NormalizeRequest(request, runtime.Path);
            await requestScheduler.EnqueueAsync(normalizedRequest, prioritizeRequests, cancellationToken).ConfigureAwait(false);
            runtime.State.IncrementQueued();
            SignalStateChanged();
        }
    }

    /// <summary>
    ///     普通请求工作循环。
    /// </summary>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "工作循环需兜底节点异常，避免单任务中断全局管线。")]
    private async Task RequestWorkerAsync(NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler, ChannelWriter<ItemEnvelope<TState>> persistWriter, LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                var request = await requestScheduler.DequeueAsync(runRuntime.Token).ConfigureAwait(false);
                if (request is null) return;

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var runtime))
                {
                    SignalStateChanged();
                    continue;
                }

                runtime.State.DecrementQueued();
                SignalStateChanged();
                if (runtime.CancellationTokenSource.IsCancellationRequested) continue;

                var enteredExecutionSlot = false;
                runtime.State.IncrementActive();
                Interlocked.Increment(ref _activeNetworkCount);
                SignalStateChanged();

                try
                {
                    await runtime.WaitRequestExecutionSlotAsync(runtime.Context.CancellationToken).ConfigureAwait(false);
                    enteredExecutionSlot = true;
                    using var response = await SendAsync(request, runtime.Context, runtime.Context.CancellationToken).ConfigureAwait(false);
                    await runtime.Node.HandleResponseAsync(response, runtime.Context).ConfigureAwait(false);
                    await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistWriter, request, false).ConfigureAwait(false);

                    bool shouldDownload;
                    try
                    {
                        shouldDownload = await runtime.Node.ShouldDownloadAsync(response, runtime.Context).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.ShouldDownload, request, response, null).ConfigureAwait(false)) throw;

                        shouldDownload = false;
                    }

                    if (shouldDownload)
                    {
                        try
                        {
                            await foreach (var downloadRequest in runtime.Node.BuildDownloadRequestsAsync(response, runtime.Context).WithCancellation(runtime.Context.CancellationToken).ConfigureAwait(false))
                            {
                                var normalizedDownloadRequest = NormalizeRequest(downloadRequest, runtime.Path);
                                await downloadScheduler.EnqueueAsync(normalizedDownloadRequest, false, runRuntime.Token).ConfigureAwait(false);
                                runtime.State.IncrementQueued();
                                SignalStateChanged();
                            }
                        }
                        catch (Exception exception)
                        {
                            if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.BuildDownloadRequests, request, response, null).ConfigureAwait(false)) throw;
                        }

                        await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistWriter, request, false).ConfigureAwait(false);
                    }
                }
                catch (LumaStopException stopException)
                {
                    await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node response phase stopped by business rule.").ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.HandleResponse, request, null, null).ConfigureAwait(false))
                    {
                        runtime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
                        var errorType = exception.GetType().FullName ?? exception.GetType().Name;
                        _logManager.Write(LogLevelKind.Error, "Engine", $"Node response handling failed: {runtime.Path}. Error={errorType}: {exception.Message}", exception);
                        LumaEngineLogMessages.NodeResponseHandlingFailedLog(_logger, runtime.Path, errorType, exception.Message, exception);
                    }
                }
                finally
                {
                    if (enteredExecutionSlot) runtime.ReleaseRequestExecutionSlot();

                    runtime.State.DecrementActive();
                    Interlocked.Decrement(ref _activeNetworkCount);
                    SignalStateChanged();
                }
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    ///     下载请求工作循环。
    /// </summary>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "下载循环需兜底节点异常，避免单任务中断全局管线。")]
    private async Task DownloadWorkerAsync(NodeTaskScheduler downloadScheduler, NodeTaskScheduler requestScheduler, ChannelWriter<ItemEnvelope<TState>> persistWriter, LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                var request = await downloadScheduler.DequeueAsync(runRuntime.Token).ConfigureAwait(false);
                if (request is null) return;

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var runtime))
                {
                    SignalStateChanged();
                    continue;
                }

                runtime.State.DecrementQueued();
                SignalStateChanged();
                if (runtime.CancellationTokenSource.IsCancellationRequested) continue;

                var enteredExecutionSlot = false;
                runtime.State.IncrementActive();
                Interlocked.Increment(ref _activeNetworkCount);
                SignalStateChanged();

                try
                {
                    await runtime.WaitRequestExecutionSlotAsync(runtime.Context.CancellationToken).ConfigureAwait(false);
                    enteredExecutionSlot = true;
                    using var response = await SendAsync(request, runtime.Context, runtime.Context.CancellationToken).ConfigureAwait(false);
                    await runtime.Node.HandleDownloadResponseAsync(response, request, runtime.Context).ConfigureAwait(false);
                    await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistWriter, request, false).ConfigureAwait(false);
                }
                catch (LumaStopException stopException)
                {
                    await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node download phase stopped by business rule.").ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested || runRuntime.Token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.HandleDownloadResponse, request, null, null).ConfigureAwait(false))
                    {
                        runtime.State.SetStatus(NodeExecutionStatus.Failed, exception.Message);
                        var errorType = exception.GetType().FullName ?? exception.GetType().Name;
                        _logManager.Write(LogLevelKind.Error, "Engine", $"Node download handling failed: {runtime.Path}. Error={errorType}: {exception.Message}", exception);
                        LumaEngineLogMessages.NodeResponseHandlingFailedLog(_logger, runtime.Path, errorType, exception.Message, exception);
                    }
                }
                finally
                {
                    if (enteredExecutionSlot) runtime.ReleaseRequestExecutionSlot();

                    runtime.State.DecrementActive();
                    Interlocked.Decrement(ref _activeNetworkCount);
                    SignalStateChanged();
                }
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    ///     分发节点待处理批次。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="sourceRequest">源请求。</param>
    /// <param name="prioritizeRequests">是否优先入队。</param>
    /// <returns>异步任务。</returns>
    private async Task DispatchNodeBatchAsync(LumaNodeRuntime<TState> runtime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler, ChannelWriter<ItemEnvelope<TState>> persistWriter, LumaRequest? sourceRequest,
        bool prioritizeRequests)
    {
        var dispatchBatch = runtime.Node.DrainDispatchBatch();

        if (dispatchBatch.StopNode) runtime.Cancel(dispatchBatch.StopReason);

        foreach (var request in dispatchBatch.Requests)
        {
            var normalizedRequest = NormalizeRequest(request, runtime.Path);
            await requestScheduler.EnqueueAsync(normalizedRequest, prioritizeRequests, runRuntime.Token).ConfigureAwait(false);
            runtime.State.IncrementQueued();
            SignalStateChanged();
        }

        foreach (var item in dispatchBatch.Items) await persistWriter.WriteAsync(new ItemEnvelope<TState>(item, runtime.Context, sourceRequest), runRuntime.Token).ConfigureAwait(false);

        if (dispatchBatch.Children.Count > 0) await RegisterChildrenAsync(dispatchBatch.Children, runtime, runRuntime, requestScheduler, persistWriter).ConfigureAwait(false);
    }

    /// <summary>
    ///     按节点策略注册子节点。
    /// </summary>
    /// <param name="children">子节点集合。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildrenAsync(IReadOnlyList<NodeChildBinding<TState>> children, LumaNodeRuntime<TState> parentRuntime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler,
        ChannelWriter<ItemEnvelope<TState>> persistWriter)
    {
        var traversalPolicy = parentRuntime.Node.ExecutionOptions.ChildTraversalPolicy;
        var prioritizeRequests = traversalPolicy == ChildTraversalPolicy.Depth;
        var tasks = new List<Task>(children.Count);

        foreach (var childBinding in children)
        {
            await parentRuntime.WaitChildSlotAsync(runRuntime.Token).ConfigureAwait(false);
            tasks.Add(RegisterChildInternalAsync(childBinding, parentRuntime, runRuntime, requestScheduler, persistWriter, prioritizeRequests));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     注册单个子节点并释放并发闸门。
    /// </summary>
    /// <param name="childBinding">子节点映射定义。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistWriter">持久化通道写入器。</param>
    /// <param name="prioritizeRequests">是否优先入队。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildInternalAsync(NodeChildBinding<TState> childBinding, LumaNodeRuntime<TState> parentRuntime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler,
        ChannelWriter<ItemEnvelope<TState>> persistWriter, bool prioritizeRequests)
    {
        try
        {
            var childState = childBinding.StateMapper(parentRuntime.Context.State);
            var cookieAccessor = new NetCookieAccessor(_netClient, ResolveRouteKind);
            await RegisterNodeAsync(childBinding.Node, childState, parentRuntime.Path, runRuntime, cookieAccessor, requestScheduler, persistWriter, prioritizeRequests).ConfigureAwait(false);
        }
        finally
        {
            parentRuntime.ReleaseChildSlot();
        }
    }

    /// <summary>
    ///     执行网络请求。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="context">节点上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>HTTP 响应。</returns>
    private async ValueTask<HttpResponseMessage> SendAsync(LumaRequest request, LumaContext<TState> context, CancellationToken cancellationToken)
    {
        var effectiveRouteKind = ResolveRouteKind(request.RouteKind, context.DefaultRouteKind);
        var netRouteKind = effectiveRouteKind == LumaRouteKind.Proxy ? NetRouteKind.Proxy : NetRouteKind.Direct;
        await using var lease = await _netClient.RentAsync(netRouteKind, cancellationToken).ConfigureAwait(false);
        using var timeoutCancellationTokenSource = CreateTimeoutCancellationTokenSource(request, cancellationToken);
        var effectiveCancellationToken = timeoutCancellationTokenSource?.Token ?? cancellationToken;

        try
        {
            return await lease.HttpClient.SendAsync(request.HttpRequestMessage, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebException)
        {
            var failedResponse = new HttpResponseMessage((HttpStatusCode)599) { RequestMessage = request.HttpRequestMessage, ReasonPhrase = exception.Message, Content = new ByteArrayContent([]) };
            failedResponse.Headers.TryAddWithoutValidation("X-Luma-Transport-Error", exception.GetType().Name);
            return failedResponse;
        }
    }

    /// <summary>
    ///     归一化请求并绑定节点路径。
    /// </summary>
    /// <param name="request">源请求。</param>
    /// <param name="nodePath">节点路径。</param>
    /// <returns>归一化请求。</returns>
    private static LumaRequest NormalizeRequest(LumaRequest request, string nodePath)
    {
        return new LumaRequest(request.HttpRequestMessage, nodePath)
        {
            RouteKind = request.RouteKind,
            Timeout = request.Timeout
        };
    }

    /// <summary>
    ///     按请求级超时创建联动取消源。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>取消源；未配置超时则返回 null。</returns>
    private static CancellationTokenSource? CreateTimeoutCancellationTokenSource(LumaRequest request, CancellationToken cancellationToken)
    {
        if (request.Timeout is not { } timeout || timeout <= TimeSpan.Zero) return null;

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        return cancellationTokenSource;
    }

    /// <summary>
    ///     持久化工作循环。
    /// </summary>
    /// <param name="reader">持久化通道读取器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "持久化循环需要吞吐保护，避免单批次失败中断全局。")]
    private async Task PersistWorkerAsync(ChannelReader<ItemEnvelope<TState>> reader, LumaRunRuntime runRuntime)
    {
        var buffer = new List<ItemEnvelope<TState>>(_options.PersistBatchSize);
        try
        {
            while (await TryReadPersistEnvelopeAsync(reader, buffer, runRuntime.Token).ConfigureAwait(false))
            {
                await FillPersistBatchAsync(reader, buffer).ConfigureAwait(false);
                await FlushPersistBatchAsync(buffer, runRuntime, runRuntime.Token).ConfigureAwait(false);
                SignalStateChanged();
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
            return;
        }

        if (buffer.Count > 0)
        {
            using var finalFlushCancellationTokenSource = CreateFinalFlushCancellationTokenSource(runRuntime.Token);
            try
            {
                await FlushPersistBatchAsync(buffer, runRuntime, finalFlushCancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (finalFlushCancellationTokenSource.IsCancellationRequested)
            {
                LumaEngineLogMessages.FinalPersistBatchFlushTimedOutLog(_logger, null);
            }

            SignalStateChanged();
        }
    }

    /// <summary>
    ///     尝试读取一条持久化信封。
    /// </summary>
    /// <param name="reader">读取器。</param>
    /// <param name="buffer">缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取成功返回 true。</returns>
    private static async ValueTask<bool> TryReadPersistEnvelopeAsync(ChannelReader<ItemEnvelope<TState>> reader, List<ItemEnvelope<TState>> buffer, CancellationToken cancellationToken)
    {
        if (!reader.TryRead(out var envelope) && (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) || !reader.TryRead(out envelope))) return false;

        buffer.Add(envelope);
        return true;
    }

    /// <summary>
    ///     聚合批量持久化数据。
    /// </summary>
    /// <param name="reader">读取器。</param>
    /// <param name="buffer">缓冲区。</param>
    /// <returns>异步任务。</returns>
    private async Task FillPersistBatchAsync(ChannelReader<ItemEnvelope<TState>> reader, List<ItemEnvelope<TState>> buffer)
    {
        if (buffer.Count <= 0 || buffer.Count >= _options.PersistBatchSize) return;

        var deadlineUtc = DateTimeOffset.UtcNow + _options.PersistFlushInterval;
        while (buffer.Count < _options.PersistBatchSize)
        {
            while (buffer.Count < _options.PersistBatchSize && reader.TryRead(out var envelope)) buffer.Add(envelope);

            if (buffer.Count >= _options.PersistBatchSize) return;

            var remaining = deadlineUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return;

            try
            {
                var canRead = await reader.WaitToReadAsync().AsTask().WaitAsync(remaining).ConfigureAwait(false);
                if (!canRead) return;
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     刷新当前持久化批次。
    /// </summary>
    /// <param name="buffer">缓冲区。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "持久化异常需转换为失败结果，不能终止整个运行。")]
    private async Task FlushPersistBatchAsync(List<ItemEnvelope<TState>> buffer, LumaRunRuntime runRuntime, CancellationToken cancellationToken)
    {
        if (buffer.Count <= 0) return;

        var persistedIndexes = new List<int>(buffer.Count);
        var filteredEnvelopes = new List<ItemEnvelope<TState>>(buffer.Count);
        var resolvedResults = new PersistResult[buffer.Count];

        for (var index = 0; index < buffer.Count; index++)
        {
            var envelope = buffer[index];
            if (!_nodeRuntimes.TryGetValue(envelope.NodePath, out var runtime))
            {
                resolvedResults[index] = PersistResult.Skipped("Node runtime not found.");
                continue;
            }

            var persistContext = new PersistContext<TState>(runtime.Context, envelope.SourceRequest, index);
            bool shouldPersist;
            try
            {
                shouldPersist = await runtime.Node.ShouldPersistAsync(envelope.Item, persistContext).ConfigureAwait(false);
            }
            catch (LumaStopException stopException)
            {
                await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node persist filter phase stopped by business rule.").ConfigureAwait(false);
                if (stopException.Scope != LumaStopScope.Node) throw;

                resolvedResults[index] = PersistResult.Skipped(stopException.Message);
                continue;
            }
            catch (Exception exception)
            {
                if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.ShouldPersist, envelope.SourceRequest, null, envelope.Item).ConfigureAwait(false)) throw;

                resolvedResults[index] = PersistResult.Failed(exception.Message);
                continue;
            }

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
            batchPersistResults = [];
        else
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
                _logManager.Write(LogLevelKind.Error, "Persist", $"Persist batch failed. {exception.Message}", exception);
                LumaEngineLogMessages.PersistBatchFailedLog(_logger, exception);
                batchPersistResults = CreateFailedPersistResults(filteredEnvelopes.Count, exception.Message);
            }

        if (batchPersistResults.Count != filteredEnvelopes.Count) throw new InvalidOperationException("Persist batch result count must match filtered input count.");

        for (var resultIndex = 0; resultIndex < persistedIndexes.Count; resultIndex++) resolvedResults[persistedIndexes[resultIndex]] = batchPersistResults[resultIndex];

        for (var index = 0; index < buffer.Count; index++)
        {
            var envelope = buffer[index];
            if (!_nodeRuntimes.TryGetValue(envelope.NodePath, out var runtime)) continue;

            var persistResult = resolvedResults[index];
            runtime.State.ApplyPersistResult(persistResult);
            if (persistResult.Decision == PersistDecision.Stored) Interlocked.Increment(ref _storedItemCount);

            var callbackContext = new PersistContext<TState>(runtime.Context, envelope.SourceRequest, index);
            try
            {
                await runtime.Node.OnPersistedAsync(envelope.Item, persistResult, callbackContext).ConfigureAwait(false);
            }
            catch (LumaStopException stopException)
            {
                await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node persisted callback phase stopped by business rule.").ConfigureAwait(false);
                if (stopException.Scope != LumaStopScope.Node) throw;
            }
            catch (Exception exception)
            {
                if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.OnPersisted, envelope.SourceRequest, null, envelope.Item).ConfigureAwait(false)) throw;
            }

            var shouldStopByPersistSuggestion = persistResult is { Decision: PersistDecision.AlreadyExists, SuggestStopNode: true };
            var shouldStopByThreshold = persistResult.Decision == PersistDecision.AlreadyExists && runtime.Node.ConsecutiveExistingStopThreshold > 0 && runtime.State.ConsecutiveExistingCount >= runtime.Node.ConsecutiveExistingStopThreshold;
            if (!shouldStopByPersistSuggestion && !shouldStopByThreshold) continue;

            runtime.Cancel(persistResult.Message);
            SignalStateChanged();
        }

        buffer.Clear();
    }

    /// <summary>
    ///     构造整批失败结果集合。
    /// </summary>
    /// <param name="count">结果数量。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>失败结果集合。</returns>
    private static PersistResult[] CreateFailedPersistResults(int count, string message)
    {
        var results = new PersistResult[count];
        for (var index = 0; index < count; index++) results[index] = PersistResult.Failed(message);

        return results;
    }

    /// <summary>
    ///     创建最终收尾批次取消源。
    /// </summary>
    /// <param name="runCancellationToken">运行级取消令牌。</param>
    /// <returns>取消源。</returns>
    private CancellationTokenSource CreateFinalFlushCancellationTokenSource(CancellationToken runCancellationToken)
    {
        var finalFlushTimeout = _options.PersistFlushInterval > TimeSpan.FromSeconds(5) ? _options.PersistFlushInterval : TimeSpan.FromSeconds(5);
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(runCancellationToken);
        cancellationTokenSource.CancelAfter(finalFlushTimeout);
        return cancellationTokenSource;
    }

    /// <summary>
    ///     等待运行完成。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <returns>异步任务。</returns>
    private async Task WaitForCompletionAsync(LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler)
    {
        while (!runRuntime.Token.IsCancellationRequested)
        {
            if (IsRunCompleted(requestScheduler, downloadScheduler))
            {
                foreach (var runtime in _nodeRuntimes.Values)
                    if (runtime.State.Status is NodeExecutionStatus.Running or NodeExecutionStatus.Stopping)
                        runtime.State.SetStatus(runtime.CancellationTokenSource.IsCancellationRequested ? NodeExecutionStatus.Cancelled : NodeExecutionStatus.Completed, runtime.State.Reason);

                return;
            }

            await _stateSignalChannel.Reader.ReadAsync(runRuntime.Token).ConfigureAwait(false);
            while (_stateSignalChannel.Reader.TryRead(out _))
            {
            }
        }
    }

    /// <summary>
    ///     判断当前运行是否已完成。
    /// </summary>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <returns>完成返回 true。</returns>
    private bool IsRunCompleted(NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler)
    {
        if (Interlocked.Read(ref _initializingNodeCount) > 0) return false;

        if (requestScheduler.Count > 0 || downloadScheduler.Count > 0) return false;

        if (Interlocked.Read(ref _activeNetworkCount) > 0) return false;

        return _nodeRuntimes.Values.All(static runtime => runtime.State is { ActiveRequestCount: <= 0, QueuedRequestCount: <= 0 });
    }

    /// <summary>
    ///     发送状态变更信号。
    /// </summary>
    private void SignalStateChanged()
    {
        _stateSignalChannel.Writer.TryWrite(true);
    }

    /// <summary>
    ///     解析最终路由类型。
    /// </summary>
    /// <param name="requestRouteKind">请求路由类型。</param>
    /// <param name="nodeRouteKind">节点默认路由类型。</param>
    /// <returns>最终路由类型。</returns>
    private LumaRouteKind ResolveRouteKind(LumaRouteKind requestRouteKind, LumaRouteKind nodeRouteKind)
    {
        if (requestRouteKind != LumaRouteKind.Auto) return requestRouteKind;

        if (nodeRouteKind != LumaRouteKind.Auto) return nodeRouteKind;

        return _options.DefaultRouteKind == LumaRouteKind.Proxy ? LumaRouteKind.Proxy : LumaRouteKind.Direct;
    }

    /// <summary>
    ///     处理节点生命周期异常并按节点策略决定后续动作。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="exception">异常对象。</param>
    /// <param name="phase">异常阶段。</param>
    /// <param name="sourceRequest">源请求。</param>
    /// <param name="response">源响应。</param>
    /// <param name="item">源数据项。</param>
    /// <returns>已处理返回 true；需要继续上抛返回 false。</returns>
    private async ValueTask<bool> HandleNodeExceptionAsync(LumaNodeRuntime<TState> runtime, LumaRunRuntime runRuntime, Exception exception, NodeExceptionPhase phase, LumaRequest? sourceRequest,
        HttpResponseMessage? response, IItem? item)
    {
        var exceptionContext = new NodeExceptionContext<TState>(runtime.Context, phase, sourceRequest, response, item);
        NodeExceptionAction action;
        try
        {
            action = await runtime.Node.OnExceptionAsync(exception, exceptionContext).ConfigureAwait(false);
        }
        catch (Exception callbackException)
        {
            runtime.State.SetStatus(NodeExecutionStatus.Failed, callbackException.Message);
            _logManager.Write(LogLevelKind.Error, "Engine", $"Node exception callback failed. Node={runtime.Path}, Phase={phase}, Error={callbackException.Message}", callbackException);
            return false;
        }

        var reason = $"[{phase}] {exception.Message}";
        _logManager.Write(LogLevelKind.Warning, "Engine", $"Node exception handled. Node={runtime.Path}, Phase={phase}, Action={action}, Error={exception.GetType().Name}: {exception.Message}");

        switch (action)
        {
            case NodeExceptionAction.KeepRunning:
            {
                return true;
            }
            case NodeExceptionAction.StopNode:
            {
                runtime.Cancel(reason);
                runtime.State.SetStatus(NodeExecutionStatus.Failed, reason);
                return true;
            }
            case NodeExceptionAction.StopRun:
            {
                runtime.State.SetStatus(NodeExecutionStatus.Failed, reason);
                runRuntime.SetStatus("Stopped");
                if (!runRuntime.CancellationTokenSource.IsCancellationRequested) await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);

                return true;
            }
            case NodeExceptionAction.Rethrow:
            {
                return false;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown node exception action.");
            }
        }
    }

    /// <summary>
    ///     解析有效普通请求工作线程数量。
    /// </summary>
    /// <param name="rootNode">根节点。</param>
    /// <returns>有效线程数量。</returns>
    private int ResolveEffectiveRequestWorkerCount(LumaNode<TState> rootNode)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        var configuredWorkerCount = Math.Max(1, _options.RequestWorkerCount);
        if (rootNode.ExecutionOptions.ChildTraversalPolicy != ChildTraversalPolicy.Depth) return configuredWorkerCount;

        if (configuredWorkerCount > 1) _logManager.Write(LogLevelKind.Warning, "Engine", $"Depth traversal detected. RequestWorkerCount forced from {configuredWorkerCount} to 1 for strict pre-order traversal.");

        return 1;
    }

    /// <summary>
    ///     处理节点主动抛出的停止异常。
    /// </summary>
    /// <param name="exception">停止异常。</param>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="phase">阶段文本。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask HandleStopExceptionAsync(LumaStopException exception, LumaNodeRuntime<TState> runtime, LumaRunRuntime runRuntime, string phase)
    {
        var reason = $"[{exception.Code}] {exception.Message}";
        switch (exception.Scope)
        {
            case LumaStopScope.Node:
            {
                runtime.Cancel(reason);
                _logManager.Write(LogLevelKind.Warning, "Engine", $"{phase} Node={runtime.Path}, Scope={exception.Scope}, Reason={reason}");
                LumaEngineLogMessages.NodeScopedStopLog(_logger, runtime.Path, exception.Code, reason, null);
                return;
            }
            case LumaStopScope.Run:
            case LumaStopScope.App:
            {
                runtime.State.SetStatus(NodeExecutionStatus.Cancelled, reason);
                runRuntime.SetStatus("Stopped");
                if (!runRuntime.CancellationTokenSource.IsCancellationRequested) await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);

                _logManager.Write(LogLevelKind.Error, "Engine", $"{phase} Node={runtime.Path}, Scope={exception.Scope}, Reason={reason}");
                LumaEngineLogMessages.RunScopedStopLog(_logger, runtime.Path, exception.Scope.ToString(), exception.Code, reason, null);
                return;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(exception), exception.Scope, "Unknown stop scope.");
            }
        }
    }

    /// <summary>
    ///     周期发布进度快照。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <returns>异步任务。</returns>
    private async Task PublishSnapshotsLoopAsync(LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                PublishSnapshot(runRuntime, requestScheduler, downloadScheduler);
                await Task.Delay(_options.PresentationRefreshInterval, runRuntime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
            PublishSnapshot(runRuntime, requestScheduler, downloadScheduler);
        }
    }

    /// <summary>
    ///     发布单次快照。
    /// </summary>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    private void PublishSnapshot(LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler)
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
                runtime.State.Reason)
            ).ToArray();

        _progressManager.Publish(new ProgressSnapshot
        {
            RunId = runRuntime.RunId,
            RunName = runRuntime.RunName,
            CommandName = runRuntime.CommandName,
            Status = runRuntime.Status,
            StoredItemCount = Interlocked.Read(ref _storedItemCount),
            ActiveRequestCount = Interlocked.Read(ref _activeNetworkCount),
            QueuedRequestCount = requestScheduler.Count + downloadScheduler.Count,
            Elapsed = DateTimeOffset.UtcNow - runRuntime.StartedAtUtc,
            Nodes = nodes
        });
    }

    /// <summary>
    ///     基于网络租约实现 Cookie 访问器。
    /// </summary>
    private sealed class NetCookieAccessor : ICookieAccessor
    {
        /// <summary>
        ///     网络客户端入口。
        /// </summary>
        private readonly INetClient _netClient;

        /// <summary>
        ///     路由解析函数。
        /// </summary>
        private readonly Func<LumaRouteKind, LumaRouteKind, LumaRouteKind> _routeResolver;

        /// <summary>
        ///     初始化 Cookie 访问器。
        /// </summary>
        /// <param name="netClient">网络客户端入口。</param>
        /// <param name="routeResolver">路由解析函数。</param>
        public NetCookieAccessor(INetClient netClient, Func<LumaRouteKind, LumaRouteKind, LumaRouteKind> routeResolver)
        {
            _netClient = netClient ?? throw new ArgumentNullException(nameof(netClient));
            _routeResolver = routeResolver ?? throw new ArgumentNullException(nameof(routeResolver));
        }

        /// <inheritdoc />
        public async ValueTask SetCookieAsync(LumaRouteKind routeKind, Cookie cookie, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            var domain = cookie.Domain.TrimStart('.');
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
            var uri = BuildUri(domain, path);
            await WithCookieContainerAsync(routeKind, container => { container.Add(uri, cookie); }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask<IReadOnlyList<Cookie>> GetCookiesAsync(LumaRouteKind routeKind, string domain, string path, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            var uri = BuildUri(domain, path);
            return await WithCookieContainerAsync(routeKind, container => { return (IReadOnlyList<Cookie>)container.GetCookies(uri).Select(CloneCookie).ToArray(); }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask ClearCookiesAsync(LumaRouteKind routeKind, string domain, string path, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            var uri = BuildUri(domain, path);
            await WithCookieContainerAsync(routeKind, container =>
            {
                foreach (var cookie in container.GetCookies(uri).Cast<Cookie>()) container.Add(uri, new Cookie(cookie.Name, string.Empty, cookie.Path, cookie.Domain) { Expires = DateTime.UtcNow.AddYears(-1) });
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     获取指定路由的 Cookie 容器。
        /// </summary>
        /// <param name="routeKind">路由类型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>Cookie 容器。</returns>
        private async ValueTask WithCookieContainerAsync(LumaRouteKind routeKind, Action<CookieContainer> action, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(action);
            var resolvedRouteKind = _routeResolver(routeKind, LumaRouteKind.Auto);
            var netRouteKind = resolvedRouteKind == LumaRouteKind.Proxy ? NetRouteKind.Proxy : NetRouteKind.Direct;
            await using var lease = await _netClient.RentAsync(netRouteKind, cancellationToken).ConfigureAwait(false);
            action(lease.CookieContainer);
        }

        /// <summary>
        ///     在 Cookie 容器租约作用域内执行并返回结果。
        /// </summary>
        /// <typeparam name="T">返回结果类型。</typeparam>
        /// <param name="routeKind">路由类型。</param>
        /// <param name="func">容器回调函数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>回调结果。</returns>
        private async ValueTask<T> WithCookieContainerAsync<T>(LumaRouteKind routeKind, Func<CookieContainer, T> func, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(func);
            var resolvedRouteKind = _routeResolver(routeKind, LumaRouteKind.Auto);
            var netRouteKind = resolvedRouteKind == LumaRouteKind.Proxy ? NetRouteKind.Proxy : NetRouteKind.Direct;
            await using var lease = await _netClient.RentAsync(netRouteKind, cancellationToken).ConfigureAwait(false);
            return func(lease.CookieContainer);
        }

        /// <summary>
        ///     克隆 Cookie。
        /// </summary>
        /// <param name="cookie">源 Cookie。</param>
        /// <returns>克隆 Cookie。</returns>
        private static Cookie CloneCookie(Cookie cookie)
        {
            return new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
            {
                Expires = cookie.Expires,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                Comment = cookie.Comment,
                CommentUri = cookie.CommentUri,
                Discard = cookie.Discard,
                Expired = cookie.Expired,
                Port = cookie.Port,
                Version = cookie.Version
            };
        }

        /// <summary>
        ///     构造 Cookie 查询地址。
        /// </summary>
        /// <param name="domain">域名。</param>
        /// <param name="path">路径。</param>
        /// <returns>地址对象。</returns>
        private static Uri BuildUri(string domain, string path)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith('/') ? path : $"/{path}";
            return new Uri($"https://{domain.TrimStart('.')}{normalizedPath}", UriKind.Absolute);
        }
    }
}

/// <summary>
///     <b>LumaEngine 日志消息定义</b>
/// </summary>
internal static class LumaEngineLogMessages
{
    /// <summary>
    ///     运行启动日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, string, Exception?> RunStartedLog =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1001, nameof(RunStartedLog)), "Luma run started. Command={CommandName}, Run={RunName}");

    /// <summary>
    ///     运行结束日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, string, long, long, long, Exception?> RunFinishedLog =
        LoggerMessage.Define<string, string, long, long, long>(LogLevel.Information, new EventId(1002, nameof(RunFinishedLog)),
            "Luma run finished. Command={CommandName}, Run={RunName}, Stored={StoredItemCount}, Active={ActiveRequestCount}, Queued={QueuedRequestCount}");

    /// <summary>
    ///     节点启动失败日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, Exception?> NodeStartFailedLog =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1003, nameof(NodeStartFailedLog)), "Node start failed: {NodePath}");

    /// <summary>
    ///     节点响应处理失败日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, string, string, Exception?> NodeResponseHandlingFailedLog =
        LoggerMessage.Define<string, string, string>(LogLevel.Error, new EventId(1004, nameof(NodeResponseHandlingFailedLog)),
            "Node response handling failed: {NodePath}. Error={ErrorType}: {ErrorMessage}");

    /// <summary>
    ///     最终批次刷新超时日志委托。
    /// </summary>
    internal static readonly Action<ILogger, Exception?> FinalPersistBatchFlushTimedOutLog =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1005, nameof(FinalPersistBatchFlushTimedOutLog)), "Final persist batch flush timed out and was aborted.");

    /// <summary>
    ///     持久化批次失败日志委托。
    /// </summary>
    internal static readonly Action<ILogger, Exception?> PersistBatchFailedLog =
        LoggerMessage.Define(LogLevel.Error, new EventId(1006, nameof(PersistBatchFailedLog)), "Persist batch failed.");

    /// <summary>
    ///     路径冲突日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, Exception?> DuplicateNodePathLog =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1007, nameof(DuplicateNodePathLog)), "Duplicate node path skipped: {NodePath}");

    /// <summary>
    ///     节点级停止日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, string, string, Exception?> NodeScopedStopLog =
        LoggerMessage.Define<string, string, string>(LogLevel.Warning, new EventId(1008, nameof(NodeScopedStopLog)), "Node scoped stop triggered. Node={NodePath}, Code={Code}, Reason={Reason}");

    /// <summary>
    ///     运行级停止日志委托。
    /// </summary>
    internal static readonly Action<ILogger, string, string, string, string, Exception?> RunScopedStopLog =
        LoggerMessage.Define<string, string, string, string>(LogLevel.Error, new EventId(1009, nameof(RunScopedStopLog)), "Run scoped stop triggered. Node={NodePath}, Scope={Scope}, Code={Code}, Reason={Reason}");
}