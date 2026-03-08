using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Presentation.Core;

/// <summary>
/// <b>进度管理器</b>
/// </summary>
public sealed class ProgressManager : IProgressManager
{
    /// <summary>
    /// 当前快照引用。
    /// </summary>
    private ProgressSnapshot _snapshot = new();

    /// <inheritdoc />
    public void Publish(ProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
    }

    /// <inheritdoc />
    public ProgressSnapshot CreateSnapshot() => Volatile.Read(ref _snapshot);
}

