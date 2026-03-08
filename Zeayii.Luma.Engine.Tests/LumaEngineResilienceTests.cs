using System.Collections.Concurrent;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using Infrastructure.Net.Abstractions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Engine;

namespace Zeayii.Luma.Engine.Tests;

/// <summary>
/// <b>LumaEngine 稳定性与边界行为测试</b>
/// <para>
/// 覆盖取消、异常隔离、并发限制、背压、持久化边界与 Cookie 隔离语义。
/// </para>
/// </summary>
public sealed class LumaEngineResilienceTests
{
    /// <summary>
    /// 验证运行级取消能中断持续请求链路并完成收尾。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldRespectCancellationAndExit()
    {
        var node = new EndlessRequestNode("root", "https://example.com/loop");
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

        var runTask = engine.RunAsync(new StaticSpider(node), "test-command", "run-cancel", cancellationTokenSource.Token);
        var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(3), cancellationTokenSource.Token)).ConfigureAwait(true);

        Assert.Same(runTask, completedTask);
        await runTask.ConfigureAwait(true);
        Assert.True(fixture.Downloader.DownloadCount > 0);
    }

    /// <summary>
    /// 验证批量未满时会在最终收尾阶段刷新持久化批次。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldFlushFinalPersistBatchWhenBatchNotFull()
    {
        var node = new SingleItemNode("root", "https://example.com/final-flush", new TestItem("A"));
        var fixture = CreateFixture(options: new LumaEngineOptions
        {
            DefaultRouteKind = LumaRouteKind.Direct,
            DownloadWorkerCount = 1,
            PersistWorkerCount = 1,
            RequestChannelCapacity = 16,
            PersistChannelCapacity = 16,
            PersistBatchSize = 10,
            PersistFlushInterval = TimeSpan.FromMilliseconds(500),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(20),
            MaxResponseBodyBytes = 1024 * 1024
        });

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-final-flush", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(fixture.ItemSink.StoredBatches);
        Assert.Single(fixture.ItemSink.StoredBatches[0]);
    }

    /// <summary>
    /// 验证子节点并发限制会约束 Start 阶段并发展开上限。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldApplyChildMaxConcurrencyLimit()
    {
        var probe = new ConcurrencyProbe();
        var children = Enumerable.Range(0, 8)
            .Select(index => (LumaNode)new DelayStartNode($"child-{index}", probe, TimeSpan.FromMilliseconds(60)))
            .ToArray();
        var root = new ParentNode("root", ChildTraversalPolicy.Breadth, 2, children);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-concurrency", CancellationToken.None).ConfigureAwait(true);

        Assert.True(probe.MaxObserved <= 2, $"Expected max concurrency <= 2, actual: {probe.MaxObserved}");
    }

    /// <summary>
    /// 验证请求通道容量较小时不会丢请求（背压生效）。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldNotLoseRequestsWhenRequestChannelIsFull()
    {
        var node = new MultiRequestNode("root", Enumerable.Range(1, 20).Select(index => $"https://example.com/request/{index}").ToArray());
        var fixture = CreateFixture(
            options: new LumaEngineOptions
            {
                DefaultRouteKind = LumaRouteKind.Direct,
                DownloadWorkerCount = 1,
                PersistWorkerCount = 1,
                RequestChannelCapacity = 1,
                PersistChannelCapacity = 16,
                PersistBatchSize = 1,
                PersistFlushInterval = TimeSpan.FromMilliseconds(20),
                PresentationRefreshInterval = TimeSpan.FromMilliseconds(20),
                MaxResponseBodyBytes = 1024 * 1024
            },
            downloaderDelay: TimeSpan.FromMilliseconds(8));

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-backpressure", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(20, fixture.Downloader.DownloadCount);
    }

    /// <summary>
    /// 验证连续已存在阈值命中后节点会进入停止语义。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldStopNodeWhenAlreadyExistsThresholdReached()
    {
        var node = new ThresholdNode("root", "https://example.com/already", new TestItem("Y"), 1);
        var fixture = CreateFixture(storeBehavior: static _ => [PersistResult.AlreadyExists("exists", suggestStopNode: false)]);

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-threshold", CancellationToken.None).ConfigureAwait(true);

        var snapshot = fixture.ProgressManager.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.Nodes, static s => s is { Path: "root", Status: NodeExecutionStatus.Cancelled or NodeExecutionStatus.Stopping });
    }

    /// <summary>
    /// 验证持久化返回数量与输入不一致时引擎会快速失败。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldThrowWhenPersistResultCountMismatch()
    {
        var node = new SingleItemNode("root", "https://example.com/mismatch", new TestItem("M"));
        var fixture = CreateFixture(storeBehavior: static _ => Array.Empty<PersistResult>());

        async Task Action() => await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-mismatch", CancellationToken.None).ConfigureAwait(true);

        await Assert.ThrowsAsync<InvalidOperationException>(Action).ConfigureAwait(true);
    }

    /// <summary>
    /// 验证 Direct/Proxy Cookie 在路由维度隔离。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldIsolateCookiesBetweenDirectAndProxy()
    {
        var node = new CookieIsolationNode("root");
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-cookie-isolation", CancellationToken.None).ConfigureAwait(true);

        Assert.True(node.DirectContainsDirectCookie);
        Assert.True(node.ProxyContainsProxyCookie);
        Assert.False(node.ProxyContainsDirectCookie);
        Assert.False(node.DirectContainsProxyCookie);
    }

    /// <summary>
    /// 创建测试夹具。
    /// </summary>
    /// <param name="options">引擎配置。</param>
    /// <param name="storeBehavior">持久化行为。</param>
    /// <param name="downloaderDelay">下载延迟。</param>
    /// <returns>测试夹具。</returns>
    private static EngineFixture CreateFixture(
        LumaEngineOptions? options = null,
        Func<IReadOnlyList<ItemEnvelope>, IReadOnlyList<PersistResult>>? storeBehavior = null,
        TimeSpan? downloaderDelay = null)
    {
        var resolvedOptions = options ?? new LumaEngineOptions
        {
            DefaultRouteKind = LumaRouteKind.Direct,
            DownloadWorkerCount = 2,
            PersistWorkerCount = 1,
            RequestChannelCapacity = 64,
            PersistChannelCapacity = 64,
            PersistBatchSize = 1,
            PersistFlushInterval = TimeSpan.FromMilliseconds(20),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(20),
            MaxResponseBodyBytes = 1024 * 1024
        };

        var resolvedStoreBehavior = storeBehavior ?? (static batch => batch.Select(static _ => PersistResult.Stored()).ToArray());
        return new EngineFixture(
            new FakeDownloader(downloaderDelay ?? TimeSpan.Zero),
            new FakeItemSink(resolvedStoreBehavior),
            new FakeLogManager(),
            new FakeProgressManager(),
            new FakeHtmlParser(),
            new FakeNetClient(),
            resolvedOptions);
    }

    /// <summary>
    /// 测试夹具。
    /// </summary>
    private sealed class EngineFixture
    {
        /// <summary>
        /// HTML 解析器。
        /// </summary>
        private readonly FakeHtmlParser _htmlParser;

        /// <summary>
        /// 引擎配置。
        /// </summary>
        private readonly LumaEngineOptions _options;

        /// <summary>
        /// 初始化测试夹具。
        /// </summary>
        /// <param name="downloader">下载器。</param>
        /// <param name="itemSink">持久化入口。</param>
        /// <param name="logManager">日志管理器。</param>
        /// <param name="progressManager">进度管理器。</param>
        /// <param name="htmlParser">HTML 解析器。</param>
        /// <param name="netClient">网络客户端。</param>
        /// <param name="options">引擎配置。</param>
        public EngineFixture(
            FakeDownloader downloader,
            FakeItemSink itemSink,
            FakeLogManager logManager,
            FakeProgressManager progressManager,
            FakeHtmlParser htmlParser,
            FakeNetClient netClient,
            LumaEngineOptions options)
        {
            Downloader = downloader;
            ItemSink = itemSink;
            LogManager = logManager;
            ProgressManager = progressManager;
            NetClient = netClient;
            _htmlParser = htmlParser;
            _options = options;
        }

        /// <summary>
        /// 下载器。
        /// </summary>
        public FakeDownloader Downloader { get; }

        /// <summary>
        /// 持久化入口。
        /// </summary>
        public FakeItemSink ItemSink { get; }

        /// <summary>
        /// 日志管理器。
        /// </summary>
        private FakeLogManager LogManager { get; }

        /// <summary>
        /// 进度管理器。
        /// </summary>
        public FakeProgressManager ProgressManager { get; }

        /// <summary>
        /// 网络客户端。
        /// </summary>
        private FakeNetClient NetClient { get; }

        /// <summary>
        /// 创建引擎。
        /// </summary>
        /// <returns>引擎实例。</returns>
        public LumaEngine CreateEngine()
        {
            return new LumaEngine(
                Downloader,
                ItemSink,
                LogManager,
                ProgressManager,
                _htmlParser,
                NetClient,
                NullLogger<LumaEngine>.Instance,
                _options);
        }
    }

    /// <summary>
    /// 静态蜘蛛。
    /// </summary>
    /// <param name="root">根节点。</param>
    private sealed class StaticSpider(LumaNode root) : ISpider
    {
        /// <inheritdoc />
        public ValueTask<LumaNode> CreateRootAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(root);
        }
    }

    /// <summary>
    /// 测试数据项。
    /// </summary>
    /// <param name="Id">标识。</param>
    private sealed record TestItem(string Id) : IItem;

    /// <summary>
    /// 父节点。
    /// </summary>
    private sealed class ParentNode : LumaNode
    {
        /// <summary>
        /// 遍历策略。
        /// </summary>
        private readonly ChildTraversalPolicy _policy;

        /// <summary>
        /// 子节点并发上限。
        /// </summary>
        private readonly int _childMaxConcurrency;

        /// <summary>
        /// 初始化父节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="policy">遍历策略。</param>
        /// <param name="childMaxConcurrency">并发上限。</param>
        /// <param name="children">子节点。</param>
        public ParentNode(string key, ChildTraversalPolicy policy, int childMaxConcurrency, params LumaNode[] children) : base(key)
        {
            _policy = policy;
            _childMaxConcurrency = childMaxConcurrency;
            foreach (var child in children)
            {
                AddChild(child);
            }
        }

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => new()
        {
            ChildTraversalPolicy = _policy,
            ChildMaxConcurrency = _childMaxConcurrency
        };

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult.Empty);
        }
    }

    /// <summary>
    /// 单数据项节点。
    /// </summary>
    private class SingleItemNode : LumaNode
    {
        /// <summary>
        /// 请求地址。
        /// </summary>
        private readonly string _url;

        /// <summary>
        /// 数据项。
        /// </summary>
        private readonly IItem _item;

        /// <summary>
        /// 持久化回调结果。
        /// </summary>
        public List<PersistResult> PersistedResults { get; } = [];

        /// <summary>
        /// 初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="url">地址。</param>
        /// <param name="item">数据项。</param>
        public SingleItemNode(string key, string url, IItem item) : base(key)
        {
            _url = url;
            _item = item;
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult
            {
                Requests = [new LumaRequest(new Uri(_url), context.NodePath)]
            });
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult
            {
                Items = [_item]
            });
        }

        /// <inheritdoc />
        public override ValueTask OnPersistedAsync(IItem item, PersistResult persistResult, PersistContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PersistedResults.Add(persistResult);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 连续已存在阈值节点。
    /// </summary>
    private sealed class ThresholdNode : SingleItemNode
    {
        /// <summary>
        /// 阈值。
        /// </summary>
        private readonly int _threshold;

        /// <summary>
        /// 初始化阈值节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="url">地址。</param>
        /// <param name="item">数据项。</param>
        /// <param name="threshold">阈值。</param>
        public ThresholdNode(string key, string url, IItem item, int threshold) : base(key, url, item)
        {
            _threshold = threshold;
        }

        /// <inheritdoc />
        public override int ConsecutiveExistingStopThreshold => _threshold;
    }

    /// <summary>
    /// 启动延迟节点。
    /// </summary>
    private sealed class DelayStartNode : LumaNode
    {
        /// <summary>
        /// 并发探针。
        /// </summary>
        private readonly ConcurrencyProbe _probe;

        /// <summary>
        /// 延迟时长。
        /// </summary>
        private readonly TimeSpan _delay;

        /// <summary>
        /// 初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="probe">并发探针。</param>
        /// <param name="delay">延迟。</param>
        public DelayStartNode(string key, ConcurrencyProbe probe, TimeSpan delay) : base(key)
        {
            _probe = probe;
            _delay = delay;
        }

        /// <inheritdoc />
        public override async ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                return NodeResult.Empty;
            }
            finally
            {
                _probe.Exit();
            }
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult.Empty);
        }
    }

    /// <summary>
    /// 多请求节点。
    /// </summary>
    private sealed class MultiRequestNode : LumaNode
    {
        /// <summary>
        /// 地址列表。
        /// </summary>
        private readonly IReadOnlyList<string> _urls;

        /// <summary>
        /// 初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="urls">地址列表。</param>
        public MultiRequestNode(string key, IReadOnlyList<string> urls) : base(key)
        {
            _urls = urls;
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requests = _urls.Select(url => new LumaRequest(new Uri(url), context.NodePath)).ToArray();
            return ValueTask.FromResult(new NodeResult
            {
                Requests = requests
            });
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult.Empty);
        }
    }

    /// <summary>
    /// 持续请求节点。
    /// </summary>
    private sealed class EndlessRequestNode : LumaNode
    {
        /// <summary>
        /// 地址。
        /// </summary>
        private readonly Uri _url;

        /// <summary>
        /// 初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="url">地址。</param>
        public EndlessRequestNode(string key, string url) : base(key)
        {
            _url = new Uri(url);
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult
            {
                Requests = [new LumaRequest(_url, context.NodePath)]
            });
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult
            {
                Requests = [new LumaRequest(_url, context.NodePath)]
            });
        }
    }

    /// <summary>
    /// Cookie 隔离验证节点。
    /// </summary>
    private sealed class CookieIsolationNode(string key) : LumaNode(key)
    {
        /// <summary>
        /// 直连是否包含直连 Cookie。
        /// </summary>
        public bool DirectContainsDirectCookie { get; private set; }

        /// <summary>
        /// 代理是否包含代理 Cookie。
        /// </summary>
        public bool ProxyContainsProxyCookie { get; private set; }

        /// <summary>
        /// 代理是否包含直连 Cookie。
        /// </summary>
        public bool ProxyContainsDirectCookie { get; private set; }

        /// <summary>
        /// 直连是否包含代理 Cookie。
        /// </summary>
        public bool DirectContainsProxyCookie { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
        {
            var uri = new Uri("https://example.com/");
            await context.SetCookieAsync(uri, new Cookie("direct", "D", "/", "example.com"), LumaRouteKind.Direct, cancellationToken).ConfigureAwait(false);
            await context.SetCookieAsync(uri, new Cookie("proxy", "P", "/", "example.com"), LumaRouteKind.Proxy, cancellationToken).ConfigureAwait(false);

            DirectContainsDirectCookie = await context.ContainsCookieAsync(uri, "direct", LumaRouteKind.Direct, cancellationToken).ConfigureAwait(false);
            ProxyContainsProxyCookie = await context.ContainsCookieAsync(uri, "proxy", LumaRouteKind.Proxy, cancellationToken).ConfigureAwait(false);
            ProxyContainsDirectCookie = await context.ContainsCookieAsync(uri, "direct", LumaRouteKind.Proxy, cancellationToken).ConfigureAwait(false);
            DirectContainsProxyCookie = await context.ContainsCookieAsync(uri, "proxy", LumaRouteKind.Direct, cancellationToken).ConfigureAwait(false);

            return NodeResult.Empty;
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult> HandleResponseAsync(LumaResponse response, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult.Empty);
        }
    }

    /// <summary>
    /// 并发探针。
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        /// <summary>
        /// 当前并发数。
        /// </summary>
        private int _current;

        /// <summary>
        /// 最大并发数。
        /// </summary>
        private int _max;

        /// <summary>
        /// 最大并发数。
        /// </summary>
        public int MaxObserved => Volatile.Read(ref _max);

        /// <summary>
        /// 进入并发区。
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
        /// 离开并发区。
        /// </summary>
        public void Exit()
        {
            Interlocked.Decrement(ref _current);
        }
    }

    /// <summary>
    /// 伪下载器。
    /// </summary>
    private sealed class FakeDownloader : IDownloader
    {
        /// <summary>
        /// 每次下载延迟。
        /// </summary>
        private readonly TimeSpan _delay;

        /// <summary>
        /// 下载计数。
        /// </summary>
        private int _downloadCount;

        /// <summary>
        /// 初始化下载器。
        /// </summary>
        /// <param name="delay">延迟。</param>
        public FakeDownloader(TimeSpan delay)
        {
            _delay = delay;
        }

        /// <summary>
        /// 下载次数。
        /// </summary>
        public int DownloadCount => Volatile.Read(ref _downloadCount);

        /// <inheritdoc />
        public async ValueTask<LumaResponse> DownloadAsync(LumaRequest request, LumaNodeContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Increment(ref _downloadCount);
            return new LumaResponse(request, 200, request.Url, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, string.Empty);
        }
    }

    /// <summary>
    /// 伪持久化入口。
    /// </summary>
    private sealed class FakeItemSink : IItemSink
    {
        /// <summary>
        /// 持久化行为。
        /// </summary>
        private readonly Func<IReadOnlyList<ItemEnvelope>, IReadOnlyList<PersistResult>> _storeBehavior;

        /// <summary>
        /// 已存储批次。
        /// </summary>
        public List<IReadOnlyList<ItemEnvelope>> StoredBatches { get; } = [];

        /// <summary>
        /// 初始化持久化入口。
        /// </summary>
        /// <param name="storeBehavior">行为函数。</param>
        public FakeItemSink(Func<IReadOnlyList<ItemEnvelope>, IReadOnlyList<PersistResult>> storeBehavior)
        {
            _storeBehavior = storeBehavior;
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<PersistResult>> StoreBatchAsync(IReadOnlyList<ItemEnvelope> items, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredBatches.Add(items.ToArray());
            return ValueTask.FromResult(_storeBehavior(items));
        }
    }

    /// <summary>
    /// 伪日志管理器。
    /// </summary>
    private sealed class FakeLogManager : ILogManager
    {
        /// <summary>
        /// 日志队列。
        /// </summary>
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        /// <summary>
        /// 日志序号。
        /// </summary>
        private long _sequenceId;

        /// <summary>
        /// 日志条目。
        /// </summary>
        public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

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
    /// 伪进度管理器。
    /// </summary>
    private sealed class FakeProgressManager : IProgressManager
    {
        /// <summary>
        /// 最新快照。
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
    /// 伪 HTML 解析器。
    /// </summary>
    private sealed class FakeHtmlParser : IHtmlParser
    {
        /// <inheritdoc />
        public ValueTask<AngleSharp.Dom.IDocument> ParseAsync(string html, CancellationToken cancellationToken)
        {
            _ = html;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This parser is not used in current tests.");
        }
    }

    /// <summary>
    /// 伪网络客户端。
    /// </summary>
    private sealed class FakeNetClient : INetClient
    {
        /// <summary>
        /// 直连 Cookie 容器。
        /// </summary>
        private CookieContainer DirectCookies { get; } = new();

        /// <summary>
        /// 代理 Cookie 容器。
        /// </summary>
        private CookieContainer ProxyCookies { get; } = new();

        /// <summary>
        /// HTTP 客户端。
        /// </summary>
        private HttpClient HttpClient { get; } = new();

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
    /// 伪租约。
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
}
