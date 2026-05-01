using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zeayii.Infrastructure.Net.Abstractions.Http;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.FlowControl;
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
    ///     脏节点路径队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _dirtyRuntimePaths = new();

    /// <summary>
    ///     脏节点路径去重集合。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _dirtyRuntimePathSet = new(StringComparer.Ordinal);

    /// <summary>
    ///     节点流控策略解析器。
    /// </summary>
    private readonly Func<string?, INodeRequestFlowControlStrategy> _flowControlStrategyResolver;

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
    ///     按节点类型共享的请求流控器集合。
    /// </summary>
    private readonly ConcurrentDictionary<Type, NodeTypeRequestFlowController> _nodeTypeRequestFlowControllers = new();

    /// <summary>
    ///     按节点类型共享的并发闸门集合。
    /// </summary>
    private readonly ConcurrentDictionary<Type, NodeTypeConcurrencyController> _nodeTypeConcurrencyControllers = new();

    /// <summary>
    ///     按阶段共享执行选项目录。
    /// </summary>
    private readonly ConcurrentDictionary<string, NodeStageExecutionOptions> _stageExecutionOptionsCatalog = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     按阶段共享普通请求并发闸门。
    /// </summary>
    private readonly ConcurrentDictionary<string, StageConcurrencyController> _stageRequestConcurrencyGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     按阶段共享下载请求并发闸门。
    /// </summary>
    private readonly ConcurrentDictionary<string, StageConcurrencyController> _stageDownloadConcurrencyGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     按阶段共享最小请求间隔控制器。
    /// </summary>
    private readonly ConcurrentDictionary<string, StageMinIntervalController> _stageRequestIntervalControllers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     全局最小请求间隔限流器。
    /// </summary>
    private readonly RateLimiter? _globalRequestIntervalLimiter;

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
    ///     时间提供器。
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     子节点槽位等待诊断阈值（毫秒）。
    /// </summary>
    private const int ChildSlotWaitDiagnosticThresholdMilliseconds = 2000;

    /// <summary>
    ///     子节点槽位慢等待日志节流窗口（毫秒）。
    /// </summary>
    private const int ChildSlotWaitLogThrottleMilliseconds = 15000;

    /// <summary>
    ///     子节点槽位严重慢等待阈值（毫秒）。
    /// </summary>
    private const int ChildSlotWaitCriticalMilliseconds = 60000;

    /// <summary>
    ///     持久化批次常规摘要采样步长。
    /// </summary>
    private const int PersistBatchSummarySampleEvery = 100;

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
    ///     最近一次运行等待态日志时间戳（Unix 毫秒）。
    /// </summary>
    private long _lastRunWaitingLogTimestampMs;

    /// <summary>
    ///     子节点慢等待最近输出时间戳（按父节点路径）。
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _childSlotWaitLastLogTimestampByParentPath = new(StringComparer.Ordinal);

    /// <summary>
    ///     持久化批次摘要序列号。
    /// </summary>
    private long _persistBatchSummarySequence;

    /// <summary>
    ///     根节点运行时。
    /// </summary>
    private LumaNodeRuntime<TState>? _rootRuntime;

    /// <summary>
    ///     已成功持久化数量。
    /// </summary>
    private long _storedItemCount;

    /// <summary>
    ///     请求内容缓存键（用于重试期间复用缓冲内容）。
    /// </summary>
    private static readonly HttpRequestOptionsKey<byte[]> CachedRequestContentBytesOptionKey = new("luma.cached-request-content-bytes");

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
        _timeProvider = options.TimeProvider;
        _flowControlStrategyResolver = options.NodeFlowControlStrategyResolver ?? NodeRequestFlowControlStrategyRegistry.ResolveOrDefault;
        _globalRequestIntervalLimiter = options.GlobalMinRequestIntervalMilliseconds > 0
            ? CreateMinIntervalLimiter(options.GlobalMinRequestIntervalMilliseconds)
            : null;
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

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("LumaEngine is already running.");
        }

        try
        {
            ResetRuntimeState();
            LumaEngineLogMessages.RunStartedLog(_logger, commandName, runName, null);

            var runRuntime = new LumaRunRuntime(commandName, runName, cancellationToken, _timeProvider);
            await using (runRuntime.ConfigureAwait(false))
            {
                var rootState = await spider.CreateStateAsync(runRuntime.Token).ConfigureAwait(false);
                var rootNode = await spider.CreateRootAsync(rootState, runRuntime.Token).ConfigureAwait(false);
                var effectiveRequestWorkerCount = ResolveEffectiveRequestWorkerCount(rootNode);
                var requestScheduler = new NodeTaskScheduler(_options.RequestChannelCapacity);
                var downloadScheduler = new NodeTaskScheduler(_options.DownloadChannelCapacity);
                var effectivePersistWorkerCount = ResolveEffectivePersistWorkerCount(rootNode);
                var persistScheduler = new PriorityTaskScheduler<ItemEnvelope<TState>>(_options.PersistChannelCapacity);
                var cookieAccessor = new NetCookieAccessor(_netClient, ResolveRouteKind);

                try
                {
                    var persistWorkers = Enumerable.Range(0, effectivePersistWorkerCount).Select(_ => PersistWorkerAsync(persistScheduler, runRuntime)).ToArray();
                    var requestWorkers = Enumerable.Range(0, effectiveRequestWorkerCount).Select(_ => RequestWorkerAsync(requestScheduler, downloadScheduler, persistScheduler, runRuntime)).ToArray();
                    var downloadWorkers = Enumerable.Range(0, _options.DownloadWorkerCount).Select(_ => DownloadWorkerAsync(downloadScheduler, requestScheduler, persistScheduler, runRuntime)).ToArray();
                    var snapshotTask = PublishSnapshotsLoopAsync(runRuntime, requestScheduler, downloadScheduler);
                    var rootRuntime = await RegisterNodeAsync(rootNode, rootState, null, runRuntime, cookieAccessor, requestScheduler, persistScheduler).ConfigureAwait(false);
                    _rootRuntime = rootRuntime ?? throw new InvalidOperationException("Root runtime registration failed unexpectedly.");

                    try
                    {
                        await WaitForCompletionAsync(runRuntime, requestScheduler, downloadScheduler).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
                    {
                        if (!string.Equals(runRuntime.Status, "Stopped", StringComparison.Ordinal))
                        {
                            runRuntime.SetStatus("Cancelled");
                        }
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
                        persistScheduler.Complete();
                        await Task.WhenAll(persistWorkers).ConfigureAwait(false);

                        if (string.Equals(runRuntime.Status, "Running", StringComparison.Ordinal))
                        {
                            runRuntime.SetStatus("Completed");
                        }

                        if (!runRuntime.CancellationTokenSource.IsCancellationRequested)
                        {
                            await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);
                        }

                        await snapshotTask.ConfigureAwait(false);
                        foreach (var runtime in _nodeRuntimes.Values)
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }

                        _nodeRuntimes.Clear();
                        LumaEngineLogMessages.RunFinishedLog(_logger, commandName, runName, Interlocked.Read(ref _storedItemCount), Interlocked.Read(ref _activeNetworkCount), requestScheduler.Count + downloadScheduler.Count, null);
                    }
                }
                finally
                {
                    requestScheduler.Dispose();
                    downloadScheduler.Dispose();
                    persistScheduler.Dispose();
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
        foreach (var flowController in _nodeTypeRequestFlowControllers.Values)
        {
            flowController.Dispose();
        }

        foreach (var concurrencyController in _nodeTypeConcurrencyControllers.Values)
        {
            concurrencyController.Dispose();
        }

        _nodeRuntimes.Clear();
        _nodeTypeRequestFlowControllers.Clear();
        _nodeTypeConcurrencyControllers.Clear();
        _stageExecutionOptionsCatalog.Clear();
        foreach (var gate in _stageRequestConcurrencyGates.Values)
        {
            gate.Dispose();
        }

        foreach (var gate in _stageDownloadConcurrencyGates.Values)
        {
            gate.Dispose();
        }

        foreach (var controller in _stageRequestIntervalControllers.Values)
        {
            controller.Dispose();
        }

        _stageRequestConcurrencyGates.Clear();
        _stageDownloadConcurrencyGates.Clear();
        _stageRequestIntervalControllers.Clear();
        _rootRuntime = null;
        _dirtyRuntimePathSet.Clear();
        while (_dirtyRuntimePaths.TryDequeue(out _))
        {
            // do nothing
        }

        Interlocked.Exchange(ref _activeNetworkCount, 0);
        Interlocked.Exchange(ref _storedItemCount, 0);
        Interlocked.Exchange(ref _initializingNodeCount, 0);
        Interlocked.Exchange(ref _lastRunWaitingLogTimestampMs, 0);
        Interlocked.Exchange(ref _persistBatchSummarySequence, 0);
        _childSlotWaitLastLogTimestampByParentPath.Clear();
        while (_stateSignalChannel.Reader.TryRead(out _))
        {
            // do nothing
        }
    }

    /// <summary>
    ///     注册节点并执行初始化阶段。
    /// </summary>
    /// <param name="node">节点实例。</param>
    /// <param name="nodeState">节点状态。</param>
    /// <param name="parentRuntime">父节点运行时；根节点为 <c>null</c>。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="cookieAccessor">Cookie 访问器。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "引擎需隔离节点异常，保证主流程可持续。")]
    private async Task<LumaNodeRuntime<TState>?> RegisterNodeAsync(
        LumaNode<TState> node,
        TState nodeState,
        LumaNodeRuntime<TState>? parentRuntime,
        LumaRunRuntime runRuntime,
        ICookieAccessor cookieAccessor,
        NodeTaskScheduler requestScheduler,
        PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler)
    {
        var parentPath = parentRuntime?.Path;
        var path = string.IsNullOrWhiteSpace(parentPath) ? node.Key : $"{parentPath}/{node.Key}";
        var depth = parentRuntime is null ? 0 : parentRuntime.Depth + 1;
        var parentToken = parentRuntime?.Context.CancellationToken ?? runRuntime.Token;
        var bootstrapContext = new LumaContext<TState>(
            runRuntime.RunId,
            runRuntime.RunName,
            runRuntime.CommandName,
            path,
            depth,
            node.ExecutionOptions.DefaultRouteKind,
            nodeState,
            _htmlParser,
            cookieAccessor,
            _loggerFactory.CreateLogger(node.GetType()),
            parentToken);
        var executionProfile = node.ResolveExecutionProfile(bootstrapContext);
        var runtime = new LumaNodeRuntime<TState>(
            node,
            path,
            depth,
            runRuntime.RunId,
            runRuntime.RunName,
            runRuntime.CommandName,
            nodeState,
            _htmlParser,
            cookieAccessor,
            _loggerFactory,
            executionProfile,
            parentToken);

        if (!_nodeRuntimes.TryAdd(path, runtime))
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            WriteUnifiedLog(LogLevelKind.Warning, "Engine", $"Event=DuplicateNodePathSkipped NodePath={path}");
            LumaEngineLogMessages.DuplicateNodePathLog(_logger, path, null);
            return null;
        }

        Interlocked.Increment(ref _initializingNodeCount);
        runtime.IncrementInitializing();
        SignalStateChanged(runtime);

        try
        {
            runtime.State.SetStatus(NodeExecutionStatus.Running);
            await BuildNodeRequestsAsync(runtime, requestScheduler, runtime.Context.CancellationToken).ConfigureAwait(false);
            await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistScheduler, null).ConfigureAwait(false);
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
                WriteUnifiedLog(LogLevelKind.Error, "Engine", $"Event=NodeStartFailed NodePath={runtime.Path}", exception);
                LumaEngineLogMessages.NodeStartFailedLog(_logger, runtime.Path, exception);
            }
        }
        finally
        {
            runtime.DecrementInitializing();
            Interlocked.Decrement(ref _initializingNodeCount);
            SignalStateChanged(runtime);
        }

        return runtime;
    }

    /// <summary>
    ///     构建节点初始请求并入队。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task BuildNodeRequestsAsync(LumaNodeRuntime<TState> runtime, NodeTaskScheduler requestScheduler, CancellationToken cancellationToken)
    {
        var emittedRequestCount = 0;
        await foreach (var request in runtime.Node.BuildRequestsAsync(runtime.Context).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            emittedRequestCount++;
            if (emittedRequestCount > 1)
            {
                throw new InvalidOperationException($"Node '{runtime.Path}' emitted more than one primary request. A node must emit at most one request.");
            }

            var stageOptions = ResolveStageExecutionOptions(runtime);
            var normalizedRequest = NormalizeRequest(request, runtime.Path, stageOptions.StageKey);
            await EnqueueRequestWithStageControlAsync(runtime, normalizedRequest, requestScheduler, cancellationToken).ConfigureAwait(false);
            runtime.State.IncrementQueued();
            SignalStateChanged(runtime);
        }
    }

    /// <summary>
    ///     普通请求工作循环。
    /// </summary>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "工作循环需兜底节点异常，避免单任务中断全局管线。")]
    private async Task RequestWorkerAsync(NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler, PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                var request = await requestScheduler.DequeueAsync(runRuntime.Token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var runtime))
                {
                    SignalStateChanged();
                    continue;
                }

                runtime.State.DecrementQueued();
                SignalStateChanged(runtime);
                if (runtime.CancellationTokenSource.IsCancellationRequested)
                {
                    continue;
                }

                var activeMarked = false;
                try
                {
                    await using var stageConcurrencyLease = await AcquireStageConcurrencyLeaseAsync(runtime, isDownload: false, runtime.Context.CancellationToken).ConfigureAwait(false);
                    await using var concurrencyLease = await AcquireNodeTypeConcurrencyLeaseAsync(runtime, runtime.Context.CancellationToken).ConfigureAwait(false);

                    runtime.State.IncrementActive();
                    Interlocked.Increment(ref _activeNetworkCount);
                    SignalStateChanged(runtime);
                    activeMarked = true;

                    var requestFlowController = await ResolveNodeTypeRequestFlowControllerAsync(runtime).ConfigureAwait(false);
                    if (requestFlowController is not null)
                    {
                        await requestFlowController.WaitTurnAsync(runtime.Context.CancellationToken).ConfigureAwait(false);
                    }

                    using var response = await SendWithRiskControlAsync(request, runtime, runtime.Context.CancellationToken).ConfigureAwait(false);
                    requestFlowController?.ObserveResponse(response.StatusCode);
                    await runtime.Node.HandleResponseAsync(response, runtime.Context).ConfigureAwait(false);
                    await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistScheduler, request).ConfigureAwait(false);

                    bool shouldDownload;
                    try
                    {
                        shouldDownload = await runtime.Node.ShouldDownloadAsync(response, runtime.Context).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.ShouldDownload, request, response, null).ConfigureAwait(false))
                        {
                            throw;
                        }

                        shouldDownload = false;
                    }

                    if (shouldDownload)
                    {
                        try
                        {
                            await foreach (var downloadRequest in runtime.Node.BuildDownloadRequestsAsync(response, runtime.Context).WithCancellation(runtime.Context.CancellationToken).ConfigureAwait(false))
                            {
                                var stageOptions = ResolveStageExecutionOptions(runtime);
                                var normalizedDownloadRequest = NormalizeRequest(downloadRequest, runtime.Path, stageOptions.StageKey);
                                await EnqueueDownloadWithStageControlAsync(runtime, normalizedDownloadRequest, downloadScheduler, runRuntime.Token).ConfigureAwait(false);
                                runtime.State.IncrementQueued();
                                SignalStateChanged(runtime);
                            }
                        }
                        catch (Exception exception)
                        {
                            if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.BuildDownloadRequests, request, response, null).ConfigureAwait(false))
                            {
                                throw;
                            }
                        }

                        await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistScheduler, request).ConfigureAwait(false);
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
                        WriteUnifiedLog(LogLevelKind.Error, "Engine", $"Event=NodeResponseHandlingFailed NodePath={runtime.Path} ErrorType={errorType} ErrorMessage={exception.Message}", exception);
                        LumaEngineLogMessages.NodeResponseHandlingFailedLog(_logger, runtime.Path, errorType, exception.Message, exception);
                    }
                }
                finally
                {
                    if (activeMarked)
                    {
                        runtime.State.DecrementActive();
                        Interlocked.Decrement(ref _activeNetworkCount);
                        SignalStateChanged(runtime);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
            // ignore
        }
    }

    /// <summary>
    ///     下载请求工作循环。
    /// </summary>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    /// <param name="requestScheduler">请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "下载循环需兜底节点异常，避免单任务中断全局管线。")]
    private async Task DownloadWorkerAsync(NodeTaskScheduler downloadScheduler, NodeTaskScheduler requestScheduler, PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, LumaRunRuntime runRuntime)
    {
        try
        {
            while (!runRuntime.Token.IsCancellationRequested)
            {
                var request = await downloadScheduler.DequeueAsync(runRuntime.Token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                if (!_nodeRuntimes.TryGetValue(request.NodePath, out var runtime))
                {
                    SignalStateChanged();
                    continue;
                }

                runtime.State.DecrementQueued();
                SignalStateChanged(runtime);
                if (runtime.CancellationTokenSource.IsCancellationRequested)
                {
                    continue;
                }

                var activeMarked = false;
                try
                {
                    await using var stageConcurrencyLease = await AcquireStageConcurrencyLeaseAsync(runtime, isDownload: true, runtime.Context.CancellationToken).ConfigureAwait(false);
                    await using var concurrencyLease = await AcquireNodeTypeConcurrencyLeaseAsync(runtime, runtime.Context.CancellationToken).ConfigureAwait(false);

                    runtime.State.IncrementActive();
                    Interlocked.Increment(ref _activeNetworkCount);
                    SignalStateChanged(runtime);
                    activeMarked = true;

                    var requestFlowController = await ResolveNodeTypeRequestFlowControllerAsync(runtime).ConfigureAwait(false);
                    if (requestFlowController is not null)
                    {
                        await requestFlowController.WaitTurnAsync(runtime.Context.CancellationToken).ConfigureAwait(false);
                    }

                    using var response = await SendWithRiskControlAsync(request, runtime, runtime.Context.CancellationToken).ConfigureAwait(false);
                    requestFlowController?.ObserveResponse(response.StatusCode);
                    await runtime.Node.HandleDownloadResponseAsync(response, request, runtime.Context).ConfigureAwait(false);
                    await DispatchNodeBatchAsync(runtime, runRuntime, requestScheduler, persistScheduler, request).ConfigureAwait(false);
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
                        WriteUnifiedLog(LogLevelKind.Error, "Engine", $"Event=NodeDownloadHandlingFailed NodePath={runtime.Path} ErrorType={errorType} ErrorMessage={exception.Message}", exception);
                        LumaEngineLogMessages.NodeResponseHandlingFailedLog(_logger, runtime.Path, errorType, exception.Message, exception);
                    }
                }
                finally
                {
                    if (activeMarked)
                    {
                        runtime.State.DecrementActive();
                        Interlocked.Decrement(ref _activeNetworkCount);
                        SignalStateChanged(runtime);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested)
        {
            // ignore
        }
    }

    /// <summary>
    ///     分发节点待处理批次。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="sourceRequest">源请求。</param>
    /// <returns>异步任务。</returns>
    private async Task DispatchNodeBatchAsync(LumaNodeRuntime<TState> runtime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler, PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler,
        LumaRequest? sourceRequest)
    {
        var dispatchBatch = runtime.Node.DrainDispatchBatch();
        if (dispatchBatch.HasWork)
        {
            WriteUnifiedLog(LogLevelKind.Debug, "Scheduler",
                $"Event=DispatchBatchPrepared NodePath={runtime.Path} ChildMaxConcurrency={runtime.ExecutionProfile.ExecutionOptions.ChildMaxConcurrency} RequestCount={dispatchBatch.Requests.Count} ItemCount={dispatchBatch.Items.Count} ChildCount={dispatchBatch.Children.Count}");
        }

        if (dispatchBatch.StopNode)
        {
            runtime.Cancel(dispatchBatch.StopReason);
            SignalStateChanged(runtime);
        }

        var shouldBlockExpansion = dispatchBatch.StopNode || runtime.CancellationTokenSource.IsCancellationRequested;

        if (!shouldBlockExpansion)
        {
            foreach (var request in dispatchBatch.Requests)
            {
                var stageOptions = ResolveStageExecutionOptions(runtime);
                var normalizedRequest = NormalizeRequest(request, runtime.Path, stageOptions.StageKey);
                await EnqueueRequestWithStageControlAsync(runtime, normalizedRequest, requestScheduler, runRuntime.Token).ConfigureAwait(false);
                runtime.State.IncrementQueued();
                SignalStateChanged(runtime);
            }
        }

        foreach (var item in dispatchBatch.Items)
        {
            await persistScheduler.EnqueueAsync(new ItemEnvelope<TState>(item, runtime.Context, sourceRequest), runRuntime.Token).ConfigureAwait(false);
        }

        if (!shouldBlockExpansion && dispatchBatch.Children.Count > 0)
        {
            StartChildRegistrations(dispatchBatch.Children, runtime, runRuntime, requestScheduler, persistScheduler);
        }
    }

    /// <summary>
    ///     启动子节点注册任务。
    /// </summary>
    /// <param name="children">子节点集合。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    private void StartChildRegistrations(IReadOnlyList<NodeChildBinding<TState>> children, LumaNodeRuntime<TState> parentRuntime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler,
        PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler)
    {
        if (children.Count == 0)
        {
            return;
        }

        var childIndex = 0;
        foreach (var childBinding in children)
        {
            childIndex++;
            parentRuntime.IncrementPendingChildRegistrationTask();
            _ = RegisterChildWithSlotAsync(childBinding, parentRuntime, runRuntime, requestScheduler, persistScheduler, childIndex);
        }

        SignalStateChanged(parentRuntime);
    }

    /// <summary>
    ///     通过并发槽位保护注册单个子节点。
    /// </summary>
    /// <param name="childBinding">子节点映射定义。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="childIndex">子节点序号。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildWithSlotAsync(NodeChildBinding<TState> childBinding, LumaNodeRuntime<TState> parentRuntime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler,
        PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, int childIndex)
    {
        var acquiredSlot = false;
        var registrationStarted = false;
        try
        {
            var waitStopwatch = Stopwatch.StartNew();
            await parentRuntime.WaitChildSlotAsync(parentRuntime.Context.CancellationToken).ConfigureAwait(false);
            waitStopwatch.Stop();
            acquiredSlot = true;
            if (waitStopwatch.ElapsedMilliseconds >= ChildSlotWaitDiagnosticThresholdMilliseconds)
            {
                TryWriteChildSlotWaitSlowLog(parentRuntime, requestScheduler, childIndex, waitStopwatch.ElapsedMilliseconds);
            }

            parentRuntime.IncrementRegisteringChild();
            registrationStarted = true;
            await RegisterChildInternalAsync(childBinding, parentRuntime, runRuntime, requestScheduler, persistScheduler).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (runRuntime.Token.IsCancellationRequested || parentRuntime.CancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            WriteUnifiedLog(LogLevelKind.Error, "Scheduler", $"Event=RegisterChildFailed ParentNodePath={parentRuntime.Path} ChildIndex={childIndex} ErrorMessage={exception.Message}", exception);
        }
        finally
        {
            parentRuntime.DecrementPendingChildRegistrationTask();
            if (acquiredSlot && !registrationStarted)
            {
                parentRuntime.ReleaseChildSlot();
            }

            SignalStateChanged(parentRuntime);
        }
    }

    /// <summary>
    ///     注册单个子节点。
    ///     <para>
    ///         子节点并发槽在“子树完成”时释放；若注册失败则立即释放。
    ///     </para>
    /// </summary>
    /// <param name="childBinding">子节点映射定义。</param>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <returns>异步任务。</returns>
    private async Task RegisterChildInternalAsync(NodeChildBinding<TState> childBinding, LumaNodeRuntime<TState> parentRuntime, LumaRunRuntime runRuntime, NodeTaskScheduler requestScheduler,
        PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler)
    {
        try
        {
            var childState = childBinding.StateMapper(parentRuntime.Context.State);
            var cookieAccessor = new NetCookieAccessor(_netClient, ResolveRouteKind);
            var childRuntime = await RegisterNodeAsync(childBinding.Node, childState, parentRuntime, runRuntime, cookieAccessor, requestScheduler, persistScheduler).ConfigureAwait(false);
            if (childRuntime is null)
            {
                parentRuntime.ReleaseChildSlot();
                return;
            }

            parentRuntime.IncrementPendingChildSubtree();
            _ = childRuntime.SubtreeCompletionTask.ContinueWith(
                _ =>
                {
                    parentRuntime.DecrementPendingChildSubtree();
                    parentRuntime.ReleaseChildSlot();
                    SignalStateChanged(parentRuntime);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch
        {
            parentRuntime.ReleaseChildSlot();
            throw;
        }
        finally
        {
            parentRuntime.DecrementRegisteringChild();
            SignalStateChanged(parentRuntime);
        }
    }

    /// <summary>
    ///     等待阶段最小请求间隔。
    /// </summary>
    /// <param name="stageOptions">阶段选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask WaitStageMinIntervalAsync(NodeStageExecutionOptions stageOptions, CancellationToken cancellationToken)
    {
        if (stageOptions.MinRequestIntervalMilliseconds <= 0)
        {
            return;
        }

        var stageKey = NormalizeStageKey(stageOptions.StageKey);
        var controller = _stageRequestIntervalControllers.GetOrAdd(stageKey, _ => new StageMinIntervalController(stageOptions.MinRequestIntervalMilliseconds));
        controller.Update(stageOptions.MinRequestIntervalMilliseconds);
        await controller.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     等待全局最小请求间隔。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask WaitGlobalMinIntervalAsync(CancellationToken cancellationToken)
    {
        if (_globalRequestIntervalLimiter is null)
        {
            return;
        }

        using var lease = await _globalRequestIntervalLimiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new OperationCanceledException("Global min-interval limiter lease was not acquired.", cancellationToken);
        }
    }

    /// <summary>
    ///     发送请求并应用阶段风控重试策略。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>HTTP 响应。</returns>
    private async ValueTask<HttpResponseMessage> SendWithRiskControlAsync(LumaRequest request, LumaNodeRuntime<TState> runtime, CancellationToken cancellationToken)
    {
        var stageOptions = ResolveStageExecutionOptions(runtime);
        var maxRetries = Math.Max(0, stageOptions.RiskControlMaxRetries);
        var initialDelay = Math.Max(1, stageOptions.RiskControlInitialDelayMilliseconds);
        var maxDelay = Math.Max(initialDelay, stageOptions.RiskControlMaxDelayMilliseconds);
        var retryStatusCodes = stageOptions.RiskControlStatusCodes.Count == 0 ? null : stageOptions.RiskControlStatusCodes;

        var attempt = 0;
        while (true)
        {
            await WaitGlobalMinIntervalAsync(cancellationToken).ConfigureAwait(false);
            await WaitStageMinIntervalAsync(stageOptions, cancellationToken).ConfigureAwait(false);
            var response = await SendAsync(request, runtime.Context, cancellationToken).ConfigureAwait(false);
            if (!ShouldRiskRetry(response.StatusCode, retryStatusCodes) || attempt >= maxRetries)
            {
                return response;
            }

            attempt++;
            var delayMilliseconds = ComputeRiskRetryDelayMilliseconds(initialDelay, maxDelay, attempt);
            WriteUnifiedLog(LogLevelKind.Warning, "RiskControl",
                $"Event=RiskControlRetry Stage={NormalizeStageKey(stageOptions.StageKey)} NodePath={runtime.Path} Attempt={attempt} DelayMs={delayMilliseconds} StatusCode={(int)response.StatusCode}");
            response.Dispose();
            await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
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
        var requestMessage = await CloneHttpRequestMessageAsync(request.HttpRequestMessage, cancellationToken).ConfigureAwait(false);
        using var timeoutCancellationTokenSource = CreateTimeoutCancellationTokenSource(request, cancellationToken);
        var effectiveCancellationToken = timeoutCancellationTokenSource?.Token ?? cancellationToken;

        try
        {
            return await lease.HttpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebException)
        {
            var failedResponse = new HttpResponseMessage((HttpStatusCode)599) { RequestMessage = requestMessage, ReasonPhrase = exception.Message, Content = new ByteArrayContent([]) };
            failedResponse.Headers.TryAddWithoutValidation("X-Luma-Transport-Error", exception.GetType().Name);
            return failedResponse;
        }
    }

    /// <summary>
    ///     克隆 HTTP 请求消息，确保请求可安全重发。
    /// </summary>
    /// <param name="requestMessage">源请求消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>克隆后的请求消息。</returns>
    private static async ValueTask<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestMessage);

        var clone = new HttpRequestMessage(requestMessage.Method, requestMessage.RequestUri)
        {
            Version = requestMessage.Version,
            VersionPolicy = requestMessage.VersionPolicy
        };

        foreach (var header in requestMessage.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (requestMessage.Content is not null)
        {
            if (!requestMessage.Options.TryGetValue(CachedRequestContentBytesOptionKey, out var bytes))
            {
                bytes = await requestMessage.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                requestMessage.Options.Set(CachedRequestContentBytesOptionKey, bytes);
            }

            var clonedContent = new ByteArrayContent(bytes);
            foreach (var contentHeader in requestMessage.Content.Headers)
            {
                clonedContent.Headers.TryAddWithoutValidation(contentHeader.Key, contentHeader.Value);
            }

            clone.Content = clonedContent;
        }

        return clone;
    }

    /// <summary>
    ///     归一化请求并绑定节点路径。
    /// </summary>
    /// <param name="request">源请求。</param>
    /// <param name="nodePath">节点路径。</param>
    /// <param name="stageKey">阶段键。</param>
    /// <returns>归一化请求。</returns>
    private static LumaRequest NormalizeRequest(LumaRequest request, string nodePath, string stageKey)
    {
        return new LumaRequest(request.HttpRequestMessage, nodePath)
        {
            StageKey = stageKey,
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
        if (request.Timeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        return cancellationTokenSource;
    }

    /// <summary>
    ///     解析节点类型共享请求流控器。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <returns>节点类型共享流控器；未启用时返回 <c>null</c>。</returns>
    private ValueTask<NodeTypeRequestFlowController?> ResolveNodeTypeRequestFlowControllerAsync(LumaNodeRuntime<TState> runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var options = runtime.ExecutionProfile.FlowControlOptions;
        if (options.MinIntervalMilliseconds <= 0)
        {
            return ValueTask.FromResult<NodeTypeRequestFlowController?>(null);
        }

        var nodeType = runtime.Node.GetType();
        var controller = _nodeTypeRequestFlowControllers.GetOrAdd(
            nodeType,
            _ => new NodeTypeRequestFlowController(
                nodeType,
                options.ScopeName,
                options.MinIntervalMilliseconds,
                options.AdaptiveBackoffEnabled,
                options.AdaptiveBackoffStatusCodes,
                options.AdaptiveBackoffMaxHits,
                options.AdaptiveMaxIntervalMilliseconds,
                options.AdaptiveInitialIntervalMilliseconds,
                options.FlowControlStrategyKey,
                _timeProvider,
                _flowControlStrategyResolver));
        controller.Update(options.ScopeName, options.MinIntervalMilliseconds, options.AdaptiveBackoffEnabled, options.AdaptiveBackoffStatusCodes, options.AdaptiveBackoffMaxHits, options.AdaptiveMaxIntervalMilliseconds,
            options.AdaptiveInitialIntervalMilliseconds, options.FlowControlStrategyKey);
        return ValueTask.FromResult<NodeTypeRequestFlowController?>(controller);
    }

    /// <summary>
    ///     获取节点类型共享并发闸门租约。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>并发闸门租约；未限制时返回空租约。</returns>
    private async ValueTask<IAsyncDisposable> AcquireNodeTypeConcurrencyLeaseAsync(LumaNodeRuntime<TState> runtime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var maxConcurrency = runtime.ExecutionProfile.ExecutionOptions.ResolveNodeTypeMaxConcurrency();
        if (maxConcurrency <= 0)
        {
            return NoopAsyncDisposable.Instance;
        }

        var nodeType = runtime.Node.GetType();
        var controller = _nodeTypeConcurrencyControllers.GetOrAdd(nodeType, _ => new NodeTypeConcurrencyController(maxConcurrency));
        controller.Update(maxConcurrency);
        return await controller.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     基于阶段配置执行带背压的普通请求入队。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="request">请求对象。</param>
    /// <param name="scheduler">请求调度器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask EnqueueRequestWithStageControlAsync(LumaNodeRuntime<TState> runtime, LumaRequest request, NodeTaskScheduler scheduler, CancellationToken cancellationToken)
    {
        var stageOptions = ResolveStageExecutionOptions(runtime);
        await scheduler.EnqueueAsync(request, stageOptions.StageKey, stageOptions.RequestCapacity, stageOptions.RequestBackpressureMode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     基于阶段配置执行带背压的下载请求入队。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="request">请求对象。</param>
    /// <param name="scheduler">下载调度器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async ValueTask EnqueueDownloadWithStageControlAsync(LumaNodeRuntime<TState> runtime, LumaRequest request, NodeTaskScheduler scheduler, CancellationToken cancellationToken)
    {
        var stageOptions = ResolveStageExecutionOptions(runtime);
        await scheduler.EnqueueAsync(request, stageOptions.StageKey, stageOptions.DownloadCapacity, stageOptions.DownloadBackpressureMode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     获取阶段并发闸门租约。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <param name="isDownload">是否下载请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租约。</returns>
    private async ValueTask<IAsyncDisposable> AcquireStageConcurrencyLeaseAsync(LumaNodeRuntime<TState> runtime, bool isDownload, CancellationToken cancellationToken)
    {
        var stageOptions = ResolveStageExecutionOptions(runtime);
        var stageKey = NormalizeStageKey(stageOptions.StageKey);
        var maxConcurrency = isDownload ? stageOptions.DownloadConcurrency : stageOptions.RequestConcurrency;
        if (maxConcurrency <= 0)
        {
            return NoopAsyncDisposable.Instance;
        }

        var gates = isDownload ? _stageDownloadConcurrencyGates : _stageRequestConcurrencyGates;
        var controller = gates.GetOrAdd(stageKey, _ => new StageConcurrencyController(maxConcurrency));
        controller.Update(maxConcurrency);
        return await controller.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     规范化阶段键。
    /// </summary>
    /// <param name="stageKey">输入阶段键。</param>
    /// <returns>规范化阶段键。</returns>
    private static string NormalizeStageKey(string stageKey)
    {
        return string.IsNullOrWhiteSpace(stageKey) ? "default" : stageKey.Trim();
    }

    /// <summary>
    ///     解析阶段执行选项（同一阶段键采用统一配置）。
    /// </summary>
    /// <param name="runtime">节点运行时。</param>
    /// <returns>统一阶段执行选项。</returns>
    private NodeStageExecutionOptions ResolveStageExecutionOptions(LumaNodeRuntime<TState> runtime)
    {
        var incoming = runtime.ExecutionProfile.StageOptions;
        var normalized = NormalizeStageExecutionOptions(incoming);
        var stageKey = NormalizeStageKey(normalized.StageKey);
        return _stageExecutionOptionsCatalog.AddOrUpdate(
            stageKey,
            _ => normalized,
            (_, existing) => MergeStageExecutionOptions(existing, normalized));
    }

    /// <summary>
    ///     标准化阶段执行选项。
    /// </summary>
    /// <param name="options">原始选项。</param>
    /// <returns>标准化选项。</returns>
    private static NodeStageExecutionOptions NormalizeStageExecutionOptions(NodeStageExecutionOptions options)
    {
        return new NodeStageExecutionOptions
        {
            StageKey = NormalizeStageKey(options.StageKey),
            RequestCapacity = Math.Max(1, options.RequestCapacity),
            DownloadCapacity = Math.Max(1, options.DownloadCapacity),
            RequestConcurrency = Math.Max(0, options.RequestConcurrency),
            DownloadConcurrency = Math.Max(0, options.DownloadConcurrency),
            RequestBackpressureMode = options.RequestBackpressureMode,
            DownloadBackpressureMode = options.DownloadBackpressureMode,
            MinRequestIntervalMilliseconds = Math.Max(0, options.MinRequestIntervalMilliseconds),
            RiskControlInitialDelayMilliseconds = Math.Max(1, options.RiskControlInitialDelayMilliseconds),
            RiskControlMaxDelayMilliseconds = Math.Max(Math.Max(1, options.RiskControlInitialDelayMilliseconds), options.RiskControlMaxDelayMilliseconds),
            RiskControlMaxRetries = Math.Max(0, options.RiskControlMaxRetries),
            RiskControlStatusCodes = options.RiskControlStatusCodes.Count > 0 ? options.RiskControlStatusCodes : [429, 503]
        };
    }

    /// <summary>
    ///     合并同阶段键执行选项。
    /// </summary>
    /// <param name="existing">已存在选项。</param>
    /// <param name="incoming">新选项。</param>
    /// <returns>合并后的选项。</returns>
    private static NodeStageExecutionOptions MergeStageExecutionOptions(NodeStageExecutionOptions existing, NodeStageExecutionOptions incoming)
    {
        EnsureStageStaticConfigurationConsistency(existing, incoming);

        return new NodeStageExecutionOptions
        {
            StageKey = existing.StageKey,
            RequestCapacity = existing.RequestCapacity,
            DownloadCapacity = existing.DownloadCapacity,
            RequestConcurrency = ResolveStageConcurrency(existing.RequestConcurrency, incoming.RequestConcurrency),
            DownloadConcurrency = ResolveStageConcurrency(existing.DownloadConcurrency, incoming.DownloadConcurrency),
            RequestBackpressureMode = existing.RequestBackpressureMode,
            DownloadBackpressureMode = existing.DownloadBackpressureMode,
            MinRequestIntervalMilliseconds = Math.Max(existing.MinRequestIntervalMilliseconds, incoming.MinRequestIntervalMilliseconds),
            RiskControlInitialDelayMilliseconds = Math.Max(existing.RiskControlInitialDelayMilliseconds, incoming.RiskControlInitialDelayMilliseconds),
            RiskControlMaxDelayMilliseconds = Math.Max(existing.RiskControlMaxDelayMilliseconds, incoming.RiskControlMaxDelayMilliseconds),
            RiskControlMaxRetries = Math.Max(existing.RiskControlMaxRetries, incoming.RiskControlMaxRetries),
            RiskControlStatusCodes = existing.RiskControlStatusCodes.Concat(incoming.RiskControlStatusCodes).Distinct().OrderBy(static code => code).ToArray()
        };
    }

    /// <summary>
    ///     校验阶段静态配置一致性（容量与背压模式）。
    /// </summary>
    /// <param name="existing">已存在选项。</param>
    /// <param name="incoming">新选项。</param>
    private static void EnsureStageStaticConfigurationConsistency(NodeStageExecutionOptions existing, NodeStageExecutionOptions incoming)
    {
        if (existing.RequestCapacity != incoming.RequestCapacity)
        {
            throw new InvalidOperationException(
                $"Stage option conflict detected. Stage={existing.StageKey}, Option=RequestCapacity, Existing={existing.RequestCapacity}, Incoming={incoming.RequestCapacity}");
        }

        if (existing.DownloadCapacity != incoming.DownloadCapacity)
        {
            throw new InvalidOperationException(
                $"Stage option conflict detected. Stage={existing.StageKey}, Option=DownloadCapacity, Existing={existing.DownloadCapacity}, Incoming={incoming.DownloadCapacity}");
        }

        if (existing.RequestBackpressureMode != incoming.RequestBackpressureMode)
        {
            throw new InvalidOperationException(
                $"Stage option conflict detected. Stage={existing.StageKey}, Option=RequestBackpressureMode, Existing={existing.RequestBackpressureMode}, Incoming={incoming.RequestBackpressureMode}");
        }

        if (existing.DownloadBackpressureMode != incoming.DownloadBackpressureMode)
        {
            throw new InvalidOperationException(
                $"Stage option conflict detected. Stage={existing.StageKey}, Option=DownloadBackpressureMode, Existing={existing.DownloadBackpressureMode}, Incoming={incoming.DownloadBackpressureMode}");
        }
    }

    /// <summary>
    ///     解析阶段并发度。
    /// </summary>
    /// <param name="left">并发度左值。</param>
    /// <param name="right">并发度右值。</param>
    /// <returns>合并并发度。</returns>
    private static int ResolveStageConcurrency(int left, int right)
    {
        if (left <= 0 || right <= 0)
        {
            return 0;
        }

        return Math.Max(left, right);
    }

    /// <summary>
    ///     判断状态码是否应触发风控重试。
    /// </summary>
    /// <param name="statusCode">状态码。</param>
    /// <param name="retryStatusCodes">重试状态码集合。</param>
    /// <returns>应重试返回 true。</returns>
    private static bool ShouldRiskRetry(HttpStatusCode statusCode, IReadOnlyList<int>? retryStatusCodes)
    {
        if (retryStatusCodes is null)
        {
            return false;
        }

        var code = (int)statusCode;
        foreach (var retryCode in retryStatusCodes)
        {
            if (retryCode == code)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     计算风控重试延迟（指数递增）。
    /// </summary>
    /// <param name="initialDelayMilliseconds">起始延迟。</param>
    /// <param name="maxDelayMilliseconds">最大延迟。</param>
    /// <param name="attempt">重试次数（从 1 开始）。</param>
    /// <returns>延迟毫秒数。</returns>
    private static int ComputeRiskRetryDelayMilliseconds(int initialDelayMilliseconds, int maxDelayMilliseconds, int attempt)
    {
        if (attempt <= 1)
        {
            return initialDelayMilliseconds;
        }

        var multiplied = (long)initialDelayMilliseconds * (1L << Math.Min(30, attempt - 1));
        return (int)Math.Min(maxDelayMilliseconds, Math.Min(int.MaxValue, multiplied));
    }

    /// <summary>
    ///     创建阶段最小请求间隔限流器。
    /// </summary>
    /// <param name="minIntervalMilliseconds">最小请求间隔毫秒数。</param>
    /// <returns>速率限制器。</returns>
    private static RateLimiter CreateMinIntervalLimiter(int minIntervalMilliseconds)
    {
        var normalized = Math.Max(1, minIntervalMilliseconds);
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(normalized),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    /// <summary>
    ///     持久化工作循环。
    /// </summary>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="runRuntime">运行时宿主。</param>
    /// <returns>异步任务。</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "持久化循环需要吞吐保护，避免单批次失败中断全局。")]
    private async Task PersistWorkerAsync(PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, LumaRunRuntime runRuntime)
    {
        var buffer = new List<ItemEnvelope<TState>>(_options.PersistBatchSize);
        try
        {
            while (await TryReadPersistEnvelopeAsync(persistScheduler, buffer, runRuntime.Token).ConfigureAwait(false))
            {
                FillPersistBatchAsync(persistScheduler, buffer);
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
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="buffer">缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取成功返回 true。</returns>
    private static async ValueTask<bool> TryReadPersistEnvelopeAsync(PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, List<ItemEnvelope<TState>> buffer, CancellationToken cancellationToken)
    {
        var dequeueResult = await persistScheduler.DequeueAsync(cancellationToken).ConfigureAwait(false);
        if (!dequeueResult.HasItem)
        {
            return false;
        }

        buffer.Add(dequeueResult.Item);
        return true;
    }

    /// <summary>
    ///     聚合批量持久化数据。
    /// </summary>
    /// <param name="persistScheduler">持久化调度器。</param>
    /// <param name="buffer">缓冲区。</param>
    private void FillPersistBatchAsync(PriorityTaskScheduler<ItemEnvelope<TState>> persistScheduler, List<ItemEnvelope<TState>> buffer)
    {
        if (buffer.Count <= 0 || buffer.Count >= _options.PersistBatchSize)
        {
            return;
        }

        while (buffer.Count < _options.PersistBatchSize)
        {
            if (!persistScheduler.TryDequeue(out var envelope))
            {
                return;
            }

            buffer.Add(envelope);
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
        if (buffer.Count <= 0)
        {
            return;
        }

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
                if (stopException.Scope != LumaStopScope.Node)
                {
                    throw;
                }

                resolvedResults[index] = PersistResult.Skipped(stopException.Message);
                continue;
            }
            catch (Exception exception)
            {
                if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.ShouldPersist, envelope.SourceRequest, null, envelope.Item).ConfigureAwait(false))
                {
                    throw;
                }

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
        {
            batchPersistResults = [];
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
                WriteUnifiedLog(LogLevelKind.Error, "Persist", $"Event=PersistBatchFailed ErrorMessage={exception.Message}", exception);
                LumaEngineLogMessages.PersistBatchFailedLog(_logger, exception);
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

        WriteUnifiedLog(LogLevelKind.Debug, "Persist", $"Event=PersistBatchResolved BufferedCount={buffer.Count} PersistedCount={filteredEnvelopes.Count} ResultCount={batchPersistResults.Count}");
        WritePersistBatchSummaryLog(buffer.Count, filteredEnvelopes.Count, resolvedResults);

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

            var callbackContext = new PersistContext<TState>(runtime.Context, envelope.SourceRequest, index);
            try
            {
                await runtime.Node.OnPersistedAsync(envelope.Item, persistResult, callbackContext).ConfigureAwait(false);
            }
            catch (LumaStopException stopException)
            {
                await HandleStopExceptionAsync(stopException, runtime, runRuntime, "Node persisted callback phase stopped by business rule.").ConfigureAwait(false);
                if (stopException.Scope != LumaStopScope.Node)
                {
                    throw;
                }
            }
            catch (Exception exception)
            {
                if (!await HandleNodeExceptionAsync(runtime, runRuntime, exception, NodeExceptionPhase.OnPersisted, envelope.SourceRequest, null, envelope.Item).ConfigureAwait(false))
                {
                    throw;
                }
            }

            var shouldStopByPersistSuggestion = persistResult is { Decision: PersistDecision.AlreadyExists, SuggestStopNode: true };
            var shouldStopByThreshold = persistResult.Decision == PersistDecision.AlreadyExists && runtime.Node.ConsecutiveExistingStopThreshold > 0 && runtime.State.ConsecutiveExistingCount >= runtime.Node.ConsecutiveExistingStopThreshold;
            if (!shouldStopByPersistSuggestion && !shouldStopByThreshold)
            {
                continue;
            }

            runtime.Cancel(persistResult.Message);
            SignalStateChanged(runtime);
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
        for (var index = 0; index < count; index++)
        {
            results[index] = PersistResult.Failed(message);
        }

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
                {
                    if (runtime.State.Status is NodeExecutionStatus.Running or NodeExecutionStatus.Stopping)
                    {
                        runtime.State.SetStatus(runtime.CancellationTokenSource.IsCancellationRequested ? NodeExecutionStatus.Cancelled : NodeExecutionStatus.Completed, runtime.State.Reason);
                    }
                }

                return;
            }

            TryWriteRunWaitingLog(requestScheduler, downloadScheduler);

            await _stateSignalChannel.Reader.ReadAsync(runRuntime.Token).ConfigureAwait(false);
            while (_stateSignalChannel.Reader.TryRead(out _))
            {
                // do nothing
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
        if (_rootRuntime is null)
        {
            return false;
        }

        if (!_rootRuntime.SubtreeCompletionTask.IsCompleted)
        {
            return false;
        }

        if (requestScheduler.Count > 0 || downloadScheduler.Count > 0)
        {
            return false;
        }

        if (Interlocked.Read(ref _activeNetworkCount) > 0)
        {
            return false;
        }

        return _nodeRuntimes.Values.All(static runtime =>
            runtime is { State: { ActiveRequestCount: <= 0, QueuedRequestCount: <= 0 }, RegisteringChildCount: <= 0, InitializingCount: <= 0, PendingChildSubtreeCount: <= 0 } &&
            runtime.State.Status is not NodeExecutionStatus.Pending);
    }

    /// <summary>
    ///     发送状态变更信号。
    /// </summary>
    /// <param name="runtime">发生变更的节点运行时；为空表示全局事件。</param>
    private void SignalStateChanged(LumaNodeRuntime<TState>? runtime = null)
    {
        if (runtime is not null)
        {
            if (_dirtyRuntimePathSet.TryAdd(runtime.Path, 0))
            {
                _dirtyRuntimePaths.Enqueue(runtime.Path);
            }
        }

        TryCompleteNodeSubtrees();
        _stateSignalChannel.Writer.TryWrite(true);
    }

    /// <summary>
    ///     尝试完成已排空的节点子树。
    ///     <para>
    ///         子节点并发槽释放通过子树完成回调处理，此处仅负责驱动节点子树完成状态收敛。
    ///     </para>
    /// </summary>
    private void TryCompleteNodeSubtrees()
    {
        while (_dirtyRuntimePaths.TryDequeue(out var path))
        {
            _dirtyRuntimePathSet.TryRemove(path, out _);
            if (!_nodeRuntimes.TryGetValue(path, out var runtime))
            {
                continue;
            }

            if (!runtime.TryCompleteSubtree() || _rootRuntime is null || string.Equals(path, _rootRuntime.Path, StringComparison.Ordinal))
            {
                continue;
            }

            if (_dirtyRuntimePathSet.TryAdd(_rootRuntime.Path, 0))
            {
                _dirtyRuntimePaths.Enqueue(_rootRuntime.Path);
            }
        }
    }

    /// <summary>
    ///     统一写入引擎运行日志。
    ///     <para>
    ///         同时写入窗口日志管理器与 <see cref="ILogger" />，确保控制台与文本日志内容一致可定位。
    ///     </para>
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="tag">日志标签。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">可选异常对象。</param>
    private void WriteUnifiedLog(LogLevelKind level, string tag, string message, Exception? exception = null)
    {
        _logManager.Write(level, tag, message, exception);
        var mapped = level switch
        {
            LogLevelKind.Trace => LogLevel.Trace,
            LogLevelKind.Debug => LogLevel.Debug,
            LogLevelKind.Information => LogLevel.Information,
            LogLevelKind.Warning => LogLevel.Warning,
            LogLevelKind.Error => LogLevel.Error,
            LogLevelKind.Critical => LogLevel.Critical,
            _ => LogLevel.Information
        };
        _logger.Log(mapped, exception, "{Tag} {Message}", tag, message);
    }

    /// <summary>
    ///     记录运行等待态日志（节流）。
    /// </summary>
    /// <param name="requestScheduler">普通请求调度器。</param>
    /// <param name="downloadScheduler">下载请求调度器。</param>
    private void TryWriteRunWaitingLog(NodeTaskScheduler requestScheduler, NodeTaskScheduler downloadScheduler)
    {
        var nowMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var lastMs = Interlocked.Read(ref _lastRunWaitingLogTimestampMs);
        if (nowMs - lastMs < 5000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastRunWaitingLogTimestampMs, nowMs, lastMs) != lastMs)
        {
            return;
        }

        WriteUnifiedLog(LogLevelKind.Debug, "Engine",
            $"Event=RunWaitingState InitializingNodeCount={Interlocked.Read(ref _initializingNodeCount)} QueuedRequestCount={requestScheduler.Count} QueuedDownloadCount={downloadScheduler.Count} ActiveNetworkCount={Interlocked.Read(ref _activeNetworkCount)}");
    }

    /// <summary>
    ///     输出持久化批次摘要日志。
    /// </summary>
    /// <param name="bufferedCount">批次缓存总数。</param>
    /// <param name="persistedCount">批次实际持久化输入数。</param>
    /// <param name="results">批次结果集合。</param>
    private void WritePersistBatchSummaryLog(int bufferedCount, int persistedCount, IReadOnlyList<PersistResult> results)
    {
        var storedCount = 0;
        var existsCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        foreach (var result in results)
        {
            switch (result.Decision)
            {
                case PersistDecision.Stored:
                {
                    storedCount++;
                    break;
                }
                case PersistDecision.AlreadyExists:
                {
                    existsCount++;
                    break;
                }
                case PersistDecision.Failed:
                {
                    failedCount++;
                    break;
                }
                case PersistDecision.Skipped:
                {
                    skippedCount++;
                    break;
                }
            }
        }

        if (storedCount <= 0 && existsCount <= 0 && failedCount <= 0 && skippedCount <= 0)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _persistBatchSummarySequence);
        var hasFailures = failedCount > 0;
        var hasSkips = skippedCount > 0;
        var shouldWriteInfoSample = sequence <= 5 || sequence % PersistBatchSummarySampleEvery == 0;

        if (!hasFailures && !hasSkips && !shouldWriteInfoSample)
        {
            return;
        }

        var level = hasFailures ? LogLevelKind.Warning : LogLevelKind.Information;
        WriteUnifiedLog(
            level,
            "Persist",
            $"Event=PersistBatchSummary Seq={sequence} BufferedCount={bufferedCount} PersistedInputCount={persistedCount} StoredCount={storedCount} ExistsCount={existsCount} FailedCount={failedCount} SkippedCount={skippedCount}");
    }

    /// <summary>
    ///     尝试输出子节点槽位慢等待日志（按父节点节流）。
    /// </summary>
    /// <param name="parentRuntime">父节点运行时。</param>
    /// <param name="requestScheduler">请求调度器。</param>
    /// <param name="childIndex">子节点序号。</param>
    /// <param name="waitMilliseconds">等待毫秒数。</param>
    private void TryWriteChildSlotWaitSlowLog(LumaNodeRuntime<TState> parentRuntime, NodeTaskScheduler requestScheduler, int childIndex, long waitMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(parentRuntime);
        ArgumentNullException.ThrowIfNull(requestScheduler);

        var isCritical = waitMilliseconds >= ChildSlotWaitCriticalMilliseconds;
        var nowMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (!isCritical)
        {
            var previousMilliseconds = _childSlotWaitLastLogTimestampByParentPath.GetOrAdd(parentRuntime.Path, static _ => 0);
            if (nowMilliseconds - previousMilliseconds < ChildSlotWaitLogThrottleMilliseconds)
            {
                return;
            }

            if (!_childSlotWaitLastLogTimestampByParentPath.TryUpdate(parentRuntime.Path, nowMilliseconds, previousMilliseconds))
            {
                return;
            }
        }
        else
        {
            _childSlotWaitLastLogTimestampByParentPath[parentRuntime.Path] = nowMilliseconds;
        }

        WriteUnifiedLog(
            isCritical ? LogLevelKind.Warning : LogLevelKind.Information,
            "Scheduler",
            $"Event=ChildSlotWaitSlow ParentNodePath={parentRuntime.Path} ChildIndex={childIndex} WaitMs={waitMilliseconds} ParentRegisteringChildCount={parentRuntime.RegisteringChildCount} ParentPendingChildSubtreeCount={parentRuntime.PendingChildSubtreeCount} RequestQueueCount={requestScheduler.Count} ActiveNetworkCount={Interlocked.Read(ref _activeNetworkCount)}");
    }

    /// <summary>
    ///     解析最终路由类型。
    /// </summary>
    /// <param name="requestRouteKind">请求路由类型。</param>
    /// <param name="nodeRouteKind">节点默认路由类型。</param>
    /// <returns>最终路由类型。</returns>
    private LumaRouteKind ResolveRouteKind(LumaRouteKind requestRouteKind, LumaRouteKind nodeRouteKind)
    {
        if (requestRouteKind != LumaRouteKind.Auto)
        {
            return requestRouteKind;
        }

        if (nodeRouteKind != LumaRouteKind.Auto)
        {
            return nodeRouteKind;
        }

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
    private async ValueTask<bool> HandleNodeExceptionAsync(LumaNodeRuntime<TState> runtime, LumaRunRuntime runRuntime, Exception exception, NodeExceptionPhase phase, LumaRequest? sourceRequest, HttpResponseMessage? response, IItem? item)
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
            WriteUnifiedLog(LogLevelKind.Error, "Engine", $"Event=NodeExceptionCallbackFailed NodePath={runtime.Path} Phase={phase} ErrorMessage={callbackException.Message}", callbackException);
            return false;
        }

        var reason = $"[{phase}] {exception.Message}";
        WriteUnifiedLog(LogLevelKind.Warning, "Engine", $"Event=NodeExceptionHandled NodePath={runtime.Path} Phase={phase} Action={action} ErrorType={exception.GetType().Name} ErrorMessage={exception.Message}");

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
                SignalStateChanged(runtime);
                return true;
            }
            case NodeExceptionAction.StopRun:
            {
                runtime.State.SetStatus(NodeExecutionStatus.Failed, reason);
                runRuntime.SetStatus("Stopped");
                if (!runRuntime.CancellationTokenSource.IsCancellationRequested)
                {
                    await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);
                }

                SignalStateChanged(runtime);

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
        return configuredWorkerCount;
    }

    /// <summary>
    ///     解析有效持久化工作线程数量。
    /// </summary>
    /// <param name="rootNode">根节点。</param>
    /// <returns>有效线程数量。</returns>
    private int ResolveEffectivePersistWorkerCount(LumaNode<TState> rootNode)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        var configuredWorkerCount = Math.Max(1, _options.PersistWorkerCount);
        return configuredWorkerCount;
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
                SignalStateChanged(runtime);
                WriteUnifiedLog(LogLevelKind.Warning, "Engine", $"Event=StopExceptionNodeHandled Phase={phase} NodePath={runtime.Path} Scope={exception.Scope} Reason={reason}");
                LumaEngineLogMessages.NodeScopedStopLog(_logger, runtime.Path, exception.Code, reason, null);
                return;
            }
            case LumaStopScope.Run:
            case LumaStopScope.App:
            {
                runtime.State.SetStatus(NodeExecutionStatus.Cancelled, reason);
                runRuntime.SetStatus("Stopped");
                if (!runRuntime.CancellationTokenSource.IsCancellationRequested)
                {
                    await runRuntime.CancellationTokenSource.CancelAsync().ConfigureAwait(false);
                }

                SignalStateChanged(runtime);

                WriteUnifiedLog(LogLevelKind.Error, "Engine", $"Event=StopExceptionRunStopped Phase={phase} NodePath={runtime.Path} Scope={exception.Scope} Reason={reason}");
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
            Elapsed = _timeProvider.GetUtcNow() - runRuntime.StartedAtUtc,
            Nodes = nodes
        });
    }

    /// <summary>
    ///     <b>节点类型共享请求流控器</b>
    ///     <para>
    ///         负责节点类型级别的统一排队窗口，并将响应结果委托给可插拔流控策略。
    ///     </para>
    /// </summary>
    private sealed class NodeTypeRequestFlowController
    {
        /// <summary>
        ///     策略解析器。
        /// </summary>
        private readonly Func<string?, INodeRequestFlowControlStrategy> _strategyResolver;

        /// <summary>
        ///     时间提供器。
        /// </summary>
        private readonly TimeProvider _timeProvider;

        /// <summary>
        ///     当前策略实例。
        /// </summary>
        private INodeRequestFlowControlStrategy _strategy;

        /// <summary>
        ///     当前策略键。
        /// </summary>
        private string _strategyKey;

        /// <summary>
        ///     当前请求速率限制器。
        /// </summary>
        private RateLimiter _rateLimiter;

        /// <summary>
        ///     当前最小间隔（毫秒）。
        /// </summary>
        private int _configuredMinIntervalMilliseconds;

        /// <summary>
        ///     额外间隔排队使用的“下次可用时间戳”（UTC 毫秒）。
        /// </summary>
        private long _nextAdditionalWindowUtcMilliseconds;

        /// <summary>
        ///     已退役待释放限流器集合。
        /// </summary>
        private readonly ConcurrentQueue<RateLimiter> _retiredRateLimiters = new();

        /// <summary>
        ///     初始化节点类型请求流控器。
        /// </summary>
        /// <param name="nodeType">节点类型。</param>
        /// <param name="scopeName">流控语义名称。</param>
        /// <param name="configuredMinIntervalMilliseconds">基础最小请求间隔毫秒数。</param>
        /// <param name="adaptiveBackoffEnabled">是否启用自适应退避。</param>
        /// <param name="adaptiveBackoffStatusCodes">触发自适应退避的状态码集合。</param>
        /// <param name="adaptiveBackoffMaxHits">自适应退避命中次数上限。</param>
        /// <param name="adaptiveMaxIntervalMilliseconds">自适应退避上限毫秒数。</param>
        /// <param name="adaptiveInitialIntervalMilliseconds">自适应退避起始间隔毫秒数。</param>
        /// <param name="flowControlStrategyKey">流控策略键。</param>
        /// <param name="timeProvider">时间提供器。</param>
        /// <param name="strategyResolver">策略解析器。</param>
        public NodeTypeRequestFlowController(
            Type nodeType,
            string scopeName,
            int configuredMinIntervalMilliseconds,
            bool adaptiveBackoffEnabled,
            IReadOnlyList<int>? adaptiveBackoffStatusCodes,
            int adaptiveBackoffMaxHits,
            int adaptiveMaxIntervalMilliseconds,
            int adaptiveInitialIntervalMilliseconds,
            string? flowControlStrategyKey,
            TimeProvider timeProvider,
            Func<string?, INodeRequestFlowControlStrategy> strategyResolver)
        {
            _ = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _strategyResolver = strategyResolver ?? throw new ArgumentNullException(nameof(strategyResolver));
            var resolvedScopeName = string.IsNullOrWhiteSpace(scopeName) ? nodeType.Name : scopeName.Trim();
            _strategyKey = ResolveStrategyKey(flowControlStrategyKey);
            _strategy = _strategyResolver(_strategyKey);
            _configuredMinIntervalMilliseconds = Math.Max(0, configuredMinIntervalMilliseconds);
            _strategy.Update(new NodeRequestFlowControlStrategyOptions(
                resolvedScopeName,
                configuredMinIntervalMilliseconds,
                adaptiveBackoffEnabled,
                adaptiveBackoffStatusCodes,
                adaptiveBackoffMaxHits,
                adaptiveMaxIntervalMilliseconds,
                adaptiveInitialIntervalMilliseconds));
            _rateLimiter = CreateRateLimiter(_configuredMinIntervalMilliseconds);
        }

        /// <summary>
        ///     更新流控配置。
        /// </summary>
        /// <param name="scopeName">流控语义名称。</param>
        /// <param name="configuredMinIntervalMilliseconds">基础最小请求间隔毫秒数。</param>
        /// <param name="adaptiveBackoffEnabled">是否启用自适应退避。</param>
        /// <param name="adaptiveBackoffStatusCodes">触发自适应退避的状态码集合。</param>
        /// <param name="adaptiveBackoffMaxHits">自适应退避命中次数上限。</param>
        /// <param name="adaptiveMaxIntervalMilliseconds">自适应退避上限毫秒数。</param>
        /// <param name="adaptiveInitialIntervalMilliseconds">自适应退避起始间隔毫秒数。</param>
        /// <param name="flowControlStrategyKey">流控策略键。</param>
        public void Update(
            string scopeName,
            int configuredMinIntervalMilliseconds,
            bool adaptiveBackoffEnabled,
            IReadOnlyList<int>? adaptiveBackoffStatusCodes,
            int adaptiveBackoffMaxHits,
            int adaptiveMaxIntervalMilliseconds,
            int adaptiveInitialIntervalMilliseconds,
            string? flowControlStrategyKey)
        {
            var resolvedStrategyKey = ResolveStrategyKey(flowControlStrategyKey);
            var nextStrategy = _strategy;
            if (!string.Equals(_strategyKey, resolvedStrategyKey, StringComparison.OrdinalIgnoreCase))
            {
                nextStrategy = _strategyResolver(resolvedStrategyKey);
                _strategyKey = resolvedStrategyKey;
                Volatile.Write(ref _strategy, nextStrategy);
            }

            nextStrategy.Update(new NodeRequestFlowControlStrategyOptions(
                scopeName,
                configuredMinIntervalMilliseconds,
                adaptiveBackoffEnabled,
                adaptiveBackoffStatusCodes,
                adaptiveBackoffMaxHits,
                adaptiveMaxIntervalMilliseconds,
                adaptiveInitialIntervalMilliseconds));

            var nextMinIntervalMilliseconds = Math.Max(0, configuredMinIntervalMilliseconds);
            if (nextMinIntervalMilliseconds != Volatile.Read(ref _configuredMinIntervalMilliseconds))
            {
                var previous = Interlocked.Exchange(ref _rateLimiter, CreateRateLimiter(nextMinIntervalMilliseconds));
                Interlocked.Exchange(ref _configuredMinIntervalMilliseconds, nextMinIntervalMilliseconds);
                _retiredRateLimiters.Enqueue(previous);
            }
        }

        /// <summary>
        ///     等待请求发送窗口。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public async ValueTask WaitTurnAsync(CancellationToken cancellationToken)
        {
            var effectiveMinIntervalMilliseconds = Volatile.Read(ref _strategy).ResolveEffectiveMinIntervalMilliseconds();
            if (effectiveMinIntervalMilliseconds <= 0)
            {
                return;
            }

            using var lease = await Volatile.Read(ref _rateLimiter).AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                throw new OperationCanceledException("Rate limiter lease was not acquired.", cancellationToken);
            }

            var configuredMinIntervalMilliseconds = Volatile.Read(ref _configuredMinIntervalMilliseconds);
            if (effectiveMinIntervalMilliseconds > configuredMinIntervalMilliseconds)
            {
                await WaitAdditionalWindowAsync(effectiveMinIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     将响应状态码反馈给当前流控策略。
        /// </summary>
        /// <param name="statusCode">响应状态码。</param>
        public void ObserveResponse(HttpStatusCode statusCode)
        {
            Volatile.Read(ref _strategy).ObserveResponse(statusCode, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        }

        /// <summary>
        ///     释放节点类型请求流控器资源。
        /// </summary>
        public void Dispose()
        {
            while (_retiredRateLimiters.TryDequeue(out var retiredRateLimiter))
            {
                retiredRateLimiter.Dispose();
            }

            Volatile.Read(ref _rateLimiter).Dispose();
        }

        /// <summary>
        ///     解析可用策略键。
        /// </summary>
        /// <param name="strategyKey">输入策略键。</param>
        /// <returns>可用策略键。</returns>
        private static string ResolveStrategyKey(string? strategyKey)
        {
            return string.IsNullOrWhiteSpace(strategyKey) ? NodeRequestFlowControlStrategyRegistry.DefaultStrategyKey : strategyKey.Trim();
        }

        /// <summary>
        ///     等待额外最小间隔窗口（用于自适应退避扩展窗口）。
        /// </summary>
        /// <param name="effectiveMinIntervalMilliseconds">当前有效最小请求间隔毫秒数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async ValueTask WaitAdditionalWindowAsync(int effectiveMinIntervalMilliseconds, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nowUtcMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                var previousNextWindow = Volatile.Read(ref _nextAdditionalWindowUtcMilliseconds);
                var scheduledStart = Math.Max(nowUtcMilliseconds, previousNextWindow);
                var nextWindow = scheduledStart + effectiveMinIntervalMilliseconds;

                if (Interlocked.CompareExchange(ref _nextAdditionalWindowUtcMilliseconds, nextWindow, previousNextWindow) != previousNextWindow)
                {
                    continue;
                }

                var waitMilliseconds = scheduledStart - nowUtcMilliseconds;
                if (waitMilliseconds > 0)
                {
                    await Task.Delay((int)Math.Min(int.MaxValue, waitMilliseconds), cancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }

        /// <summary>
        ///     创建最小间隔速率限制器。
        /// </summary>
        /// <param name="minIntervalMilliseconds">最小请求间隔毫秒数。</param>
        /// <returns>速率限制器。</returns>
        private static RateLimiter CreateRateLimiter(int minIntervalMilliseconds)
        {
            var normalized = Math.Max(1, minIntervalMilliseconds);
            return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(normalized),
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
        }
    }

    /// <summary>
    ///     阶段最小请求间隔控制器。
    /// </summary>
    private sealed class StageMinIntervalController : IDisposable
    {
        /// <summary>
        ///     当前限流器。
        /// </summary>
        private RateLimiter _rateLimiter;

        /// <summary>
        ///     当前最小请求间隔（毫秒）。
        /// </summary>
        private int _configuredMinIntervalMilliseconds;

        /// <summary>
        ///     已退役待释放限流器集合。
        /// </summary>
        private readonly ConcurrentQueue<RateLimiter> _retiredRateLimiters = new();

        /// <summary>
        ///     初始化阶段最小间隔控制器。
        /// </summary>
        /// <param name="minIntervalMilliseconds">最小请求间隔（毫秒）。</param>
        public StageMinIntervalController(int minIntervalMilliseconds)
        {
            _configuredMinIntervalMilliseconds = Math.Max(1, minIntervalMilliseconds);
            _rateLimiter = CreateMinIntervalLimiter(_configuredMinIntervalMilliseconds);
        }

        /// <summary>
        ///     更新最小请求间隔。
        /// </summary>
        /// <param name="minIntervalMilliseconds">最小请求间隔（毫秒）。</param>
        public void Update(int minIntervalMilliseconds)
        {
            var nextMinIntervalMilliseconds = Math.Max(1, minIntervalMilliseconds);
            if (nextMinIntervalMilliseconds == Volatile.Read(ref _configuredMinIntervalMilliseconds))
            {
                return;
            }

            var previous = Interlocked.Exchange(ref _rateLimiter, CreateMinIntervalLimiter(nextMinIntervalMilliseconds));
            Interlocked.Exchange(ref _configuredMinIntervalMilliseconds, nextMinIntervalMilliseconds);
            _retiredRateLimiters.Enqueue(previous);
        }

        /// <summary>
        ///     等待阶段最小请求间隔。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            using var lease = await Volatile.Read(ref _rateLimiter).AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                throw new OperationCanceledException("Stage min-interval limiter lease was not acquired.", cancellationToken);
            }
        }

        /// <summary>
        ///     释放资源。
        /// </summary>
        public void Dispose()
        {
            while (_retiredRateLimiters.TryDequeue(out var retiredRateLimiter))
            {
                retiredRateLimiter.Dispose();
            }

            Volatile.Read(ref _rateLimiter).Dispose();
        }
    }

    /// <summary>
    ///     节点类型并发闸门控制器。
    /// </summary>
    private sealed class NodeTypeConcurrencyController : IDisposable
    {
        /// <summary>
        ///     并发闸门信号量。
        /// </summary>
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        ///     当前配置的最大并发度。
        /// </summary>
        private int _configuredMaxConcurrency;

        /// <summary>
        ///     初始化并发闸门控制器。
        /// </summary>
        /// <param name="maxConcurrency">最大并发度。</param>
        public NodeTypeConcurrencyController(int maxConcurrency)
        {
            _configuredMaxConcurrency = Math.Max(1, maxConcurrency);
            _semaphore = new SemaphoreSlim(_configuredMaxConcurrency, int.MaxValue);
        }

        /// <summary>
        ///     更新最大并发度（仅支持升高上限）。
        /// </summary>
        /// <param name="maxConcurrency">最大并发度。</param>
        public void Update(int maxConcurrency)
        {
            var next = Math.Max(1, maxConcurrency);
            var current = Volatile.Read(ref _configuredMaxConcurrency);
            if (next <= current)
            {
                return;
            }

            Interlocked.Exchange(ref _configuredMaxConcurrency, next);
            _semaphore.Release(next - current);
        }

        /// <summary>
        ///     获取并发租约。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>租约对象。</returns>
        public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreLease(_semaphore);
        }

        /// <summary>
        ///     释放并发闸门资源。
        /// </summary>
        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }

    /// <summary>
    ///     阶段并发闸门控制器。
    /// </summary>
    private sealed class StageConcurrencyController : IDisposable
    {
        /// <summary>
        ///     并发闸门信号量。
        /// </summary>
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        ///     当前配置并发上限。
        /// </summary>
        private int _configuredMaxConcurrency;

        /// <summary>
        ///     初始化阶段并发闸门控制器。
        /// </summary>
        /// <param name="maxConcurrency">并发上限。</param>
        public StageConcurrencyController(int maxConcurrency)
        {
            _configuredMaxConcurrency = Math.Max(1, maxConcurrency);
            _semaphore = new SemaphoreSlim(_configuredMaxConcurrency, int.MaxValue);
        }

        /// <summary>
        ///     更新并发上限（仅支持提升上限）。
        /// </summary>
        /// <param name="maxConcurrency">并发上限。</param>
        public void Update(int maxConcurrency)
        {
            var next = Math.Max(1, maxConcurrency);
            var current = Volatile.Read(ref _configuredMaxConcurrency);
            if (next <= current)
            {
                return;
            }

            Interlocked.Exchange(ref _configuredMaxConcurrency, next);
            _semaphore.Release(next - current);
        }

        /// <summary>
        ///     获取并发租约。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>租约。</returns>
        public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreLease(_semaphore);
        }

        /// <summary>
        ///     释放资源。
        /// </summary>
        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }

    /// <summary>
    ///     空异步释放器。
    /// </summary>
    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        /// <summary>
        ///     单例实例。
        /// </summary>
        public static NoopAsyncDisposable Instance { get; } = new();

        /// <summary>
        ///     释放空资源。
        /// </summary>
        /// <returns>已完成任务。</returns>
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     信号量租约。
    /// </summary>
    private sealed class SemaphoreLease : IAsyncDisposable
    {
        /// <summary>
        ///     信号量实例。
        /// </summary>
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        ///     是否已释放。
        /// </summary>
        private int _disposed;

        /// <summary>
        ///     初始化信号量租约。
        /// </summary>
        /// <param name="semaphore">信号量实例。</param>
        public SemaphoreLease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
        }

        /// <summary>
        ///     释放租约并归还并发槽位。
        /// </summary>
        /// <returns>已完成任务。</returns>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
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
            return await WithCookieContainerAsync(routeKind, container => (IReadOnlyList<Cookie>)container.GetCookies(uri).Select(CloneCookie).ToArray(), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask ClearCookiesAsync(LumaRouteKind routeKind, string domain, string path, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            var uri = BuildUri(domain, path);
            await WithCookieContainerAsync(routeKind, container =>
            {
                foreach (var cookie in container.GetCookies(uri).Cast<Cookie>())
                {
                    container.Add(uri, new Cookie(cookie.Name, string.Empty, cookie.Path, cookie.Domain) { Expires = DateTime.UtcNow.AddYears(-1) });
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     获取指定路由的 Cookie 容器。
        /// </summary>
        /// <param name="routeKind">路由类型。</param>
        /// <param name="action">Cookie操作。</param>
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
