using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
/// <b>示例爬虫</b>
/// </summary>
internal sealed class SampleSpider : ISpider
{
    /// <summary>
    /// 默认入口地址。
    /// </summary>
    private static readonly Uri EntryUrl = new("https://example.com");

    /// <inheritdoc />
    public async IAsyncEnumerable<LumaNode> CreateRootsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new SampleListNode("sample", EntryUrl);
    }
}

