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
///     <b>LumaEngine{TestState} 关键运行链路测试</b>
/// </summary>
public sealed class LumaEngineCriticalTests
{
    /// <summary>
    ///     验证单节点请求到持久化的完整主链路。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldProcessRequestAndPersistCallbacks()
    {
        var node = new SingleRequestNode("root", "https://example.com/item/1", new TestItem("item-1"));
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-1", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(fixture.NetClient.RequestedUrls);
        Assert.Equal("https://example.com/item/1", fixture.NetClient.RequestedUrls[0]);
        Assert.Single(fixture.ItemSink.StoredBatches);
        Assert.Single(node.OnPersistedResults);
        Assert.Equal(PersistDecision.Stored, node.OnPersistedResults[0].Decision);
    }

    /// <summary>
    ///     验证 ShouldPersist=false 时不会调用持久化入口。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldSkipStoreWhenNodeFiltersItem()
    {
        var node = new FilterItemNode("root", "https://example.com/item/2", new TestItem("item-2"));
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-filter", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(fixture.ItemSink.StoredBatches);
        Assert.Single(node.OnPersistedResults);
        Assert.Equal(PersistDecision.Skipped, node.OnPersistedResults[0].Decision);
    }

    /// <summary>
    ///     验证结构化并发配置下会先处理先注册子节点的请求。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldEnqueueByChildRegistrationOrderForChildren()
    {
        var childA = new MultiRequestNode("A", "https://example.com/a/1", "https://example.com/a/2");
        var childB = new MultiRequestNode("B", "https://example.com/b/1");
        var root = new RootWithChildrenNode("root", new NodeExecutionOptions(LumaRouteKind.Auto, 1), childA, childB);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-child-order", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("https://example.com/a/1", fixture.NetClient.RequestedUrls[0]);
        Assert.Equal(3, fixture.NetClient.RequestedUrls.Count);
        Assert.Contains("https://example.com/a/2", fixture.NetClient.RequestedUrls);
        Assert.Contains("https://example.com/b/1", fixture.NetClient.RequestedUrls);
    }

    /// <summary>
    ///     验证结构化并发配置下子节点请求可全部完成。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldCompleteRequestsForAllChildren()
    {
        var childA = new MultiRequestNode("A", "https://example.com/a/1", "https://example.com/a/2");
        var childB = new MultiRequestNode("B", "https://example.com/b/1");
        var root = new RootWithChildrenNode("root", new NodeExecutionOptions(LumaRouteKind.Auto, 1), childA, childB);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-children", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, fixture.NetClient.RequestedUrls.Count);
        Assert.Equal(
            [
                "https://example.com/a/1",
                "https://example.com/a/2",
                "https://example.com/b/1"
            ],
            fixture.NetClient.RequestedUrls.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    ///     验证节点通过上下文读写 Cookie 时，会路由到对应会话。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldMapCookieOperationsToCorrectRouteContainer()
    {
        var node = new CookieOperationNode("root");
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(node), "test-command", "run-cookie", CancellationToken.None).ConfigureAwait(true);

        Assert.True(node.ContainsCookie);
        Assert.Equal("proxy-value", node.CookieValue);
        Assert.Contains(NetRouteKind.Proxy, fixture.NetClient.RentedRouteKinds);
    }

    /// <summary>
    ///     验证结构化并发配置下会先完成当前子树持久化，再推进后续兄弟节点。
    /// </summary>
    [Fact]
    public async Task RunAsyncShouldPersistSubtreeBeforeNextSibling()
    {
        var aLeaf = new SnapshotTreeNode("A1", "A1");
        var aNode = new SnapshotTreeNode("A", "A", new NodeExecutionOptions(LumaRouteKind.Auto, 1), aLeaf);
        var bNode = new SnapshotTreeNode("B", "B");
        var root = new SnapshotTreeNode("root", "Root", new NodeExecutionOptions(LumaRouteKind.Auto, 1), aNode, bNode);
        var fixture = CreateFixture();

        await fixture.CreateEngine().RunAsync(new StaticSpider(root), "test-command", "run-subtree-persist-order", CancellationToken.None).ConfigureAwait(true);

        var persistedIds = fixture.ItemSink.StoredBatches
            .SelectMany(static batch => batch)
            .Select(static envelope => ((TestItem)envelope.Item).Id)
            .ToArray();

        Assert.Equal(4, persistedIds.Length);
        var indexA1 = Array.IndexOf(persistedIds, "A1");
        var indexB = Array.IndexOf(persistedIds, "B");
        Assert.True(indexA1 >= 0 && indexB >= 0 && indexA1 < indexB, $"PersistOrder={string.Join(',', persistedIds)}");
    }

    /// <summary>
    ///     创建测试夹具。
    /// </summary>
    private static EngineTestFixture CreateFixture()
    {
        var itemSink = new FakeItemSink(static batch => batch.Select(static _ => PersistResult.Stored()).ToArray());
        return new EngineTestFixture(itemSink, new FakeLogManager(), new FakeProgressManager(), new FakeHtmlParser(), new FakeNetClient(), CreateDefaultOptions());
    }

    /// <summary>
    ///     构造默认引擎参数。
    /// </summary>
    private static LumaEngineOptions CreateDefaultOptions()
    {
        return new LumaEngineOptions
        {
            DefaultRouteKind = LumaRouteKind.Direct,
            RequestWorkerCount = 1,
            DownloadWorkerCount = 1,
            PersistWorkerCount = 1,
            RequestChannelCapacity = 64,
            DownloadChannelCapacity = 64,
            PersistChannelCapacity = 64,
            PersistBatchSize = 1,
            PersistFlushInterval = TimeSpan.FromMilliseconds(20),
            PresentationRefreshInterval = TimeSpan.FromMilliseconds(20)
        };
    }

    /// <summary>
    ///     测试状态对象。
    /// </summary>
    private sealed class TestState;

    /// <summary>
    ///     引擎测试夹具。
    /// </summary>
    private sealed class EngineTestFixture
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
        ///     初始化夹具。
        /// </summary>
        public EngineTestFixture(FakeItemSink itemSink, FakeLogManager logManager, FakeProgressManager progressManager, FakeHtmlParser htmlParser, FakeNetClient netClient, LumaEngineOptions options)
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
        ///     进度管理器。
        /// </summary>
        public FakeProgressManager ProgressManager { get; }

        /// <summary>
        ///     日志管理器。
        /// </summary>
        public FakeLogManager LogManager { get; }

        /// <summary>
        ///     网络客户端。
        /// </summary>
        public FakeNetClient NetClient { get; }

        /// <summary>
        ///     创建引擎实例。
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
    ///     静态根节点蜘蛛。
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
    ///     单请求节点。
    /// </summary>
    private class SingleRequestNode(string key, string url, IItem item) : LumaNode<TestState>(key)
    {
        /// <summary>
        ///     持久化回调结果。
        /// </summary>
        public List<PersistResult> OnPersistedResults { get; } = [];

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new LumaRequest(new HttpRequestMessage(HttpMethod.Get, new Uri(url)), context.NodePath);
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AddItem(item);
            return ValueTask.CompletedTask;
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
    ///     过滤持久化节点。
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
    ///     根节点（包含可配置子节点遍历策略）。
    /// </summary>
    private sealed class RootWithChildrenNode : LumaNode<TestState>
    {
        /// <summary>
        ///     执行选项。
        /// </summary>
        private readonly NodeExecutionOptions _executionOptions;

        /// <summary>
        ///     初始化根节点。
        /// </summary>
        public RootWithChildrenNode(string key, NodeExecutionOptions executionOptions, params LumaNode<TestState>[] children) : base(key)
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
    ///     产出多请求节点。
    /// </summary>
    private sealed class MultiRequestNode : LumaNode<TestState>
    {
        /// <summary>
        ///     请求地址列表。
        /// </summary>
        private readonly IReadOnlyList<string> _urls;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public MultiRequestNode(string key, params string[] urls) : base(key)
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
    ///     纯快照树节点（不发请求，仅在构建阶段产出数据项与子节点）。
    /// </summary>
    private sealed class SnapshotTreeNode : LumaNode<TestState>
    {
        /// <summary>
        ///     子节点集合。
        /// </summary>
        private readonly IReadOnlyList<LumaNode<TestState>> _children;

        /// <summary>
        ///     节点执行选项。
        /// </summary>
        private readonly NodeExecutionOptions _executionOptions;

        /// <summary>
        ///     节点数据项标识。
        /// </summary>
        private readonly string _itemId;

        /// <summary>
        ///     初始化节点。
        /// </summary>
        public SnapshotTreeNode(string key, string itemId, NodeExecutionOptions? executionOptions = null, params LumaNode<TestState>[] children) : base(key)
        {
            _itemId = itemId;
            _children = children;
            _executionOptions = executionOptions ?? new NodeExecutionOptions(LumaRouteKind.Auto, 1);
        }

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => _executionOptions;

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AddItem(new TestItem(_itemId));
            foreach (var child in _children)
            {
                AddChild(child);
            }

            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        /// <inheritdoc />
        public override ValueTask HandleResponseAsync(HttpResponseMessage response, LumaContext<TestState> context)
        {
            _ = response;
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Cookie 操作节点。
    /// </summary>
    private sealed class CookieOperationNode(string key) : LumaNode<TestState>(key)
    {
        /// <summary>
        ///     默认路由是否存在 Cookie。
        /// </summary>
        public bool ContainsCookie { get; private set; }

        /// <summary>
        ///     默认路由 Cookie 值。
        /// </summary>
        public string CookieValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public override NodeExecutionOptions ExecutionOptions => new NodeExecutionOptions(LumaRouteKind.Proxy, 1);

        /// <inheritdoc />
        public override async IAsyncEnumerable<LumaRequest> BuildRequestsAsync(LumaContext<TestState> context)
        {
            var targetUri = new Uri("https://example.com/");
            await context.SetCookieAsync(targetUri, new Cookie("proxy-token", "proxy-value", "/", "example.com")).ConfigureAwait(false);
            ContainsCookie = await context.ContainsCookieAsync(targetUri, "proxy-token").ConfigureAwait(false);
            var cookie = await context.GetCookieAsync(targetUri, "proxy-token").ConfigureAwait(false);
            CookieValue = cookie?.Value ?? string.Empty;
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
    ///     伪持久化入口。
    /// </summary>
    private sealed class FakeItemSink(Func<IReadOnlyList<ItemEnvelope<TestState>>, IReadOnlyList<PersistResult>> storeBatchFactory) : IItemSink<TestState>
    {
        /// <summary>
        ///     已接收批次。
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
    ///     伪日志管理器。
    /// </summary>
    private sealed class FakeLogManager : ILogManager
    {
        /// <summary>
        ///     日志条目集合。
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
        ///     初始化网络客户端。
        /// </summary>
        public FakeNetClient()
        {
            HttpClient = new HttpClient(new FakeHttpMessageHandler(RequestedUrls));
        }

        /// <summary>
        ///     路由租用记录。
        /// </summary>
        public List<NetRouteKind> RentedRouteKinds { get; } = [];

        /// <summary>
        ///     已请求地址。
        /// </summary>
        public List<string> RequestedUrls { get; } = [];

        /// <summary>
        ///     直连容器。
        /// </summary>
        private CookieContainer DirectCookies { get; } = new();

        /// <summary>
        ///     代理容器。
        /// </summary>
        private CookieContainer ProxyCookies { get; } = new();

        /// <summary>
        ///     HTTP 客户端。
        /// </summary>
        private HttpClient HttpClient { get; }

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
    ///     伪会话租约。
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
    private sealed class FakeHttpMessageHandler(List<string> requestedUrls) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestedUrls.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
