using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Engine.Policies;

/// <summary>
/// <b>基于连续已存在阈值的节点停止策略</b>
/// </summary>
internal sealed class ThresholdNodeStopPolicy : INodeStopPolicy
{
    /// <inheritdoc />
    public ValueTask<bool> ShouldStopAsync(LumaNodeContext context, PersistResult persistResult, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shouldStop = persistResult.Decision == PersistDecision.AlreadyExists && persistResult.SuggestStopNode;
        return ValueTask.FromResult(shouldStop);
    }
}

