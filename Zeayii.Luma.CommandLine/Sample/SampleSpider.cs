using System.Diagnostics.CodeAnalysis;
using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
/// <b>示例爬虫</b>
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "由 DI 容器在运行时反射创建。")]
internal sealed class SampleSpider : ISpider
{
    /// <summary>
    /// 默认入口地址。
    /// </summary>
    private static readonly Uri EntryUrl = new("https://example.com");

    /// <inheritdoc />
    public ValueTask<LumaNode> CreateRootAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LumaNode root = new SampleListNode("sample", EntryUrl);
        return ValueTask.FromResult(root);
    }
}