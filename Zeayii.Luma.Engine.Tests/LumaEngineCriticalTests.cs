using System.Collections.Concurrent;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using Zeayii.Infrastructure.Net.Abstractions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Engine;

namespace Zeayii.Luma.Engine.Tests;

/// <summary>
/// <b>LumaEngine<TestState> 关键运行链路测试</b>
/// <para>
/// 覆盖节点生命周期、调度语义、持久化回调与 Cookie 上下文能力。
/// </para>
/// </summary>
public sealed class LumaEngineCriticalTests
{
    /// <summary>
    /// 测试状态对象。
    /// </summary>
    private sealed class TestState
    {
    }

    /// <summary>
    /// 验证单节点请求到持久化的完整主链路。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldProcessRequestAndPersistCallbacks()
    {
        var node = new SingleRequestNode("root", "https://example.com/item/1", new TestItem("item-1"));
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(node), "test-command", "run-1", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(fixture.Downloader.DownloadedUrls);
        Assert.Equal("https://example.com/item/1", fixture.Downloader.DownloadedUrls[0]);
        Assert.Single(fixture.ItemSink.StoredBatches);
        Assert.Single(fixture.ItemSink.StoredBatches[0]);
        Assert.Single(node.OnPersistedResults);
        Assert.Equal(PersistDecision.Stored, node.OnPersistedResults[0].Decision);

        var lastSnapshot = fixture.ProgressManager.LastSnapshot;
        Assert.NotNull(lastSnapshot);
        Assert.Equal("Completed", lastSnapshot!.Status);
        Assert.Equal(1, lastSnapshot.StoredItemCount);
        Assert.Contains(lastSnapshot.Nodes, static snapshot => snapshot.Path == "root" && snapshot.Status == NodeExecutionStatus.Completed);
    }

    /// <summary>
    /// 验证 ShouldPersist=false 时不会调用持久化入口，但仍会触发节点回调。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldSkipStoreWhenNodeFiltersItem()
    {
        var node = new FilterItemNode("root", "https://example.com/item/2", new TestItem("item-2"));
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(node), "test-command", "run-filter", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(fixture.ItemSink.StoredBatches);
        Assert.Single(node.OnPersistedResults);
        Assert.Equal(PersistDecision.Skipped, node.OnPersistedResults[0].Decision);
        Assert.Equal("Filtered by node policy.", node.OnPersistedResults[0].Message);
    }

    /// <summary>
    /// 验证根节点对子节点选择广度策略时，请求按队尾顺序入队。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldEnqueueByBreadthStrategyForChildren()
    {
        var childA = new MultiRequestStartNode("A", "https://example.com/a/1", "https://example.com/a/2");
        var childB = new MultiRequestStartNode("B", "https://example.com/b/1");
        var root = new RootWithChildrenNode("root", ChildTraversalPolicy.Breadth, childA, childB);
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(root), "test-command", "run-breadth", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
        [
            "https://example.com/a/1",
            "https://example.com/a/2",
            "https://example.com/b/1"
        ],
        fixture.Downloader.DownloadedUrls);
    }

    /// <summary>
    /// 验证根节点对子节点选择深度策略时，请求按队首优先规则入队。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldEnqueueByDepthStrategyForChildren()
    {
        var childA = new MultiRequestStartNode("A", "https://example.com/a/1", "https://example.com/a/2");
        var childB = new MultiRequestStartNode("B", "https://example.com/b/1");
        var root = new RootWithChildrenNode("root", ChildTraversalPolicy.Depth, childA, childB);
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(root), "test-command", "run-depth", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
        [
            "https://example.com/a/1",
            "https://example.com/a/2",
            "https://example.com/b/1"
        ],
        fixture.Downloader.DownloadedUrls);
    }

    /// <summary>
    /// 验证重复节点路径只会保留首个节点，后续同路径节点被跳过。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldSkipDuplicateNodePath()
    {
        var first = new CountingStartNode("dup");
        var second = new CountingStartNode("dup");
        var root = new RootWithChildrenNode("root", ChildTraversalPolicy.Breadth, first, second);
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(root), "test-command", "run-duplicate", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, first.StartCount + second.StartCount);
        Assert.Contains(fixture.LogManager.Entries, static entry => entry.Message.Contains("Duplicate node path skipped", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证节点通过上下文读写 Cookie 时，能够映射到对应路由会话容器。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldMapCookieOperationsToCorrectRouteContainer()
    {
        var node = new CookieOperationNode("root");
        var fixture = CreateFixture();
        var engine = fixture.CreateEngine();

        await engine.RunAsync(new StaticSpider(node), "test-command", "run-cookie", CancellationToken.None).ConfigureAwait(true);

        Assert.True(node.ContainsCookie);
        Assert.Equal("proxy-value", node.CookieValue);
        Assert.Contains(NetRouteKind.Proxy, fixture.NetClient.RentedRouteKinds);
    }

    /// <summary>
    /// 创建测试夹具。
    /// </summary>
    /// <returns>夹具对象。</returns>
    private static EngineTestFixture CreateFixture()
    {
        var itemSink = new FakeItemSink(static batch => batch.Select(static _ => PersistResult.Stored()).ToArray());
        return new EngineTestFixture(new FakeDownloader(), itemSink, new FakeLogManager(), new FakeProgressManager(), new FakeHtmlParser(), new FakeNetClient(), CreateDefaultOptions());
    }

    /// <summary>
    /// 构造默认引擎参数。
    /// </summary>
    /// <returns>引擎配置。</returns>
    private static LumaEngineOptions CreateDefaultOptions()
    {
        return new LumaEngineOptions
        {
            DefaultRouteKind = LumaRouteKind.Direct,
            DownloadWorkerCount = 1,
            PersistWorkerCount = 1,
            RequestChannelCapacity = 64,
            PersistChannelCapacity = 64,
            PersistBatchSize = 1,
            PersistFlushInterval = TimeSpan.FromMilliseconds(20),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(20),
            MaxResponseBodyBytes = 1024 * 1024
        };
    }

    /// <summary>
    /// 引擎测试夹具。
    /// </summary>
    private sealed class EngineTestFixture
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
        /// 初始化夹具。
        /// </summary>
        /// <param name="downloader">下载器。</param>
        /// <param name="itemSink">持久化入口。</param>
        /// <param name="logManager">日志管理器。</param>
        /// <param name="progressManager">进度管理器。</param>
        /// <param name="htmlParser">HTML 解析器。</param>
        /// <param name="netClient">网络客户端。</param>
        /// <param name="options">引擎配置。</param>
        public EngineTestFixture(
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
        /// 进度管理器。
        /// </summary>
        public FakeProgressManager ProgressManager { get; }

        /// <summary>
        /// 日志管理器。
        /// </summary>
        public FakeLogManager LogManager { get; }

        /// <summary>
        /// 网络客户端。
        /// </summary>
        public FakeNetClient NetClient { get; }

        /// <summary>
        /// 创建引擎实例。
        /// </summary>
        /// <returns>引擎对象。</returns>
        public LumaEngine<TestState> CreateEngine()
        {
            return new LumaEngine<TestState>(
                Downloader,
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
    /// 静态根节点蜘蛛。
    /// </summary>
    /// <param name="root">根节点。</param>
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
            ArgumentNullException.ThrowIfNull(state);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(root);
        }
    }

    /// <summary>
    /// 测试数据项。
    /// </summary>
    /// <param name="Id">数据标识。</param>
    private sealed record TestItem(string Id) : IItem;

    /// <summary>
    /// 单请求节点。
    /// </summary>
    private class SingleRequestNode(string key, string url, IItem item) : LumaNode<TestState>(key)
    {
        /// <summary>
        /// 持久化回调结果。
        /// </summary>
        public List<PersistResult> OnPersistedResults { get; } = [];

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> StartAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult<TestState>
            {
                Requests = [new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(url)), context.NodePath)]
            });
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeResult<TestState>
            {
                Items = [item]
            });
        }

        /// <inheritdoc />
        public override ValueTask OnPersistedAsync(IItem persistedItem, PersistResult persistResult, PersistContext<TestState> context)
        {
            context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
            OnPersistedResults.Add(persistResult);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 过滤持久化节点。
    /// </summary>
    private sealed class FilterItemNode(string key, string url, IItem item) : SingleRequestNode(key, url, item)
    {
        /// <inheritdoc />
        public override ValueTask<bool> ShouldPersistAsync(IItem item, PersistContext<TestState> context)
        {
            context.NodeContext.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// 根节点（包含可配置子节点遍历策略）。
    /// </summary>
    private sealed class RootWithChildrenNode : LumaNode<TestState>
    {
        /// <summary>
        /// 子节点遍历策略。
        /// </summary>
        private readonly ChildTraversalPolicy _policy;

        /// <summary>
        /// 初始化根节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="policy">遍历策略。</param>
        /// <param name="children">子节点集合。</param>
        public RootWithChildrenNode(string key, ChildTraversalPolicy policy, params LumaNode<TestState>[] children) : base(key)
        {
            _policy = policy;
            foreach (var child in children)
            {
                AddChild(child);
            }
        }

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => new()
        {
            DefaultRouteKind = LumaRouteKind.Auto,
            ChildTraversalPolicy = _policy,
            ChildMaxConcurrency = 1
        };

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult<TestState>.Empty);
        }
    }

    /// <summary>
    /// 启动时产出多请求节点。
    /// </summary>
    private sealed class MultiRequestStartNode : LumaNode<TestState>
    {
        /// <summary>
        /// 请求地址列表。
        /// </summary>
        private readonly IReadOnlyList<string> _urls;

        /// <summary>
        /// 初始化节点。
        /// </summary>
        /// <param name="key">节点键。</param>
        /// <param name="urls">请求地址。</param>
        public MultiRequestStartNode(string key, params string[] urls) : base(key)
        {
            _urls = urls;
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> StartAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var requests = _urls.Select(url => new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(url)), context.NodePath)).ToArray();
            return ValueTask.FromResult(new NodeResult<TestState>
            {
                Requests = requests
            });
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult<TestState>.Empty);
        }
    }

    /// <summary>
    /// 仅统计启动次数的节点。
    /// </summary>
    private sealed class CountingStartNode(string key) : LumaNode<TestState>(key)
    {
        /// <summary>
        /// 启动计数。
        /// </summary>
        public int StartCount { get; private set; }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> StartAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.FromResult(NodeResult<TestState>.Empty);
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult<TestState>.Empty);
        }
    }

    /// <summary>
    /// Cookie 操作节点。
    /// </summary>
    private sealed class CookieOperationNode(string key) : LumaNode<TestState>(key)
    {
        /// <summary>
        /// 默认路由是否存在 Cookie。
        /// </summary>
        public bool ContainsCookie { get; private set; }

        /// <summary>
        /// 默认路由 Cookie 值。
        /// </summary>
        public string CookieValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => new()
        {
            DefaultRouteKind = LumaRouteKind.Proxy,
            ChildTraversalPolicy = ChildTraversalPolicy.Breadth,
            ChildMaxConcurrency = 1
        };

        /// <inheritdoc />
        public override async ValueTask<NodeResult<TestState>> StartAsync(LumaContext<TestState> context)
        {
            var targetUri = new Uri("https://example.com/");
            await context.SetCookieAsync(targetUri, new Cookie("proxy-token", "proxy-value", "/", "example.com")).ConfigureAwait(false);
            ContainsCookie = await context.ContainsCookieAsync(targetUri, "proxy-token").ConfigureAwait(false);
            var cookie = await context.GetCookieAsync(targetUri, "proxy-token").ConfigureAwait(false);
            CookieValue = cookie?.Value ?? string.Empty;

            return NodeResult<TestState>.Empty;
        }

        /// <inheritdoc />
        public override ValueTask<NodeResult<TestState>> HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(NodeResult<TestState>.Empty);
        }
    }

    /// <summary>
    /// 伪下载器。
    /// </summary>
    private sealed class FakeDownloader : IDownloader<TestState>
    {
        /// <summary>
        /// 已下载地址。
        /// </summary>
        public List<string> DownloadedUrls { get; } = [];

        /// <inheritdoc />
        public ValueTask<HttpResponseMessage> DownloadAsync(LumaRequest request, LumaContext<TestState> context, CancellationToken cancellationToken)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            DownloadedUrls.Add(request.HttpRequestMessage.RequestUri?.AbsoluteUri ?? string.Empty);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request.HttpRequestMessage, Content = new ByteArrayContent([]) };
            return ValueTask.FromResult(response);
        }
    }

    /// <summary>
    /// 伪持久化入口。
    /// </summary>
    private sealed class FakeItemSink(Func<IReadOnlyList<ItemEnvelope<TestState>>, IReadOnlyList<PersistResult>> storeBatchFactory) : IItemSink<TestState>
    {
        /// <summary>
        /// 已接收批次。
        /// </summary>
        public List<IReadOnlyList<ItemEnvelope<TestState>>> StoredBatches { get; } = [];

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<PersistResult>> StoreBatchAsync(IReadOnlyList<ItemEnvelope<TestState>> items, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredBatches.Add(items.ToArray());
            return ValueTask.FromResult(storeBatchFactory(items));
        }
    }

    /// <summary>
    /// 伪日志管理器。
    /// </summary>
    private sealed class FakeLogManager : ILogManager
    {
        /// <summary>
        /// 日志条目集合。
        /// </summary>
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        /// <summary>
        /// 日志序号。
        /// </summary>
        private long _sequenceId;

        /// <summary>
        /// 日志快照。
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
        /// 路由租用记录。
        /// </summary>
        public List<NetRouteKind> RentedRouteKinds { get; } = [];

        /// <summary>
        /// 直连容器。
        /// </summary>
        private CookieContainer DirectCookies { get; } = new();

        /// <summary>
        /// 代理容器。
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
            RentedRouteKinds.Add(routeKind);
            var container = routeKind == NetRouteKind.Proxy ? ProxyCookies : DirectCookies;
            return ValueTask.FromResult<IHttpSessionLease>(new FakeHttpSessionLease(HttpClient, container));
        }
    }

    /// <summary>
    /// 伪会话租约。
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




