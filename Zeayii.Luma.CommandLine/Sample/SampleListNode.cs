using Zeayii.Luma.Abstractions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
/// <b>示例列表节点</b>
/// </summary>
internal sealed class SampleListNode : LumaNode
{
    /// <summary>
    /// 入口地址。
    /// </summary>
    private readonly Uri _entryUrl;

    /// <summary>
    /// 初始化节点。
    /// </summary>
    /// <param name="key">节点键。</param>
    /// <param name="entryUrl">入口地址。</param>
    public SampleListNode(string key, Uri entryUrl) : base(key)
    {
        _entryUrl = entryUrl ?? throw new ArgumentNullException(nameof(entryUrl));
    }

    /// <inheritdoc />
    public override ValueTask<NodeResult> StartAsync(LumaNodeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new NodeResult { Requests = [new LumaRequest(new HttpRequestMessage(HttpMethod.Get, _entryUrl), context.NodePath)] });
    }

    /// <inheritdoc />
    public override ValueTask<NodeResult> HandleResponseAsync(HttpResponseMessage response, LumaNodeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var title = response.IsSuccessStatusCode ? "Example Domain" : "Request Failed";
        var url = response.RequestMessage?.RequestUri?.AbsoluteUri ?? _entryUrl.AbsoluteUri;
        return ValueTask.FromResult(new NodeResult { Items = [new SampleItem(url, title)] });
    }
}
