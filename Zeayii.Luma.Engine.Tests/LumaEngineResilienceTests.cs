using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging.Abstractions;
using Zeayii.Infrastructure.Net.Abstractions.Http;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Engine;

namespace Zeayii.Luma.Engine.Tests;

/// <summary>
///     <b>LumaEngine<TestState> 稳定性与边界行为测试</b>
/// </summary>
public sealed class LumaEngineResilienceTests
{
    /// <summary>
    ///     验证运行级取消能中断持续请求链路并完成收尾。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldRespectCancellationAndExit()
    {
        var node = new EndlessRequestNode("root", "https://example.com/loop");
        var fixture = CreateFixture(downloaderDelay: TimeSpan.FromMilliseconds(10));
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-cancel", cancellationTokenSource.Token).ConfigureAwait(true);

        Assert.True(fixture.NetClient.RequestCount > 0);
    }

    /// <summary>
    ///     验证子节点并发限制会约束 BuildRequests 阶段并发展开上限。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldApplyChildMaxConcurrencyLimit()
    {
        var probe = new ConcurrencyProbe();
        var children = Enumerable.Range(0, 8)
            .Select(index => (LumaNode<TestState>)new DelayBuildNode($"child-{index}", probe, TimeSpan.FromMilliseconds(60)))
            .ToArray();
        var root = new ParentNode("root", new NodeExecutionOptions(LumaRouteKind.Auto, 2), children);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-concurrency", CancellationToken.None).ConfigureAwait(true);

        Assert.True(probe.MaxObserved <= 2, $"Expected max concurrency <= 2, actual: {probe.MaxObserved}");
    }

    /// <summary>
    ///     验证请求通道容量较小时不会丢请求（背压生效）。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldNotLoseRequestsWhenRequestChannelIsFull()
    {
        var node = new MultiRequestNode("root", Enumerable.Range(1, 20).Select(index => $"https://example.com/request/{index}").ToArray());
        var fixture = CreateFixture(
            new LumaEngineOptions
            {
                DefaultRouteKind = LumaRouteKind.Direct,
                RequestWorkerCount = 1,
                DownloadWorkerCount = 1,
                PersistWorkerCount = 1,
                RequestChannelCapacity = 1,
                DownloadChannelCapacity = 1,
                PersistChannelCapacity = 16,
                PersistBatchSize = 1,
                PersistFlushInterval = TimeSpan.FromMilliseconds(20),
                PresentationRefreshInterval = TimeSpan.FromMilliseconds(20)
            },
            downloaderDelay: TimeSpan.FromMilliseconds(8));

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-backpressure", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(20, fixture.NetClient.RequestCount);
    }

    /// <summary>
    ///     验证连续已存在阈值命中后节点会进入停止语义。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldStopNodeWhenAlreadyExistsThresholdReached()
    {
        var node = new ThresholdNode("root", "https://example.com/already", new TestItem("Y"), 1);
        var fixture = CreateFixture(storeBehavior: static _ => [PersistResult.AlreadyExists("exists")]);

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-threshold", CancellationToken.None).ConfigureAwait(true);

        var snapshot = fixture.ProgressManager.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.Nodes, static s => s is { Path: "root", Status: NodeExecutionStatus.Cancelled or NodeExecutionStatus.Stopping });
    }

    /// <summary>
    ///     验证持久化返回数量与输入不一致时引擎会快速失败。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldThrowWhenPersistResultCountMismatch()
    {
        var node = new SingleItemNode("root", "https://example.com/mismatch", new TestItem("M"));
        var fixture = CreateFixture(storeBehavior: static _ => Array.Empty<PersistResult>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-mismatch", CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    /// <summary>
    ///     验证节点级停止异常仅停止当前节点，不中断整次运行。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldOnlyStopCurrentNodeWhenNodeScopedStopExceptionThrown()
    {
        var stopNode = new NodeScopedStopNode("stop-child");
        var keepNode = new SingleItemNode("keep-child", "https://example.com/keep", new TestItem("K"));
        var root = new ParentNode("root", new NodeExecutionOptions(LumaRouteKind.Auto, 1), stopNode, keepNode);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-node-stop-scope", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(fixture.ItemSink.StoredBatches);
    }

    /// <summary>
    ///     验证运行级停止异常会触发全局取消并终止运行。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldStopRunWhenRunScopedStopExceptionThrown()
    {
        var stopNode = new RunScopedStopNode("stop-run");
        var keepNode = new SingleItemNode("keep-child", "https://example.com/keep", new TestItem("K"));
        var root = new ParentNode("root", new NodeExecutionOptions(LumaRouteKind.Auto, 1), stopNode, keepNode);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-global-stop-scope", CancellationToken.None).ConfigureAwait(true);

        var snapshot = fixture.ProgressManager.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal("Stopped", snapshot!.Status);
    }

    /// <summary>
    ///     验证节点异常处理钩子返回 KeepRunning 时不会中断整次运行。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldContinueWhenNodeExceptionActionIsKeepRunning()
    {
        var node = new KeepRunningOnExceptionNode("root", "https://example.com/exception");
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-node-exception-keep-running", CancellationToken.None).ConfigureAwait(true);

        Assert.True(node.OnExceptionInvoked);
        var snapshot = fixture.ProgressManager.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal("Completed", snapshot!.Status);
    }

    /// <summary>
    ///     验证父节点停止会级联取消子节点（父 -> 子单向传播）。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldCascadeCancellationFromParentToChild()
    {
        var child = new CancellationAwareChildNode("child", TimeSpan.FromSeconds(10));
        var root = new ParentStopsSelfNode("root", "https://example.com/parent-stop", child);
        var fixture = CreateFixture(downloaderDelay: TimeSpan.FromMilliseconds(120));

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-parent-cancel-cascade", CancellationToken.None).ConfigureAwait(true);

        Assert.True(child.CancellationObserved);
    }

    /// <summary>
    ///     创建测试夹具。
    /// </summary>
    private static EngineFixture CreateFixture(
        LumaEngineOptions? options = null,
        Func<IReadOnlyList<ItemEnvelope<TestState>>, IReadOnlyList<PersistResult>>? storeBehavior = null,
        TimeSpan? downloaderDelay = null)
    {
        var resolvedOptions = options ?? new LumaEngineOptions
        {
            DefaultRouteKind = LumaRouteKind.Direct,
            RequestWorkerCount = 2,
            DownloadWorkerCount = 2,
            PersistWorkerCount = 1,
            RequestChannelCapacity = 64,
            DownloadChannelCapacity = 64,
            PersistChannelCapacity = 64,
            PersistBatchSize = 1,
            PersistFlushInterval = TimeSpan.FromMilliseconds(20),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(20)
        };

        var resolvedStoreBehavior = storeBehavior ?? (static batch => batch.Select(static _ => PersistResult.Stored()).ToArray());
        return new EngineFixture(
            new FakeItemSink(resolvedStoreBehavior),
            new FakeLogManager(),
            new FakeProgressManager(),
            new FakeHtmlParser(),
            new FakeNetClient(downloaderDelay ?? TimeSpan.Zero),
            resolvedOptions);
    }

    /// <summary>
    ///     测试状态对象。
    /// </summary>
    private sealed class TestState
    {
    }

    /// <summary>
    ///     测试夹具。
    /// </summary>
    private sealed class EngineFixture
    {
        /// <summary>
        ///     HTML 解析器。
        /// </summary>
        private readonly FakeHtmlParser _htmlParser;

        /// <summary>
        ///     引擎配置。
        /// </summary>
        private readonly LumaEngineOptions _options;

        /// <summary>
        ///     初始化测试夹具。
        /// </summary>
        public EngineFixture(FakeItemSink itemSink, FakeLogManager logManager, FakeProgressManager progressManager, FakeHtmlParser htmlParser, FakeNetClient netClient, LumaEngineOptions options)
        {
            ItemSink = itemSink;
            LogManager = logManager;
            ProgressManager = progressManager;
            NetClient = netClient;
            _htmlParser = htmlParser;
            _options = options;
        }

        /// <summary>
        ///     持久化入口。
        /// </summary>
        public FakeItemSink ItemSink { get; }

        /// <summary>
        ///     日志管理器。
        /// </summary>
        private FakeLogManager LogManager { get; }

        /// <summary>
        ///     进度管理器。
        /// </summary>
        public FakeProgressManager ProgressManager { get; }

        /// <summary>
        ///     网络客户端。
        /// </summary>
        public FakeNetClient NetClient { get; }

        /// <summary>
        ///     创建引擎。
        /// </summary>
        public LumaEngine<TestState> CreateEngine()
        {
            return new LumaEngine<TestState>(
                ItemSink,
                LogManager,
                ProgressManager,
                _htmlParser,
                NetClient,
                NullLoggerFactory.Instance,
                NullLogger<LumaEngine<TestState>>.Instance,
                _options);
        }
    }

    /// <summary>
    ///     静态蜘蛛。
    /// </summary>
    private sealed class StaticSpider(LumaNode<TestState> root) : ISpider<TestState>
    {
        /// <inheritdoc />
        public ValueTask<TestState> CreateStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new TestState());
        }

        /// <inheritdoc />
        public ValueTask<LumaNode<TestState>> CreateRootAsync(TestState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(root);
        }
    }

    /// <summary>
    ///     测试数据项。
    /// </summary>
    private sealed record TestItem(string Id) : IItem;

    /// <summary>
    ///     父节点。
    /// </summary>
    private sealed class ParentNode : LumaNode<TestState>
    {
        /// <summary>
        ///     执行选项。
        /// </summary>
        private readonly NodeExecutionOptions _executionOptions;

        /// <summary>
        ///     初始化父节点。
        /// </summary>
        public ParentNode(string key, NodeExecutionOptions executionOptions, params LumaNode<TestState>[] children) : base(key)
        {
            _executionOptions = executionOptions;
            foreach (var child in children)
            {
                AddChild(child);
            }
        }

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => _executionOptions;

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     单数据项节点。
    /// </summary>
    private class SingleItemNode : LumaNode<TestState>
    {
        /// <summary>
        ///     数据项。
        /// </summary>
        private readonly IItem _item;

        /// <summary>
        ///     请求地址。
        /// </summary>
        private readonly string _url;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public SingleItemNode(string key, string url, IItem item) : base(key)
        {
            _url = url;
            _item = item;
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(_url)), context.NodePath);
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AddItem(_item);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     连续已存在阈值节点。
    /// </summary>
    private sealed class ThresholdNode : SingleItemNode
    {
        /// <summary>
        ///     阈值。
        /// </summary>
        private readonly int _threshold;

        /// <summary>
        ///     初始化阈值节点。
        /// </summary>
        public ThresholdNode(string key, string url, IItem item, int threshold) : base(key, url, item)
        {
            _threshold = threshold;
        }

        /// <inheritdoc />
        public override int ConsecutiveExistingStopThreshold => _threshold;
    }

    /// <summary>
    ///     启动延迟节点。
    /// </summary>
    private sealed class DelayBuildNode : LumaNode<TestState>
    {
        /// <summary>
        ///     延迟时长。
        /// </summary>
        private readonly TimeSpan _delay;

        /// <summary>
        ///     并发探针。
        /// </summary>
        private readonly ConcurrencyProbe _probe;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public DelayBuildNode(string key, ConcurrencyProbe probe, TimeSpan delay) : base(key)
        {
            _probe = probe;
            _delay = delay;
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(_delay, context.CancellationToken).ConfigureAwait(false);
                yield break;
            }
            finally
            {
                _probe.Exit();
            }
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     父节点自停止测试节点。
    /// </summary>
    private sealed class ParentStopsSelfNode : LumaNode<TestState>
    {
        /// <summary>
        ///     请求地址。
        /// </summary>
        private readonly string _url;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public ParentStopsSelfNode(string key, string url, LumaNode<TestState> child) : base(key)
        {
            _url = url;
            AddChild(child);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(_url)), context.NodePath);
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            StopNode("parent-stop");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     取消感知子节点。
    /// </summary>
    private sealed class CancellationAwareChildNode : LumaNode<TestState>
    {
        /// <summary>
        ///     构建阶段延迟时长。
        /// </summary>
        private readonly TimeSpan _delay;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public CancellationAwareChildNode(string key, TimeSpan delay) : base(key)
        {
            _delay = delay;
        }

        /// <summary>
        ///     是否观察到取消。
        /// </summary>
        public bool CancellationObserved { get; private set; }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            try
            {
                await Task.Delay(_delay, context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }

            yield break;
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     多请求节点。
    /// </summary>
    private sealed class MultiRequestNode : LumaNode<TestState>
    {
        /// <summary>
        ///     地址列表。
        /// </summary>
        private readonly IReadOnlyList<string> _urls;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public MultiRequestNode(string key, IReadOnlyList<string> urls) : base(key)
        {
            _urls = urls;
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            foreach (var requestUrl in _urls)
            {
                yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(requestUrl)), context.NodePath);
            }
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     持续请求节点。
    /// </summary>
    private sealed class EndlessRequestNode : LumaNode<TestState>
    {
        /// <summary>
        ///     地址。
        /// </summary>
        private readonly Uri _url;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public EndlessRequestNode(string key, string url) : base(key)
        {
            _url = new Uri(url);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, _url), context.NodePath);
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AddRequest(new LumaRequest(new HttpRequestMessage(HttpMethod.Get, _url), context.NodePath));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     节点级停止测试节点。
    /// </summary>
    private sealed class NodeScopedStopNode(string key) : LumaNode<TestState>(key)
    {
        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new LumaStopException(LumaStopScope.Node, "NodeBusinessStop", "Node scoped stop requested.");
#pragma warning disable CS0162
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
#pragma warning restore CS0162
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     运行级停止测试节点。
    /// </summary>
    private sealed class RunScopedStopNode(string key) : LumaNode<TestState>(key)
    {
        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new LumaStopException(LumaStopScope.Run, "RunBusinessStop", "Run scoped stop requested.");
#pragma warning disable CS0162
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
#pragma warning restore CS0162
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     异常后继续运行节点。
    /// </summary>
    private sealed class KeepRunningOnExceptionNode : LumaNode<TestState>
    {
        /// <summary>
        ///     请求地址。
        /// </summary>
        private readonly Uri _requestUri;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="requestUrl">请求地址。</param>
        public KeepRunningOnExceptionNode(string key, string requestUrl) : base(key)
        {
            _requestUri = new Uri(requestUrl);
        }

        /// <summary>
        ///     异常钩子是否被调用。
        /// </summary>
        public bool OnExceptionInvoked { get; private set; }

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, _requestUri), context.NodePath);
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            throw new InvalidOperationException("Expected test exception.");
        }

        /// <inheritdoc />
        public override ValueTask<NodeExceptionAction> OnExceptionAsync(Exception exception, NodeExceptionContext<TestState> context)
        {
            OnExceptionInvoked = true;
            return ValueTask.FromResult(NodeExceptionAction.KeepRunning);
        }
    }

    /// <summary>
    ///     并发探针。
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        /// <summary>
        ///     当前并发数。
        /// </summary>
        private int _current;

        /// <summary>
        ///     最大并发数。
        /// </summary>
        private int _max;

        /// <summary>
        ///     最大并发数。
        /// </summary>
        public int MaxObserved => Volatile.Read(ref _max);

        /// <summary>
        ///     进入并发区。
        /// </summary>
        public void Enter()
        {
            var current = Interlocked.Increment(ref _current);
            while (true)
            {
                var snapshot = Volatile.Read(ref _max);
                if (current <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _max, current, snapshot) == snapshot)
                {
                    return;
                }
            }
        }

        /// <summary>
        ///     离开并发区。
        /// </summary>
        public void Exit()
        {
            Interlocked.Decrement(ref _current);
        }
    }

    /// <summary>
    ///     伪持久化入口。
    /// </summary>
    private sealed class FakeItemSink : IItemSink<TestState>
    {
        /// <summary>
        ///     持久化行为。
        /// </summary>
        private readonly Func<IReadOnlyList<ItemEnvelope<TestState>>, IReadOnlyList<PersistResult>> _storeBehavior;

        /// <summary>
        ///     初始化持久化入口。
        /// </summary>
        public FakeItemSink(Func<IReadOnlyList<ItemEnvelope<TestState>>, IReadOnlyList<PersistResult>> storeBehavior)
        {
            _storeBehavior = storeBehavior;
        }

        /// <summary>
        ///     已存储批次。
        /// </summary>
        public List<IReadOnlyList<ItemEnvelope<TestState>>> StoredBatches { get; } = [];

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<PersistResult>> StoreBatchAsync(IReadOnlyList<ItemEnvelope<TestState>> items, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredBatches.Add(items.ToArray());
            return ValueTask.FromResult(_storeBehavior(items));
        }
    }

    /// <summary>
    ///     伪日志管理器。
    /// </summary>
    private sealed class FakeLogManager : ILogManager
    {
        /// <summary>
        ///     日志队列。
        /// </summary>
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        /// <summary>
        ///     日志序号。
        /// </summary>
        private long _sequenceId;

        /// <inheritdoc />
        public void MarkPresentationStarted()
        {
        }

        /// <inheritdoc />
        public void DrainPendingEntries(int maxBatch = 4096)
        {
            _ = maxBatch;
        }

        /// <inheritdoc />
        public void Write(LogLevelKind level, string tag, string message, Exception? exception = null)
        {
            var text = exception is null ? message : $"{message} | {exception.Message}";
            var sequenceId = Interlocked.Increment(ref _sequenceId);
            _entries.Enqueue(new LogEntry(sequenceId, DateTimeOffset.UtcNow, level, tag, text));
        }

        /// <inheritdoc />
        public LogSnapshot CreateSnapshot()
        {
            return new LogSnapshot
            {
                Entries = _entries.ToArray()
            };
        }
    }

    /// <summary>
    ///     伪进度管理器。
    /// </summary>
    private sealed class FakeProgressManager : IProgressManager
    {
        /// <summary>
        ///     最新快照。
        /// </summary>
        public ProgressSnapshot? LastSnapshot { get; private set; }

        /// <inheritdoc />
        public void Publish(ProgressSnapshot snapshot)
        {
            LastSnapshot = snapshot;
        }

        /// <inheritdoc />
        public ProgressSnapshot CreateSnapshot()
        {
            return LastSnapshot ?? new ProgressSnapshot();
        }
    }

    /// <summary>
    ///     伪 HTML 解析器。
    /// </summary>
    private sealed class FakeHtmlParser : IHtmlParser
    {
        /// <inheritdoc />
        public ValueTask<IDocument> ParseAsync(string html, CancellationToken cancellationToken)
        {
            _ = html;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This parser is not used in current tests.");
        }
    }

    /// <summary>
    ///     伪网络客户端。
    /// </summary>
    private sealed class FakeNetClient : INetClient
    {
        /// <summary>
        ///     请求计数。
        /// </summary>
        private int _requestCount;

        /// <summary>
        ///     初始化网络客户端。
        /// </summary>
        public FakeNetClient(TimeSpan delay)
        {
            HttpClient = new HttpClient(new FakeHttpMessageHandler(delay, () => Interlocked.Increment(ref _requestCount)));
        }

        /// <summary>
        ///     直连 Cookie 容器。
        /// </summary>
        private CookieContainer DirectCookies { get; } = new();

        /// <summary>
        ///     代理 Cookie 容器。
        /// </summary>
        private CookieContainer ProxyCookies { get; } = new();

        /// <summary>
        ///     HTTP 客户端。
        /// </summary>
        private HttpClient HttpClient { get; }

        /// <summary>
        ///     请求计数。
        /// </summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <inheritdoc />
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "测试桩租约对象按引擎调用方生命周期释放。")]
        public ValueTask<IHttpSessionLease> RentAsync(NetRouteKind routeKind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cookieContainer = routeKind == NetRouteKind.Proxy ? ProxyCookies : DirectCookies;
            return ValueTask.FromResult<IHttpSessionLease>(new FakeHttpSessionLease(HttpClient, cookieContainer));
        }
    }

    /// <summary>
    ///     伪租约。
    /// </summary>
    private sealed class FakeHttpSessionLease(HttpClient httpClient, CookieContainer cookieContainer) : IHttpSessionLease
    {
        /// <inheritdoc />
        public HttpClient HttpClient { get; } = httpClient;

        /// <inheritdoc />
        public CookieContainer CookieContainer { get; } = cookieContainer;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     伪 HTTP 消息处理器。
    /// </summary>
    private sealed class FakeHttpMessageHandler(TimeSpan delay, Action onRequest) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            onRequest();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}