using System.Diagnostics.CodeAnalysis;
using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
///     <b>示例爬虫</b>
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "由 DI 容器在运行时反射创建。")]
internal sealed class SampleSpider : ISpider<SampleState>
{
    /// <summary>
    ///     默认入口地址。
    /// </summary>
    private static readonly Uri EntryUrl = new("https://example.com");

    /// <inheritdoc />
    public ValueTask<SampleState> CreateStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SampleState());
    }

    /// <inheritdoc />
    public ValueTask<LumaNode<SampleState>> CreateRootAsync(SampleState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        LumaNode<SampleState> root = new SampleListNode("sample", EntryUrl);
        return ValueTask.FromResult(root);
    }
}