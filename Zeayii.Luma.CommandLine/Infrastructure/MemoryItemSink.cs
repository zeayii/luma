using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.CommandLine.Infrastructure;

/// <summary>
/// <b>内存持久化实现</b>
/// <para>
/// 作为框架最小可运行示例使用。
/// </para>
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "由 DI 容器在运行时反射创建。")]
internal sealed class MemoryItemSink : IItemSink
{
    /// <summary>
    /// 已持久化键集合。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<PersistResult>> StoreBatchAsync(IReadOnlyList<ItemEnvelope> items, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(items);

        var results = new PersistResult[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var envelope = items[index];
            var key = $"{envelope.NodePath}:{envelope.Item}";
            results[index] = _keys.TryAdd(key, 0) ? PersistResult.Stored("Stored into memory") : PersistResult.AlreadyExists("Duplicate item");
        }

        return ValueTask.FromResult<IReadOnlyList<PersistResult>>(results);
    }
}